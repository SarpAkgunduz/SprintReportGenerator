using DocumentFormat.OpenXml.Bibliography;
using SprintReportGenerator.Models;
using SprintReportGenerator.Services;
using SprintReportGenerator.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using JiraIssue = SprintReportGenerator.Services.JiraClient.JiraIssue;

namespace SprintReportGenerator.Forms
{
    public partial class MainForm : Form
    {
        private readonly AppSettings _settings;
        private readonly OpenXmlTemplateProcessor _report = new OpenXmlTemplateProcessor();
        private readonly ISettingsStore _store = new JsonSettingsStore();

        // Two-column ListView (use your Designer control)
        private ListView lvIssues;

        // Auto-preview infra
        private System.Windows.Forms.Timer _autoPreviewTimer;
        private CancellationTokenSource? _activeLoadCts;

        public MainForm(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings ?? new AppSettings();

            // Form title and icon
            const string AppTitle = "Open Sprint Generator";
            this.Text = AppTitle;
            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            // Wire only Generate
            btnGenerate.Click -= btnGenerate_Click;
            btnGenerate.Click += btnGenerate_Click;

            // Use the existing Designer ListView (rename if your control name differs)
            lvIssues = issueList;

            // Two-column view (Issue Type | Key)
            lvIssues.View = View.Details;
            lvIssues.FullRowSelect = true;
            lvIssues.GridLines = true;
            lvIssues.MultiSelect = false;
            lvIssues.HideSelection = false;
            lvIssues.UseCompatibleStateImageBehavior = false;

            lvIssues.Columns.Clear();
            lvIssues.Columns.Add("Issue Type", 160, HorizontalAlignment.Left);
            lvIssues.Columns.Add("Key", 140, HorizontalAlignment.Left);

            // StatusStrip progress defaults
            tsProg.Style = ProgressBarStyle.Marquee;
            tsProg.MarqueeAnimationSpeed = 30;
            tsProg.Visible = false;

            // Preload
            if (txtSprintName != null) txtSprintName.Text = _settings.SprintName ?? string.Empty;
            if (txtMemberName != null) txtMemberName.Text = _settings.MemberName ?? string.Empty;
            if (txtProjectName != null) txtProjectName.Text = _settings.ProjectName ?? string.Empty;
            chkOpenAfterGenerate.Checked = _settings.OpenAfterGenerate;

            // Debounce timer
            _autoPreviewTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _autoPreviewTimer.Tick += async (_, __) =>
            {
                _autoPreviewTimer.Stop();
                await LoadIssuesAsync();
            };

            // Trigger on text changes
            if (txtProjectName != null) txtProjectName.TextChanged += ProjectOrSprint_Changed;
            if (txtSprintName != null) txtSprintName.TextChanged += ProjectOrSprint_Changed;

            // First load (does nothing if project is empty)
            this.Shown += async (_, __) => await LoadIssuesAsync();
        }

