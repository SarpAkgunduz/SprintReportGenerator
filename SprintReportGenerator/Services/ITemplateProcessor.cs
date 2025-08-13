using SprintReportGenerator.Models;

namespace SprintReportGenerator.Services
{
    public interface ITemplateProcessor
    {
        void Process(string templatePath, string outputPath, TemplateData data);
    }
}
