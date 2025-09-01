using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SprintReportGenerator.Services
{
    public static class JqlBuilder
    {
        public static string SprintIssues(string projectName, string sprintName)
            => $"project = \"{projectName}\" AND sprint = \"{sprintName}\" ORDER BY key ASC";
    }
}
