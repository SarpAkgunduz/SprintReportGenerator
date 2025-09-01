namespace SprintReportGenerator.Models
{
    public class TemplateData
    {
        public string ReportDate { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string MemberName { get; set; } = string.Empty;
        public string SprintName { get; set; } = string.Empty;
        public string IssuesList { get; set; } = string.Empty;

        // Display-ready dates (e.g., "12 Mayıs 2025") – filled by MainForm after Jira fetch
        public string SprintStartDate { get; set; } = string.Empty;
        public string SprintEndDate { get; set; } = string.Empty;
    }
}
