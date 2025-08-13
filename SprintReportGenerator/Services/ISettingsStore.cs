using SprintReportGenerator.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System;
using System.IO;


namespace SprintReportGenerator.Services
{
    public interface ISettingsStore
    {
        AppSettings Load();
        void Save(AppSettings settings);
        string Protect(string plain);     // encrypt token
        string Unprotect(string cipher);  // decrypt token
    }
    public sealed class JsonSettingsStore : ISettingsStore
    {
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
            var json = File.ReadAllText(_settingsPath, Encoding.UTF8);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }

        public void Save(AppSettings settings)
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json, Encoding.UTF8);
        }

        // DPAPI (user scope): simple local encryption for token
        public string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return string.Empty;
            var bytes = Encoding.UTF8.GetBytes(plain);
            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        public string Unprotect(string cipher)
        {
            if (string.IsNullOrEmpty(cipher)) return string.Empty;
            try
            {
                var data = Convert.FromBase64String(cipher);
                var bytes = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch { return string.Empty; }
        }
    }
}
