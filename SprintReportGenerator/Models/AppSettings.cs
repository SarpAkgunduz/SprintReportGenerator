namespace SprintReportGenerator.Models
{
    public class AppSettings
    {
        public string UserName { get; set; } = string.Empty;
        public string EncSecret { get; set; } = string.Empty;   // DPAPI-encrypted password / token (only if RememberMe)
        public string JiraUrl { get; set; } = string.Empty;     // e.g., https://jira.example.com
        public string ProjectName { get; set; } = string.Empty; // e.g., AIRPMD
        public string SprintName { get; set; } = string.Empty;  // e.g., Sprint 70
        public string MemberName { get; set; } = string.Empty;  // e.g., Sarp Akgündüz
        public bool IsBasic { get; set; }                       // true: password (Basic), false: API token / PAT
        public bool RememberMe { get; set; }
        public bool OpenAfterGenerate { get; set; } = true;
    }
}
