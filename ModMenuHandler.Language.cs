using System.Collections.Generic;

namespace SO2RAccess
{
    /// <summary>
    /// "Language and speech" submenu of the mod settings menu: which language
    /// the mod speaks in, how much of a voiced dialogue line is read out, and
    /// whether on-screen captions and cutscene subtitles are read.
    /// </summary>
    public partial class ModMenuHandler
    {
        #region Screen

        /// <summary>Opens the language and speech submenu.</summary>
        private void OpenLanguageScreen()
        {
            OpenSettingsSubmenu("mod_menu_language_group_open", BuildLanguageItems());
        }

        private List<ModMenuItem> BuildLanguageItems()
        {
            return new List<ModMenuItem>
            {
                // Speech language: Automatic (follows the game's text language)
                // or a specific translation file from UserData\SO2RAccess\lang.
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_language",
                    GetValue = LanguageValueText,
                    Change = ChangeLanguage
                },
                // How much of a voiced dialogue line the screen reader speaks.
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_dialogue_mode",
                    GetValue = () => ModSettings.DialogueVoiceMode == DialogueVoiceMode.Full
                        ? Loc.Get("mod_menu_dialogue_full")
                        : Loc.Get("mod_menu_dialogue_name_only"),
                    Change = _ =>
                    {
                        ModSettings.DialogueVoiceMode =
                            ModSettings.DialogueVoiceMode == DialogueVoiceMode.Full
                                ? DialogueVoiceMode.NameOnlyWhenVoiced
                                : DialogueVoiceMode.Full;
                    }
                },
                Toggle("mod_menu_label_subtitles",
                    () => ModSettings.SubtitlesEnabled,
                    v => ModSettings.SubtitlesEnabled = v),
                // How often spoken directions repeat the current leg while walking.
                new ModMenuItem
                {
                    LabelKey = "mod_menu_label_guide_reminder",
                    GetValue = () => ModSettings.GuideReminderSeconds == 0
                        ? Loc.Get("mod_menu_off")
                        : Loc.Get("mod_menu_seconds", ModSettings.GuideReminderSeconds),
                    Change = ChangeGuideReminder
                }
            };
        }

        /// <summary>Cycles the directions reminder interval through the fixed choices.</summary>
        private static void ChangeGuideReminder(int delta)
        {
            var choices = ModSettings.GuideReminderChoices;
            int index = System.Array.IndexOf(choices, ModSettings.GuideReminderSeconds);
            if (index < 0) index = 0; // default Off
            index = ((index + delta) % choices.Length + choices.Length) % choices.Length;
            ModSettings.GuideReminderSeconds = choices[index];
        }

        #endregion

        #region Language Row

        /// <summary>Spoken value of the language row, e.g. "Automatic (English)" or "Deutsch".</summary>
        private static string LanguageValueText()
        {
            if (ModSettings.Language == "auto")
                return Loc.Get("mod_menu_language_auto", LocLoader.PeekLanguageName(Loc.ActiveCode));
            return LocLoader.PeekLanguageName(ModSettings.Language);
        }

        /// <summary>
        /// Cycles the language setting: Automatic, English, then every
        /// translation file in the lang folder. Applies immediately, so the
        /// menu re-announces this row in the newly loaded language.
        /// </summary>
        private static void ChangeLanguage(int delta)
        {
            var options = new List<string> { "auto" };
            options.AddRange(Loc.AvailableCodes());

            int index = options.IndexOf(ModSettings.Language);
            if (index < 0) index = 0; // stale setting (file deleted): restart the cycle
            index = (index + delta) % options.Count;
            if (index < 0) index += options.Count;

            string choice = options[index];
            ModSettings.Language = choice;
            if (choice == "auto")
                LanguageHandler.DetectNow(announce: false); // this row's re-announce covers it
            else
                Loc.SetLanguage(choice);
        }

        #endregion
    }
}
