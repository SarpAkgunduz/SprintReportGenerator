using SprintReportGenerator.Models;
using SprintReportGenerator.Services;

namespace SprintReportGenerator.Forms
{
    public partial class LoginForm : Form
    {
        private readonly JsonSettingsStore _store = new JsonSettingsStore();

        /// <summary>Settings saved after a successful login.</summary>
        public AppSettings Settings { get; private set; } = new AppSettings();

        /// <summary>Plain password / token of the successful login (kept in memory only).</summary>
        public string Secret { get; private set; } = string.Empty;

        public LoginForm()
        {
            InitializeComponent();
            FormBranding.Apply(this);
            LoadSettings();
        }

        private void LoadSettings()
        {
            Settings = _store.Load();

            txtUserName.Text = Settings.UserName;
            txtJiraUrl.Text = Settings.JiraUrl;
            chkRememberMe.Checked = Settings.RememberMe;
            chkIsBasic.Checked = Settings.IsBasic;
            txtSecret.Text = _store.Unprotect(Settings.EncSecret);
        }

        private async void btnContinue_Click(object sender, EventArgs e)
        {
            var userName = txtUserName.Text.Trim();
            var jiraUrl = txtJiraUrl.Text.Trim().TrimEnd('/');
            var secret = txtSecret.Text.Trim();
            bool passwordMode = chkIsBasic.Checked;

            var (ok, error) = ValidationService.ValidateLoginFormInputs(userName, jiraUrl);
            if (!ok)
            {
                MessageBox.Show(error, "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (secret.Length == 0)
            {
                MessageBox.Show("Please enter your API token or password.",
                    "Missing Credential", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Jira Cloud does not accept password-based Basic Auth
            if (passwordMode && JiraClient.IsCloudUrl(jiraUrl))
            {
                MessageBox.Show("Jira Cloud requires an API token. Password-based Basic Auth is not supported.",
                    "Unsupported Mode", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Online authentication check (MainForm is blocked unless it succeeds)
            btnContinue.Enabled = false;
            UseWaitCursor = true;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var client = new JiraClient(jiraUrl, userName, secret,
                    useBasicAuth: JiraClient.ShouldUseBasicAuth(jiraUrl, passwordMode));

                var (success, errorMessage) = await client.ValidateAsync(cts.Token);
                if (!success)
                {
                    var mode = passwordMode ? "password" : "API token";
                    MessageBox.Show($"Authentication failed with {mode}." +
                                    (string.IsNullOrEmpty(errorMessage) ? "" : "\n" + errorMessage),
                        "Authentication Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            catch (HttpRequestException ex)
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
            finally
            {
                btnContinue.Enabled = true;
                UseWaitCursor = false;
            }

            Settings.UserName = userName;
            Settings.JiraUrl = jiraUrl;
            Settings.RememberMe = chkRememberMe.Checked;
            Settings.IsBasic = passwordMode;
            Settings.EncSecret = Settings.RememberMe ? _store.Protect(secret) : string.Empty;
            _store.Save(Settings);

            Secret = secret;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
