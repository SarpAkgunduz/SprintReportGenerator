using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SprintReportGenerator.Models;
using SprintReportGenerator.Services.Interfaces;

namespace SprintReportGenerator.Services
{
    public sealed class TemplateProcessor : ITemplateProcessor
    {
        public void Process(string templatePath, string outputPath, TemplateData data)
        {
            // We are copying the template to the output path
            File.Copy(templatePath, outputPath, overwrite: true);

            using (WordprocessingDocument doc = WordprocessingDocument.Open(outputPath, true))
            {
                var body = doc.MainDocumentPart?.Document?.Body;

                if (body == null || doc.MainDocumentPart?.Document == null) return; // Ensure body and Document are not null

                // Replace placeholders with actual data that user provided on main form
                ReplaceText(body, "{{FORM_DATE}}", data.ReportDate);
                ReplaceText(body, "{{PROJECT}}", data.ProjectName);
                ReplaceText(body, "{{MEMBER_NAME}}", data.MemberName);

                doc.MainDocumentPart.Document.Save();
            }
        }

        private void ReplaceText(Body body, string placeholder, string newValue)
        {
            foreach (var text in body.Descendants<Text>())
            {
                if (text.Text.Contains(placeholder))
                {
                    text.Text = text.Text.Replace(placeholder, newValue ?? string.Empty);
                }
            }
        }
    }
}
