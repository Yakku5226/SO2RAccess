using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace SO2RAccess
{
    /// <summary>
    /// Handles the field "operation" prompts — the world-space button guide the game shows
    /// above the player at a one-way jump-down ledge ("X Jump") and over interactables
    /// (save points, etc.). The jump prompt is conveyed to the player via an optional audio
    /// cue and/or a one-time screen-reader announcement (both toggle independently in the
    /// F4 mod menu).
    ///
    /// Hook: UIFieldOperationPresenter.Set(List&lt;string&gt; operationList, Transform followTransform,
    ///       Canvas canvas, ref Vector3 worldOffset, bool isCancelLocalPosition,
    ///       bool isPlayer, List&lt;Color&gt; textColorList) — [CallerCount(7)], confirmed hookable.
    ///
    /// The prompt's action word is the discriminator (e.g. "Jump"): the in-game test showed the
    /// jump prompt arrives as operationList[0] = "&lt;sprite name=Cross&gt;Jump" with isPlayer=false,
    /// so we filter on the action word, NOT the isPlayer flag. The presenter is shared with other
    /// prompts, so "jump no longer showing" is detected either by a different prompt being Set or
    /// by polling the presenter going inactive / losing the jump text (Hide is native-only and
    /// fires no managed hook).
    ///
    /// In debug mode (F12) every operation prompt is also logged under [GAME] FieldPrompt to
    /// catalogue prompts we have not handled yet (Talk, Open, Examine, ...).
    /// </summary>
    public class FieldPromptHandler
    {
        #region Fields

        private bool _patchesApplied = false;

        /// <summary>The action word that identifies the jump-down prompt (English build).</summary>
        private const string JumpAction = "Jump";

        /// <summary>Parses a "&lt;sprite name=BUTTON&gt;ACTION" operation entry into button + action.</summary>
        private static readonly Regex _operationParser = new Regex(
            @"<sprite\s+name\s*=\s*([^>]+?)>\s*(.*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>True while a jump prompt is currently showing (drives announce-once + hide poll).</summary>
        private static bool _jumpShowing = false;

        /// <summary>The presenter currently showing the jump prompt, cached for hide polling.</summary>
        private static UIFieldOperationPresenter _jumpPresenter = null;

        // --- Debug-log dedup (suppresses per-frame repeats of the same prompt) ---
        private static string _lastSignature = "";
        private static float _lastLogTime = -100f;
        private const float DedupWindow = 2f;

        #endregion

        #region Patch Application

        /// <summary>
        /// Applies the operation-prompt Harmony patch. Safe to call repeatedly — applied once.
        /// </summary>
        /// <param name="harmony">The mod's Harmony instance from Main.</param>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(UIFieldOperationPresenter).TypeHandle);

                // Only one Set overload exists on this class, so a name-only lookup is unambiguous
                // and avoids matching the ref Vector3 / optional parameter types by hand.
                harmony.Patch(
                    AccessTools.Method(typeof(UIFieldOperationPresenter), "Set"),
                    postfix: new HarmonyMethod(typeof(FieldPromptHandler),
                        nameof(FieldOperationPresenter_Set_Postfix))
                );

                _patchesApplied = true;
                DebugLogger.LogState("FieldPromptHandler: patch applied.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"FieldPromptHandler.ApplyPatches failed: {ex.Message}");
            }
        }

        #endregion

        #region Harmony Patch

        /// <summary>
        /// Postfix for UIFieldOperationPresenter.Set(...). Fires when a field button prompt is
        /// shown. Detects the jump prompt, drives the audio cue + one-time speech, and (in debug
        /// mode) logs every prompt for cataloguing. Deduped so a prompt held over many frames
        /// logs once.
        /// </summary>
        private static void FieldOperationPresenter_Set_Postfix(
            UIFieldOperationPresenter __instance,
            Il2CppSystem.Collections.Generic.List<string> operationList,
            UnityEngine.Transform followTransform,
            bool isPlayer)
        {
            try
            {
                // Find a jump entry and remember the button glyph used (controller-dependent).
                bool isJump = TryFindAction(operationList, JumpAction, out string jumpButton);

                if (isJump)
                {
                    // Announce/cue once on a new appearance, not every frame it is re-Set.
                    if (!_jumpShowing)
                    {
                        _jumpShowing = true;
                        AnnounceJump(jumpButton);
                    }
                    _jumpPresenter = __instance;   // always track the live presenter
                }
                else if (_jumpShowing && (_jumpPresenter == null || _jumpPresenter.Equals(__instance)))
                {
                    // A different prompt replaced the jump prompt on the (shared) presenter.
                    _jumpShowing = false;
                    _jumpPresenter = null;
                }

                LogPromptDebug(__instance, operationList, followTransform, isPlayer);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"FieldOperationPresenter_Set_Postfix: {ex.Message}");
            }
        }

        #endregion

        #region Update (hide detection)

        /// <summary>
        /// Called each frame from Main.UpdateHandlers(). Detects when the jump prompt has been
        /// hidden — the game's Hide() is native-only and fires no managed hook, so the only
        /// reliable signal is the presenter going inactive or losing its jump text. Clearing
        /// the flag here lets a later re-appearance announce again.
        /// </summary>
        public void Update()
        {
            if (!_jumpShowing) return;

            if (!IsJumpStillShowing())
            {
                _jumpShowing = false;
                _jumpPresenter = null;
                DebugLogger.LogState("FieldPrompt: jump prompt cleared.");
            }
        }

        /// <summary>
        /// Returns true if the cached presenter is still active and still displaying the jump
        /// action. Any IL2CPP access failure (destroyed object) is treated as "not showing".
        /// </summary>
        private static bool IsJumpStillShowing()
        {
            try
            {
                if (_jumpPresenter == null) return false;
                if (!_jumpPresenter.gameObject.activeInHierarchy) return false;

                // Confirm the live on-screen text still contains the jump action — guards the
                // case where the presenter stays active but its text was swapped/cleared.
                // The action word survives tag-stripping unchanged, so the raw text can be
                // substring-checked directly, avoiding a regex strip pass on this per-frame path.
                var texts = _jumpPresenter.operationTextList;
                if (texts == null || texts.Count == 0) return false;

                for (int i = 0; i < texts.Count; i++)
                {
                    var gt = texts[i];
                    if (gt == null) continue;
                    string raw = gt.text;
                    if (!string.IsNullOrEmpty(raw) &&
                        raw.IndexOf(JumpAction, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Announce

        /// <summary>
        /// Plays the audio cue and/or speaks the jump prompt once, honouring the independent
        /// F4-menu toggles. Speech names the button so the player knows which to press.
        /// </summary>
        private static void AnnounceJump(string button)
        {
            if (ModSettings.JumpPromptSoundEnabled)
                AudioCuePlayer.PlayJumpCue();

            if (ModSettings.JumpPromptSpeechEnabled)
            {
                string speech = string.IsNullOrEmpty(button)
                    ? Loc.Get("jump_prompt_no_button")
                    : Loc.Get("jump_prompt", button);
                ScreenReader.Say(speech);
            }

            DebugLogger.LogGameValue("FieldPrompt", $"jump prompt shown (button='{button}')");
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Scans an operation list for an entry whose action word matches <paramref name="action"/>.
        /// Outputs the readable button name (e.g. "Cross") parsed from that entry's sprite tag.
        /// </summary>
        private static bool TryFindAction(
            Il2CppSystem.Collections.Generic.List<string> operationList,
            string action, out string button)
        {
            button = "";
            if (operationList == null || operationList.Count == 0) return false;

            for (int i = 0; i < operationList.Count; i++)
            {
                string raw = operationList[i];
                if (string.IsNullOrEmpty(raw)) continue;

                ParseOperation(raw, out string entryButton, out string entryAction);
                if (entryAction.Equals(action, StringComparison.OrdinalIgnoreCase))
                {
                    button = entryButton;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Splits a "&lt;sprite name=BUTTON&gt;ACTION" entry into a readable button name and the
        /// trailing action word. Falls back to the whole stripped string as the action when no
        /// sprite tag is present.
        /// </summary>
        private static void ParseOperation(string raw, out string button, out string action)
        {
            button = "";
            var m = _operationParser.Match(raw);
            if (m.Success)
            {
                button = NotificationHandler.StripControllerPrefixPublic(m.Groups[1].Value.Trim());
                action = NotificationHandler.StripTagsPublic(m.Groups[2].Value).Trim();
            }
            else
            {
                action = NotificationHandler.StripTagsPublic(raw).Trim();
            }
        }

        /// <summary>
        /// Logs every operation prompt under [GAME] FieldPrompt in debug mode, deduped. Used to
        /// catalogue prompt types we have not yet handled (Talk, Open, Examine, ...).
        /// </summary>
        private static void LogPromptDebug(
            UIFieldOperationPresenter presenter,
            Il2CppSystem.Collections.Generic.List<string> operationList,
            UnityEngine.Transform followTransform,
            bool isPlayer)
        {
            if (!Main.DebugMode) return;

            string rawJoined = JoinIl2CppStrings(operationList);
            string displayText = ReadDisplayText(presenter);

            string anchor = "?";
            try { if (followTransform != null) anchor = followTransform.gameObject.name; }
            catch { /* destroyed/native edge — ignore for a diagnostic */ }

            string signature = $"{isPlayer}|{rawJoined}|{displayText}";
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (signature == _lastSignature && (now - _lastLogTime) < DedupWindow)
                return;
            _lastSignature = signature;
            _lastLogTime = now;

            DebugLogger.LogGameValue("FieldPrompt",
                $"isPlayer={isPlayer} anchor='{anchor}' raw=[{rawJoined}] display='{displayText}'");
        }

        /// <summary>
        /// Joins an Il2Cpp List of strings into a readable "[0]=a | [1]=b" form, preserving the
        /// raw (un-stripped) text so the exact button-sprite tags are visible in the log.
        /// </summary>
        private static string JoinIl2CppStrings(
            Il2CppSystem.Collections.Generic.List<string> list)
        {
            if (list == null || list.Count == 0) return "";

            var sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(" | ");
                sb.Append('[').Append(i).Append("]=").Append(list[i] ?? "");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Reads the cleaned, on-screen text from the presenter's GameText list, using the same
        /// tag-stripping as the rest of the mod so sprite tags become readable.
        /// </summary>
        private static string ReadDisplayText(UIFieldOperationPresenter presenter)
        {
            try
            {
                var texts = presenter?.operationTextList;
                if (texts == null || texts.Count == 0) return "";

                var sb = new StringBuilder();
                for (int i = 0; i < texts.Count; i++)
                {
                    var gt = texts[i];
                    if (gt == null) continue;
                    string clean = NotificationHandler.StripTagsPublic(gt.text ?? "");
                    if (string.IsNullOrEmpty(clean)) continue;
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(clean);
                }
                return sb.ToString();
            }
            catch
            {
                return "";
            }
        }

        #endregion
    }
}
