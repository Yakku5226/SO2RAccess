using System;
using System.IO;
using System.Text.Json;
using MelonLoader;

namespace SO2RAccess
{
    /// <summary>
    /// Controls how dialogue is announced when voice acting is present.
    /// </summary>
    public enum DialogueVoiceMode
    {
        /// <summary>Always read speaker name and full text.</summary>
        Full = 0,
        /// <summary>Read only the speaker name when the line is voiced.</summary>
        NameOnlyWhenVoiced = 1
    }

    /// <summary>
    /// Where NPCs that currently have an active event (the red "!") appear in the
    /// navigation list. In crowded maps (castle, arena) they are hard to find among
    /// dozens of identically-named NPCs, so they can be surfaced in the Events
    /// category instead of, or in addition to, the NPCs category.
    /// </summary>
    public enum EventNpcDisplayMode
    {
        /// <summary>Only in the NPCs category (tagged "(event)").</summary>
        NpcList = 0,
        /// <summary>Only in the Events category.</summary>
        EventsList = 1,
        /// <summary>In both the NPCs and the Events categories.</summary>
        Both = 2
    }

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

        /// <summary>How dialogue is announced when voice acting is present.</summary>
        public static DialogueVoiceMode DialogueVoiceMode { get; set; } = DialogueVoiceMode.Full;

        /// <summary>Whether ally health warnings (below 50%, below 25%, KO) are announced in battle.</summary>
        public static bool AllyHealthWarningEnabled { get; set; } = true;

        /// <summary>Whether ally negative status ailments are announced in battle.</summary>
        public static bool AllyStatusAilmentEnabled { get; set; } = true;

        /// <summary>Whether damage dealt by the player-controlled character is announced.</summary>
        public static bool PlayerDamageDealtEnabled { get; set; } = true;

        /// <summary>Volume of the private action notification sound (0.0 to 1.0). 0 = off.</summary>
        public static float PrivateActionSoundVolume { get; set; } = 0.7f;

        /// <summary>Volume of the bonus gauge fill sound (0.0 to 1.0). 0 = off.</summary>
        public static float BonusGaugeSoundVolume { get; set; } = 0.7f;

        /// <summary>Whether the bonus gauge break level/buff announcement is enabled.</summary>
        public static bool BonusGaugeBreakAnnouncementEnabled { get; set; } = true;

        /// <summary>
        /// Whether the bonus gauge fill percentage is spoken (every 5%) as it rises.
        /// Independent of the beep cue. Default off.
        /// </summary>
        public static bool BonusGaugePercentAnnounceEnabled { get; set; } = false;

        /// <summary>
        /// Whether the jump-prompt audio cue plays when the "press X to jump down"
        /// prompt appears above the player at a one-way ledge.
        /// </summary>
        public static bool JumpPromptSoundEnabled { get; set; } = true;

        /// <summary>Volume of the jump-prompt audio cue (0.0 to 1.0).</summary>
        public static float JumpPromptSoundVolume { get; set; } = 0.8f;

        /// <summary>
        /// Whether the jump prompt is spoken once via the screen reader when it
        /// appears. Independent of the audio cue — either, both, or neither.
        /// </summary>
        public static bool JumpPromptSpeechEnabled { get; set; } = true;

        /// <summary>
        /// Whether the soft spatial-awareness walk assist is enabled. When on, the
        /// auto-walk heading is gently nudged around nearby NPCs/clutter so the
        /// player gets stuck less often. The nudge is hard-capped in angle and
        /// never changes the destination. See <see cref="SpatialSensor"/>.
        /// </summary>
        public static bool WalkAssistEnabled { get; set; } = true;

        /// <summary>
        /// Where event-carrying NPCs (the red "!") appear in the navigation list:
        /// the NPCs category, the Events category, or both. Default Both so they are
        /// easy to find in crowded maps without disappearing from the usual NPC list.
        /// </summary>
        public static EventNpcDisplayMode EventNpcDisplay { get; set; } = EventNpcDisplayMode.Both;

