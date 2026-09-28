using Il2CppGame;
using System;
using System.Collections.Generic;

namespace SO2RAccess
{
    /// <summary>
    /// Skip-ahead for long dialogue messages (setting
    /// <see cref="ModSettings.DialogueSkipAheadEnabled"/>).
    ///
    /// The problem: the handler speaks a long message in full the moment it
    /// appears, but the game shows it three lines at a time and scrolls one or
    /// two lines per confirm press. After the speech ends the player must press
    /// through box after box of text already heard, and nothing audible happens:
    /// it feels frozen (the bunny race tipster, 25 lines, log 2026-09-28 12:02).
    /// Page-by-page speech and a per-press click were both tried and rejected.
    ///
    /// The fix: the mod moves the box forward itself, through the same call a
    /// confirm press makes (<c>UIConversationPresenter.OnDecision</c>), until the
    /// box shows the message's last line. The player then presses once, as for a
    /// one-box message. Rules:
    /// - Only messages longer than one box (more than three lines).
    /// - Never past the last box: a choice or the next message still waits for
    ///   the player's own press.
    /// - While a voice line plays, it waits; voices are never cut.
    /// - One call every few frames, and only when the game is not mid-scroll.
    /// </summary>
    public partial class DialogueHandler
    {
        #region Skip-ahead fields

        /// <summary>The box shows three lines; a message with more lines needs presses to scroll.</summary>
        private const int LinesPerBox = 3;

        /// <summary>Frames between two automatic presses, so each scroll step settles.</summary>
        private const int SkipIntervalFrames = 3;

        /// <summary>Safety cap: a message never gets more automatic presses than this.</summary>
        private const int MaxSkipPresses = 200;

        private static UIConversationPresenter _longPresenter;
        private static string _lastLine;
        private static int _nextSkipFrame;
        private static int _skipPresses;

        #endregion

        /// <summary>
        /// Called from the SetMessage postfix for every new message: arms the
        /// skip-ahead when the message is longer than one box.
        /// </summary>
        private static void NoteMessage(UIConversationPresenter presenter, string cleanMessage)
        {
            _longPresenter = null;
            _lastLine = null;
            _skipPresses = 0;
            if (!ModSettings.DialogueSkipAheadEnabled || presenter == null) return;

            var lines = new List<string>();
            foreach (string line in cleanMessage.Split('\n'))
            {
                string l = line.Trim();
                if (l.Length > 0) lines.Add(l);
            }
            if (lines.Count <= LinesPerBox) return;

            _longPresenter = presenter;
            _lastLine = lines[lines.Count - 1];
            _nextSkipFrame = UnityEngine.Time.frameCount + SkipIntervalFrames;
            DebugLogger.LogState($"DialogueSkip: long message, {lines.Count} lines, last='{_lastLine}'.");
        }

        /// <summary>
        /// Called every frame from Main. Presses on for the player until the box
        /// shows the message's last line.
        /// </summary>
        public static void UpdateSkipAhead()
        {
            if (_longPresenter == null) return;
            int frame = UnityEngine.Time.frameCount;
            if (frame < _nextSkipFrame) return;
            _nextSkipFrame = frame + SkipIntervalFrames;

            try
            {
                if (_longPresenter.gameObject == null || !_longPresenter.gameObject.activeInHierarchy)
                {
                    StopSkip("conversation closed");
                    return;
                }

                var feed = _longPresenter.textFeedController;
                if (feed == null) { StopSkip("no text feed"); return; }
                var state = feed.CurrentState;
                if (state == TextFeedController.State.EndDisplay) { StopSkip("message ended"); return; }

                // A box still typing is completed by the press; a finished box that
                // already holds the last line is where the player takes over.
                if (state == TextFeedController.State.Completed
                    && LastLineOf(TextUtil.StripTags(feed.CurrentText ?? "")) == _lastLine)
                {
                    StopSkip($"last box reached after {_skipPresses} press(es)");
                    return;
                }

                if (VoicePlaying()) return;

                if (_skipPresses >= MaxSkipPresses) { StopSkip("safety cap"); return; }
                _skipPresses++;
                _longPresenter.OnDecision();
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"DialogueSkip: error, stopping: {ex.Message}");
                _longPresenter = null;
            }
        }

        private static void StopSkip(string reason)
        {
            DebugLogger.LogState($"DialogueSkip: stop ({reason}).");
            _longPresenter = null;
        }

        private static string LastLineOf(string text)
        {
            string last = "";
            foreach (string line in text.Split('\n'))
            {
                string l = line.Trim();
                if (l.Length > 0) last = l;
            }
            return last;
        }

        /// <summary>True while the conversation's voice clip plays.</summary>
        private static bool VoicePlaying()
        {
            try
            {
                if (_cachedSelector == null)
                    _cachedSelector = UnityEngine.Object.FindObjectOfType<UIConversationSelector>();
                var vc = _cachedSelector?.currentVoiceController;
                return vc != null && vc.IsPlaying();
            }
            catch
            {
                return false;
            }
        }
    }
}
