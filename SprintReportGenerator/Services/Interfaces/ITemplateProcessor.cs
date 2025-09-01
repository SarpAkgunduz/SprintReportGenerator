// Services/Interfaces/ITemplateProcessor.cs
using SprintReportGenerator.Models;
using System.Collections.Generic;
using static SprintReportGenerator.Services.JiraClient;

namespace SprintReportGenerator.Services.Interfaces
{
    public interface ITemplateProcessor
    {
        void Process(string templatePath, string outputPath, TemplateData data);
    }
}
