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

        /// <summary>True while the fishing bubble is showing (announce-once edge state).</summary>
        private static bool _fishShowing = false;

        /// <summary>
        /// True while the game shows its fishing bubble — the world-space icon above the
        /// player's head that means "press the action button to fish". World-map auto-walk
        /// to a fishing spot treats this as an authoritative arrival signal. Detected by
        /// POLLING the UIFieldIconSelector's presenters for a visible FieldIconType.Fishing
        /// sprite each frame: the bubble is shown via ShowFieldIcon(..., ref Vector3, ...),
        /// which cannot be hooked (ref IL2CPP value-type param = native crash). The earlier
        /// FieldManager.GetContactFishingWaterPlaceID poll was proven WRONG 2026-08-29: it
        /// is contact with the water-place VOLUME (some span 200m+ over land — one overlaps
        /// the Krosse City exit), not "can fish now", causing false prompts and false
        /// auto-walk arrivals.
        /// </summary>
        public static bool FishPromptShowing => _fishShowing;

        // --- Fishing bubble poll state ---

        /// <summary>Cached world-space icon selector that draws the fishing bubble.</summary>
        private static UIFieldIconSelector _iconSelector = null;

        /// <summary>Next allowed FindObjectOfType time for the icon selector (throttle).</summary>
        private static float _iconSelectorNextFindTime = 0f;

        /// <summary>Seconds between icon-selector find attempts while it is unresolved.</summary>
        private const float IconSelectorFindInterval = 2f;

        /// <summary>Instance ID of the fishing icon sprite, 0 while unresolved.</summary>
        private static int _fishingSpriteId = 0;

        /// <summary>
        /// True after "You can fish here" was spoken for the current approach. The game
        /// BLINKS the bubble (hides/re-shows it in cycles while the player stands still —
        /// observed 2026-08-29), so each re-show must not re-announce. Cleared only once
        /// the player moves away from the announcement position (see
        /// <see cref="FishReannounceDistance"/>), so a genuine re-approach announces again.
        /// </summary>
        private static bool _fishAnnounceLatched = false;

        /// <summary>Player position at the last fishing announcement.</summary>
        private static UnityEngine.Vector3 _fishAnnouncePos;

        /// <summary>Meters the player must move from the announcement position before
        /// the bubble may announce again (blink-proofing, not a rate limit).</summary>
        private const float FishReannounceDistance = 3f;

        /// <summary>Debug-only: last logged fishing diagnostic state (log-on-change).</summary>
        private static string _lastFishDiagSignature = "";

        /// <summary>Parses a "&lt;sprite name=BUTTON&gt;ACTION" operation entry into button + action.</summary>
        private static readonly Regex _operationParser = new Regex(
            @"<sprite\s+name\s*=\s*([^>]+?)>\s*(.*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>True while a jump prompt is currently showing (drives announce-once + hide poll).</summary>
        private static bool _jumpShowing = false;

        /// <summary>The presenter currently showing the jump prompt, cached for hide polling.</summary>
        private static UIFieldOperationPresenter _jumpPresenter = null;

        // --- Label-operation prompt (e.g. world-map "Press X to enter <town>") ---

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

        // --- Debug-log dedup (suppresses per-frame repeats of the same prompt) ---
        private static string _lastSignature = "";
        private static float _lastLogTime = -100f;
        private static string _lastLabelSignature = "";
        private static float _lastLabelLogTime = -100f;
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

        /// <summary>
        /// Postfix for UIFieldLabelOperationPresenter.Set(...). Fires when a labelled button
        /// prompt is shown — most notably the world-map "Press X to enter &lt;town&gt;" guide.
        /// Speaks the prompt once (honouring its F4 toggle) and raises <see cref="EnterPromptShowing"/>
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

        #region Update (hide detection)

        /// <summary>
        /// Called each frame from Main.UpdateHandlers(). Detects when the jump prompt has been
        /// hidden — the game's Hide() is native-only and fires no managed hook, so the only
        /// reliable signal is the presenter going inactive or losing its jump text. Clearing
        /// the flag here lets a later re-appearance announce again.
        /// </summary>
        public void Update()
        {
            if (_jumpShowing && !IsActionStillShowing(_jumpPresenter, JumpAction))
            {
                _jumpShowing = false;
                _jumpPresenter = null;
                DebugLogger.LogState("FieldPrompt: jump prompt cleared.");
            }

            UpdateFishingBubblePoll();

            if (_enterShowing && !IsEnterStillShowing())
            {
                _enterShowing = false;
                _enterPresenter = null;
                EnterPromptLabel = "";
                DebugLogger.LogState("FieldPrompt: enter prompt cleared.");
            }
        }

        /// <summary>
        /// Returns true if the cached presenter is still active and still displaying the
        /// given action word. Any IL2CPP access failure (destroyed object) is treated as
        /// "not showing". Shared by the jump and fishing hide polls.
        /// </summary>
        private static bool IsActionStillShowing(
            UIFieldOperationPresenter presenter, string action)
        {
            try
            {
                if (presenter == null) return false;
                if (!presenter.gameObject.activeInHierarchy) return false;

                // Confirm the live on-screen text still contains the action — guards the
                // case where the presenter stays active but its text was swapped/cleared.
                // The action word survives tag-stripping unchanged, so the raw text can be
                // substring-checked directly, avoiding a regex strip pass on this per-frame path.
                var texts = presenter.operationTextList;
                if (texts == null || texts.Count == 0) return false;

                for (int i = 0; i < texts.Count; i++)
                {
                    var gt = texts[i];
                    if (gt == null) continue;
                    string raw = gt.text;
                    if (!string.IsNullOrEmpty(raw) &&
                        raw.IndexOf(action, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
                return false;
            }
            catch
            {
                return false;
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

        /// <summary>
        /// Per-frame poll of the game's fishing bubble — the world-space icon it shows
        /// above the player's head exactly while fishing can be started. The bubble is
        /// native-driven UI (ShowFieldIcon has a ref Vector3 param, unhookable), so the
        /// icon presenters are polled instead. Edge-triggered: announces once when the
        /// bubble appears, clears when it hides. Shares the enter-prompt F4 speech
        /// toggle — both are "you can act here" guides. In debug mode, every change of
        /// bubble/contact/visible-icon state is logged for evidence.
        /// </summary>
        private static void UpdateFishingBubblePoll()
        {
            bool bubble = IsFishingBubbleShowing(out string visibleIcons);

            if (Main.DebugMode)
                LogFishingDiag(bubble, visibleIcons);

            // Re-arm the announcement once the player has left the spot: the game
            // blinks the bubble while standing still, so hiding alone must NOT
            // re-arm — only real movement away from where it was announced.
            if (!bubble && _fishAnnounceLatched &&
                TryGetPlayerPos(out var pos) &&
                (pos - _fishAnnouncePos).sqrMagnitude >
                    FishReannounceDistance * FishReannounceDistance)
            {
                _fishAnnounceLatched = false;
                DebugLogger.LogState(
                    "FieldPrompt: fishing announce re-armed (moved away).");
            }

            if (bubble == _fishShowing) return;
            _fishShowing = bubble;

            if (bubble)
            {
                if (_fishAnnounceLatched)
                {
                    // A blink re-show at the same spot — stay quiet.
                    DebugLogger.LogState(
                        "FieldPrompt: fishing bubble re-shown (blink), announce suppressed.");
                    return;
                }

                // Bubble sound instead of speech (user decision 2026-08-30);
                // speech only as fallback when the WAV is missing/unparseable so
                // the prompt never goes silent by accident.
                if (AudioCuePlayer.IsFishPromptSoundLoaded)
                {
                    if (ModSettings.FishPromptSoundEnabled)
                        AudioCuePlayer.PlayFishPromptCue();
                }
                else if (ModSettings.EnterPromptSpeechEnabled)
                {
                    ScreenReader.Say(Loc.Get("fish_prompt"));
                }
                _fishAnnounceLatched = true;
                if (!TryGetPlayerPos(out _fishAnnouncePos))
                    _fishAnnouncePos = UnityEngine.Vector3.zero;
                DebugLogger.LogGameValue("FieldPrompt", "fishing bubble shown");
            }
            else
            {
                DebugLogger.LogState("FieldPrompt: fishing bubble hidden.");
            }
        }

        /// <summary>
        /// Reads the control player's world position. False during scene
        /// transitions or when no player exists.
        /// </summary>
        private static bool TryGetPlayerPos(out UnityEngine.Vector3 pos)
        {
            pos = UnityEngine.Vector3.zero;
            try
            {
                var player = FieldManager.Instance?.GetControlPlayer();
                if (player == null) return false;
                pos = player.transform.position;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// True while any world-space icon presenter is visibly showing the fishing
        /// sprite. Outputs the names of all visible icon sprites in debug mode (for
        /// cataloguing — empty otherwise) so a wrong sprite-index assumption shows up
        /// as log evidence instead of silence.
        /// </summary>
        private static bool IsFishingBubbleShowing(out string visibleIcons)
        {
            visibleIcons = "";
            var sel = GetIconSelector();
            if (sel == null || _fishingSpriteId == 0) return false;

            bool fishing = false;
            StringBuilder catalog = Main.DebugMode ? new StringBuilder() : null;

            try
            {
                var list = sel.iconPresenterList;
                if (list == null) return false;

                for (int i = 0; i < list.Count; i++)
                {
                    var presenter = list[i];
                    if (!IsPresenterVisible(presenter)) continue;

                    var img = presenter.icon;
                    var sprite = img != null ? img.sprite : null;
                    if (sprite == null) continue;

                    if (sprite.GetInstanceID() == _fishingSpriteId)
                        fishing = true;

                    if (catalog != null)
                    {
                        if (catalog.Length > 0) catalog.Append(", ");
                        catalog.Append(sprite.name);
                    }
                    else if (fishing)
                    {
                        break;  // no catalog wanted — first hit is enough
                    }
                }
            }
            catch
            {
                // Presenters destroyed mid-transition — treat as not showing this frame.
                return false;
            }

            if (catalog != null) visibleIcons = catalog.ToString();
            return fishing;
        }

        /// <summary>
        /// True if the icon presenter is actually visible on screen: active in the
        /// hierarchy and not faded out by its canvas group. Any IL2CPP access failure
        /// (destroyed object) is treated as "not visible".
        /// </summary>
        private static bool IsPresenterVisible(UIFieldIconPresenter presenter)
        {
            try
            {
                if (presenter == null) return false;
                if (!presenter.gameObject.activeInHierarchy) return false;

                var canvasGroup = presenter.canvasGroup;
                if (canvasGroup != null && canvasGroup.alpha < 0.5f) return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Returns the cached UIFieldIconSelector, re-finding it (throttled) after scene
        /// changes destroy it. Caches the fishing sprite's instance ID alongside — the
        /// sprite at index FieldIconType.Fishing of the selector's sprite list.
        /// </summary>
        private static UIFieldIconSelector GetIconSelector()
        {
            try
            {
                // Touching gameObject validates the cached instance; a destroyed
                // selector throws and falls through to the re-find below.
                if (_iconSelector != null && _iconSelector.gameObject != null)
                    return _iconSelector;
            }
            catch
            {
                _iconSelector = null;
            }

            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now < _iconSelectorNextFindTime) return null;
            _iconSelectorNextFindTime = now + IconSelectorFindInterval;

            try
            {
                // includeInactive: the selector may be disabled while no icon shows.
                _iconSelector =
                    UnityEngine.Object.FindObjectOfType<UIFieldIconSelector>(true);
                if (_iconSelector == null) return null;

                _fishingSpriteId = 0;
                var sprites = _iconSelector.spriteList;
                int fishingIndex = (int)UIDefine.FieldIconType.Fishing;
                if (sprites != null && sprites.Count > fishingIndex &&
                    sprites[fishingIndex] != null)
                {
                    _fishingSpriteId = sprites[fishingIndex].GetInstanceID();
                }

                DebugLogger.LogState(
                    $"FieldPrompt: UIFieldIconSelector cached " +
                    $"(sprites={(sprites != null ? sprites.Count : -1)}, " +
                    $"fishingSpriteId={_fishingSpriteId}).");
                if (_fishingSpriteId == 0)
                    DebugLogger.LogState(
                        "FieldPrompt: fishing sprite NOT resolved — bubble " +
                        "detection inactive (sprite list too short or null entry).");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState(
                    $"FieldPrompt: icon selector find failed: {ex.Message}");
                _iconSelector = null;
            }
            return _iconSelector;
        }

        /// <summary>
        /// Debug-only, log-on-change: correlates the bubble state with the old
        /// water-place contact signal and the visible icon sprites, plus the player
        /// position — the evidence trail for tuning the bubble detection.
        /// </summary>
        private static void LogFishingDiag(bool bubble, string visibleIcons)
        {
            int contactId = 0;
            try
            {
                var fm = FieldManager.Instance;
                if (fm != null && fm.GetControlPlayer() != null)
                    contactId = fm.GetContactFishingWaterPlaceID();
            }
            catch
            {
                // Scene teardown — leave 0; diagnostic only.
            }

            string signature = $"{bubble}|{contactId}|{visibleIcons}";
            if (signature == _lastFishDiagSignature) return;
            _lastFishDiagSignature = signature;

            string pos = TryGetPlayerPos(out var p)
                ? $"({p.x:F1},{p.y:F1},{p.z:F1})" : "?";

            DebugLogger.LogGameValue("FieldPrompt",
                $"FISHDIAG bubble={bubble} contactID={contactId} " +
                $"icons=[{visibleIcons}] pos={pos}");
        }

        /// <summary>
        /// Speaks a label-operation prompt once via the screen reader, honouring its F4 toggle.
        /// The game text is already localized, so it is echoed through Loc unchanged (the Loc
        /// template is a pass-through placeholder). Builds "Press {button} to {action}. {label}"
        /// when the operation carries a sprite-tagged action word, else falls back to the raw
        /// cleaned text so the player always hears whatever the game shows.
        /// </summary>
        private static void AnnounceEnter(string label, string operation)
        {
            if (!ModSettings.EnterPromptSpeechEnabled) return;

            ParseOperation(operation ?? "", out string button, out string action);
            string cleanLabel = NotificationHandler.StripTagsPublic(label ?? "").Trim();

            string core;
            if (!string.IsNullOrEmpty(button) && !string.IsNullOrEmpty(action))
                core = Loc.Get("enter_prompt", button, action);
            else if (!string.IsNullOrEmpty(action))
                core = Loc.Get("enter_prompt_no_button", action);
            else
                core = "";

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
                $"enter prompt shown (button='{button}' action='{action}' label='{cleanLabel}')");
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
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (signature == _lastLabelSignature && (now - _lastLabelLogTime) < DedupWindow)
                return;
            _lastLabelSignature = signature;
            _lastLabelLogTime = now;

            DebugLogger.LogGameValue("FieldPrompt",
                $"LABEL isPlayer={isPlayer} worldmap={worldmap} anchor='{anchor}' " +
                $"label=[{label}] operation=[{operation}]");
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
