using System.Diagnostics;
using System.Globalization;
using SprintReportGenerator.Models;
using SprintReportGenerator.Services;
using SprintReportGenerator.Services.Interfaces;

namespace SprintReportGenerator.Forms
{
    public partial class MainForm : Form
    {
        private static readonly CultureInfo Turkish = new CultureInfo("tr-TR");
        private static readonly TimeSpan JiraTimeout = TimeSpan.FromSeconds(60);

        private readonly AppSettings _settings;
        private readonly string _secret;
        private readonly OpenXmlTemplateProcessor _report = new OpenXmlTemplateProcessor();
        private readonly ISettingsStore _store = new JsonSettingsStore();
        private readonly System.Windows.Forms.Timer _autoPreviewTimer = new System.Windows.Forms.Timer { Interval = 500 };

        private CancellationTokenSource? _previewCts;
        private int _previewVersion;

        public MainForm(AppSettings settings, string secret)
        {
            InitializeComponent();
            FormBranding.Apply(this);

            _settings = settings ?? new AppSettings();
            _secret = secret ?? string.Empty;

            issueList.View = View.Details;
            issueList.FullRowSelect = true;
            issueList.GridLines = true;
            issueList.MultiSelect = false;
            issueList.HideSelection = false;
            issueList.Columns.Clear();
            issueList.Columns.Add("Issue Type", 100, HorizontalAlignment.Left);
            issueList.Columns.Add("Key", 120, HorizontalAlignment.Left);

            tsProg.Style = ProgressBarStyle.Marquee;
            ShowProgress(false);

            txtSprintName.Text = _settings.SprintName;
            txtMemberName.Text = _settings.MemberName;
            txtProjectName.Text = _settings.ProjectName;
            chkOpenAfterGenerate.Checked = _settings.OpenAfterGenerate;

            _autoPreviewTimer.Tick += async (_, _) =>
            {
                _autoPreviewTimer.Stop();
                await LoadIssuesAsync();
            };

            txtProjectName.TextChanged += ProjectOrSprint_Changed;
            txtSprintName.TextChanged += ProjectOrSprint_Changed;

            Shown += async (_, _) => await LoadIssuesAsync();
            FormClosed += (_, _) =>
            {
                _autoPreviewTimer.Dispose();
                _previewCts?.Cancel();
                _previewCts?.Dispose();
            };
        }

        private string ProjectNameText => txtProjectName.Text.Trim();
        private string SprintNameText => txtSprintName.Text.Trim();
        private string MemberNameText => txtMemberName.Text.Trim();

        private bool HasCredentials =>
            !string.IsNullOrWhiteSpace(_settings.UserName) &&
            !string.IsNullOrWhiteSpace(_settings.JiraUrl) &&
            !string.IsNullOrWhiteSpace(_secret);

        private JiraClient CreateClient() =>
            new JiraClient(_settings.JiraUrl, _settings.UserName, _secret,
                useBasicAuth: JiraClient.ShouldUseBasicAuth(_settings.JiraUrl, _settings.IsBasic),
                timeout: TimeSpan.FromSeconds(30));

