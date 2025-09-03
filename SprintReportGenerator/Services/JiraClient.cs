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
    /// Minimal Jira client for issue search and sprint date lookups.
    /// Dual auth (Basic/Bearer), REST v3/v2 fallbacks, and Agile API helpers.
    /// </summary>
    public class JiraClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;

        private readonly AuthenticationHeaderValue _authBasic;
        private readonly AuthenticationHeaderValue _authBearer;

        public JiraClient(string baseUrl, string email, string apiToken, TimeSpan? timeout = null)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentException("baseUrl is required", nameof(baseUrl));
            if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("email is required", nameof(email));
            if (string.IsNullOrWhiteSpace(apiToken)) throw new ArgumentException("apiToken is required", nameof(apiToken));

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

            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{apiToken}"));
            _authBasic = new AuthenticationHeaderValue("Basic", basic);
            _authBearer = new AuthenticationHeaderValue("Bearer", apiToken);

            _http.DefaultRequestHeaders.Authorization = _authBasic;
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("SprintReportGenerator/1.0");
        }

        // DTO order: Project, Type, Key, Summary, Status
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

            foreach (var auth in new[] { _authBasic, _authBearer })
            {
                _http.DefaultRequestHeaders.Authorization = auth;

                var list = await TrySearch(urlV3, ct).ConfigureAwait(false);
                if (list is { Count: > 0 }) return list!;

                list = await TrySearch(urlV2, ct).ConfigureAwait(false);
                if (list is { Count: > 0 }) return list!;
            }

            return Array.Empty<JiraIssue>();
        }

        /// <summary>
        /// Search issues by project and sprint (optional type filter).
        /// Builds robust JQL variants (id/name, quoted/unquoted, numeric).
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

            if (int.TryParse(sprintText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num))
            {
                jqls.Add($"{projectClause} AND sprint = {num}{typeClause} ORDER BY updated DESC");
                jqls.Add($"sprint = {num}{typeClause} ORDER BY updated DESC");
            }

            var m = Regex.Match(sprintText ?? string.Empty, @"\d+");
            if (m.Success && int.TryParse(m.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numFromText))
            {
                jqls.Add($"{projectClause} AND sprint = {numFromText}{typeClause} ORDER BY updated DESC");
                jqls.Add($"sprint = {numFromText}{typeClause} ORDER BY updated DESC");
            }

            jqls.Add($"{projectClause} AND sprint = \"{Esc(sprintText)}\"{typeClause} ORDER BY updated DESC");
            jqls.Add($"sprint = \"{Esc(sprintText)}\"{typeClause} ORDER BY updated DESC");

            jqls.Add($"{projectClause} AND sprint in openSprints(){typeClause} ORDER BY updated DESC");
            jqls.Add($"{projectClause} AND sprint in closedSprints(){typeClause} ORDER BY updated DESC");

            return await TryFirstNonEmptyAsync(jqls, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Search latest issues by project only (no sprint). Returns up to 'take' results ordered by updated desc.
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
        /// Resolve sprint id by matching name or trailing number against Agile API sprint list.
        /// </summary>
        public async Task<long?> ResolveSprintIdAsync(string projectKeyOrId, string sprintText, CancellationToken ct = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(projectKeyOrId) || string.IsNullOrWhiteSpace(sprintText))
                    return null;

                var wantNum = TryParseAnyNumber(sprintText);

                var boardsUrl = $"{_baseUrl}/rest/agile/1.0/board?projectKeyOrId={Uri.EscapeDataString(projectKeyOrId)}&maxResults=50";
                using (var boardsResp = await _http.GetAsync(boardsUrl, ct).ConfigureAwait(false))
                {
                    if (!boardsResp.IsSuccessStatusCode) return null;

                    var boardsJson = await boardsResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var boardsDoc = JsonDocument.Parse(boardsJson);

                    if (!boardsDoc.RootElement.TryGetProperty("values", out var boardsEl) || boardsEl.ValueKind != JsonValueKind.Array)
                        return null;

                    foreach (var b in boardsEl.EnumerateArray())
                    {
                        if (!b.TryGetProperty("id", out var idEl)) continue;
                        var boardId = idEl.GetInt32();

                        var sUrl = $"{_baseUrl}/rest/agile/1.0/board/{boardId}/sprint?state=active,closed,future&maxResults=200";
                        using var sResp = await _http.GetAsync(sUrl, ct).ConfigureAwait(false);
                        if (!sResp.IsSuccessStatusCode) continue;

                        var sJson = await sResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        using var sDoc = JsonDocument.Parse(sJson);
                        if (!sDoc.RootElement.TryGetProperty("values", out var vals) || vals.ValueKind != JsonValueKind.Array)
                            continue;

                        foreach (var s in vals.EnumerateArray())
                        {
                            var name = s.TryGetProperty("name", out var n) ? (n.GetString() ?? string.Empty) : string.Empty;
                            if (name.Length == 0) continue;

                            bool match =
                                name.Equals(sprintText, StringComparison.OrdinalIgnoreCase) ||
                                name.Contains(sprintText, StringComparison.OrdinalIgnoreCase);

                            if (!match && wantNum.HasValue && TryGetTrailingNumber(name, out var tail) && tail == wantNum.Value)
                                match = true;

                            if (match)
                                return s.TryGetProperty("id", out var sid) ? sid.GetInt32() : (int?)null;
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
        /// Try to get sprint start/end dates using Agile API. Matches by name or trailing number.
        /// Falls back to sprint detail endpoint when list lacks dates, and to activated/complete dates if needed.
        /// </summary>
        public async Task<(DateTime? start, DateTime? end)> TryGetSprintDatesAsync(
            string projectKeyOrId, string sprintText, CancellationToken ct = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(projectKeyOrId) || string.IsNullOrWhiteSpace(sprintText))
                    return (null, null);

                var wantNum = TryParseAnyNumber(sprintText);

                var boardsUrl = $"{_baseUrl}/rest/agile/1.0/board?projectKeyOrId={Uri.EscapeDataString(projectKeyOrId)}&maxResults=50";
                using var boardsResp = await _http.GetAsync(boardsUrl, ct).ConfigureAwait(false);
                if (!boardsResp.IsSuccessStatusCode) return await TryByIdFallback(projectKeyOrId, sprintText, ct).ConfigureAwait(false);

                var boardsJson = await boardsResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var boardsDoc = JsonDocument.Parse(boardsJson);
                if (!boardsDoc.RootElement.TryGetProperty("values", out var boardsEl) || boardsEl.ValueKind != JsonValueKind.Array)
                    return await TryByIdFallback(projectKeyOrId, sprintText, ct).ConfigureAwait(false);

                foreach (var b in boardsEl.EnumerateArray())
                {
                    if (!b.TryGetProperty("id", out var idEl)) continue;
                    var boardId = idEl.GetInt32();

                    var sUrl = $"{_baseUrl}/rest/agile/1.0/board/{boardId}/sprint?state=active,closed,future&maxResults=200";
                    using var sResp = await _http.GetAsync(sUrl, ct).ConfigureAwait(false);
                    if (!sResp.IsSuccessStatusCode) continue;

                    var sJson = await sResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var sDoc = JsonDocument.Parse(sJson);
                    if (!sDoc.RootElement.TryGetProperty("values", out var vals) || vals.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var s in vals.EnumerateArray())
                    {
                        var name = s.TryGetProperty("name", out var n) ? (n.GetString() ?? string.Empty) : string.Empty;
                        if (name.Length == 0) continue;

                        bool match =
                            name.Equals(sprintText, StringComparison.OrdinalIgnoreCase) ||
                            name.Contains(sprintText, StringComparison.OrdinalIgnoreCase);

                        if (!match && wantNum.HasValue && TryGetTrailingNumber(name, out var tail) && tail == wantNum.Value)
                            match = true;

                        if (match)
                        {
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
            catch
            {
                // swallow
            }

            return await TryByIdFallback(projectKeyOrId, sprintText, ct).ConfigureAwait(false);
        }

        // ----- Internals -----

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

                    list.Add(new JiraIssue(project, type, key, summary, status));
                }

                return list;
            }
            catch
            {
                return null;
            }
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

        private static (DateTime? start, DateTime? end) ReadDatesFromJsonObject(JsonElement s)
        {
            DateTime? start = null, end = null;

            if (s.TryGetProperty("startDate", out var sd) && sd.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(sd.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ds))
                start = ds;

            if (s.TryGetProperty("endDate", out var ed) && ed.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(ed.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var de))
                end = de;

            if (!start.HasValue && s.TryGetProperty("activatedDate", out var ad) && ad.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(ad.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var da))
                start = da;

            if (!end.HasValue && s.TryGetProperty("completeDate", out var cd) && cd.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(cd.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dc))
                end = dc;

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
                using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;

                var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                DateTime? start = null, end = null;

                if (root.TryGetProperty("startDate", out var sd) && sd.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(sd.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ds))
                    start = ds;

                if (root.TryGetProperty("endDate", out var ed) && ed.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(ed.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var de))
                    end = de;

                if (!start.HasValue && root.TryGetProperty("activatedDate", out var ad) && ad.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(ad.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var da))
                    start = da;

                if (!end.HasValue && root.TryGetProperty("completeDate", out var cd) && cd.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(cd.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dc))
                    end = dc;

                return (start, end);
            }
            catch { return null; }
        }

        private static bool TryGetTrailingNumber(string? name, out int n)
        {
            n = 0;
            if (string.IsNullOrWhiteSpace(name)) return false;
            var m = Regex.Match(name, @"(\d+)\s*$");
            return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
        }

        private static int? TryParseAnyNumber(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var m = Regex.Match(text, @"\d+");
            if (!m.Success) return null;
            return int.TryParse(m.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : (int?)null;
        }

        public void Dispose() => _http.Dispose();
    }
}
