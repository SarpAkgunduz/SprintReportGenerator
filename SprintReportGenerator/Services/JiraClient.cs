﻿// Services/JiraClient.cs
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
using static System.Net.WebRequestMethods;

namespace SprintReportGenerator.Services
{
    /// <summary>
    /// Minimal Jira client for issue search and sprint metadata.
    /// - Supports Basic (email:apiToken) and Bearer (PAT) auth
    /// - Uses Agile (greenhopper) Sprint Report for exact key sets
    /// - Falls back to REST v3/v2 JQL when necessary
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

            _http.DefaultRequestHeaders.Authorization = _authBasic; // start with Basic
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("SprintReportGenerator/1.0");
        }

        // DTO order: Project, Type, Key, Summary, Status
        public record JiraIssue(string Project, string Type, string Key, string Summary, string Status);

        // ========= PUBLIC SEARCH APIS =========

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
        /// Returns (statusCode, body). Tries Basic first, then Bearer.
        /// </summary>
        /// <param name="url"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        private async Task<(HttpStatusCode code, string body)> GetWithAuthFallbackAsync(string url, CancellationToken ct)
        {
            foreach (var auth in new[] { _authBasic, _authBearer })
            {
                _http.DefaultRequestHeaders.Authorization = auth;
                using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode) return (resp.StatusCode, body);
            }
            // Sonuncunun sonucunu döndür
            return (HttpStatusCode.Unauthorized, string.Empty);
        }
        /// <summary>
        /// STRICT sprint search:
        /// 1) Collect ALL candidate (boardId,sprintId) pairs matching the sprint (exact name first, then numeric).
        /// 2) For each candidate, read Sprint Report and extract official keys. Choose the first that yields keys.
        /// 3) If none yields keys, fall back to "sprint = {id}" (no open/closed fallbacks).
        /// </summary>
        public async Task<IReadOnlyList<JiraIssue>> SearchIssuesByProjectAndSprintAsync(
            string project, string sprintText, IEnumerable<string>? issueTypes, CancellationToken ct = default)
        {
            var dbg = new List<string>
            {
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] STRICT SEARCH project='{project}', sprintText='{sprintText}'"
            };

            static string Esc(string? s) => (s ?? string.Empty).Replace("\"", "\\\"");
            static int? Num(string? s)
            {
                if (string.IsNullOrWhiteSpace(s)) return null;
                var m = Regex.Match(s, @"\d+");
                return m.Success ? int.Parse(m.Value) : (int?)null;
            }

            bool projectLooksLikeKey = Regex.IsMatch(project ?? string.Empty, @"^[A-Z0-9_]+$");
            string projectClause = projectLooksLikeKey ? $"project = {project}" : $"project = \"{Esc(project)}\"";
            var typeClause = BuildIssueTypeClause(issueTypes);
            var wantNum = Num(sprintText);

            // 1) Gather candidate board/sprint pairs
            var candidates = new List<(int boardId, long sprintId, string name, DateTime? start, DateTime? end)>();
            try
            {
                if (!string.IsNullOrWhiteSpace(project) && !string.IsNullOrWhiteSpace(sprintText))
                {
                    var boardsUrl = $"{_baseUrl}/rest/agile/1.0/board?projectKeyOrId={Uri.EscapeDataString(project)}&maxResults=50";
                    var (bCode, boardsJson) = await GetWithAuthFallbackAsync(boardsUrl, ct);
                    dbg.Add($"boards GET {bCode}");

                    if (bCode == HttpStatusCode.OK)
                    {
                        using var bDoc = JsonDocument.Parse(boardsJson);
                        if (bDoc.RootElement.TryGetProperty("values", out var boards) && boards.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var b in boards.EnumerateArray())
                            {
                                if (!b.TryGetProperty("id", out var idEl)) continue;
                                var bid = idEl.GetInt32();

                                var sUrl = $"{_baseUrl}/rest/agile/1.0/board/{bid}/sprint?state=active,closed,future&maxResults=500";
                                var (sCode, sJson) = await GetWithAuthFallbackAsync(sUrl, ct);
                                dbg.Add($"sprints(board={bid}) GET {sCode}");
                                if (sCode != HttpStatusCode.OK) continue;

                                using var sDoc = JsonDocument.Parse(sJson);
                                if (!sDoc.RootElement.TryGetProperty("values", out var vals) || vals.ValueKind != JsonValueKind.Array)
                                    continue;

                                // collect exact-name matches first
                                foreach (var s in vals.EnumerateArray())
                                {
                                    var name = s.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
                                    if (name.Equals(sprintText, StringComparison.OrdinalIgnoreCase) &&
                                        (!projectLooksLikeKey || name.IndexOf(project, StringComparison.OrdinalIgnoreCase) >= 0))
                                    {
                                        var sid = s.GetProperty("id").GetInt32();
                                        var (st, en) = ReadDatesFromJsonObject(s);
                                        candidates.Add((bid, sid, name, st, en));
                                    }
                                }

                                // then numeric-equality matches (if none collected yet)
                                if (candidates.Count == 0 && wantNum.HasValue)
                                {
                                    foreach (var s in vals.EnumerateArray())
                                    {
                                        var name = s.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
                                        var mn = Regex.Match(name, @"\d+");
                                        if (mn.Success && int.Parse(mn.Value) == wantNum.Value &&
                                            (!projectLooksLikeKey || name.IndexOf(project, StringComparison.OrdinalIgnoreCase) >= 0))
                                        {
                                            var sid = s.GetInt32("id");
                                            var (st, en) = ReadDatesFromJsonObject(s);
                                            candidates.Add((bid, sid, name, st, en));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                dbg.Add("candidate collection EX: " + ex.Message);
            }

            dbg.Add($"candidates: {candidates.Count}");
            foreach (var c in candidates.Take(5))
                dbg.Add($" - cand board={c.boardId} sprint={c.sprintId} name='{c.name}' start={c.start} end={c.end}");

            // Helper: try get official keys for a candidate
            async Task<HashSet<string>> TrySprintReportKeysAsync(int boardId, long sprintId)
            {
                var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    var url = $"{_baseUrl}/rest/greenhopper/1.0/rapid/charts/sprintreport?rapidViewId={boardId}&sprintId={sprintId}";
                    var (rCode, rText) = await GetWithAuthFallbackAsync(url, ct);
                    dbg.Add($"sprintreport(board={boardId},sprint={sprintId}) -> {rCode}");
                    if (rCode != HttpStatusCode.OK) return keys;

                    using var doc = JsonDocument.Parse(rText);
                    if (!doc.RootElement.TryGetProperty("contents", out var contents)) return keys;

                    ExtractKeysFromSprintReport(contents, keys);
                }
                catch (Exception ex)
                {
                    dbg.Add("sprintreport EX: " + ex.Message);
                }
                return keys;
            }

            int? chosenBoard = null;
            long? chosenSprint = null;
            var officialKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 2) Prefer candidate whose Sprint Report yields keys
            foreach (var cand in candidates)
            {
                var k = await TrySprintReportKeysAsync(cand.boardId, cand.sprintId);
                if (k.Count > 0)
                {
                    chosenBoard = cand.boardId;
                    chosenSprint = cand.sprintId;
                    dbg.Add($"CHOSEN_BY_KEYS board={chosenBoard} sprint={chosenSprint} name='{cand.name}' start={cand.start:o} end={cand.end:o}");
                    officialKeys = k;
                    dbg.Add($"CHOSEN by keys: board={chosenBoard}, sprint={chosenSprint}, keys={officialKeys.Count}");
                    break;
                }
            }

            // If none gave keys but at least one candidate exists, pick the latest by start date
            if (!chosenBoard.HasValue && candidates.Count > 0)
            {
                var pick = candidates
                    .OrderByDescending(c => c.start ?? DateTime.MinValue)
                    .First();
                chosenBoard = pick.boardId;
                chosenSprint = pick.sprintId;
                dbg.Add($"CHOSEN by fallback-candidate: board={chosenBoard}, sprint={chosenSprint}");
            }

            // If still nothing, we try a generic resolve (older logic)
            if (!chosenBoard.HasValue || !chosenSprint.HasValue)
            {
                var sid = await ResolveSprintIdAsync(project ?? string.Empty, sprintText ?? string.Empty, ct).ConfigureAwait(false);
                if (sid.HasValue)
                {
                    chosenBoard = -1; // unknown
                    chosenSprint = sid.Value;
                    dbg.Add($"CHOSEN by ResolveSprintId fallback: sprint={chosenSprint}");
                }
            }

            // 3) If we have official keys -> query strictly by keys
            if (officialKeys.Count > 0)
            {
                dbg.Add("STRICT by official key set");
                var list = await FetchIssuesByKeysAsync(officialKeys, ct).ConfigureAwait(false);

                // also enforce project/type on client-side
                var filtered = list.Where(i =>
                    (projectLooksLikeKey
                        ? (i.Key?.StartsWith(project + "-", StringComparison.OrdinalIgnoreCase) ?? false)
                        : string.Equals(i.Project, project, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (issueTypes != null)
                {
                    var typeSet = new HashSet<string>(issueTypes.Where(t => !string.IsNullOrWhiteSpace(t)),
                                                      StringComparer.OrdinalIgnoreCase);
                    filtered = filtered.Where(i => typeSet.Contains(i.Type ?? string.Empty)).ToList();
                }

                dbg.Add($"RETURN keys={officialKeys.Count}, after filters={filtered.Count}");
                WriteStrictDebug(dbg, officialKeys);
                return filtered;
            }

            // 4) No key set -> fall back to sprint=id JQL (still project-scoped)
            var results = new List<JiraIssue>();

            if (chosenSprint.HasValue)
            {
                var jql = $"{projectClause} AND sprint = {chosenSprint.Value}{typeClause} ORDER BY updated DESC";
                dbg.Add("FALLBACK JQL: " + jql);
                results = (await SearchIssuesAsync(jql, ct).ConfigureAwait(false)).ToList();
            }
            else if (int.TryParse(sprintText, out var sidNumeric))
            {
                // sprintText sayısal ise ismi değil ID kabul et
                var jql = $"{projectClause} AND sprint = {sidNumeric}{typeClause} ORDER BY updated DESC";
                dbg.Add("FALLBACK JQL (by numeric): " + jql);
                results = (await SearchIssuesAsync(jql, ct).ConfigureAwait(false)).ToList();
            }
            else if (!string.IsNullOrWhiteSpace(sprintText))
            {
                var jql = $"{projectClause} AND sprint = \"{Esc(sprintText)}\"{typeClause} ORDER BY updated DESC";
                dbg.Add("FALLBACK JQL (by name): " + jql);
                results = (await SearchIssuesAsync(jql, ct).ConfigureAwait(false)).ToList();
            }

            dbg.Add($"RETURN fallback count={results.Count}");
            WriteStrictDebug(dbg, null);
            return results;
        }

        public Task<IReadOnlyList<JiraIssue>> SearchIssuesByProjectAndSprintAsync(
            string project, string sprintText, CancellationToken ct = default)
            => SearchIssuesByProjectAndSprintAsync(project, sprintText, null, ct);

        // ========= SPRINT DATES =========

        public async Task<(DateTime? start, DateTime? end)> TryGetSprintDatesAsync(
            string projectKeyOrId, string sprintText, CancellationToken ct = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(projectKeyOrId) || string.IsNullOrWhiteSpace(sprintText))
                    return (null, null);

                var boardsUrl = $"{_baseUrl}/rest/agile/1.0/board?projectKeyOrId={Uri.EscapeDataString(projectKeyOrId)}&maxResults=50";
                var (bCode2, boardsJson2) = await GetWithAuthFallbackAsync(boardsUrl, ct);
                if (bCode2 != HttpStatusCode.OK) return await TryByIdFallback(projectKeyOrId, sprintText, ct).ConfigureAwait(false);
                using var boardsDoc = JsonDocument.Parse(boardsJson2);
                if (!boardsDoc.RootElement.TryGetProperty("values", out var boardsEl) || boardsEl.ValueKind != JsonValueKind.Array)
                    return await TryByIdFallback(projectKeyOrId, sprintText, ct).ConfigureAwait(false);

                foreach (var b in boardsEl.EnumerateArray())
                {
                    if (!b.TryGetProperty("id", out var idEl)) continue;
                    var boardId = idEl.GetInt32();

                    var sUrl = $"{_baseUrl}/rest/agile/1.0/board/{boardId}/sprint?state=active,closed,future&maxResults=200";
                    var (sCode2, sJson2) = await GetWithAuthFallbackAsync(sUrl, ct);
                    if (sCode2 != HttpStatusCode.OK) continue;
                    using var sDoc = JsonDocument.Parse(sJson2);
                    if (!sDoc.RootElement.TryGetProperty("values", out var vals) || vals.ValueKind != JsonValueKind.Array)
                        continue;

                    int? wantNum = null;
                    var mm = Regex.Match(sprintText, @"\d+");
                    if (mm.Success) wantNum = int.Parse(mm.Value);

                    foreach (var s in vals.EnumerateArray())
                    {
                        var name = s.TryGetProperty("name", out var n) ? (n.GetString() ?? string.Empty) : string.Empty;
                        if (name.Length == 0) continue;

                        bool match = name.Equals(sprintText, StringComparison.OrdinalIgnoreCase);
                        if (!match && wantNum.HasValue)
                        {
                            var mn = Regex.Match(name, @"\d+");
                            match = mn.Success && int.Parse(mn.Value) == wantNum.Value;
                        }
                        if (!match) continue;

                        var (start, end) = ReadDatesFromJsonObject(s);
                        var sid = s.TryGetProperty("id", out var sidEl) ? (long?)sidEl.GetInt32() : null;

                        if ((!start.HasValue || !end.HasValue) && sid.HasValue)
                        {
                            var detail = await TryGetSprintDatesByIdAsync(sid.Value, ct).ConfigureAwait(false);
                            if (detail.HasValue) return detail.Value;
                        }
                        return (start, end);
                    }
                }
            }
            catch { /* swallow */ }

            return await TryByIdFallback(projectKeyOrId, sprintText, ct).ConfigureAwait(false);
        }

        public async Task<long?> ResolveSprintIdAsync(string projectKeyOrId, string sprintText, CancellationToken ct = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(projectKeyOrId) || string.IsNullOrWhiteSpace(sprintText))
                    return null;

                int? wantNum = null;
                var m = Regex.Match(sprintText, @"\d+");
                if (m.Success) wantNum = int.Parse(m.Value);

                var boardsUrl = $"{_baseUrl}/rest/agile/1.0/board?projectKeyOrId={Uri.EscapeDataString(projectKeyOrId)}&maxResults=50";
                var (bCode3, boardsJson3) = await GetWithAuthFallbackAsync(boardsUrl, ct);
                if (bCode3 != HttpStatusCode.OK) return null;
                using var boardsDoc = JsonDocument.Parse(boardsJson3);
                if (!boardsDoc.RootElement.TryGetProperty("values", out var boardsEl) || boardsEl.ValueKind != JsonValueKind.Array)
                    return null;

                foreach (var b in boardsEl.EnumerateArray())
                {
                    if (!b.TryGetProperty("id", out var idEl)) continue;
                    var boardId = idEl.GetInt32();

                    var sUrl = $"{_baseUrl}/rest/agile/1.0/board/{boardId}/sprint?state=active,closed,future&maxResults=200";
                    var (sCode3, sJson3) = await GetWithAuthFallbackAsync(sUrl, ct);
                    if (sCode3 != HttpStatusCode.OK) continue;
                    using var sDoc = JsonDocument.Parse(sJson3);

                    if (!sDoc.RootElement.TryGetProperty("values", out var vals) || vals.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var s in vals.EnumerateArray())
                    {
                        var name = s.TryGetProperty("name", out var n) ? (n.GetString() ?? string.Empty) : string.Empty;
                        if (name.Equals(sprintText, StringComparison.OrdinalIgnoreCase) &&
                            (!Regex.IsMatch(projectKeyOrId, @"^[A-Z0-9_]+$") || name.IndexOf(projectKeyOrId, StringComparison.OrdinalIgnoreCase) >= 0))
                            return s.TryGetProperty("id", out var sid) ? sid.GetInt32() : (long?)null;
                    }

                    if (wantNum.HasValue)
                    {
                        foreach (var s in vals.EnumerateArray())
                        {
                            var name = s.TryGetProperty("name", out var n) ? (n.GetString() ?? string.Empty) : string.Empty;
                            var mn = Regex.Match(name, @"\d+");
                            if (mn.Success && int.Parse(mn.Value) == wantNum.Value &&
                                (!Regex.IsMatch(projectKeyOrId, @"^[A-Z0-9_]+$") || name.IndexOf(projectKeyOrId, StringComparison.OrdinalIgnoreCase) >= 0))
                                return s.TryGetProperty("id", out var sid) ? sid.GetInt32() : (long?)null;
                        }
                    }
                }
            }
            catch { /* best-effort */ }
            return null;
        }

        // ========= INTERNALS =========

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

        private async Task<IReadOnlyList<JiraIssue>> FetchIssuesByKeysAsync(IEnumerable<string> keys, CancellationToken ct)
        {
            var list = new List<JiraIssue>();
            const int chunk = 50;

            var arr = keys.Where(k => !string.IsNullOrWhiteSpace(k))
                          .Distinct(StringComparer.OrdinalIgnoreCase)
                          .ToArray();

            for (int i = 0; i < arr.Length; i += chunk)
            {
                var slice = arr.Skip(i).Take(chunk).ToArray();
                var jql = $"key in ({string.Join(", ", slice.Select(k => $"\"{k}\""))})";
                var part = await SearchIssuesAsync(jql, ct).ConfigureAwait(false);
                if (part.Count > 0) list.AddRange(part);
            }
            return list;
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
                var (code, json) = await GetWithAuthFallbackAsync(url, ct);
                if (code != HttpStatusCode.OK) return null;

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                DateTime? start = null, end = null;
                if (root.TryGetProperty("startDate", out var sd) && sd.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(sd.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ds))
                    start = ds;

                if (root.TryGetProperty("endDate", out var ed) && ed.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(ed.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var de))
                    end = de;

                return (start, end);
            }
            catch { return null; }
        }

        // ===== Helpers for sprintreport parsing & debug =====

        private static void ExtractKeysFromSprintReport(JsonElement contents, HashSet<string> keys)
        {
            // Known arrays with objects: completedIssues, issuesNotCompletedInCurrentSprint, puntedIssues, issuesCompletedInAnotherSprint
            var objectArrays = new[] {
                "completedIssues",
                "issuesNotCompletedInCurrentSprint",
                "puntedIssues",
                "issuesCompletedInAnotherSprint",
                "allIssues"
            };
            foreach (var prop in objectArrays)
            {
                if (!contents.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
                foreach (var el in arr.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("key", out var kEl))
                    {
                        var k = kEl.GetString();
                        if (!string.IsNullOrWhiteSpace(k)) keys.Add(k!);
                    }
                }
            }

            // Strings: issueKeysAddedDuringSprint
            if (contents.TryGetProperty("issueKeysAddedDuringSprint", out var strArr) && strArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in strArr.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.String)
                    {
                        var k = el.GetString();
                        if (!string.IsNullOrWhiteSpace(k)) keys.Add(k!);
                    }
                }
            }

            // Be defensive: iterate any array & collect "key" or string entries
            foreach (var prop in contents.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Array) continue;
                foreach (var el in prop.Value.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("key", out var kEl))
                    {
                        var k = kEl.GetString();
                        if (!string.IsNullOrWhiteSpace(k)) keys.Add(k!);
                    }
                    else if (el.ValueKind == JsonValueKind.String)
                    {
                        var k = el.GetString();
                        if (!string.IsNullOrWhiteSpace(k)) keys.Add(k!);
                    }
                }
            }
        }

        private static void WriteStrictDebug(List<string> lines, HashSet<string>? keys)
        {
            try
            {
                if (keys != null)
                {
                    lines.Add($"officialKeys count={keys.Count}");
                    lines.Add("officialKeys sample: " + string.Join(", ", keys.Take(20)));
                }
                var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "strict_debug.txt");
                System.IO.File.AppendAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine + new string('-', 80) + Environment.NewLine);
            }
            catch { /* ignore logging failures */ }
        }
        public void Dispose()
        {
            _http.Dispose();
        }
    }

    internal static class JsonElementExt
    {
        public static int GetInt32(this JsonElement el, string property)
        {
            return el.TryGetProperty(property, out var p) ? p.GetInt32() : 0;
        }
    }

}


