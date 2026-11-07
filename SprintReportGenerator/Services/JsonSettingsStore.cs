using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SprintReportGenerator.Models;
using SprintReportGenerator.Services.Interfaces;

namespace SprintReportGenerator.Services
{
    public sealed class JsonSettingsStore : ISettingsStore
    {
        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
        private readonly string _settingsPath;

        public JsonSettingsStore()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "SprintReportGenerator");
            Directory.CreateDirectory(dir);
            _settingsPath = Path.Combine(dir, "settings.json");
        }

        public AppSettings Load()
        {
            if (!File.Exists(_settingsPath)) return new AppSettings();
            try
            {
                var json = File.ReadAllText(_settingsPath, Encoding.UTF8);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // A corrupt or locked settings file must not prevent the app from starting
                return new AppSettings();
            }
        }

        public void Save(AppSettings settings)
        {
            var json = JsonSerializer.Serialize(settings, WriteOptions);
            File.WriteAllText(_settingsPath, json, Encoding.UTF8);
        }

        public string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return string.Empty;
            var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        public string Unprotect(string cipher)
        {
            if (string.IsNullOrEmpty(cipher)) return string.Empty;
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(cipher), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                return string.Empty;
            }
        }
    }
}
