using System;
using System.Collections.Generic;

namespace SO2RAccess
{
    /// <summary>
    /// "Sound and announcements" submenu of the mod settings menu: every audio
    /// cue (on/off plus volume) followed by the spoken announcements that are
    /// not part of the language settings.
    ///
    /// Rows that control a sound carry a preview action, played with Space or
    /// the Square button, so a volume can be judged by ear without leaving the
    /// menu. Previews deliberately ignore the row's own on/off state — hearing
    /// a cue you have switched off is the point of a preview.
    /// </summary>
    public partial class ModMenuHandler
    {
        #region Preview State

        /// <summary>
        /// How long a looping cue plays when previewed. Loops have no natural end,
        /// unlike the one-shot cues, so the mixer stops the preview voice itself.
        /// </summary>
        private const float LoopPreviewSeconds = 1.5f;

        /// <summary>The loop preview currently sounding, if any.</summary>
        private MixerVoice _previewVoice;

        #endregion

        #region Screen

        /// <summary>Opens the sound and announcements submenu.</summary>
        private void OpenSoundScreen()
        {
            OpenSettingsSubmenu("mod_menu_sound_group_open", BuildSoundItems());
        }

        /// <summary>
        /// Builds the submenu rows: the audio cues first (each on/off row
        /// followed by its volume), then the spoken announcements.
        /// </summary>
        private List<ModMenuItem> BuildSoundItems()
        {
            var saveCue = Cue(() => AudioCuePlayer.IsSaveSoundLoaded, AudioCuePlayer.PlaySaveCue);
            var dodgeCue = Cue(() => AudioCuePlayer.IsDodgeSoundLoaded, AudioCuePlayer.PlayDodgeWarningCue);
            var paCue = Cue(() => AudioCuePlayer.IsPrivateActionSoundLoaded, AudioCuePlayer.PlayPrivateActionCue);
            var gaugeCue = Cue(() => AudioCuePlayer.IsGaugeFillSoundLoaded, AudioCuePlayer.PlayGaugeFillCue);
            var jumpCue = Cue(() => AudioCuePlayer.IsJumpSoundLoaded, AudioCuePlayer.PlayJumpCue);
            var fishCue = Cue(() => AudioCuePlayer.IsFishPromptSoundLoaded, AudioCuePlayer.PlayFishPromptCue);

            return new List<ModMenuItem>
            {
                Toggle("mod_menu_label_save_sound",
                    () => ModSettings.SaveSoundEnabled,
                    v => ModSettings.SaveSoundEnabled = v, saveCue),
                Volume("mod_menu_label_save_volume",
                    () => ModSettings.SaveSoundVolume,
                    v => ModSettings.SaveSoundVolume = v, saveCue),

                Toggle("mod_menu_label_dodge_sound",
                    () => ModSettings.DodgeSoundEnabled,
                    v => ModSettings.DodgeSoundEnabled = v, dodgeCue),
                Volume("mod_menu_label_dodge_volume",
                    () => ModSettings.DodgeSoundVolume,
                    v => ModSettings.DodgeSoundVolume = v, dodgeCue),

                Toggle("mod_menu_label_proximity_sound",
                    () => ModSettings.EnemyProximitySoundEnabled,
                    v => ModSettings.EnemyProximitySoundEnabled = v, PreviewProximitySound),
                Volume("mod_menu_label_proximity_volume",
                    () => ModSettings.EnemyProximitySoundVolume,
                    v => ModSettings.EnemyProximitySoundVolume = v, PreviewProximitySound),

                Volume("mod_menu_label_pa_volume",
                    () => ModSettings.PrivateActionSoundVolume,
                    v => ModSettings.PrivateActionSoundVolume = v, paCue),

                Volume("mod_menu_label_gauge_volume",
                    () => ModSettings.BonusGaugeSoundVolume,
                    v => ModSettings.BonusGaugeSoundVolume = v, gaugeCue),

                Toggle("mod_menu_label_jump_sound",
                    () => ModSettings.JumpPromptSoundEnabled,
                    v => ModSettings.JumpPromptSoundEnabled = v, jumpCue),
                Volume("mod_menu_label_jump_volume",
                    () => ModSettings.JumpPromptSoundVolume,
                    v => ModSettings.JumpPromptSoundVolume = v, jumpCue),

                Toggle("mod_menu_label_fish_sound",
                    () => ModSettings.FishPromptSoundEnabled,
                    v => ModSettings.FishPromptSoundEnabled = v, fishCue),
                Volume("mod_menu_label_fish_volume",
                    () => ModSettings.FishPromptSoundVolume,
                    v => ModSettings.FishPromptSoundVolume = v, fishCue),

                // Spoken announcements — no preview, they are screen reader output.
                Toggle("mod_menu_label_ally_health",
                    () => ModSettings.AllyHealthWarningEnabled,
                    v => ModSettings.AllyHealthWarningEnabled = v),
                Toggle("mod_menu_label_ally_ailment",
                    () => ModSettings.AllyStatusAilmentEnabled,
                    v => ModSettings.AllyStatusAilmentEnabled = v),
                Toggle("mod_menu_label_player_damage",
                    () => ModSettings.PlayerDamageDealtEnabled,
                    v => ModSettings.PlayerDamageDealtEnabled = v),
                Toggle("mod_menu_label_gauge_break_announce",
                    () => ModSettings.BonusGaugeBreakAnnouncementEnabled,
                    v => ModSettings.BonusGaugeBreakAnnouncementEnabled = v),
                Toggle("mod_menu_label_gauge_percent",
                    () => ModSettings.BonusGaugePercentAnnounceEnabled,
                    v => ModSettings.BonusGaugePercentAnnounceEnabled = v),
                Toggle("mod_menu_label_jump_speech",
                    () => ModSettings.JumpPromptSpeechEnabled,
                    v => ModSettings.JumpPromptSpeechEnabled = v),
                Toggle("mod_menu_label_enter_speech",
                    () => ModSettings.EnterPromptSpeechEnabled,
                    v => ModSettings.EnterPromptSpeechEnabled = v)
            };
        }

