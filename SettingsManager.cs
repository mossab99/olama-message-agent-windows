using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace OlamaMessageAgent
{
    public class AgentSettings
    {
        public string SiteUrl { get; set; } = "http://olama3.local";
        public string AgentUuid { get; set; } = string.Empty;
        public string ProtectedApiKeyBase64 { get; set; } = string.Empty;
        public int HeartbeatIntervalSeconds { get; set; } = 60;
        public string KdeCliPath { get; set; } = @"C:\Program Files\KDE Connect\bin\kdeconnect-cli.exe";
        public string KdeDeviceId { get; set; } = string.Empty;
        public bool ReportDryRunFailures { get; set; } = false;
        public bool DispatcherEnabled { get; set; } = true;

        // Run-time helper (not saved directly to JSON as plain text)
        [ScriptIgnore]
        public string ApiKey
        {
            get => SettingsManager.DecryptKey(ProtectedApiKeyBase64);
            set => ProtectedApiKeyBase64 = SettingsManager.EncryptKey(value);
        }
    }

    public static class SettingsManager
    {
        private static readonly string SettingsDir;
        private static readonly string SettingsFilePath;
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        static SettingsManager()
        {
            string envDir = Environment.GetEnvironmentVariable("OLAMA_AGENT_SETTINGS_DIR");
            if (!string.IsNullOrEmpty(envDir))
            {
                SettingsDir = envDir;
            }
            else
            {
                SettingsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OlamaMessageAgent"
                );
            }
            SettingsFilePath = Path.Combine(SettingsDir, "settings.json");
        }

        public static AgentSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath, Encoding.UTF8);
                    var loaded = Serializer.Deserialize<AgentSettings>(json);
                    if (loaded != null)
                    {
                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to load settings file. Reverting to defaults.", ex);
            }

            return new AgentSettings();
        }

        public static bool SaveSettings(AgentSettings settings)
        {
            try
            {
                if (!Directory.Exists(SettingsDir))
                {
                    Directory.CreateDirectory(SettingsDir);
                }

                string json = Serializer.Serialize(settings);
                File.WriteAllText(SettingsFilePath, json, Encoding.UTF8);
                Logger.Info("Agent settings successfully saved to disk.");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to save settings to disk.", ex);
                return false;
            }
        }

        public static string GetDataFilePath(string fileName)
        {
            if (!Directory.Exists(SettingsDir))
            {
                Directory.CreateDirectory(SettingsDir);
            }
            return Path.Combine(SettingsDir, fileName);
        }

        public static string EncryptKey(string rawKey)
        {
            if (string.IsNullOrEmpty(rawKey)) return string.Empty;
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(rawKey);
                byte[] encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encrypted);
            }
            catch (Exception ex)
            {
                Logger.Error("DPAPI Encryption failed.", ex);
                return string.Empty;
            }
        }

        public static string DecryptKey(string base64Encrypted)
        {
            if (string.IsNullOrEmpty(base64Encrypted)) return string.Empty;
            if (base64Encrypted.StartsWith("olama_agent_"))
            {
                return base64Encrypted;
            }
            try
            {
                byte[] encrypted = Convert.FromBase64String(base64Encrypted);
                byte[] decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch (Exception ex)
            {
                Logger.Error("DPAPI Decryption failed. API Key could not be read.", ex);
                // Fallback: if input looks like a raw key, return it
                if (base64Encrypted.StartsWith("olama_agent_"))
                {
                    return base64Encrypted;
                }
                return string.Empty;
            }
        }
    }
}