        // =========================
        // Generate report
        // =========================
        private async void btnGenerate_Click(object? sender, EventArgs e)
        {
            string sprintName = SprintNameText, memberName = MemberNameText, projectName = ProjectNameText;

            var (ok, error) = ValidationService.ValidateMainFormInputs(sprintName, memberName, projectName);
            if (!ok)
            {
                MessageBox.Show(error, "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!HasCredentials)
            {
                MessageBox.Show("Jira credentials are missing. Please restart the application and log in again.",
                    "Missing Credentials", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string template = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "report_template.docx");
            if (!File.Exists(template))
            {
                MessageBox.Show($"Template not found:\n{template}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _settings.SprintName = sprintName;
            _settings.MemberName = memberName;
            _settings.ProjectName = projectName;
            _settings.OpenAfterGenerate = chkOpenAfterGenerate.Checked;
            _store.Save(_settings);

            SetGenerating(true);
            SetJiraStatus("Jira: Fetching sprint...", Color.DodgerBlue);

            try
            {
                SprintSearchResult result;
                using (var cts = new CancellationTokenSource(JiraTimeout))
                using (var client = CreateClient())
                {
                    result = await client.SearchIssuesByProjectAndSprintAsync(
                        projectName, sprintName, IssueTypes.ReportTypes, cts.Token);
                }

                FillIssues(result.Issues);
                SetJiraStatus($"Jira: {result.Issues.Count} issues", result.Issues.Count > 0 ? Color.ForestGreen : Color.Firebrick);

                // An empty report is almost always a wrong project/sprint name — don't produce it silently
                if (result.Issues.Count == 0 &&
                    MessageBox.Show($"No Bug/Improvement/Story issues were found for \"{projectName}\" / \"{sprintName}\".\n" +
                                    "Generate an empty report anyway?",
                        "No Issues", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;

                var data = new TemplateData
                {
                    ReportDate = DateTime.Today.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                    ProjectName = projectName,
                    MemberName = memberName,
                    SprintName = sprintName,
                    SprintStartDate = result.Sprint?.Start?.ToString("d MMMM yyyy", Turkish) ?? string.Empty,
                    SprintEndDate = result.Sprint?.End?.ToString("d MMMM yyyy", Turkish) ?? string.Empty
                };

                string output = _report.Generate(template, data, result.Issues);

                MessageBox.Show($"Report created successfully:\n{output}", "Success",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);

                if (chkOpenAfterGenerate.Checked && File.Exists(output))
                    Process.Start(new ProcessStartInfo { FileName = output, UseShellExecute = true });
            }
            catch (TaskCanceledException)
            {
                SetJiraStatus("Jira: timeout", Color.OrangeRed);
                MessageBox.Show("Jira did not answer in time. Please try again.", "Timeout",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (HttpRequestException ex)
            {
                SetJiraStatus("Jira: network/SSL error", Color.IndianRed);
                MessageBox.Show($"Network/SSL error:\n{ex.Message}", "Network Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (IOException ioEx)
            {
                MessageBox.Show($"File error (is the report already open in Word?):\n{ioEx.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unexpected error:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetGenerating(false);
            }
        }

        // =========================
        // Auto-preview
        // =========================

        private void SetJiraStatus(string text, Color color)
        {
            tslJira.Text = text;
            tslJira.ForeColor = color;
        }

        /// <summary>
        /// While a report is generated: stop/cancel the auto-preview and lock the inputs,
        /// so a preview can neither start nor hide the progress bar in the middle of it.
        /// </summary>
        private void SetGenerating(bool busy)
        {
            if (busy)
            {
                _autoPreviewTimer.Stop();
                _previewCts?.Cancel();
                _previewVersion++; // a preview that is still running must not touch the UI anymore
            }

            btnGenerate.Enabled = !busy;
            txtProjectName.ReadOnly = busy;
            txtSprintName.ReadOnly = busy;
            txtMemberName.ReadOnly = busy;
            ShowProgress(busy);
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

        private void FillIssues(IEnumerable<JiraIssue> issues)
        {
            issueList.BeginUpdate();
            try
            {
                issueList.Items.Clear();
                foreach (var issue in issues)
                    issueList.Items.Add(new ListViewItem(new[] { issue.Type, issue.Key }));

                FitColumns();
            }
            finally
            {
                issueList.EndUpdate();
            }
        }

        /// <summary>
        /// Sizes every column to max(header, content). (ColumnContent alone collapses
        /// columns to almost zero width when the list is empty or the values are short.)
        /// </summary>
        private void FitColumns()
        {
            foreach (ColumnHeader column in issueList.Columns)
            {
                column.AutoResize(ColumnHeaderAutoResizeStyle.HeaderSize);
                int headerWidth = column.Width;
                if (issueList.Items.Count > 0)
                {
                    column.AutoResize(ColumnHeaderAutoResizeStyle.ColumnContent);
                    column.Width = Math.Max(column.Width, headerWidth);
                }
            }
        }

        private async Task LoadIssuesAsync()
        {
            // Supersede any running preview; only the latest one may touch the UI
            _previewCts?.Cancel();
            _previewCts?.Dispose();
            _previewCts = new CancellationTokenSource(JiraTimeout);
            var token = _previewCts.Token;
            int version = ++_previewVersion;
            ShowProgress(false);

            if (string.IsNullOrWhiteSpace(ProjectNameText))
            {
                FillIssues(Array.Empty<JiraIssue>());
                SetJiraStatus("Jira: waiting for Project...", Color.DodgerBlue);
                return;
            }

            if (!HasCredentials)
            {
                FillIssues(Array.Empty<JiraIssue>());
                SetJiraStatus("Jira: Missing credentials", Color.OrangeRed);
                return;
            }

            ShowProgress(true);
            FillIssues(Array.Empty<JiraIssue>());
            SetJiraStatus("Jira: Loading issues...", Color.DodgerBlue);

            try
            {
                IReadOnlyList<JiraIssue> issues;
                using (var client = CreateClient())
                {
                    issues = string.IsNullOrWhiteSpace(SprintNameText)
                        ? await client.SearchIssuesByProjectAsync(ProjectNameText, take: 50, ct: token)
                        : (await client.SearchIssuesByProjectAndSprintAsync(
                              ProjectNameText, SprintNameText, IssueTypes.ReportTypes, token)).Issues;
                }

                if (version != _previewVersion) return;

                FillIssues(issues);
                SetJiraStatus($"Jira: {issues.Count} issues", issues.Count > 0 ? Color.ForestGreen : Color.Firebrick);
            }
            catch (OperationCanceledException) when (version != _previewVersion)
            {
                // superseded by a newer preview – nothing to report
            }
            catch (OperationCanceledException)
            {
                SetJiraStatus("Jira: timeout", Color.OrangeRed);
            }
            catch (HttpRequestException ex)
            {
                if (version != _previewVersion) return;
                SetJiraStatus("Jira: network/SSL error", Color.IndianRed);
                MessageBox.Show($"Network/SSL error:\n{ex.Message}", "Network Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                if (version != _previewVersion) return;
                SetJiraStatus("Jira: error", Color.IndianRed);
                MessageBox.Show($"Unexpected error:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (version == _previewVersion) ShowProgress(false);
            }
        }
    }
}
