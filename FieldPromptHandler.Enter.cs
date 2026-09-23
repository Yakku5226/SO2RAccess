using Il2CppGame;
using MelonLoader;
using System;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// The labelled prompt — a name plus one button glyph — that the game shows
    /// through UIFieldLabelOperationPresenter, most notably the world-map
    /// "Press X to enter &lt;town&gt;" guide. Spoken once per appearance under the
    /// same F4 switch as the other prompts, and exposed as an arrival signal for
    /// world-map auto-walk.
    /// </summary>
    public partial class FieldPromptHandler
    {
        #region Fields

        /// <summary>True while a label-operation prompt is currently showing.</summary>
        private static bool _enterShowing = false;

        /// <summary>The label presenter currently showing the prompt, cached for hide polling.</summary>
        private static UIFieldLabelOperationPresenter _enterPresenter = null;

        /// <summary>
        /// True while a label-operation prompt (world-map "enter" guide) is on screen.
        /// Navigation reads this as an authoritative "arrived at the location" signal during
        /// world-map auto-walk, since the prompt only appears once the player is close enough
        /// to enter — even when the location's collision ring blocks getting nearer.
        /// </summary>
        public static bool EnterPromptShowing => _enterShowing;

        /// <summary>The cleaned label text of the current enter prompt (location name), or "".</summary>
        public static string EnterPromptLabel { get; private set; } = "";

        // Debug-log dedup for label prompts.
        private static string _lastLabelSignature = "";
        private static float _lastLabelLogTime = -100f;

        #endregion

        #region Harmony Patch

        /// <summary>
        /// Postfix for UIFieldLabelOperationPresenter.Set(...). Fires when a labelled button
        /// prompt is shown — most notably the world-map "Press X to enter &lt;town&gt;" guide.
        /// Speaks the prompt once (honouring the F4 toggle) and raises <see cref="EnterPromptShowing"/>
        /// so world-map auto-walk can treat it as arrival. In debug mode every label prompt is
        /// logged so its exact label/operation text can be confirmed.
        /// </summary>
        private static void FieldLabelOperationPresenter_Set_Postfix(
            UIFieldLabelOperationPresenter __instance,
            string label,
            string operation,
            UnityEngine.Transform followTransform,
            bool isPlayer)
        {
            try
            {
                // Announce once on a new appearance, not every frame it is re-Set.
                if (!_enterShowing)
                {
                    _enterShowing = true;
                    EnterPromptLabel = NotificationHandler.StripTagsPublic(label ?? "").Trim();
                    AnnounceEnter(label, operation);
                }
                _enterPresenter = __instance;   // always track the live presenter

                LogLabelPromptDebug(__instance, label, operation, followTransform, isPlayer);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"FieldLabelOperationPresenter_Set_Postfix: {ex.Message}");
            }
        }

        #endregion

        #region Hide poll

        /// <summary>Clears the enter-prompt state once its presenter has gone; called every frame.</summary>
        private static void UpdateEnterPromptPoll()
        {
            if (_enterShowing && !IsEnterStillShowing())
            {
                _enterShowing = false;
                _enterPresenter = null;
                EnterPromptLabel = "";
                DebugLogger.LogState("FieldPrompt: enter prompt cleared.");
            }
        }

        /// <summary>
        /// Returns true if the cached label presenter is still active and still displaying
        /// label or operation text. Any IL2CPP access failure is treated as "not showing".
        /// </summary>
        private static bool IsEnterStillShowing()
        {
            try
            {
                if (_enterPresenter == null) return false;
                // The label presenter is dedicated to label prompts (not shared like the
                // operation presenter), so its active state is a reliable hide signal.
                if (!_enterPresenter.gameObject.activeInHierarchy) return false;

                var op = _enterPresenter.operation;
                string opText = op != null ? op.text : null;
                return !string.IsNullOrEmpty(opText);
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Announce

        /// <summary>
        /// Speaks a label-operation prompt once via the screen reader, honouring the F4 toggle.
        /// The game text is already localized, so it is echoed through Loc unchanged (the Loc
        /// template is a pass-through placeholder). Builds "Press {button} to {action}. {label}"
        /// when the operation carries a sprite-tagged action word, else falls back to the raw
        /// cleaned text so the player always hears whatever the game shows.
        /// </summary>
        private static void AnnounceEnter(string label, string operation)
        {
            if (!ModSettings.PromptSpeechEnabled) return;

            string core = SpeechFor(operation ?? "");
            string cleanLabel = NotificationHandler.StripTagsPublic(label ?? "").Trim();

            string spoken;
            if (string.IsNullOrEmpty(core))
                spoken = cleanLabel;
            else if (string.IsNullOrEmpty(cleanLabel))
                spoken = core;
            else
                spoken = core + " " + cleanLabel;

            if (!string.IsNullOrEmpty(spoken))
                ScreenReader.Say(Loc.Get("enter_prompt_echo", spoken));

            DebugLogger.LogGameValue("FieldPrompt",
                $"enter prompt shown (operation='{operation}' label='{cleanLabel}')");
        }

        #endregion

        #region Debug

        /// <summary>
        /// Logs every label-operation prompt under [GAME] FieldPrompt in debug mode, deduped.
        /// Records the raw label/operation text and whether the current map is the world map, so
        /// the exact world-map "enter" prompt content can be confirmed on the first test walk.
        /// </summary>
        private static void LogLabelPromptDebug(
            UIFieldLabelOperationPresenter presenter,
            string label,
            string operation,
            UnityEngine.Transform followTransform,
            bool isPlayer)
        {
            if (!Main.DebugMode) return;

            string anchor = "?";
            try { if (followTransform != null) anchor = followTransform.gameObject.name; }
            catch { /* destroyed/native edge — ignore for a diagnostic */ }

            bool worldmap = false;
            try { worldmap = FieldManager.Instance?.IsWorldmap() == true; }
            catch { /* manager unavailable — diagnostic only */ }

            string signature = $"{isPlayer}|{label}|{operation}";
            float now = Time.realtimeSinceStartup;
            if (signature == _lastLabelSignature && (now - _lastLabelLogTime) < DedupWindow)
                return;
            _lastLabelSignature = signature;
            _lastLabelLogTime = now;

            DebugLogger.LogGameValue("FieldPrompt",
                $"LABEL isPlayer={isPlayer} worldmap={worldmap} anchor='{anchor}' " +
                $"label=[{label}] operation=[{operation}]");
        }

        #endregion
    }
}