        /// <summary>
        /// Wraps a one-shot cue as a preview: plays it only if its WAV loaded,
        /// and reports back so a missing file is spoken rather than silent.
        /// </summary>
        private static Func<bool> Cue(Func<bool> isLoaded, Action play)
        {
            return () =>
            {
                if (!isLoaded()) return false;
                play();
                return true;
            };
        }

        #endregion

        #region Preview

        /// <summary>
        /// Space / Square: plays the focused row's sound at its current volume.
        /// Rows with no sound, and sounds whose WAV is missing, say so — silence
        /// alone would be indistinguishable from a broken preview.
        /// </summary>
        private void PreviewCurrent()
        {
            if (_items == null || _items.Count == 0) return;

            var item = _items[_currentIndex];
            if (item.Preview == null)
            {
                ScreenReader.Say(Loc.Get("mod_menu_no_preview"));
                return;
            }

            if (item.Preview())
            {
                DebugLogger.LogState($"ModMenu: previewed {item.LabelKey}.");
                return;
            }

            ScreenReader.Say(Loc.Get("mod_menu_preview_unavailable"));
            DebugLogger.LogState($"ModMenu: preview of {item.LabelKey} skipped, sound not loaded.");
        }

        /// <summary>
        /// Previews the enemy-proximity cue: centred and at full distance volume,
        /// so only the user's own volume setting is being judged.
        /// </summary>
        private bool PreviewProximitySound()
        {
            return PreviewLoop(EnemyProximityHandler.CueFile, ModSettings.EnemyProximitySoundVolume);
        }

        /// <summary>
        /// Plays a looping cue for <see cref="LoopPreviewSeconds"/> at the given
        /// volume, pan and muffle, replacing any preview already sounding. Returns
        /// false when the cue's WAV is missing so the menu can say so.
        /// </summary>
        private bool PreviewLoop(string cue, float volume, float pan = 0f, float muffle = 0f)
        {
            if (!LoopMixer.IsCueAvailable(cue)) return false;

            StopSoundPreview();
            _previewVoice = LoopMixer.Play(cue, volume, pan, phase01: 0f, autoStopSeconds: LoopPreviewSeconds);
            if (_previewVoice == null) return false;

            _previewVoice.Set(volume, pan, muffle);
            return true;
        }

        /// <summary>
        /// Silences a running preview. Called when leaving the submenu or closing
        /// the menu so a loop never escapes into normal play; the one-shot cues
        /// are short enough to be left alone.
        /// </summary>
        private void StopSoundPreview()
        {
            if (_previewVoice == null) return;

            _previewVoice.Stop();
            _previewVoice = null;
        }

        #endregion
    }
}
