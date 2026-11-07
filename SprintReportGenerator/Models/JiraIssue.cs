namespace SprintReportGenerator.Models
{
    /// <summary>Minimal issue projection used by the preview list and the report tables.</summary>
    public sealed record JiraIssue(string Project, string Type, string Key, string Summary, string Status);

    /// <summary>A sprint found on one of the project's Agile boards.</summary>
    public sealed record SprintInfo(int BoardId, long Id, string Name, DateTime? Start, DateTime? End);

    /// <summary>Issues of a sprint plus the sprint they were resolved from (null if no sprint could be resolved).</summary>
    public sealed record SprintSearchResult(IReadOnlyList<JiraIssue> Issues, SprintInfo? Sprint);

    /// <summary>Issue types that the report works with.</summary>
    public static class IssueTypes
    {
        public const string Bug = "Bug";
        public const string Improvement = "Improvement";
        public const string Story = "Story";

        public static readonly IReadOnlyList<string> ReportTypes = new[] { Bug, Improvement, Story };

        public static bool Is(JiraIssue issue, string type) =>
            string.Equals(issue.Type?.Trim(), type, StringComparison.OrdinalIgnoreCase);

        public static bool IsReportType(JiraIssue issue) => ReportTypes.Any(t => Is(issue, t));
    }
}
