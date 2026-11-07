using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SprintReportGenerator.Models;

namespace SprintReportGenerator.Services
{
    /// <summary>
    /// Minimal Jira client for issue search and sprint metadata.
    /// - Basic (user:password / email:apiToken) or Bearer (PAT) auth, chosen once in the constructor
    /// - Uses the Agile (greenhopper) Sprint Report for the exact key set of a sprint
    /// - Falls back to a "sprint = id" JQL when the Sprint Report is not available
    /// </summary>
    public sealed class JiraClient : IDisposable
    {
        private const int AgilePageSize = 50;    // Jira caps Agile endpoints at 50 per page
        private const int SearchPageSize = 100;
        private const int KeyChunkSize = 50;     // keeps "key in (...)" JQL URLs short
        private const string SearchFields = "summary,issuetype,status,project";

        // Sprint Report arrays that make up the official sprint scope
        private static readonly string[] SprintReportIssueArrays =
        {
            "completedIssues",
            "issuesNotCompletedInCurrentSprint"
        };

        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly Dictionary<string, IReadOnlyList<SprintInfo>> _sprintCache = new(StringComparer.OrdinalIgnoreCase);

        public JiraClient(string baseUrl, string userName, string secret,
                          bool useBasicAuth = true, TimeSpan? timeout = null)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentException("baseUrl is required", nameof(baseUrl));
            if (string.IsNullOrWhiteSpace(userName)) throw new ArgumentException("userName is required", nameof(userName));
            if (string.IsNullOrWhiteSpace(secret)) throw new ArgumentException("secret is required", nameof(secret));

            _baseUrl = baseUrl.Trim().TrimEnd('/');

            var handler = new HttpClientHandler
            {
                UseProxy = WebRequest.DefaultWebProxy != null,
                Proxy = WebRequest.DefaultWebProxy,
                DefaultProxyCredentials = CredentialCache.DefaultCredentials
            };

            _http = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = timeout ?? TimeSpan.FromSeconds(20)
            };

