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
        
            using var login = new LoginForm();
            if (login.ShowDialog() != DialogResult.OK)
                return;

            // Load settings after login
            var store = new JsonSettingsStore();
            var settings = store.Load();

            Application.Run(new MainForm(settings));
        }
    }
}
