using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using SprintReportGenerator.Models;
using SprintReportGenerator.Services;

namespace SprintReportGenerator.Forms
{
    public partial class MainForm : Form
    {
        private readonly AppSettings _settings;
        private readonly ITemplateProcessor _processor = new OpenXmlTemplateProcessor();

        public MainForm(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings ?? new AppSettings();
        }

        private void btnGenerate_Click(object sender, EventArgs e)
        {
            try
            {
                string today = DateTime.Now.ToString("dd.MM.yyyy", new CultureInfo("tr-TR"));
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string template = Path.Combine(exeDir, "report_template.docx");
                string nameNoExt = Path.GetFileNameWithoutExtension(template);
                string output = Path.Combine(exeDir, $"{nameNoExt}_FILLED_{DateTime.Now:yyyyMMddHHmm}.docx");
                string sprintName = txtSprintName.Text?.Trim() ?? string.Empty;
                
                if (string.IsNullOrEmpty(sprintName))
                {
                    MessageBox.Show("Please enter a sprint name.", "Warning",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!File.Exists(template))
                {
                    MessageBox.Show($"Template not found:\n{template}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                var store = new JsonSettingsStore();
                _settings.SprintName = sprintName;
                new JsonSettingsStore().Save(_settings);

                var data = new TemplateData
                {
                    ReportDate = today,
                    Email = _settings.Email ?? string.Empty,
                    Project = _settings.ProjectKey ?? string.Empty,
                    MemberName = _settings.MemberName ?? string.Empty,                         
                };

                _processor.Process(template, output, data);

                MessageBox.Show($"Report created:\n{output}", "Done",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (IOException ioEx)
            {
                MessageBox.Show($"File error:\n{ioEx.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unexpected error:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