            // Only the configured scheme is used. Silently retrying with the other scheme
            // produces failed logins on Jira Server/DC, which can trigger CAPTCHA / account lock.
            _http.DefaultRequestHeaders.Authorization = useBasicAuth
                ? new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userName}:{secret}")))
                : new AuthenticationHeaderValue("Bearer", secret);

            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("SprintReportGenerator/1.0");
        }

        /// <summary>Jira Cloud only accepts API tokens over Basic auth (email:token).</summary>
        public static bool IsCloudUrl(string? url) =>
            (url ?? string.Empty).Contains("atlassian.net", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Resolves the auth scheme: password mode always uses Basic; token mode uses Bearer (PAT)
        /// on Server/DC and Basic (email:token) on Cloud.
        /// </summary>
        public static bool ShouldUseBasicAuth(string? url, bool passwordMode) => passwordMode || IsCloudUrl(url);

        // ========= AUTH CHECK =========

        public async Task<(bool success, string? errorMessage)> ValidateAsync(CancellationToken ct = default)
        {
            var urls = new[]
            {
                $"{_baseUrl}/rest/api/2/myself",
                $"{_baseUrl}/rest/api/3/myself"
            };

            string? lastError = null;
            foreach (var url in urls)
            {
                var (status, body) = await GetAsync(url, ct).ConfigureAwait(false);
                if (IsSuccess(status)) return (true, null);

                lastError = status switch
                {
                    HttpStatusCode.Unauthorized =>
                        "401 Unauthorized: Invalid credentials or API token.\n" +
                        $"URL: {url}\n" +
                        $"Auth: {_http.DefaultRequestHeaders.Authorization?.Scheme}\n" +
                        $"Response: {Truncate(body, 200)}",
                    HttpStatusCode.Forbidden =>
                        "403 Forbidden: Account may be locked (CAPTCHA) or lacks permissions.\n" +
                        $"Response: {Truncate(body, 200)}",
                    _ => $"{(int)status} {status}\n{Truncate(body, 500)}"
                };

                // Only an unknown endpoint is worth retrying on the other API version.
                if (status != HttpStatusCode.NotFound) break;
            }

            return (false, lastError ?? "Could not reach Jira API endpoints.");
        }

        // ========= ISSUE SEARCH =========

        /// <summary>
        /// Runs a JQL search and returns all pages.
        /// Tries the classic /rest/api/2/search (Server/DC), then /rest/api/3/search/jql (Cloud).
        /// </summary>
        public async Task<IReadOnlyList<JiraIssue>> SearchIssuesAsync(string jql, CancellationToken ct = default)
        {
            var result = await SearchClassicAsync(jql, ct).ConfigureAwait(false)
                      ?? await SearchEnhancedAsync(jql, ct).ConfigureAwait(false);
            return result ?? (IReadOnlyList<JiraIssue>)Array.Empty<JiraIssue>();
        }

        public async Task<IReadOnlyList<JiraIssue>> SearchIssuesByProjectAsync(
            string project, int take = 50, IEnumerable<string>? issueTypes = null, CancellationToken ct = default)
        {
            var jql = $"{BuildProjectClause(project)}{BuildIssueTypeClause(issueTypes)} ORDER BY updated DESC";
            var list = await SearchIssuesAsync(jql, ct).ConfigureAwait(false);
            return take > 0 && list.Count > take ? list.Take(take).ToArray() : list;
        }

        /// <summary>
        /// STRICT sprint search:
        /// 1) Find the sprint on the project's boards (exact name first, then sprint number).
        /// 2) For each candidate, read the Sprint Report and query exactly those keys.
        /// 3) If no Sprint Report yields keys, fall back to "sprint = {id}" (or the sprint text).
        /// The returned sprint carries start/end dates for the report.
        /// </summary>
        public async Task<SprintSearchResult> SearchIssuesByProjectAndSprintAsync(
            string project, string sprintText, IEnumerable<string>? issueTypes = null, CancellationToken ct = default)
        {
            var projectClause = BuildProjectClause(project);
            var typeClause = BuildIssueTypeClause(issueTypes);
            var candidates = await FindSprintCandidatesAsync(project, sprintText, ct).ConfigureAwait(false);

            foreach (var candidate in candidates)
            {
                var keys = await GetSprintReportKeysAsync(candidate.BoardId, candidate.Id, ct).ConfigureAwait(false);
                if (keys.Count == 0) continue;

                var issues = await SearchByKeysAsync(keys, $"{projectClause} AND sprint = {candidate.Id}", typeClause, ct)
                    .ConfigureAwait(false);
                var sprint = await WithDatesAsync(candidate, ct).ConfigureAwait(false);

                // Defensive: keep only issues that really belong to the requested project
                return new SprintSearchResult(issues.Where(i => BelongsToProject(i, project)).ToList(), sprint);
            }

            var pick = candidates.OrderByDescending(c => c.Start ?? DateTime.MinValue).FirstOrDefault();
            string sprintClause =
                pick != null ? $"sprint = {pick.Id}" :
                long.TryParse(sprintText, out var sprintId) ? $"sprint = {sprintId}" :
                $"sprint = \"{EscapeJql(sprintText)}\"";

            var list = await SearchIssuesAsync($"{projectClause} AND {sprintClause}{typeClause} ORDER BY updated DESC", ct)
                .ConfigureAwait(false);

            return new SprintSearchResult(list, pick == null ? null : await WithDatesAsync(pick, ct).ConfigureAwait(false));
        }

        // ========= SPRINT LOOKUP =========

        /// <summary>
        /// Single implementation of the board → sprint scan (paged). Preference order:
        /// exact name containing the project, number match containing the project,
        /// exact name, number match. Results are cached per client instance.
        /// </summary>
        public async Task<IReadOnlyList<SprintInfo>> FindSprintCandidatesAsync(
            string project, string sprintText, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(sprintText))
                return Array.Empty<SprintInfo>();

            project = project.Trim();
            sprintText = sprintText.Trim();

            var cacheKey = project + "\n" + sprintText;
            if (_sprintCache.TryGetValue(cacheKey, out var cached)) return cached;

            var wantNum = FirstNumber(sprintText);
            var exactOwned = new List<SprintInfo>();
            var numericOwned = new List<SprintInfo>();
            var exactAny = new List<SprintInfo>();
            var numericAny = new List<SprintInfo>();
            var seen = new HashSet<long>();

            var boards = await GetAgileValuesAsync(
                $"{_baseUrl}/rest/agile/1.0/board?projectKeyOrId={Uri.EscapeDataString(project)}", ct).ConfigureAwait(false);

            foreach (var board in boards)
            {
                if (!TryGetInt64(board, "id", out var boardId)) continue;

                var sprints = await GetAgileValuesAsync(
                    $"{_baseUrl}/rest/agile/1.0/board/{boardId}/sprint?state=active,closed,future", ct).ConfigureAwait(false);

                foreach (var s in sprints)
                {
                    if (!TryGetInt64(s, "id", out var sid) || !seen.Add(sid)) continue;

                    var name = GetString(s, "name");
                    bool exact = name.Equals(sprintText, StringComparison.OrdinalIgnoreCase);
                    bool numeric = !exact && wantNum.HasValue && FirstNumber(name) == wantNum;
                    if (!exact && !numeric) continue;

                    var (start, end) = ReadDates(s);
                    var info = new SprintInfo((int)boardId, sid, name, start, end);

                    // Shared boards may host sprints of several projects ("AIRPMD Sprint 73")
                    bool owned = name.Contains(project, StringComparison.OrdinalIgnoreCase);
                    (exact ? (owned ? exactOwned : exactAny) : (owned ? numericOwned : numericAny)).Add(info);
                }
            }

            var result = new[] { exactOwned, numericOwned, exactAny, numericAny }
                .FirstOrDefault(l => l.Count > 0) ?? new List<SprintInfo>();

            _sprintCache[cacheKey] = result;
            return result;
        }

        // ========= INTERNALS =========

        private async Task<SprintInfo> WithDatesAsync(SprintInfo sprint, CancellationToken ct)
        {
            if (sprint.Start.HasValue && sprint.End.HasValue) return sprint;

            using var doc = await GetJsonAsync($"{_baseUrl}/rest/agile/1.0/sprint/{sprint.Id}", ct).ConfigureAwait(false);
            if (doc == null) return sprint;

            var (start, end) = ReadDates(doc.RootElement);
            return sprint with { Start = sprint.Start ?? start, End = sprint.End ?? end };
        }

        private async Task<IReadOnlyCollection<string>> GetSprintReportKeysAsync(long boardId, long sprintId, CancellationToken ct)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var url = $"{_baseUrl}/rest/greenhopper/1.0/rapid/charts/sprintreport?rapidViewId={boardId}&sprintId={sprintId}";

            using var doc = await GetJsonAsync(url, ct).ConfigureAwait(false);
            if (doc == null || !doc.RootElement.TryGetProperty("contents", out var contents)) return keys;

            foreach (var propName in SprintReportIssueArrays)
            {
                if (!contents.TryGetProperty(propName, out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
                foreach (var el in arr.EnumerateArray())
                {
                    var key = GetString(el, "key");
                    if (key.Length > 0) keys.Add(key);
                }
            }
            return keys;
        }

        private async Task<List<JiraIssue>> SearchByKeysAsync(
            IReadOnlyCollection<string> keys, string baseClause, string typeClause, CancellationToken ct)
        {
            var list = new List<JiraIssue>();
            foreach (var slice in keys.Chunk(KeyChunkSize))
            {
                var keyList = string.Join(", ", slice.Select(k => $"\"{EscapeJql(k)}\""));
                var jql = $"{baseClause} AND key in ({keyList}){typeClause} ORDER BY key ASC";
                list.AddRange(await SearchIssuesAsync(jql, ct).ConfigureAwait(false));
            }
            return list;
        }

        /// <summary>/rest/api/2/search with startAt paging. Returns null if the endpoint is unusable.</summary>
        private async Task<List<JiraIssue>?> SearchClassicAsync(string jql, CancellationToken ct)
        {
            var all = new List<JiraIssue>();
            int startAt = 0;

            while (true)
            {
                var url = $"{_baseUrl}/rest/api/2/search?jql={Uri.EscapeDataString(jql)}" +
                          $"&fields={Uri.EscapeDataString(SearchFields)}&startAt={startAt}&maxResults={SearchPageSize}";

                using var doc = await GetJsonAsync(url, ct).ConfigureAwait(false);
                if (doc == null) return startAt == 0 ? null : all;

                var root = doc.RootElement;
                int read = ReadIssues(root, all);
                int total = TryGetInt64(root, "total", out var t) ? (int)t : all.Count;

                startAt += read;
                if (read == 0 || startAt >= total) return all;
            }
        }

        /// <summary>/rest/api/3/search/jql (Jira Cloud) with nextPageToken paging.</summary>
        private async Task<List<JiraIssue>?> SearchEnhancedAsync(string jql, CancellationToken ct)
        {
            var all = new List<JiraIssue>();
            string? pageToken = null;
            bool first = true;

            do
            {
                var url = $"{_baseUrl}/rest/api/3/search/jql?jql={Uri.EscapeDataString(jql)}" +
                          $"&fields={Uri.EscapeDataString(SearchFields)}&maxResults={SearchPageSize}" +
                          (pageToken != null ? $"&nextPageToken={Uri.EscapeDataString(pageToken)}" : string.Empty);

                using var doc = await GetJsonAsync(url, ct).ConfigureAwait(false);
                if (doc == null) return first ? null : all;
                first = false;

                var root = doc.RootElement;
                ReadIssues(root, all);

                bool isLast = root.TryGetProperty("isLast", out var il) && il.ValueKind == JsonValueKind.True;
                pageToken = isLast ? null : GetString(root, "nextPageToken") is { Length: > 0 } tok ? tok : null;
            }
            while (pageToken != null);

            return all;
        }

        /// <summary>Parses the "issues" array of a search response into <paramref name="target"/>.</summary>
        private static int ReadIssues(JsonElement root, List<JiraIssue> target)
        {
            if (!root.TryGetProperty("issues", out var issues) || issues.ValueKind != JsonValueKind.Array) return 0;

            int count = 0;
            foreach (var issue in issues.EnumerateArray())
            {
                count++;
                var fields = issue.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Object ? f : default;

                target.Add(new JiraIssue(
                    Project: GetNestedName(fields, "project"),
                    Type: GetNestedName(fields, "issuetype"),
                    Key: GetString(issue, "key"),
                    Summary: GetString(fields, "summary"),
                    Status: GetNestedName(fields, "status")));
            }
            return count;
        }

        /// <summary>Reads every page of an Agile endpoint ("values" + "isLast").</summary>
        private async Task<List<JsonElement>> GetAgileValuesAsync(string url, CancellationToken ct)
        {
            var values = new List<JsonElement>();
            var sep = url.Contains('?') ? '&' : '?';
            int startAt = 0;

            while (true)
            {
                using var doc = await GetJsonAsync($"{url}{sep}startAt={startAt}&maxResults={AgilePageSize}", ct).ConfigureAwait(false);
                if (doc == null ||
                    !doc.RootElement.TryGetProperty("values", out var arr) ||
                    arr.ValueKind != JsonValueKind.Array)
                    break;

                int read = 0;
                foreach (var v in arr.EnumerateArray())
                {
                    values.Add(v.Clone()); // detach from the disposed document
                    read++;
                }

                bool isLast = doc.RootElement.TryGetProperty("isLast", out var il) && il.ValueKind == JsonValueKind.True;
                if (isLast || read == 0) break;
                startAt += read;
            }
            return values;
        }

        private async Task<(HttpStatusCode status, string body)> GetAsync(string url, CancellationToken ct)
        {
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return (resp.StatusCode, body);
        }

        /// <summary>GET + parse. Returns null on non-2xx or invalid JSON; transport errors propagate.</summary>
        private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken ct)
        {
            var (status, body) = await GetAsync(url, ct).ConfigureAwait(false);
            if (!IsSuccess(status)) return null;
            try { return JsonDocument.Parse(body); }
            catch (JsonException) { return null; }
        }

        // ========= HELPERS =========

        private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and <= 299;

        private static string Truncate(string s, int max) => s.Length > max ? s[..max] : s;

        private static string EscapeJql(string? s) =>
            (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

        // Quoted form works for both project keys and project names.
        private static string BuildProjectClause(string project) => $"project = \"{EscapeJql(project?.Trim())}\"";

        private static string BuildIssueTypeClause(IEnumerable<string>? types)
        {
            var arr = (types ?? Enumerable.Empty<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => $"\"{EscapeJql(t.Trim())}\"")
                .ToArray();

            return arr.Length == 0 ? string.Empty : $" AND issuetype in ({string.Join(", ", arr)})";
        }

        private static bool BelongsToProject(JiraIssue issue, string project)
        {
            project = project.Trim();
            return issue.Key.StartsWith(project + "-", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(issue.Project, project, StringComparison.OrdinalIgnoreCase);
        }

        private static int? FirstNumber(string? s)
        {
            var m = Regex.Match(s ?? string.Empty, @"\d+");
            return m.Success && int.TryParse(m.Value, out var n) ? n : null;
        }

        private static string GetString(JsonElement obj, string prop) =>
            obj.ValueKind == JsonValueKind.Object &&
            obj.TryGetProperty(prop, out var v) &&
            v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? string.Empty
                : string.Empty;

        private static string GetNestedName(JsonElement obj, string prop) =>
            obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(prop, out var inner)
                ? GetString(inner, "name")
                : string.Empty;

        private static bool TryGetInt64(JsonElement obj, string prop, out long value)
        {
            value = 0;
            return obj.ValueKind == JsonValueKind.Object &&
                   obj.TryGetProperty(prop, out var v) &&
                   v.ValueKind == JsonValueKind.Number &&
                   v.TryGetInt64(out value);
        }

        private static (DateTime? start, DateTime? end) ReadDates(JsonElement obj) =>
            (ReadDate(obj, "startDate"), ReadDate(obj, "endDate"));

        private static DateTime? ReadDate(JsonElement obj, string prop)
        {
            var s = GetString(obj, prop);
            return s.Length > 0 &&
                   DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto)
                ? dto.LocalDateTime
                : null;
        }

        public void Dispose() => _http.Dispose();
    }
}
