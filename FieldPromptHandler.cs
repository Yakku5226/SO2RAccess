using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Speaks the field "operation" prompts — the button guide the game shows
    /// above the player whenever something can be done where they stand: talk
    /// to an NPC, open a chest, examine a gathering point, jump a ledge, use a
    /// save point, operate a switch. Every prompt is spoken with the game's own
    /// words ("Press Cross to Examine."), so the mod keeps no word list and the
    /// speech follows the game's text language. One F4 toggle silences them all.
    ///
    /// Hook: UIFieldOperationPresenter.Set(List&lt;string&gt; operationList, Transform followTransform,
    ///       Canvas canvas, ref Vector3 worldOffset, bool isCancelLocalPosition,
    ///       bool isPlayer, List&lt;Color&gt; textColorList) — [CallerCount(7)], confirmed hookable.
    /// Entries look like "&lt;sprite name=Cross&gt;Jump". Hide() is native-only and
    /// fires no managed hook, so "gone" is detected by polling the presenter.
    ///
    /// Announce-once rules, kept per presenter instance because the presenter is
    /// shared between prompts: a prompt speaks when its text changes, or when it
    /// re-appears after being hidden AND the player has moved at least
    /// <see cref="ReannounceDistance"/> from where it was last spoken. The game
    /// blinks bubbles while the player stands still — a re-show without movement,
    /// silent. A chain of ledges is a series of re-shows with movement — spoken.
    ///
    /// Only the jump sound needs to know that a prompt belongs to a ledge. That
    /// comes from the gimmick the player is in contact with
    /// (<see cref="InteractableRegistry.CurrentContact"/>), with the resolved text
    /// of the game's own jump prompt (SYS_3700) as the language-safe fallback.
    /// Sibling files: <c>.Fishing.cs</c> (the icon bubble that has no text),
    /// <c>.Enter.cs</c> (the labelled world-map "enter" prompt).
    /// </summary>
    public partial class FieldPromptHandler
    {
        #region Fields

        private bool _patchesApplied = false;

        /// <summary>Metres the player must move before a re-shown prompt is spoken again.</summary>
        private const float ReannounceDistance = 2f;

        /// <summary>System text key of the game's jump prompt (FieldGimmick01.GetOperationMessageID, confirmed 2026-09-23).</summary>
        private const string JumpMessageID = "SYS_3700";

        /// <summary>Seconds between attempts to resolve the jump text while it is unresolved.</summary>
        private const float JumpTextRetrySeconds = 5f;

        /// <summary>Debug-log dedup window for per-frame repeats of the same prompt.</summary>
        private const float DedupWindow = 2f;

        /// <summary>Seconds between "kept quiet while mounted" log lines.</summary>
        private const float MountedQuietLogInterval = 5f;
        private static float _lastMountedQuietLog = -100f;

        /// <summary>
        /// Raw text of the prompt spoken last by ANY presenter (the enter prompt
        /// included), and the travel mode seen last. While mounted, a re-shown
        /// prompt is repeated only when something else was spoken since; a mount
        /// or dismount clears it so the ride's own prompt speaks once again.
        /// </summary>
        private static string _lastSpokenRaw;
        private static WorldmapTravelMode _lastTravelMode = WorldmapTravelMode.Foot;

        /// <summary>Parses a "&lt;sprite name=BUTTON&gt;ACTION" operation entry into button + action.</summary>
        private static readonly Regex _operationParser = new Regex(
            @"<sprite\s+name\s*=\s*([^>]+?)>\s*(.*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>What one presenter instance is showing and what was last spoken for it.</summary>
        private sealed class PromptState
        {
            public UIFieldOperationPresenter Presenter;
            /// <summary>Raw joined entries of the current show ("" = nothing yet).</summary>
            public string  Text = "";
            public bool    Showing;
            /// <summary>True once something was spoken; <see cref="AnnouncedAt"/> is then valid.</summary>
            public bool    Announced;
            public Vector3 AnnouncedAt;
        }

        /// <summary>Prompt state per presenter instance ID. Cleared on scene change.</summary>
        private static readonly Dictionary<int, PromptState> _prompts = new Dictionary<int, PromptState>();

        /// <summary>The game's jump prompt text in its current language, null while unresolved.</summary>
        private static string _jumpText;
        private static float _jumpTextRetryTime;

        // Debug-log dedup (suppresses per-frame repeats of the same prompt).
        private static string _lastSignature = "";
        private static float _lastLogTime = -100f;

        #endregion

        #region Patch Application

        /// <summary>
        /// Applies the two prompt Harmony patches. Safe to call repeatedly — applied once.
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

                // Label-operation prompt (label + single operation glyph) — the world-map
                // "Press X to enter <town>" guide is shown through this sibling presenter.
                RuntimeHelpers.RunClassConstructor(
                    typeof(UIFieldLabelOperationPresenter).TypeHandle);
                harmony.Patch(
                    AccessTools.Method(typeof(UIFieldLabelOperationPresenter), "Set"),
                    postfix: new HarmonyMethod(typeof(FieldPromptHandler),
                        nameof(FieldLabelOperationPresenter_Set_Postfix))
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
        /// Postfix for UIFieldOperationPresenter.Set(...). Fires whenever a field
        /// button prompt is shown or refreshed. Tracks the presenter's state and
        /// announces the prompt when it is new (see the class summary for the rules).
        /// </summary>
        private static void FieldOperationPresenter_Set_Postfix(
            UIFieldOperationPresenter __instance,
            Il2CppSystem.Collections.Generic.List<string> operationList,
            UnityEngine.Transform followTransform,
            bool isPlayer)
        {
            try
            {
                string raw = JoinIl2CppStrings(operationList);
                var state = StateFor(__instance);

                if (string.IsNullOrEmpty(raw))
                {
                    // An empty Set is the game clearing the prompt.
                    state.Showing = false;
                    state.Text    = "";
                    return;
                }

                bool changed = state.Text != raw;
                bool reshown = !state.Showing;
                state.Text    = raw;
                state.Showing = true;

                var mode = TrackTravelMode();

                if (changed)
                {
                    AnnouncePrompt(state, operationList, "changed");
                }
                else if (reshown && MovedSinceAnnounce(state))
                {
                    // While mounted the mount's own prompt ("Press Circle to
                    // Dismount") blinks continuously and the rider is always
                    // more than ReannounceDistance from where it last spoke —
                    // 25 repeats in one minute (log 2026-09-26 12:19). A
                    // re-shown prompt while riding is repeated only when
                    // another prompt was spoken in between (after a town's
                    // enter prompt, say); on foot the ledge-chain rule is
                    // untouched.
                    if (mode != WorldmapTravelMode.Foot && _lastSpokenRaw == raw)
                    {
                        if (Time.unscaledTime - _lastMountedQuietLog >= MountedQuietLogInterval)
                        {
                            _lastMountedQuietLog = Time.unscaledTime;
                            DebugLogger.LogState(
                                $"FieldPrompt: re-shown while mounted — kept quiet. raw=[{raw}]");
                        }
                    }
                    else
                    {
                        AnnouncePrompt(state, operationList, "re-shown");
                    }
                }

                LogPromptDebug(__instance, operationList, followTransform, isPlayer);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"FieldOperationPresenter_Set_Postfix: {ex.Message}");
            }
        }

        /// <summary>The state record of a presenter instance, created on first sight.</summary>
        private static PromptState StateFor(UIFieldOperationPresenter presenter)
        {
            int id = presenter.GetInstanceID();
            if (!_prompts.TryGetValue(id, out var state))
            {
                // The dictionary only ever holds the few presenters the field UI
                // owns; a runaway (pooled presenters churning) is capped, not leaked.
                if (_prompts.Count >= 32) _prompts.Clear();
                state = new PromptState();
                _prompts[id] = state;
            }
            state.Presenter = presenter;
            return state;
        }

        /// <summary>The world map travel mode (bunny / psynard / foot); foot on any error and on field maps.</summary>
        private static WorldmapTravelMode CurrentTravelMode()
        {
            try { return WorldmapTravel.CurrentMode(); }
            catch { return WorldmapTravelMode.Foot; }
        }

        /// <summary>Records what was spoken last, for the mounted re-show rule (shared with the enter prompt).</summary>
        private static void NoteSpokenPrompt(string raw) => _lastSpokenRaw = raw;

        /// <summary>
        /// Samples the travel mode and, on a change (mount or dismount), forgets the
        /// last spoken prompt so the new ride's own prompt speaks once again. Runs
        /// every frame: sampling only when a prompt fires missed the dismount at
        /// the end of a ride, so the next ride still counted as the same one and its
        /// first prompt stayed quiet (log 2026-09-26 12:55).
        /// </summary>
        private static WorldmapTravelMode TrackTravelMode()
        {
            var mode = CurrentTravelMode();
            if (mode != _lastTravelMode)
            {
                DebugLogger.LogState($"FieldPrompt: travel mode {_lastTravelMode} -> {mode}; prompt memory cleared.");
                _lastTravelMode = mode;
                _lastSpokenRaw = null;
            }
            return mode;
        }

        /// <summary>True when nothing was spoken for this presenter yet, or the player has since moved away.</summary>
        private static bool MovedSinceAnnounce(PromptState state)
        {
            if (!state.Announced) return true;
            if (!TryGetPlayerPos(out Vector3 pos)) return true;
            return (pos - state.AnnouncedAt).sqrMagnitude >= ReannounceDistance * ReannounceDistance;
        }

        #endregion

        #region Announce

        /// <summary>
        /// Plays the jump sound when the prompt belongs to a ledge, then speaks
        /// the prompt with the game's own words, each honouring its F4 switch.
        /// Remembers where it was spoken for the re-show rule.
        /// </summary>
        private static void AnnouncePrompt(PromptState state,
            Il2CppSystem.Collections.Generic.List<string> operationList, string why)
        {
            var kind = InteractableRegistry.CurrentContact(out string contactType, out string targetType);
            if (kind == InteractableKind.None && ContainsJumpText(operationList))
                kind = InteractableKind.Ledge;

            if (kind == InteractableKind.Ledge && ModSettings.JumpPromptSoundEnabled)
                AudioCuePlayer.PlayJumpCue();

            string speech = BuildSpeech(operationList);
            if (ModSettings.PromptSpeechEnabled && !string.IsNullOrEmpty(speech))
                ScreenReader.Say(speech);
            NoteSpokenPrompt(state.Text);

            state.Announced = true;
            if (!TryGetPlayerPos(out state.AnnouncedAt))
                state.AnnouncedAt = Vector3.zero;

            DebugLogger.LogGameValue("FieldPrompt",
                $"{why}: kind={kind} contact={contactType} target={targetType} " +
                $"said='{speech}' raw=[{state.Text}]");
        }

        /// <summary>
        /// The spoken form of a whole prompt: one sentence per entry ("Press Cross
        /// to Talk. Press Square to Pickpocket."). Entries without an action word
        /// are skipped. The action is the game's text, already in its language.
        /// </summary>
        private static string BuildSpeech(Il2CppSystem.Collections.Generic.List<string> operationList)
        {
            if (operationList == null) return "";
            var parts = new List<string>();
            for (int i = 0; i < operationList.Count; i++)
            {
                string part = SpeechFor(operationList[i]);
                if (!string.IsNullOrEmpty(part)) parts.Add(part);
            }
            return string.Join(" ", parts);
        }

        /// <summary>The spoken form of one "&lt;sprite name=BUTTON&gt;ACTION" entry, or "" without an action.</summary>
        private static string SpeechFor(string rawEntry)
        {
            ParseOperation(rawEntry ?? "", out string button, out string action);
            if (string.IsNullOrEmpty(action)) return "";
            return string.IsNullOrEmpty(button)
                ? Loc.Get("prompt_generic_no_button", action)
                : Loc.Get("prompt_generic", button, action);
        }

        /// <summary>
        /// True when an entry's action word is the game's jump prompt text. The
        /// text is resolved from the game's own System table, so this holds in
        /// every text language.
        /// </summary>
        private static bool ContainsJumpText(Il2CppSystem.Collections.Generic.List<string> operationList)
        {
            string jump = JumpText();
            if (jump == null || operationList == null) return false;

            for (int i = 0; i < operationList.Count; i++)
            {
                ParseOperation(operationList[i] ?? "", out _, out string action);
                if (action.Equals(jump, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>The resolved jump prompt text, cached; retried every few seconds while the text table is not ready.</summary>
        private static string JumpText()
        {
            if (_jumpText != null) return _jumpText;

            float now = Time.realtimeSinceStartup;
            if (now < _jumpTextRetryTime) return null;
            _jumpTextRetryTime = now + JumpTextRetrySeconds;

            _jumpText = TextUtil.ResolveSystemText(JumpMessageID);
            DebugLogger.LogState(_jumpText == null
                ? "FieldPrompt: jump prompt text not resolved yet."
                : $"FieldPrompt: jump prompt text = '{_jumpText}'.");
            return _jumpText;
        }

        #endregion

        #region Update (hide detection)

        /// <summary>
        /// Called each frame from Main.UpdateHandlers(). The game's Hide() is
        /// native-only and fires no managed hook, so a prompt counts as hidden
        /// when its presenter is inactive or shows no text. Then the fishing
        /// bubble and the enter prompt run their own polls.
        /// </summary>
        public void Update()
        {
            TrackTravelMode();

            foreach (var pair in _prompts)
            {
                var state = pair.Value;
                if (!state.Showing || IsStillShowing(state.Presenter)) continue;
                state.Showing = false;
                DebugLogger.LogState($"FieldPrompt: prompt hidden [{state.Text}]");
            }

            UpdateFishingBubblePoll();
            UpdateEnterPromptPoll();
        }

        /// <summary>Forgets every presenter: the scene that owned them is gone.</summary>
        public void OnSceneChanged()
        {
            _prompts.Clear();
        }

        /// <summary>
        /// True while the presenter is active and displays any text. Any IL2CPP
        /// access failure (destroyed object) is treated as "not showing".
        /// </summary>
        private static bool IsStillShowing(UIFieldOperationPresenter presenter)
        {
            try
            {
                if (presenter == null) return false;
                if (!presenter.gameObject.activeInHierarchy) return false;

                var texts = presenter.operationTextList;
                if (texts == null || texts.Count == 0) return false;

                for (int i = 0; i < texts.Count; i++)
                {
                    var gt = texts[i];
                    if (gt != null && !string.IsNullOrEmpty(gt.text)) return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Helpers

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
        /// Logs every operation prompt under [GAME] FieldPrompt in debug mode, deduped. The
        /// catalogue of raw prompt texts, kept next to the announce lines.
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
            float now = Time.realtimeSinceStartup;
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
