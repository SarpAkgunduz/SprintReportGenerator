namespace SprintReportGenerator.Services
{
    public static class ValidationService
    {
        /// <summary>
        /// Validates required fields for report generation.
        /// Returns (true, null) if valid; otherwise (false, "reason").
        /// </summary>
        public static (bool Ok, string? Error) ValidateMainFormInputs(string? sprintName, string? memberName, string? projectName)
        {
            if (string.IsNullOrWhiteSpace(sprintName)) return (false, "Sprint name cannot be empty.");
            if (string.IsNullOrWhiteSpace(memberName)) return (false, "Member name cannot be empty.");
            if (string.IsNullOrWhiteSpace(projectName)) return (false, "Project name cannot be empty.");
            return (true, null);
        }

        /// <summary>
        /// Validates LoginForm inputs (username + Jira base URL).
        /// Returns (true, null) if valid; otherwise (false, "reason").
        /// </summary>
        public static (bool Ok, string? Error) ValidateLoginFormInputs(string? userName, string? jiraUrl)
        {
            if (string.IsNullOrWhiteSpace(userName)) return (false, "Username cannot be empty.");
            if (string.IsNullOrWhiteSpace(jiraUrl)) return (false, "Jira base URL cannot be empty.");

            if (!Uri.TryCreate(jiraUrl.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return (false, "Please enter a valid Jira URL (e.g. https://jira.example.com).");

            return (true, null);
        }
    }
}
