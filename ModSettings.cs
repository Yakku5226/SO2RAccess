using System;
using System.IO;
using System.Text.Json;
using MelonLoader;

namespace SO2RAccess
{
    /// <summary>
    /// Persistent mod settings. Loads from and saves to a JSON file
    /// in UserData/SO2RAccess/settings.json. Settings are exposed as
    /// static properties for easy access from handlers and the future
    /// mod settings menu.
    /// </summary>
    public static class ModSettings
    {
        #region Settings Properties

        /// <summary>Whether to play the save sound when the game auto-saves.</summary>
        public static bool SaveSoundEnabled { get; set; } = true;

        /// <summary>Volume of the save sound (0.0 to 1.0).</summary>
        public static float SaveSoundVolume { get; set; } = 0.5f;

        /// <summary>Whether the dodge warning audio cue is enabled.</summary>
        public static bool DodgeSoundEnabled { get; set; } = true;

        /// <summary>Volume of the dodge warning audio cue (0.0 to 1.0).</summary>
        public static float DodgeSoundVolume { get; set; } = 0.8f;

        /// <summary>Whether the enemy proximity audio cue is enabled.</summary>
        public static bool EnemyProximitySoundEnabled { get; set; } = true;

        /// <summary>Volume of the enemy proximity audio cue (0.0 to 1.0).</summary>
        public static float EnemyProximitySoundVolume { get; set; } = 1.0f;

        #endregion

        #region Persistence

        private static string _settingsPath;

        /// <summary>
        /// Loads settings from disk. Call once at mod startup.
        /// Creates the settings file with defaults if it doesn't exist.
        /// </summary>
        public static void Load()
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(),
                "UserData", "SO2RAccess");
            Directory.CreateDirectory(dir);
            _settingsPath = Path.Combine(dir, "settings.json");

            if (!File.Exists(_settingsPath))
            {
                Save();
                MelonLogger.Msg("ModSettings: created default settings file.");
                return;
            }

            try
            {
                string json = File.ReadAllText(_settingsPath);
                var data = JsonSerializer.Deserialize<SettingsData>(json);
                if (data != null)
                {
                    SaveSoundEnabled = data.SaveSoundEnabled;
                    SaveSoundVolume = Math.Clamp(data.SaveSoundVolume, 0f, 1f);
                    DodgeSoundEnabled = data.DodgeSoundEnabled;
                    DodgeSoundVolume = Math.Clamp(data.DodgeSoundVolume, 0f, 1f);
                    EnemyProximitySoundEnabled = data.EnemyProximitySoundEnabled;
                    EnemyProximitySoundVolume = Math.Clamp(data.EnemyProximitySoundVolume, 0f, 1f);
                }
                MelonLogger.Msg("ModSettings: loaded.");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"ModSettings.Load failed, using defaults: {ex.Message}");
            }
        }

        /// <summary>
        /// Saves current settings to disk.
        /// </summary>
        public static void Save()
        {
            if (string.IsNullOrEmpty(_settingsPath)) return;

            try
            {
                var data = new SettingsData
                {
                    SaveSoundEnabled = SaveSoundEnabled,
                    SaveSoundVolume = SaveSoundVolume,
                    DodgeSoundEnabled = DodgeSoundEnabled,
                    DodgeSoundVolume = DodgeSoundVolume,
                    EnemyProximitySoundEnabled = EnemyProximitySoundEnabled,
                    EnemyProximitySoundVolume = EnemyProximitySoundVolume
                };

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(data, options);
                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"ModSettings.Save failed: {ex.Message}");
            }
        }

        #endregion

        #region Data Class

        private class SettingsData
        {
            public bool SaveSoundEnabled { get; set; } = true;
            public float SaveSoundVolume { get; set; } = 0.5f;
            public bool DodgeSoundEnabled { get; set; } = true;
            public float DodgeSoundVolume { get; set; } = 0.8f;
            public bool EnemyProximitySoundEnabled { get; set; } = true;
            public float EnemyProximitySoundVolume { get; set; } = 1.0f;
        }

        #endregion
    }
}
