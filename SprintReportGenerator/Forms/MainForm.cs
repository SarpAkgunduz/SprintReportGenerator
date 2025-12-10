// Forms/MainForm.cs
using SprintReportGenerator.Models;
using SprintReportGenerator.Services;
using SprintReportGenerator.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        private ListView lvIssues;

        private System.Windows.Forms.Timer _autoPreviewTimer;
        private CancellationTokenSource? _activeLoadCts;

        public MainForm(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings ?? new AppSettings();

            const string AppTitle = "Open Sprint Generator";
            this.Text = AppTitle;
            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            btnGenerate.Click -= btnGenerate_Click;
            btnGenerate.Click += btnGenerate_Click;

            lvIssues = issueList;

            lvIssues.View = View.Details;
            lvIssues.FullRowSelect = true;
            lvIssues.GridLines = true;
            lvIssues.MultiSelect = false;
            lvIssues.HideSelection = false;
            lvIssues.UseCompatibleStateImageBehavior = false;

            lvIssues.Columns.Clear();
            lvIssues.Columns.Add("Issue Type", 160, HorizontalAlignment.Left);
            lvIssues.Columns.Add("Key", 140, HorizontalAlignment.Left);

            tsProg.Style = ProgressBarStyle.Marquee;
            tsProg.MarqueeAnimationSpeed = 30;
            tsProg.Visible = false;

            if (txtSprintName != null) txtSprintName.Text = _settings.SprintName ?? string.Empty;
            if (txtMemberName != null) txtMemberName.Text = _settings.MemberName ?? string.Empty;
            if (txtProjectName != null) txtProjectName.Text = _settings.ProjectName ?? string.Empty;
            chkOpenAfterGenerate.Checked = _settings.OpenAfterGenerate;

            _autoPreviewTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _autoPreviewTimer.Tick += async (_, __) =>
            {
                _autoPreviewTimer.Stop();
                await LoadIssuesAsync();
            };

            if (txtProjectName != null) txtProjectName.TextChanged += ProjectOrSprint_Changed;
            if (txtSprintName != null) txtSprintName.TextChanged += ProjectOrSprint_Changed;

            this.Shown += async (_, __) => await LoadIssuesAsync();
        }

        // =========================
        // Generate report
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

                IReadOnlyList<JiraIssue> issues = Array.Empty<JiraIssue>();
                string sprintStartTr = string.Empty, sprintEndTr = string.Empty;

                // Fetch issues and sprint date range (best effort)
                if (!string.IsNullOrWhiteSpace(_settings.UserName) &&
                    !string.IsNullOrWhiteSpace(_settings.JiraUrl) &&
                    !string.IsNullOrWhiteSpace(_settings.EncSecret))
                {
                    var secret = _store.Unprotect(_settings.EncSecret);
                    if (!string.IsNullOrWhiteSpace(secret))
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                        using var client = new JiraClient(_settings.JiraUrl.TrimEnd('/'), _settings.UserName, secret);

                        // STRICT sprint fetch, only Bug/Improvement/Story
                        var allowedTypes = new[] { "Bug", "Improvement", "Story" };
                        var raw = await client
                            .SearchIssuesByProjectAndSprintAsync(projectName, sprintName, allowedTypes, cts.Token)
                            .ConfigureAwait(true);

                        issues = raw;

                        // derive project KEY from first issue key if available, else use user input
                        string projectKeyOrId = projectName;
                        var keyCandidate = raw.FirstOrDefault()?.Key ?? string.Empty;
                        var dash = keyCandidate.IndexOf('-');
                        if (dash > 0) projectKeyOrId = keyCandidate.Substring(0, dash);

                        try
                        {
                            var (start, end) = await client
                                .TryGetSprintDatesAsync(projectKeyOrId, sprintName, cts.Token)
                                .ConfigureAwait(true);

                            var tr = new CultureInfo("tr-TR");
                            if (start.HasValue) sprintStartTr = start.Value.ToString("d MMMM yyyy", tr);
                            if (end.HasValue) sprintEndTr = end.Value.ToString("d MMMM yyyy", tr);
                        }
                        catch { /* ignore */ }
                    }
                }

                var data = new TemplateData
                {
                    ReportDate = DateTime.Today.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                    ProjectName = _settings.ProjectName ?? string.Empty,
                    MemberName = _settings.MemberName ?? string.Empty,
                    SprintName = _settings.SprintName ?? string.Empty,
                    SprintStartDate = sprintStartTr,
                    SprintEndDate = sprintEndTr
                };

                string output = _report.Generate(template, data, issues);

                MessageBox.Show($"Report created successfully:\n{output}", "Success",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);

                if (chkOpenAfterGenerate.Checked && File.Exists(output))
                {
                    Process.Start(new ProcessStartInfo
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
        // Auto-preview
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
            lvIssues.BeginUpdate();
            lvIssues.Items.Clear();

            if (issues != null)
            {
                foreach (var issue in issues)
                {
                    var item = new ListViewItem(issue.Type ?? string.Empty);
                    item.SubItems.Add(issue.Key ?? string.Empty);
                    lvIssues.Items.Add(item);
                }
            }

            lvIssues.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);
            lvIssues.EndUpdate();
        }

        private async Task LoadIssuesAsync()
        {
            _activeLoadCts?.Cancel();
            _activeLoadCts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var token = _activeLoadCts.Token;

            if (string.IsNullOrWhiteSpace(ProjectNameText))
            {
                lvIssues.Items.Clear();
                SetJiraStatus("Jira: waiting for Project...", Color.DodgerBlue);
                return;
            }

            if (string.IsNullOrWhiteSpace(_settings.UserName) ||
                string.IsNullOrWhiteSpace(_settings.JiraUrl) ||
                string.IsNullOrWhiteSpace(_settings.EncSecret))
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
                var secretPlain = _store.Unprotect(_settings.EncSecret);
                if (string.IsNullOrWhiteSpace(secretPlain))
                {
                    ShowProgress(false);
                    SetJiraStatus("Jira: Token decrypt failed", Color.IndianRed);
                    MessageBox.Show("Stored token could not be decrypted. Please login again.",
                        "Token Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                using var client = new JiraClient(_settings.JiraUrl.TrimEnd('/'), _settings.UserName, secretPlain);

                IReadOnlyList<JiraIssue> issues;

                if (!string.IsNullOrWhiteSpace(SprintNameText))
                {
                    var types = new[] { "Bug", "Improvement", "Story" };
                    issues = await client.SearchIssuesByProjectAndSprintAsync(
                        ProjectNameText, SprintNameText, types, token);
                }
                else
                {
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