        // =========================
        // Generate report (filtered to Bug/Improvement/Story)
        // =========================
        private async void btnGenerate_Click(object? sender, EventArgs e)
        {
            try
            {
                string sprintName = txtSprintName?.Text?.Trim() ?? string.Empty;
                string memberName = txtMemberName?.Text?.Trim() ?? string.Empty;
                string projectName = txtProjectName?.Text?.Trim() ?? string.Empty;

                var (ok, error) = ValidationService.ValidateMainFormInputs(sprintName, memberName, projectName);
                if (!ok)
                {
                    MessageBox.Show(error, "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _settings.SprintName = sprintName;
                _settings.MemberName = memberName;
                _settings.ProjectName = projectName;
                _settings.OpenAfterGenerate = chkOpenAfterGenerate.Checked;
                _store.Save(_settings);

                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string template = Path.Combine(exeDir, "report_template.docx");
                if (!File.Exists(template))
                {
                    MessageBox.Show($"Template not found:\n{template}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // >>> Keep these OUTSIDE so they are visible below <<<
                IReadOnlyList<JiraIssue> issues = Array.Empty<JiraIssue>();
                string sprintStartTr = string.Empty, sprintEndTr = string.Empty;

                // Fetch issues and (best-effort) sprint date range
                if (!string.IsNullOrWhiteSpace(_settings.Email) &&
                    !string.IsNullOrWhiteSpace(_settings.JiraUrl) &&
                    !string.IsNullOrWhiteSpace(_settings.EncApiToken))
                {
                    var token = _store.Unprotect(_settings.EncApiToken);
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                        using var client = new JiraClient(_settings.JiraUrl.TrimEnd('/'), _settings.Email, token);

                        // Issues for report
                        var jql = JqlBuilder.SprintIssues(projectName, sprintName);
                        var raw = await client.SearchIssuesAsync(jql, cts.Token).ConfigureAwait(true);

                        // Keep only Bug/Improvement/Story for the report
                        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                            { "Bug", "Improvement", "Story" };

                        issues = raw.Where(i => allowed.Contains(i.Type)).ToArray();

                        // Try to fetch sprint date range (Turkish formatting) — best effort
                        try
                        {
                            var (start, end) = await client
                                .TryGetSprintDatesAsync(projectName, sprintName, cts.Token)
                                .ConfigureAwait(true);

                            var tr = new CultureInfo("tr-TR");
                            if (start.HasValue) sprintStartTr = start.Value.ToString("d MMMM yyyy", tr);
                            if (end.HasValue) sprintEndTr = end.Value.ToString("d MMMM yyyy", tr);
                        }
                        catch { /* ignore */ }
                    }
                }

                // Prepare template data
                var data = new TemplateData
                {
                    ReportDate = DateTime.Today.ToString("dd.MM.yyyy", new CultureInfo("en-US")),
                    ProjectName = _settings.ProjectName ?? string.Empty,
                    MemberName = _settings.MemberName ?? string.Empty,
                    SprintName = _settings.SprintName ?? string.Empty,
                    SprintStartDate = sprintStartTr,
                    SprintEndDate = sprintEndTr
                };

                // Generate
                string output = _report.Generate(template, data, issues);

                MessageBox.Show($"Report created successfully:\n{output}", "Success",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);

                if (chkOpenAfterGenerate.Checked && File.Exists(output))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = output,
                        UseShellExecute = true
                    });
                }
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


        // =========================
        // Auto-preview (project required; sprint optional)
        // =========================

        private string ProjectNameText => txtProjectName?.Text?.Trim() ?? string.Empty;
        private string SprintNameText => txtSprintName?.Text?.Trim() ?? string.Empty;

        private void SetJiraStatus(string text, Color color)
        {
            tslJira.Text = text;
            tslJira.ForeColor = color;
        }

        private void ShowProgress(bool on)
        {
            tsProg.Visible = on;
            tsProg.MarqueeAnimationSpeed = on ? 30 : 0;
        }

        private void ProjectOrSprint_Changed(object? sender, EventArgs e)
        {
            _autoPreviewTimer.Stop();
            _autoPreviewTimer.Start();
        }

        private void FillIssues(IEnumerable<JiraIssue>? issues)
        {
            // Two columns only: Issue Type | Key
            lvIssues.BeginUpdate();
            lvIssues.Items.Clear();

            if (issues != null)
            {
                foreach (var issue in issues)
                {
                    var item = new ListViewItem(issue.Type ?? string.Empty); // 1) Issue Type
                    item.SubItems.Add(issue.Key ?? string.Empty);            // 2) Key
                    lvIssues.Items.Add(item);
                }
            }

            lvIssues.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);
            lvIssues.EndUpdate();
        }

        private async Task LoadIssuesAsync()
        {
            // Cancel previous load
            _activeLoadCts?.Cancel();
            _activeLoadCts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var token = _activeLoadCts.Token;

            // Fill only if project is provided
            if (string.IsNullOrWhiteSpace(ProjectNameText))
            {
                lvIssues.Items.Clear();
                SetJiraStatus("Jira: waiting for Project...", Color.DodgerBlue);
                return;
            }

            // Settings check
            if (string.IsNullOrWhiteSpace(_settings.Email) ||
                string.IsNullOrWhiteSpace(_settings.JiraUrl) ||
                string.IsNullOrWhiteSpace(_settings.EncApiToken))
            {
                lvIssues.Items.Clear();
                SetJiraStatus("Jira: Missing settings", Color.OrangeRed);
                return;
            }

            ShowProgress(true);
            lvIssues.Items.Clear();
            SetJiraStatus("Jira: Loading issues...", Color.DodgerBlue);

            try
            {
                var tokenPlain = _store.Unprotect(_settings.EncApiToken);
                if (string.IsNullOrWhiteSpace(tokenPlain))
                {
                    ShowProgress(false);
                    SetJiraStatus("Jira: Token decrypt failed", Color.IndianRed);
                    MessageBox.Show("Stored token could not be decrypted. Please login again.",
                        "Token Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                using var client = new JiraClient(_settings.JiraUrl.TrimEnd('/'), _settings.Email, tokenPlain);

                IReadOnlyList<JiraIssue> issues;

                if (!string.IsNullOrWhiteSpace(SprintNameText))
                {
                    // Project + Sprint => only Bug/Improvement/Story for that sprint
                    var types = new[] { "Bug", "Improvement", "Story" };
                    issues = await client.SearchIssuesByProjectAndSprintAsync(
                        ProjectNameText, SprintNameText, types, token);
                }
                else
                {
                    // Only Project => latest issues for that project (no sprint)
                    issues = await client.SearchIssuesByProjectAsync(
                        ProjectNameText, take: 50, issueTypes: null, ct: token);
                }

                if (token.IsCancellationRequested) return;

                ShowProgress(false);

                var count = issues?.Count ?? 0;
                if (count == 0)
                {
                    SetJiraStatus("Jira: 0 issues", Color.Firebrick);
                    lvIssues.Items.Clear();
                    return;
                }

                // Show only Issue Type | Key in the debug/validate list
                FillIssues(issues);
                SetJiraStatus($"Jira: {count} issues", Color.ForestGreen);
            }
            catch (TaskCanceledException)
            {
                ShowProgress(false);
                SetJiraStatus("Jira: canceled", Color.OrangeRed);
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                ShowProgress(false);
                SetJiraStatus("Jira: network/SSL error", Color.IndianRed);
                MessageBox.Show($"Network/SSL error:\n{ex.Message}", "Network Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                ShowProgress(false);
                SetJiraStatus("Jira: error", Color.IndianRed);
                MessageBox.Show($"Unexpected error:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
