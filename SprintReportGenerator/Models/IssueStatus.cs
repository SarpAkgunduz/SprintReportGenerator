namespace SprintReportGenerator.Models
{
    public enum StatusCategory
    {
        Other,
        Open,
        InProgress,
        DevCompleted,
        InQa,
        Blocked,
        Closed,
        Cancelled
    }

    /// <summary>Single place that maps Jira status names to report categories.</summary>
    public static class IssueStatus
    {
        private static readonly Dictionary<string, StatusCategory> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Open"] = StatusCategory.Open,
            ["In Progress"] = StatusCategory.InProgress,
            ["Dev Completed"] = StatusCategory.DevCompleted,
            ["In Q&A"] = StatusCategory.InQa,
            ["In QA"] = StatusCategory.InQa,
            ["Blocked"] = StatusCategory.Blocked,
            ["Closed"] = StatusCategory.Closed,
            ["Done"] = StatusCategory.Closed,
            ["Cancelled"] = StatusCategory.Cancelled,
            ["Canceled"] = StatusCategory.Cancelled,
            ["False Positive Approval"] = StatusCategory.Cancelled,
        };

        public static StatusCategory Classify(string? status) =>
            !string.IsNullOrWhiteSpace(status) && Map.TryGetValue(status.Trim(), out var category)
                ? category
                : StatusCategory.Other;

        public static bool Is(JiraIssue issue, StatusCategory category) => Classify(issue.Status) == category;
    }
}
