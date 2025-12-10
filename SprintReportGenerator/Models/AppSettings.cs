namespace SprintReportGenerator.Models
{
    public class AppSettings
    {
        public string UserName { get; set; } = string.Empty;
        public string EncSecret { get; set; } = string.Empty; // encrypted
        public string JiraUrl { get; set; } = string.Empty; // e.g., https://your-domain.atlassian.net/browse/PROJECT-123
        public string ProjectName { get; set; } = string.Empty;  // e.g., AIRPMD
        public string SprintName { get; set; } = string.Empty;  // e.g., Sprint 70
        public string MemberName { get; set; } = string.Empty;  // e.g., Sarp Akgündüz
        public bool IsBasic { get; set; } // if true, use EncPassword for basic auth else use ApiToken
        public bool RememberMe { get; set; } = false;
        public bool OpenAfterGenerate { get; set; } = true;
        public int? OverrideBoardId { get; set; }
        public long? OverrideSprintId { get; set; }
    }
}