        /// <summary>
        /// Whether field auto-walk carves nearby standing NPCs into the NavMesh so the
        /// game's own pathfinder routes around crowds (castle/arena). When off, auto-walk
        /// behaves as before (NavMesh-only path + reactive walk-assist/detour). See
        /// <see cref="NavMeshCarverPool"/>.
        /// </summary>
        public static bool NpcAwarePathfindingEnabled { get; set; } = true;

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
                    DialogueVoiceMode = Enum.IsDefined(typeof(DialogueVoiceMode), data.DialogueVoiceMode)
                        ? (DialogueVoiceMode)data.DialogueVoiceMode
                        : DialogueVoiceMode.Full;
                    AllyHealthWarningEnabled = data.AllyHealthWarningEnabled;
                    AllyStatusAilmentEnabled = data.AllyStatusAilmentEnabled;
                    PlayerDamageDealtEnabled = data.PlayerDamageDealtEnabled;
                    PrivateActionSoundVolume = Math.Clamp(data.PrivateActionSoundVolume, 0f, 1f);
                    BonusGaugeSoundVolume = Math.Clamp(data.BonusGaugeSoundVolume, 0f, 1f);
                    BonusGaugeBreakAnnouncementEnabled = data.BonusGaugeBreakAnnouncementEnabled;
                    BonusGaugePercentAnnounceEnabled = data.BonusGaugePercentAnnounceEnabled;
                    JumpPromptSoundEnabled = data.JumpPromptSoundEnabled;
                    JumpPromptSoundVolume = Math.Clamp(data.JumpPromptSoundVolume, 0f, 1f);
                    JumpPromptSpeechEnabled = data.JumpPromptSpeechEnabled;
                    WalkAssistEnabled = data.WalkAssistEnabled;
                    EventNpcDisplay = Enum.IsDefined(typeof(EventNpcDisplayMode), data.EventNpcDisplay)
                        ? (EventNpcDisplayMode)data.EventNpcDisplay
                        : EventNpcDisplayMode.Both;
                    NpcAwarePathfindingEnabled = data.NpcAwarePathfindingEnabled;
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
                    EnemyProximitySoundVolume = EnemyProximitySoundVolume,
                    DialogueVoiceMode = (int)DialogueVoiceMode,
                    AllyHealthWarningEnabled = AllyHealthWarningEnabled,
                    AllyStatusAilmentEnabled = AllyStatusAilmentEnabled,
                    PlayerDamageDealtEnabled = PlayerDamageDealtEnabled,
                    PrivateActionSoundVolume = PrivateActionSoundVolume,
                    BonusGaugeSoundVolume = BonusGaugeSoundVolume,
                    BonusGaugeBreakAnnouncementEnabled = BonusGaugeBreakAnnouncementEnabled,
                    BonusGaugePercentAnnounceEnabled = BonusGaugePercentAnnounceEnabled,
                    JumpPromptSoundEnabled = JumpPromptSoundEnabled,
                    JumpPromptSoundVolume = JumpPromptSoundVolume,
                    JumpPromptSpeechEnabled = JumpPromptSpeechEnabled,
                    WalkAssistEnabled = WalkAssistEnabled,
                    EventNpcDisplay = (int)EventNpcDisplay,
                    NpcAwarePathfindingEnabled = NpcAwarePathfindingEnabled
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
            public int DialogueVoiceMode { get; set; } = 0;
            public bool AllyHealthWarningEnabled { get; set; } = true;
            public bool AllyStatusAilmentEnabled { get; set; } = true;
            public bool PlayerDamageDealtEnabled { get; set; } = true;
            public float PrivateActionSoundVolume { get; set; } = 0.7f;
            public float BonusGaugeSoundVolume { get; set; } = 0.7f;
            public bool BonusGaugeBreakAnnouncementEnabled { get; set; } = true;
            public bool BonusGaugePercentAnnounceEnabled { get; set; } = false;
            public bool JumpPromptSoundEnabled { get; set; } = true;
            public float JumpPromptSoundVolume { get; set; } = 0.8f;
            public bool JumpPromptSpeechEnabled { get; set; } = true;
            public bool WalkAssistEnabled { get; set; } = true;
            public int EventNpcDisplay { get; set; } = (int)EventNpcDisplayMode.Both;
            public bool NpcAwarePathfindingEnabled { get; set; } = true;
        }

        #endregion
    }
}
