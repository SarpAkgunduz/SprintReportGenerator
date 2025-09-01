namespace SprintReportGenerator.Services.Interfaces
{
    public interface ISettingsStore
    {
        SprintReportGenerator.Models.AppSettings Load();
        void Save(SprintReportGenerator.Models.AppSettings settings);

        // DPAPI helpers
        string Protect(string plain);
        string Unprotect(string cipher);
    }
}
