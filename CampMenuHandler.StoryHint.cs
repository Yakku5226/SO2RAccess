using Il2CppGame;
using System;
using UnityEngine.InputSystem;

namespace SO2RAccess
{
    /// <summary>
    /// On-demand story hint readout for the camp menu (game-api.md section 19).
    ///
    /// The camp screen shows the current story objective as a speech balloon over one
    /// of the dot characters. The balloon text lives on
    /// UICampDotCharacterPresenter.speechBalloonPresenter.speechBalloonText, so it can
    /// be read live whenever the camp is open — no caching needed.
    ///
    /// Trigger: L3 (left stick click, without the L2 mod modifier) or the story-hint
    /// key (ModKeys.CampStoryHint) while the camp menu is open. Plain L3 elsewhere
    /// stays the party-status readout (QuickRecoveryHandler, which only listens while
    /// its own field overlay is open).
    /// </summary>
    public partial class CampMenuHandler
    {
        /// <summary>
        /// Speaks the story hint balloon when the hint key is pressed in the camp menu.
        /// Called every frame from Update().
        /// </summary>
        private void UpdateStoryHint()
        {
            if (!IsCampOpen) return;

            try
            {
                var kb = Keyboard.current;
                bool fromKeyboard = kb != null && kb[ModKeys.CampStoryHint].wasPressedThisFrame;

                // Require the mod modifier (L2) NOT held so modifier+L3 stays the
                // mod-menu toggle (Main).
                var gp = Gamepad.current;
                bool fromGamepad = gp != null
                    && ModKeys.ModMenuChord(gp).wasPressedThisFrame
                    && !ModKeys.NavModifier(gp).isPressed;

                if (!fromKeyboard && !fromGamepad) return;

                DebugLogger.LogInput(
                    fromKeyboard ? ModKeys.DisplayName(ModAction.CampStoryHint) : "L3",
                    "CampStoryHint");
                AnnounceStoryHint();
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampMenu.StoryHint: key error: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads the speech balloon text live from the dot-character strip and speaks
        /// it. Always speaks something — the hint, or "no hint" when none is shown.
        /// </summary>
        private static void AnnounceStoryHint()
        {
            try
            {
                var presenters =
                    UnityEngine.Object.FindObjectsOfType<UICampDotCharacterPresenter>();
                if (presenters == null || presenters.Length == 0)
                {
                    DebugLogger.LogState("CampMenu.StoryHint: no dot-character presenters found.");
                    ScreenReader.Say(Loc.Get("camp_story_hint_none"));
                    return;
                }

                // Prefer a balloon the game says is showing; fall back to any presenter
                // that carries real text (the balloon animates in and out while idle).
                string fallback = null;
                foreach (var p in presenters)
                {
                    if (p == null || p.gameObject == null || !p.gameObject.activeInHierarchy)
                        continue;

                    string text = ReadBalloonText(p);
                    if (text == null) continue;

                    if (p.IsShowingSpeechBalloon)
                    {
                        Speak(text);
                        return;
                    }
                    if (fallback == null) fallback = text;
                }

                if (fallback != null)
                {
                    Speak(fallback);
                    return;
                }

                DebugLogger.LogState(
                    $"CampMenu.StoryHint: no balloon text on {presenters.Length} presenters.");
                ScreenReader.Say(Loc.Get("camp_story_hint_none"));
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampMenu.StoryHint: read error: {ex.Message}");
                ScreenReader.Say(Loc.Get("camp_story_hint_none"));
            }

            void Speak(string hint)
            {
                ScreenReader.Say(Loc.Get("camp_story_hint", hint));
                DebugLogger.LogGameValue("CampMenu.storyHint", hint);
            }
        }

        /// <summary>
        /// Returns the cleaned balloon text of one dot character, or null when it is
        /// empty or a placeholder ("0000" / "目的" dummy fills).
        /// </summary>
        private static string ReadBalloonText(UICampDotCharacterPresenter presenter)
        {
            var balloon = presenter.speechBalloonPresenter;
            if (balloon == null) return null;

            var gameText = balloon.speechBalloonText;
            if (gameText == null) return null;

            string text = TextUtil.StripTags(((Il2CppTMPro.TMP_Text)gameText).text);
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (text == "0000" || text == "目的" || text == "-") return null;
            return text;
        }
    }
}
