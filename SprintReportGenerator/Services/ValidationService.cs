namespace SprintReportGenerator.Services
{
    public static class ValidationService
    {
        /// <summary>
        /// Validates required fields for report generation.
        /// Returns (true, null) if valid; otherwise (false, "reason").
        /// </summary>
        public static (bool Ok, string? Error) ValidateMainFormInputs(
            string? sprintName,
            string? memberName,
            string? projectName)
        {
            if (string.IsNullOrWhiteSpace(sprintName))
            {
                return (false, "Sprint name cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(memberName))
            {
                return (false, "Member name cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(projectName))
            {
                return (false, "Project name cannot be empty.");
            }

            return (true, null);
        }

        /// <summary>
        /// Validates LoginForm inputs (email + Jira base URL).
        /// Returns (true, null) if valid; otherwise (false, "reason").
        /// </summary>
        public static (bool Ok, string? Error) ValidateLoginFormInputs(
            string? email,
            string? jira)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return (false, "User Email cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(jira))
            {
                return (false, "Jira base URL cannot be empty.");
            }

            if (!jira.StartsWith("http", System.StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Please enter a valid Jira URL (e.g. https://jira.example.com).");
            }

            return (true, null);
        }
    }
}
