using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Announces on-screen captions and cutscene subtitles to the screen reader.
    ///
    /// The game keeps captions completely separate from the conversation text box
    /// (<see cref="DialogueHandler"/> / UIConversationPresenter). Captions are the
    /// free-floating text layer: the subtitle line under a pre-rendered movie, and
    /// the small caption balloons events place above a character. Nothing in the mod
    /// covered that layer before, so cutscene subtitles were silent.
    ///
    /// Patches applied (each attached independently, so one missing method never
    /// costs us the others):
    ///   UICaptionPresenter.SetCaption(string caption, bool isSubtitles)
    ///     — the funnel every caption should pass through, with the text already
    ///       resolved from its messageID and localized. This is the one that speaks.
    ///   UICaptionController.ShowCaption(string caption, string messageID)
    ///   UICaptionSelector.ShowCaption(string message, ..., string messageID, bool isSubTitles)
    ///   UICaptionSelector.ShowMovieCaption(string message, ...)
    ///     — trace only. If a caption ever appears without reaching SetCaption, the
    ///       log names which layer it stopped at instead of the mod going silent.
    ///       The Vector2 anchor parameters are simply not declared on the postfixes;
    ///       Harmony passes only what a patch asks for, so no struct is marshalled.
    ///
    /// WHY POLLING, NOT THE HOOKS (log-proven 2026-09-01)
    ///   All four patches attach ("4/4 caption patches applied") and none of them
    ///   ever fired during a movie, yet the subtitle text was demonstrably on
    ///   screen at
    ///   <c>uiRoot/Endroll/UICaptionController/UICaptionSelector/ui_movie_caption_presenter/Caption</c>.
    ///   These are small forwarding methods, so the native build inlines them and
    ///   the standalone copies Harmony patches are never called — the same reason
    ///   camp and shop menus are polled rather than hooked. The presenter's own
    ///   GameText is therefore the source of truth: <see cref="Update"/> watches it
    ///   every frame and speaks each new line. The hooks are kept because they cost
    ///   nothing, still log, and would take over if a game update stopped inlining
    ///   them; both paths funnel through <see cref="AnnounceCaption"/>, whose repeat
    ///   filter makes a double announcement impossible.
    ///
    /// MOVIE TEXT TRACER (debug mode only, see <see cref="Update"/>)
    ///   While GameMovieManager reports a movie playing, caption text is logged the
    ///   first time it appears with its full GameObject path. The unfiltered sweep
    ///   lives on the debug text-dump key instead — running it automatically buried
    ///   the useful lines under ~1300 hidden-menu ones.
    /// </summary>
    public class SubtitleHandler
    {
        #region Fields

        private bool _patchesApplied = false;

        /// <summary>
        /// Window in which the identical caption text is treated as a repeat.
        /// Captions re-set themselves on show/refresh; a real subtitle track never
        /// repeats the same line this fast.
        /// </summary>
        private const float DedupeWindow = 1.0f;

        private static string _lastSpoken = "";
        private static float _lastSpokenTime = -999f;

        // --- Caption polling state ---

        /// <summary>Seconds between attempts to (re)find the caption selectors.</summary>
        private const float SelectorScanInterval = 1f;

        private UICaptionSelector[] _selectors;
        private float _nextSelectorScan = 0f;

        /// <summary>Last text seen per caption presenter, so only changes are spoken.</summary>
        private readonly Dictionary<IntPtr, string> _lastCaptionText =
            new Dictionary<IntPtr, string>();

        /// <summary>Presenters visited this frame — used to prune dead entries.</summary>
        private readonly HashSet<IntPtr> _visitedPresenters = new HashSet<IntPtr>();

        // --- Movie text tracer state (debug mode only) ---

        /// <summary>Seconds between tracer sweeps while a movie is playing.</summary>
        private const float TraceInterval = 0.35f;

        private float _nextTraceTime = 0f;
        private bool _movieWasPlaying = false;
        private readonly HashSet<string> _tracedTexts = new HashSet<string>(StringComparer.Ordinal);

        #endregion

        #region Patch Application

        /// <summary>
        /// Applies the caption patches. Safe to call on every scene load — patches
        /// are only applied once.
        /// </summary>
        /// <param name="harmony">The mod's Harmony instance from Main.</param>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(UICaptionPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICaptionController).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICaptionSelector).TypeHandle);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"SubtitleHandler: caption type init failed: {ex.Message}");
            }

            int attached = 0;

            // The one that speaks.
            attached += TryPatch(harmony, typeof(UICaptionPresenter),
                nameof(UICaptionPresenter.SetCaption),
                new Type[] { typeof(string), typeof(bool) },
                nameof(SetCaption_Postfix)) ? 1 : 0;

            // Trace only — upstream layers, so a caption that never reaches the
            // presenter still shows up in the log.
            attached += TryPatch(harmony, typeof(UICaptionController),
                nameof(UICaptionController.ShowCaption),
                new Type[] { typeof(string), typeof(string) },
                nameof(ControllerShowCaption_Postfix)) ? 1 : 0;

            attached += TryPatch(harmony, typeof(UICaptionSelector),
                nameof(UICaptionSelector.ShowCaption),
                new Type[] { typeof(string), typeof(Vector2), typeof(string), typeof(bool) },
                nameof(SelectorShowCaption_Postfix)) ? 1 : 0;

            attached += TryPatch(harmony, typeof(UICaptionSelector),
                nameof(UICaptionSelector.ShowMovieCaption),
                new Type[] { typeof(string), typeof(Vector2) },
                nameof(SelectorShowMovieCaption_Postfix)) ? 1 : 0;

            _patchesApplied = true;

            // MelonLogger, not DebugLogger: patches are applied at scene load, long
            // before the player can turn debug mode on, so a DebugLogger line here
            // would never appear and a silent failure would look identical to a
            // working hook.
            MelonLogger.Msg($"SubtitleHandler: {attached}/4 caption patches applied.");
        }

        /// <summary>
        /// Attaches one postfix, reporting success or the exact reason it failed.
        /// Each patch is independent so a signature that changed in a game update
        /// costs only its own hook.
        /// </summary>
        private static bool TryPatch(HarmonyLib.Harmony harmony, Type target,
            string methodName, Type[] parameters, string postfixName)
        {
            try
            {
                var method = AccessTools.Method(target, methodName, parameters);
                if (method == null)
                {
                    MelonLogger.Warning($"SubtitleHandler: {target.Name}.{methodName} not found — hook skipped.");
                    return false;
                }

                harmony.Patch(method,
                    postfix: new HarmonyMethod(typeof(SubtitleHandler), postfixName));
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"SubtitleHandler: patching {target.Name}.{methodName} failed: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Harmony Patch Methods

        /// <summary>
        /// Fires for every caption the game displays — movie subtitles and event
        /// captions. The text arrives already resolved and localized.
        /// </summary>
        private static void SetCaption_Postfix(string caption, bool isSubtitles)
        {
            try
            {
                DebugLogger.LogGameValue("Caption.set",
                    $"isSubtitles={isSubtitles} text='{TextUtil.StripTags(caption)}'");
                AnnounceCaption(caption, "hook");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"SubtitleHandler.SetCaption_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Speaks one caption line. The single voice for both the polling loop and
        /// the (currently inlined, so silent) hooks — whichever sees a line first
        /// speaks it, and the repeat filter stops the other from saying it again.
        /// </summary>
        private static void AnnounceCaption(string caption, string source)
        {
            string text = TextUtil.StripTags(caption);

            // Captions are cleared by setting empty text — normal, not a fault.
            if (string.IsNullOrWhiteSpace(text)) return;

            if (!ModSettings.SubtitlesEnabled)
            {
                DebugLogger.LogState("Caption: not spoken (subtitle reading is off in the mod menu).");
                return;
            }

            if (text == _lastSpoken && Time.unscaledTime - _lastSpokenTime < DedupeWindow)
            {
                DebugLogger.LogState($"Caption: dropped (duplicate within {DedupeWindow}s, source={source}).");
                return;
            }
            _lastSpoken = text;
            _lastSpokenTime = Time.unscaledTime;

            DebugLogger.LogGameValue("Caption.spoken", $"[{source}] {text}");

            // Interrupt: a new subtitle line replaces the previous one on screen,
            // so the spoken line should follow the same timing.
            ScreenReader.Say(text);
        }

        /// <summary>Trace of the controller-level caption call.</summary>
        private static void ControllerShowCaption_Postfix(string caption, string messageID)
        {
            LogTrace("Caption.controller", $"id='{messageID}' text='{TextUtil.StripTags(caption)}'");
        }

        /// <summary>Trace of the selector-level caption call (event captions).</summary>
        private static void SelectorShowCaption_Postfix(string message, string messageID, bool isSubTitles)
        {
            LogTrace("Caption.selector",
                $"id='{messageID}' isSubTitles={isSubTitles} text='{TextUtil.StripTags(message)}'");
        }

        /// <summary>Trace of the selector-level movie subtitle call.</summary>
        private static void SelectorShowMovieCaption_Postfix(string message)
        {
            LogTrace("Caption.movie", $"text='{TextUtil.StripTags(message)}'");
        }

        private static void LogTrace(string label, string detail)
        {
            try { DebugLogger.LogGameValue(label, detail); }
            catch (Exception ex) { DebugLogger.LogState($"SubtitleHandler.{label}: {ex.Message}"); }
        }

        #endregion

        #region Update Loop

        /// <summary>
        /// Watches the caption presenters for new lines, and runs the debug movie
        /// tracer. Called every frame from Main.UpdateHandlers().
        /// </summary>
        public void Update()
        {
            PollCaptions();
            TickMovieTracer();
        }

        #endregion

        #region Caption Polling

        /// <summary>
        /// Reads the caption presenters' own text every frame and speaks each line
        /// as it changes. This — not the Harmony hooks — is what actually makes
        /// subtitles audible; see the class summary for why the hooks never fire.
        ///
        /// A presenter is only read while its GameObject is active, which is the
        /// game's own showing/hidden signal for captions (verified in the movie
        /// trace: the movie caption object appears only while a line is on screen),
        /// so a hidden presenter's leftover text is never spoken.
        /// </summary>
        private void PollCaptions()
        {
            try
            {
                RefreshSelectors();
                if (_selectors == null) return;

                _visitedPresenters.Clear();

                foreach (var selector in _selectors)
                {
                    if (selector == null) continue;

                    ReadPresenter(selector.movieCaptionPresenter);
                    ReadPresenter(selector.captionPresenter);

                    // Event captions are created on demand and appended here.
                    var list = selector.captionPresenterList;
                    if (list == null) continue;
                    for (int i = 0; i < list.Count; i++)
                        ReadPresenter(list[i]);
                }

                PruneDeadPresenters();
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"SubtitleHandler.PollCaptions: {ex.Message}");
            }
        }

        /// <summary>
        /// (Re)finds the caption selectors. There is one per UI root that can show
        /// captions, and they are created with their scene, so a periodic rescan
        /// while none are cached is enough — no per-frame scene scan.
        /// </summary>
        private void RefreshSelectors()
        {
            bool haveLive = false;
            if (_selectors != null)
            {
                foreach (var s in _selectors)
                {
                    if (s != null) { haveLive = true; break; }
                }
            }
            if (haveLive) return;

            if (Time.unscaledTime < _nextSelectorScan) return;
            _nextSelectorScan = Time.unscaledTime + SelectorScanInterval;

            // includeInactive: the caption UI root sits inactive between movies, and
            // a scan that skipped it would only find the selector a second AFTER the
            // first subtitle line had already come and gone.
            _selectors = UnityEngine.Object.FindObjectsOfType<UICaptionSelector>(true);
            DebugLogger.LogState(
                $"Caption poll: found {(_selectors == null ? 0 : _selectors.Length)} caption selector(s).");
        }

        /// <summary>
        /// Speaks this presenter's text if it is showing and the line has changed
        /// since the last frame.
        /// </summary>
        private void ReadPresenter(UICaptionPresenter presenter)
        {
            if (presenter == null) return;

            IntPtr key = presenter.Pointer;
            _visitedPresenters.Add(key);

            var go = presenter.gameObject;
            if (go == null || !go.activeInHierarchy)
            {
                // Hidden: forget its text so re-showing the same line speaks again.
                _lastCaptionText.Remove(key);
                return;
            }

            var gameText = presenter.caption;
            string raw = gameText == null ? null : ((Il2CppTMPro.TMP_Text)gameText).text;
            string text = TextUtil.StripTags(raw);

            if (_lastCaptionText.TryGetValue(key, out string previous) && previous == text) return;
            _lastCaptionText[key] = text;

            if (string.IsNullOrWhiteSpace(text)) return;

            DebugLogger.LogGameValue("Caption.poll",
                $"isSubtitles={presenter.isSubtitles} text='{text}'");
            AnnounceCaption(text, "poll");
        }

        /// <summary>
        /// Drops remembered text for presenters that no longer exist, so a reused
        /// native pointer can never silence a genuinely new caption.
        /// </summary>
        private void PruneDeadPresenters()
        {
            if (_lastCaptionText.Count == 0) return;

            List<IntPtr> dead = null;
            foreach (var key in _lastCaptionText.Keys)
            {
                if (_visitedPresenters.Contains(key)) continue;
                (dead ??= new List<IntPtr>()).Add(key);
            }

            if (dead == null) return;
            foreach (var key in dead) _lastCaptionText.Remove(key);
        }

        #endregion

        #region Movie Text Tracer (debug only)

        /// <summary>
        /// While a movie is playing and debug mode is on, logs caption text the
        /// first time it is seen, with its full GameObject path. Speaks nothing and
        /// does no work outside a movie. For anything the filter misses, use the
        /// debug text-dump key — an unfiltered automatic sweep buried the useful
        /// lines under about 1300 hidden-menu ones.
        /// </summary>
        private void TickMovieTracer()
        {
            if (!Main.DebugMode) return;

            bool playing = IsMoviePlaying();

            if (playing != _movieWasPlaying)
            {
                _movieWasPlaying = playing;
                _tracedTexts.Clear();
                DebugLogger.LogState(playing
                    ? $"Movie tracer: movie started ({DescribeMovie()}) — logging caption text."
                    : "Movie tracer: movie ended.");
            }

            if (!playing) return;
            if (Time.unscaledTime < _nextTraceTime) return;
            _nextTraceTime = Time.unscaledTime + TraceInterval;

            TraceVisibleText();
        }

        /// <summary>True while GameMovieManager reports a movie running.</summary>
        private static bool IsMoviePlaying()
        {
            try
            {
                var mm = GameMovieManager.Instance;
                return mm != null && mm.IsPlaying;
            }
            catch
            {
                // Singleton not up yet (boot, scene swap) — not a movie, not a fault.
                return false;
            }
        }

        /// <summary>Movie identity for the log line, best-effort.</summary>
        private static string DescribeMovie()
        {
            try
            {
                var mm = GameMovieManager.Instance;
                if (mm == null) return "unknown";
                return $"file='{mm.messageFileName}' state={mm.State} " +
                       $"voice={mm.CurrentVoiceType} lang={mm.CurrentLanguage} onUI={mm.IsPlayingOnUI}";
            }
            catch (Exception ex)
            {
                return $"unreadable ({ex.Message})";
            }
        }

        /// <summary>
        /// Logs each newly seen visible TMP text with its scene path. Deduped by
        /// path+text so a static line is logged once, not every sweep.
        /// </summary>
        private void TraceVisibleText()
        {
            SweepVisibleText("MovieText", _tracedTexts, captionPathsOnly: true);
        }

        /// <summary>
        /// True for object paths belonging to the caption layer. Used to keep the
        /// automatic movie trace readable; the on-demand dump ignores it.
        /// </summary>
        private static bool IsCaptionPath(string path)
        {
            return path.IndexOf("aption", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("ubtitle", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Logs every visible on-screen text right now, ignoring what has been seen
        /// before. Bound to the debug text-dump key so a cutscene driven by
        /// something other than GameMovieManager — where the automatic tracer never
        /// arms — can still be inspected: watch the scene, press the key while a
        /// subtitle is showing, and the log names the object holding it.
        /// </summary>
        public static void DumpVisibleText()
        {
            MelonLogger.Msg("[TEXTDUMP] Visible on-screen text:");
            int found = SweepVisibleText("TextDump", null, captionPathsOnly: false);
            MelonLogger.Msg($"[TEXTDUMP] {found} visible text object(s).");
            ScreenReader.Say(Loc.Get("debug_text_dump", found));
        }

        /// <summary>
        /// Walks every active TMP text in the scene and logs the non-empty ones.
        /// When <paramref name="seen"/> is supplied, only first sightings are logged.
        /// Returns how many were logged.
        /// </summary>
        private static int SweepVisibleText(string label, HashSet<string> seen, bool captionPathsOnly)
        {
            int count = 0;
            try
            {
                var texts = UnityEngine.Object.FindObjectsOfType<Il2CppTMPro.TMP_Text>();
                if (texts == null)
                {
                    DebugLogger.LogState($"{label}: no TMP text components found in the scene.");
                    return 0;
                }

                foreach (var t in texts)
                {
                    if (t == null) continue;
                    if (t.gameObject == null || !t.gameObject.activeInHierarchy) continue;

                    string content = TextUtil.StripTags(t.text);
                    if (string.IsNullOrWhiteSpace(content)) continue;

                    string path = GetPath(t.transform);
                    if (captionPathsOnly && !IsCaptionPath(path)) continue;
                    if (seen != null && !seen.Add(path + " | " + content)) continue;

                    DebugLogger.LogGameValue(label, $"{path} = '{content}'");
                    count++;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"SubtitleHandler.SweepVisibleText({label}): {ex.Message}");
            }
            return count;
        }

        /// <summary>Full scene path of a transform, for identifying the text object.</summary>
        private static string GetPath(Transform t)
        {
            var parts = new List<string>();
            while (t != null && parts.Count < 12)
            {
                parts.Add(t.name);
                t = t.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        #endregion

        #region State

        /// <summary>Clears the repeat filter so the first caption in a new scene always speaks.</summary>
        public void OnSceneChanged()
        {
            _lastSpoken = "";
            _lastSpokenTime = -999f;
            _tracedTexts.Clear();
            _movieWasPlaying = false;

            // Caption objects belong to their scene — drop them and re-find.
            _selectors = null;
            _nextSelectorScan = 0f;
            _lastCaptionText.Clear();
            _visitedPresenters.Clear();
        }

        #endregion
    }
}
