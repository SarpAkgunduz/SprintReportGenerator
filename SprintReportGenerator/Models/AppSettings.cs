namespace SprintReportGenerator.Models
{
    public class AppSettings
    {
        public string Email { get; set; } = string.Empty;
        public string EncApiToken { get; set; } = string.Empty; // encrypted
        public string JiraUrl { get; set; } = string.Empty; // e.g., https://your-domain.atlassian.net/browse/PROJECT-123
        public string ProjectName { get; set; } = string.Empty;  // e.g., AIRPMD
        public string SprintName { get; set; } = string.Empty;  // e.g., Sprint 70
        public string MemberName { get; set; } = string.Empty;  // e.g., Sarp Akgündüz
        public bool RememberMe { get; set; } = false;
        public bool OpenAfterGenerate { get; set; } = true;

    }
}
