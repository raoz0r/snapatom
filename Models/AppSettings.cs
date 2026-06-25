using System;
using System.IO;
using System.Text.Json;

namespace SnapAtom
{
    public class AppSettings
    {
        private static readonly string SettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

        public string GeminiApiKey { get; set; } = string.Empty;
        public string GeminiModelName { get; set; } = string.Empty;
        public string ClippingsSavePath { get; set; } = @"E:\ghost\documents\argus\04-raw\clippings";

        public string AiProvider { get; set; } = "Google AI Studio";
        public string AiApiKey { get; set; } = string.Empty;
        public string AiModelName { get; set; } = "gemini-2.5-flash";
        public string CustomEndpoint { get; set; } = "http://localhost:11434/v1/chat/completions";

        public System.Collections.Generic.List<MetadataItem> CustomMetadata { get; set; } = new System.Collections.Generic.List<MetadataItem>();

        // Hotkeys configuration (Main Virtual Key code + Modifiers)
        // Modifiers: Alt=1, Control=2, Shift=4, Win=8, NoRepeat=0x4000
        // Virtual Key Codes: S=0x53 (83), D=0x44 (68)
        public uint StartGrabKey { get; set; } = 0x53; // VK_S
        public uint StartGrabModifiers { get; set; } = 0x0008; // Win

        public uint ProcessBatchKey { get; set; } = 0x53; // VK_S
        public uint ProcessBatchModifiers { get; set; } = 0x0008 | 0x0004; // Win + Shift

        public uint CopySelectedKey { get; set; } = 0x44; // VK_D
        public uint CopySelectedModifiers { get; set; } = 0x0008; // Win

        /// <summary>
        /// Loads the settings from settings.json. If it does not exist, performs migration or returns defaults.
        /// </summary>
        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        // Migrate legacy settings to new fields
                        if (string.IsNullOrEmpty(settings.AiApiKey) && !string.IsNullOrEmpty(settings.GeminiApiKey))
                        {
                            settings.AiApiKey = settings.GeminiApiKey;
                        }
                        if (string.IsNullOrEmpty(settings.AiModelName) && !string.IsNullOrEmpty(settings.GeminiModelName))
                        {
                            settings.AiModelName = settings.GeminiModelName;
                        }
                        else if (string.IsNullOrEmpty(settings.AiModelName))
                        {
                            settings.AiModelName = "gemini-2.5-flash";
                        }
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings.json: {ex.Message}");
            }

            // If file does not exist, try to migrate from .env
            var newSettings = new AppSettings();
            newSettings.MigrateFromEnv();
            newSettings.Save();
            return newSettings;
        }

        /// <summary>
        /// Saves the settings to settings.json.
        /// </summary>
        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(this, JsonOptions);
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings.json: {ex.Message}");
            }
        }

        private void MigrateFromEnv()
        {
            string envPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env");
            if (File.Exists(envPath))
            {
                try
                {
                    string[] lines = File.ReadAllLines(envPath);
                    foreach (string line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                            continue;

                        int equalIndex = line.IndexOf('=');
                        if (equalIndex > 0)
                        {
                            string key = line.Substring(0, equalIndex).Trim();
                            string val = line.Substring(equalIndex + 1).Trim();

                            if (key == "GEMINI_API_KEY")
                            {
                                GeminiApiKey = val;
                                AiApiKey = val;
                            }
                            else if (key == "GEMINI_MODEL_NAME")
                            {
                                GeminiModelName = val;
                                AiModelName = val;
                            }
                        }
                    }
                    System.Diagnostics.Debug.WriteLine("Successfully migrated settings from .env file.");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to migrate .env: {ex.Message}");
                }
            }
        }
    }

    public class MetadataItem
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
