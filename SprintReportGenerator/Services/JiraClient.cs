// Services/JiraClient.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SprintReportGenerator.Services
{
    /// <summary>
    /// Minimal Jira client for issue search. Dual-auth (Basic/Bearer) and v3/v2 fallbacks.
    /// Also provides helpers to resolve sprint IDs and read sprint start/end dates.
    /// </summary>
    public class JiraClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;

        // Prepared auth headers to allow Basic -> Bearer fallback
        private readonly AuthenticationHeaderValue _authBasic;
        private readonly AuthenticationHeaderValue _authBearer;

        public JiraClient(string baseUrl, string email, string apiToken, TimeSpan? timeout = null)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentException("baseUrl is required", nameof(baseUrl));
            if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("email is required", nameof(email));
            if (string.IsNullOrWhiteSpace(apiToken)) throw new ArgumentException("apiToken is required", nameof(apiToken));

            _baseUrl = baseUrl.Trim().TrimEnd('/');

            // HttpClient with system proxy (as requested)
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

            // Prepare both auth modes: Basic (Atlassian Cloud API token) and Bearer (DC PAT)
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{apiToken}"));
            _authBasic = new AuthenticationHeaderValue("Basic", basic);
            _authBearer = new AuthenticationHeaderValue("Bearer", apiToken);

            // Default headers
            _http.DefaultRequestHeaders.Authorization = _authBasic;
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("SprintReportGenerator/1.0");
        }

        // DTO ORDER (fixed): Project, Type, Key, Summary, Status
        public record JiraIssue(string Project, string Type, string Key, string Summary, string Status);

        /// <summary>
        /// Execute a JQL search. Tries v3 then v2 under both Basic and Bearer auth.
        /// Returns empty list on failure or when no issues are found.
        /// </summary>
        public async Task<IReadOnlyList<JiraIssue>> SearchIssuesAsync(string jql, CancellationToken ct = default)
        {
            var fields = "summary,issuetype,status,project";
            var urlV3 = $"{_baseUrl}/rest/api/3/search?jql={Uri.EscapeDataString(jql)}&fields={Uri.EscapeDataString(fields)}&maxResults=1000";
            var urlV2 = $"{_baseUrl}/rest/api/2/search?jql={Uri.EscapeDataString(jql)}&fields={Uri.EscapeDataString(fields)}&maxResults=1000";

            var origAuth = _http.DefaultRequestHeaders.Authorization;
            try
            {
                foreach (var auth in new[] { _authBasic, _authBearer })
                {
                    _http.DefaultRequestHeaders.Authorization = auth;

                    var list = await TrySearch(urlV3, ct).ConfigureAwait(false);
                    if (list is { Count: > 0 }) return list!;

                    list = await TrySearch(urlV2, ct).ConfigureAwait(false);
                    if (list is { Count: > 0 }) return list!;
                }
            }
            finally
            {
                // Do not leak Bearer to later Agile calls; default back to original (or Basic)
                _http.DefaultRequestHeaders.Authorization = origAuth ?? _authBasic;
            }

            return Array.Empty<JiraIssue>();
        }

        /// <summary>
        /// Search by project and sprint (with optional issue types filter).
        /// Builds robust JQL variants (id/name, quoted/unquoted) and returns the first non-empty result.
        /// </summary>
        public Task<IReadOnlyList<JiraIssue>> SearchIssuesByProjectAndSprintAsync(
            string project, string sprintText, CancellationToken ct = default)
            => SearchIssuesByProjectAndSprintAsync(project, sprintText, null, ct);

        public async Task<IReadOnlyList<JiraIssue>> SearchIssuesByProjectAndSprintAsync(
            string project, string sprintText, IEnumerable<string>? issueTypes, CancellationToken ct = default)
        {
            static string Esc(string? s) => (s ?? string.Empty).Replace("\"", "\\\"");
            bool projectLooksLikeKey = Regex.IsMatch(project ?? string.Empty, @"^[A-Z0-9_]+$");
            string projectClause = projectLooksLikeKey ? $"project = {project}" : $"project = \"{Esc(project)}\"";
            var typeClause = BuildIssueTypeClause(issueTypes);

            var jqls = new List<string>(capacity: 12);

            var sid = await ResolveSprintIdAsync(project ?? string.Empty, sprintText ?? string.Empty, ct).ConfigureAwait(false);
            if (sid.HasValue)
            {
                jqls.Add($"{projectClause} AND sprint = {sid.Value}{typeClause} ORDER BY updated DESC");
                jqls.Add($"sprint = {sid.Value}{typeClause} ORDER BY updated DESC");
            }

            if (int.TryParse(sprintText, out var num))
            {
                jqls.Add($"{projectClause} AND sprint = {num}{typeClause} ORDER BY updated DESC");
                jqls.Add($"sprint = {num}{typeClause} ORDER BY updated DESC");
            }

            // Extract numeric from mixed text correctly (e.g., "Sprint 14")
            var m = Regex.Match(sprintText ?? string.Empty, @"\d+");
            if (m.Success && int.TryParse(m.Value, out var numFromText))
            {
                jqls.Add($"{projectClause} AND sprint = {numFromText}{typeClause} ORDER BY updated DESC");
                jqls.Add($"sprint = {numFromText}{typeClause} ORDER BY updated DESC");
            }

            jqls.Add($"{projectClause} AND sprint = \"{Esc(sprintText)}\"{typeClause} ORDER BY updated DESC");
            jqls.Add($"sprint = \"{Esc(sprintText)}\"{typeClause} ORDER BY updated DESC");

            // Generic sprint state fallbacks
            jqls.Add($"{projectClause} AND sprint in openSprints(){typeClause} ORDER BY updated DESC");
            jqls.Add($"{projectClause} AND sprint in closedSprints(){typeClause} ORDER BY updated DESC");

            return await TryFirstNonEmptyAsync(jqls, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Search issues by project only (no sprint). Returns up to 'take' results ordered by updated desc.
        /// Issue types filter is optional.
        /// </summary>
        public async Task<IReadOnlyList<JiraIssue>> SearchIssuesByProjectAsync(
            string project, int take = 50, IEnumerable<string>? issueTypes = null, CancellationToken ct = default)
        {
            static string Esc(string? s) => (s ?? string.Empty).Replace("\"", "\\\"");
            bool projectLooksLikeKey = Regex.IsMatch(project ?? string.Empty, @"^[A-Z0-9_]+$");
            string projectClause = projectLooksLikeKey ? $"project = {project}" : $"project = \"{Esc(project)}\"";
            var typeClause = BuildIssueTypeClause(issueTypes);

            var jql = $"{projectClause}{typeClause} ORDER BY updated DESC";

            var list = await SearchIssuesAsync(jql, ct).ConfigureAwait(false);
            if (take > 0 && list.Count > take) return list.Take(take).ToArray();
            return list;
        }

        /// <summary>
        /// Attempts to retrieve and parse a list of Jira issues from the specified URL.
        /// Returns null to allow caller to try another variant/version.
        /// </summary>
        private async Task<IReadOnlyList<JiraIssue>?> TrySearch(string url, CancellationToken ct)
        {
            try
            {
                using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;

                var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("issues", out var issuesEl) || issuesEl.ValueKind != JsonValueKind.Array)
                    return Array.Empty<JiraIssue>();

                var list = new List<JiraIssue>(capacity: Math.Min(issuesEl.GetArrayLength(), 1000));

                foreach (var issueEl in issuesEl.EnumerateArray())
                {
                    var key = issueEl.GetProperty("key").GetString() ?? string.Empty;
                    var fields = issueEl.TryGetProperty("fields", out var f) ? f : default;

                    var summary = fields.ValueKind == JsonValueKind.Object && fields.TryGetProperty("summary", out var s)
                        ? s.GetString() ?? string.Empty : string.Empty;

                    var status = fields.ValueKind == JsonValueKind.Object && fields.TryGetProperty("status", out var st)
                        ? (st.TryGetProperty("name", out var sn) ? sn.GetString() ?? string.Empty : string.Empty)
                        : string.Empty;

                    var type = fields.ValueKind == JsonValueKind.Object && fields.TryGetProperty("issuetype", out var it)
                        ? (it.TryGetProperty("name", out var tn) ? tn.GetString() ?? string.Empty : string.Empty)
                        : string.Empty;

                    var project = fields.ValueKind == JsonValueKind.Object && fields.TryGetProperty("project", out var pj)
                        ? (pj.TryGetProperty("name", out var pn) ? pn.GetString() ?? string.Empty : string.Empty)
                        : string.Empty;

                    // Strict order: Project, Type, Key, Summary, Status
                    list.Add(new JiraIssue(project, type, key, summary, status));
                }

                return list;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Resolve sprint ID by enumerating boards for a project and matching by sprint name (contains/equals).
        /// </summary>
        public async Task<long?> ResolveSprintIdAsync(string projectKeyOrId, string sprintText, CancellationToken ct = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(projectKeyOrId) || string.IsNullOrWhiteSpace(sprintText))
                    return null;

                var boardsUrl = $"{_baseUrl}/rest/agile/1.0/board?projectKeyOrId={Uri.EscapeDataString(projectKeyOrId)}&maxResults=50";
                var boardsJson = await GetStringWithAuthFallbackAsync(boardsUrl, ct).ConfigureAwait(false);
                if (boardsJson == null) return null;

                using var boardsDoc = JsonDocument.Parse(boardsJson);
                if (!boardsDoc.RootElement.TryGetProperty("values", out var boardsEl) || boardsEl.ValueKind != JsonValueKind.Array)
                    return null;

                foreach (var b in boardsEl.EnumerateArray())
                {
                    if (!b.TryGetProperty("id", out var idEl)) continue;
                    var boardId = idEl.GetInt32();

                    var sUrl = $"{_baseUrl}/rest/agile/1.0/board/{boardId}/sprint?state=active,closed,future&maxResults=200";
                    var sJson = await GetStringWithAuthFallbackAsync(sUrl, ct).ConfigureAwait(false);
                    if (sJson == null) continue;

                    using var sDoc = JsonDocument.Parse(sJson);
                    if (!sDoc.RootElement.TryGetProperty("values", out var vals) || vals.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var s in vals.EnumerateArray())
                    {
                        var name = s.TryGetProperty("name", out var n) ? (n.GetString() ?? string.Empty) : string.Empty;
                        if (name.Length == 0) continue;

                        if (name.Equals(sprintText, StringComparison.OrdinalIgnoreCase) ||
                            name.Contains(sprintText, StringComparison.OrdinalIgnoreCase))
                        {
                            return s.TryGetProperty("id", out var sid) ? sid.GetInt32() : null;
                        }
                    }
                }
            }
            catch
            {
                // best-effort
            }
            return null;
        }

        /// <summary>
        /// Try to get sprint start/end dates by listing sprints under boards and, if needed, querying the sprint details endpoint.
        /// </summary>
        public async Task<(DateTime? start, DateTime? end)> TryGetSprintDatesAsync(
            string projectKeyOrId, string sprintText, CancellationToken ct = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(projectKeyOrId) || string.IsNullOrWhiteSpace(sprintText))
                    return (null, null);

                var boardsUrl = $"{_baseUrl}/rest/agile/1.0/board?projectKeyOrId={Uri.EscapeDataString(projectKeyOrId)}&maxResults=50";
                var boardsJson = await GetStringWithAuthFallbackAsync(boardsUrl, ct).ConfigureAwait(false);
                if (boardsJson == null) return await TryByIdFallback(projectKeyOrId, sprintText, ct).ConfigureAwait(false);

                using var boardsDoc = JsonDocument.Parse(boardsJson);
                if (!boardsDoc.RootElement.TryGetProperty("values", out var boardsEl) || boardsEl.ValueKind != JsonValueKind.Array)
                    return await TryByIdFallback(projectKeyOrId, sprintText, ct).ConfigureAwait(false);

                foreach (var b in boardsEl.EnumerateArray())
                {
                    if (!b.TryGetProperty("id", out var idEl)) continue;
                    var boardId = idEl.GetInt32();

                    var sUrl = $"{_baseUrl}/rest/agile/1.0/board/{boardId}/sprint?state=active,closed,future&maxResults=200";
                    var sJson = await GetStringWithAuthFallbackAsync(sUrl, ct).ConfigureAwait(false);
                    if (sJson == null) continue;

                    using var sDoc = JsonDocument.Parse(sJson);
                    if (!sDoc.RootElement.TryGetProperty("values", out var vals) || vals.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var s in vals.EnumerateArray())
                    {
                        var name = s.TryGetProperty("name", out var n) ? (n.GetString() ?? string.Empty) : string.Empty;
                        if (name.Length == 0) continue;

                        if (name.Equals(sprintText, StringComparison.OrdinalIgnoreCase) ||
                            name.Contains(sprintText, StringComparison.OrdinalIgnoreCase))
                        {
                            // The list endpoint may lack dates -> call details if needed
                            var sid = s.TryGetProperty("id", out var sidEl) ? (long?)sidEl.GetInt32() : null;
                            var (start, end) = ReadDatesFromJsonObject(s);

                            if ((!start.HasValue || !end.HasValue) && sid.HasValue)
                            {
                                var detail = await TryGetSprintDatesByIdAsync(sid.Value, ct).ConfigureAwait(false);
                                if (detail.HasValue) return detail.Value;
                            }
                            return (start, end);
                        }
                    }
                }
            }
            catch { /* swallow */ }

            // Final fallback: resolve sprint id first, then hit detail endpoint
            return await TryByIdFallback(projectKeyOrId, sprintText, ct).ConfigureAwait(false);
        }

        private static (DateTime? start, DateTime? end) ReadDatesFromJsonObject(JsonElement s)
        {
            DateTime? start = null, end = null;

            if (s.TryGetProperty("startDate", out var sd) && sd.ValueKind == JsonValueKind.String)
                start = ParseJiraDate(sd.GetString());

            if (s.TryGetProperty("endDate", out var ed) && ed.ValueKind == JsonValueKind.String)
                end = ParseJiraDate(ed.GetString());

            return (start, end);
        }

        private async Task<(DateTime? start, DateTime? end)> TryByIdFallback(string projectKeyOrId, string sprintText, CancellationToken ct)
        {
            var sid = await ResolveSprintIdAsync(projectKeyOrId, sprintText, ct).ConfigureAwait(false);
            if (sid.HasValue)
            {
                var details = await TryGetSprintDatesByIdAsync(sid.Value, ct).ConfigureAwait(false);
                if (details.HasValue) return details.Value;
            }
            return (null, null);
        }

        private async Task<(DateTime? start, DateTime? end)?> TryGetSprintDatesByIdAsync(long sprintId, CancellationToken ct)
        {
            try
            {
                var url = $"{_baseUrl}/rest/agile/1.0/sprint/{sprintId}";
                var json = await GetStringWithAuthFallbackAsync(url, ct).ConfigureAwait(false);
                if (json == null) return null;

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                DateTime? start = null, end = null;

                if (root.TryGetProperty("startDate", out var sd) && sd.ValueKind == JsonValueKind.String)
                    start = ParseJiraDate(sd.GetString());

                if (root.TryGetProperty("endDate", out var ed) && ed.ValueKind == JsonValueKind.String)
                    end = ParseJiraDate(ed.GetString());

                return (start, end);
            }
            catch { return null; }
        }

        // ==== Helpers ====

        /// <summary>
        /// For Agile API calls, try Basic then Bearer auth and return the JSON string of the first successful response.
        /// </summary>
        private async Task<string?> GetStringWithAuthFallbackAsync(string url, CancellationToken ct)
        {
            var orig = _http.DefaultRequestHeaders.Authorization;
            try
            {
                foreach (var auth in new[] { _authBasic, _authBearer })
                {
                    _http.DefaultRequestHeaders.Authorization = auth;
                    using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                }
                return null;
            }
            finally
            {
                _http.DefaultRequestHeaders.Authorization = orig ?? _authBasic;
            }
        }

        /// <summary>
        /// Safely parses Jira date formats (including timezone like +0000 by normalizing to +00:00) and returns UTC DateTime.
        /// </summary>
        private static DateTime? ParseJiraDate(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();

            // Normalize "+0000" to "+00:00" (e.g., +0530 -> +05:30)
            s = Regex.Replace(s, @"([+-]\d{2})(\d{2})$", "$1:$2");

            if (DateTimeOffset.TryParseExact(
                    s,
                    new[] { "yyyy-MM-dd'T'HH:mm:ss.fffK", "yyyy-MM-dd'T'HH:mm:ssK" },
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var dto))
            {
                return dto.UtcDateTime; // Use dto.LocalDateTime instead if you prefer local time
            }

            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out dto))
                return dto.UtcDateTime;

            return null;
        }

        private static string BuildIssueTypeClause(IEnumerable<string>? types)
        {
            if (types == null) return string.Empty;

            var arr = types
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t =>
                {
                    var s = t.Trim();
                    var needsQuotes = s.Any(ch => !char.IsLetterOrDigit(ch));
                    s = s.Replace("\"", "\\\"");
                    return needsQuotes ? $"\"{s}\"" : s;
                })
                .ToArray();

            if (arr.Length == 0) return string.Empty;
            return $" AND issuetype in ({string.Join(", ", arr)})";
        }

        private async Task<IReadOnlyList<JiraIssue>> TryFirstNonEmptyAsync(IEnumerable<string> jqls, CancellationToken ct)
        {
            foreach (var jql in jqls)
            {
                var list = await SearchIssuesAsync(jql, ct).ConfigureAwait(false);
                if (list != null && list.Count > 0)
                    return list;
            }
            return Array.Empty<JiraIssue>();
        }

        public void Dispose() => _http.Dispose();
    }
}
