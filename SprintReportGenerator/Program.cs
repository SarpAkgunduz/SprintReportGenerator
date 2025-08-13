using System;
using System.Windows.Forms;
using SprintReportGenerator.Forms;
using SprintReportGenerator.Models;
using SprintReportGenerator.Services;

namespace SprintReportGenerator
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();

            var store = new JsonSettingsStore();
            var settings = store.Load();

            bool canSkipLogin = settings.RememberMe
                                && !string.IsNullOrWhiteSpace(settings.Email);

            if (!canSkipLogin)
            {
                using var login = new LoginForm();
                var result = login.ShowDialog();
                if (result != DialogResult.OK) return;

                // LoginForm içinde settings.json güncellendi
                settings = store.Load();
            }

            Application.Run(new MainForm(settings));
        }
    }
}
