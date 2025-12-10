using SprintReportGenerator.Models;
using SprintReportGenerator.Services;
using SprintReportGenerator.Services.Interfaces;
using System;
using System.Windows.Forms;


namespace SprintReportGenerator.Forms
{
    public partial class LoginForm : Form
    {
        private readonly JsonSettingsStore _store = new JsonSettingsStore();
        private AppSettings _settings = new AppSettings();

        public LoginForm()
        {
            InitializeComponent();
            LoadSettings();

            const string AppTitle = "Open Sprint Generator";
            this.Text = AppTitle;
            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* ignore */ }
        }

        private void LoadSettings()
        {
            _settings = _store.Load();

            txtUserName.Text = _settings.UserName ?? string.Empty;  // ✅ TextBox adı değişmedi (UI component)
            txtJiraUrl.Text = _settings.JiraUrl ?? string.Empty;
            chkRememberMe.Checked = _settings.RememberMe;

            // restore mode and credential
            chkIsBasic.Checked = _settings.IsBasic;

            var plain = _store.Unprotect(_settings.EncSecret);
            txtSecret.Text = plain ?? string.Empty;
        }

        // Change the event handler to be async
        private async void btnContinue_Click(object sender, EventArgs e)
        {
            var username = txtUserName.Text?.Trim();  // ✅ email → username
            var jira = txtJiraUrl.Text?.Trim();

            // 2) basic validation
            var (ok, error) = ValidationService.ValidateLoginFormInputs(username, jira);  // ✅ email → username
            if (!ok)
            {
                MessageBox.Show(error, "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            // 3) Take credential from the single textbox (token or password)
            var secretRaw = txtSecret.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(secretRaw))
            {
                MessageBox.Show("Please enter your API token or password.",
                    "Missing Credential", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 4) Optional guard: Jira Cloud does not accept password-based Basic Auth
            bool isCloud = (jira ?? string.Empty).IndexOf("atlassian.net", StringComparison.OrdinalIgnoreCase) >= 0;
            if (chkIsBasic.Checked && isCloud)
            {
                MessageBox.Show(
                    "Jira Cloud requires an API token. Password-based Basic Auth is not supported.",
                    "Unsupported Mode", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 5) Online authentication check (block MainForm unless it succeeds)
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var client = new JiraClient(
                    (jira ?? string.Empty).TrimEnd('/'), 
                    username!,  // ✅ email → username
                    secretRaw, 
                    useBasicAuth: chkIsBasic.Checked  
                );

                var authResult = await client.ValidateAsync(cts.Token).ConfigureAwait(true);
                if (!authResult.success)
                {
                    MessageBox.Show(
                        chkIsBasic.Checked
                            ? $"Authentication failed with password.{(string.IsNullOrEmpty(authResult.errorMessage) ? "" : "\n" + authResult.errorMessage)}"
                            : $"Authentication failed with API token.{(string.IsNullOrEmpty(authResult.errorMessage) ? "" : "\n" + authResult.errorMessage)}",
                        "Authentication Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                MessageBox.Show($"Network/SSL error:\n{ex.Message}", "Network Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            catch (TaskCanceledException)
            {
                MessageBox.Show("Connection timed out. Please check URL/network and try again.",
                    "Timeout", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unexpected error:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _settings.UserName = username ?? string.Empty;  // ✅ email → username (kayıt için)
            _settings.JiraUrl = (jira ?? string.Empty).TrimEnd('/');
            _settings.RememberMe = chkRememberMe.Checked;
            _settings.IsBasic = chkIsBasic.Checked;

            if (_settings.RememberMe)
            {
                var raw = txtSecret.Text?.Trim() ?? string.Empty;
                _settings.EncSecret = _store.Protect(raw);
            }
            else
            {
                _settings.EncSecret = string.Empty;
            }

            _store.Save(_settings);

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
