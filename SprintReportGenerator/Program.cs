using SprintReportGenerator.Forms;

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

            // The secret is handed over in memory: it is only persisted when "Remember me" is checked.
            Application.Run(new MainForm(login.Settings, login.Secret));
        }
    }
}
