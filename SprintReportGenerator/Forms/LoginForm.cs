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

            // Load settings
            LoadSettings();

            // Set form title and icon
            const string AppTitle = "Open Sprint Generator";
            this.Text = AppTitle;
            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* ignore */ }
        }

        private void LoadSettings()
        {
            _settings = _store.Load();

            txtEmail.Text = _settings.Email ?? string.Empty;
            txtJiraUrl.Text = _settings.JiraUrl ?? string.Empty;
            chkRememberMe.Checked = _settings.RememberMe;

            // decrypt for UI (istersen masked bırak)
            var plainToken = _store.Unprotect(_settings.EncApiToken);
            txtApiToken.Text = plainToken ?? string.Empty;
            // txtApiToken.UseSystemPasswordChar = true;
        }

        private void btnContinue_Click(object sender, EventArgs e)
        {
            // 1) Validation (SERVİSİ DOĞRUDAN KULLAN)
            var email = txtEmail.Text?.Trim();
            var jira = txtJiraUrl.Text?.Trim();

            var (ok, error) = ValidationService.ValidateLoginFormInputs(email, jira);
            if (!ok)
            {
                MessageBox.Show(error, "Validation Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 2) Save settings
            _settings.Email = email ?? string.Empty;
            _settings.JiraUrl = (jira ?? string.Empty).TrimEnd('/'); // normalize: sondaki '/' gider
            _settings.RememberMe = chkRememberMe.Checked;

            // 3) Encrypt and store token
            var plain = txtApiToken.Text?.Trim() ?? string.Empty;
            _settings.EncApiToken = _store.Protect(plain);
            plain = string.Empty; // (opsiyonel) hafızayı temizle

            _store.Save(_settings);

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
