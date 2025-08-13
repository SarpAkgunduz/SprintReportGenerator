using SprintReportGenerator.Models;
using SprintReportGenerator.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SprintReportGenerator.Forms
{
    public partial class LoginForm : Form
    {
        private readonly JsonSettingsStore _store = new JsonSettingsStore();

        public LoginForm()
        {
            InitializeComponent();
            LoadEmailFromSettings();
        }

        private void LoadEmailFromSettings()
        {
            AppSettings s = _store.Load();

            // Only prefill when RememberMe is true
            chkRemember.Checked = s.RememberMe;
            txtEmail.Text = s.RememberMe ? (s.Email ?? string.Empty) : string.Empty;
            txtEmail.Focus();
        }

        private void btnContinue_Click(object sender, EventArgs e)
        {
            AppSettings s = _store.Load();

            if (chkRemember.Checked)
            {
                s.Email = txtEmail.Text?.Trim() ?? string.Empty;
                s.RememberMe = true;
            }
            else
            {
                // Clear remembered email if user doesn’t want to remember
                s.Email = string.Empty;
                s.RememberMe = false;
            }

            _store.Save(s);

            this.DialogResult = DialogResult.OK;
            this.Close();
        }


        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {

        }
    }
}