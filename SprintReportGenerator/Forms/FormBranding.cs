namespace SprintReportGenerator.Forms
{
    internal static class FormBranding
    {
        public const string AppTitle = "Open Sprint Generator";

        /// <summary>Sets the common window title and the executable's icon.</summary>
        public static void Apply(Form form)
        {
            form.Text = AppTitle;
            try { form.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { /* icon is cosmetic */ }
        }
    }
}
