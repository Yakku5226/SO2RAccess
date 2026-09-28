using Il2CppGame;
using System;
using System.Collections.Generic;

namespace SO2RAccess
{
    /// <summary>
    /// Rhythm cues for the cooking contest.
    ///
    /// How the game's rhythm works (log 2026-09-28 12:07-12:10): one cooking is a
    /// bar of 16 steps. Every ingredient becomes a note on one step; the judge
    /// frame is <c>CalcJustFrame(notesNo)</c>, e.g. notes 4/8/12/16 at frames
    /// 75/150/225/300, or a syncopated bar 5/6/8/10/11/12/14/16 at 63..204. The
    /// bar length shrinks as pressure changes, so the tempo differs per cooking.
    /// The game first plays the bar as a preview (manager state PlacementNotes),
    /// then the player repeats it (state Playing). The window is tight: about six
    /// frames either side of the judge frame.
    ///
    /// The first design (three 0.5 s ticks before each note) broke down as soon
    /// as notes came closer than 1.5 s: the countdowns of neighbouring notes
    /// interleaved, and the per-note hook fired every frame. This version follows
    /// the game's own structure instead:
    ///
    /// - A falling "listen" chirp on the first beat of the preview. The play bar
    ///   follows with no gap; the screen reader says "Your turn" half a second
    ///   before it starts, and its first step stays silent (the preview's last
    ///   note sounds there).
    /// - A steady tick on every beat (every fourth step) through the preview and
    ///   the play bar, so the tempo is always audible.
    /// - During the preview a note tone on every step that holds a note, in time:
    ///   the pattern to repeat.
    /// - The play bar sounds exactly like the preview (ticks and note tones at
    ///   the same steps): the player presses together with the note tones.
    /// - Judgements are collected and spoken once after the bar, so speech never
    ///   talks over the beat.
    /// </summary>
    public partial class CookingMasterHandler
    {
        #region Rhythm fields

        private static CookingMasterRhythmGameManager.State _rhythmState = CookingMasterRhythmGameManager.State.Invalid;

        /// <summary>Judge frame of every step 0..max, for the current bar; null until built.</summary>
        private static int[] _stepFrames;

        /// <summary>Steps that hold a note in the current bar.</summary>
        private static readonly HashSet<int> _noteSteps = new HashSet<int>();

        /// <summary>Last step whose cue was handled, -1 at the start of a pass.</summary>
        private static int _lastStep = -1;

        /// <summary>Steps per beat: the bar has four beats.</summary>
        private const int BeatsPerBar = 4;

        /// <summary>Bar length in steps when the game's constant cannot be read (every log so far: 16).</summary>
        private const int DefaultStepsPerBar = 16;

        private struct NoteResult
        {
            public NotesResultType Type;
            public int ItemId;
            public int Points;
        }

        private static readonly List<NoteResult> _roundResults = new List<NoteResult>();

        private static bool _perfectBonus;

        /// <summary>How long before the turn "Your turn" starts, so the words end about when the turn begins.</summary>
        private const float TurnPromptLeadSeconds = 0.5f;

        private static bool _turnPromptSpoken;

        #endregion

        #region Rhythm hooks

        /// <summary>Records one judgement; spoken with the others after the bar.</summary>
        private static void CookingResult_Postfix(int notesNumber, int cookedItemID, int addScore, NotesResultType resultType)
        {
            _lastNoteResultTime = UnityEngine.Time.unscaledTime;
            _roundResults.Add(new NoteResult { Type = resultType, ItemId = cookedItemID, Points = addScore });
            DebugLogger.LogState($"Cooking: note {notesNumber} {resultType} item={cookedItemID} +{addScore} "
                + $"(frame {CurrentRhythmFrame()}).");
        }

        private static void PerfectBonus_Postfix()
        {
            _perfectBonus = true;
        }

        /// <summary>A new bar is being set up: forget the previous one.</summary>
        private static void SettingNotes_Postfix()
        {
            _stepFrames = null;
            _noteSteps.Clear();
            _lastStep = -1;
            _roundResults.Clear();
            _perfectBonus = false;
        }

        #endregion

        #region Rhythm loop

        /// <summary>
        /// Every frame while the rhythm screen is open: follows the manager's state
        /// and plays the beat and preview cues when its frame clock crosses a step.
        /// </summary>
        private static void UpdateRhythm()
        {
            var rhythm = CookingMasterManager.Instance?.RhythmGameManager;
            if (rhythm == null) return;

            var state = rhythm.currentState;
            if (state != _rhythmState)
            {
                OnRhythmStateChanged(rhythm, _rhythmState, state);
                _rhythmState = state;
            }

            bool preview = state == CookingMasterRhythmGameManager.State.PlacementNotes;
            bool playing = state == CookingMasterRhythmGameManager.State.Playing;
            if ((!preview && !playing) || _stepFrames == null) return;

            int frame = rhythm.CurrentFrame;
            if (preview) MaybeSpeakTurnPrompt(rhythm, frame);
            int step = StepAt(frame);
            if (step <= _lastStep) return;

            // Normally one step per call; after a hitch only the newest step sounds.
            _lastStep = step;
            bool isNote = _noteSteps.Contains(step);
            bool isBeat = step % StepsPerBeat() == 0;

            // The play pass starts the instant the preview ends (log 2026-09-28 13:25:
            // PlacementNotes -> Playing at frame 0, no gap). Every bar so far ends
            // with a note on step 16, which sounds at that same instant, and winmm
            // plays one sound at a time: a "go" chirp there cut the last note off
            // (all 10 bars, log 14:04). The turn is announced by speech instead
            // (MaybeSpeakTurnPrompt, separate audio path), so its step 0 is silent.
            // Both passes sound the same, so the turn is a replay of the preview: the
            // player presses together with the note tones they just heard.
            if (step == 0) { if (preview) AudioCuePlayer.PlayCookingListen(); }
            else if (isNote) AudioCuePlayer.PlayCookingHit();
            else if (isBeat) AudioCuePlayer.PlayCookingTick();
        }

        /// <summary>
        /// Says "Your turn" once per preview, starting half a second before the
        /// preview ends, so the words finish about when the turn's bar starts. The
        /// turn follows with no gap and a note can sit on its first steps, so a
        /// prompt at the turn itself would come too late. Speech runs through the
        /// screen reader and cannot cut the note tones.
        /// </summary>
        private static void MaybeSpeakTurnPrompt(CookingMasterRhythmGameManager rhythm, int frame)
        {
            if (_turnPromptSpoken || _stepFrames == null) return;
            float fps = rhythm.Fps;
            if (fps < 10f || fps > 300f) fps = 60f;
            int end = _stepFrames[_stepFrames.Length - 1];
            int lead = (int)Math.Round(TurnPromptLeadSeconds * fps);
            if (frame < end - lead) return;
            _turnPromptSpoken = true;
            ScreenReader.Say(Loc.Get("cook_your_turn"));
            DebugLogger.LogState($"Cooking: turn prompt at preview frame {frame} (bar ends {end}, lead {lead}).");
        }

        /// <summary>Builds the bar on entering a pass, and speaks the results when the play pass ends.</summary>
        private static void OnRhythmStateChanged(CookingMasterRhythmGameManager rhythm,
            CookingMasterRhythmGameManager.State from, CookingMasterRhythmGameManager.State to)
        {
            DebugLogger.LogState($"Cooking: rhythm {from} -> {to} at frame {SafeFrame(rhythm)}.");

            if (to == CookingMasterRhythmGameManager.State.PlacementNotes
                || to == CookingMasterRhythmGameManager.State.Playing)
            {
                BuildBar(rhythm);
                _lastStep = -1;
            }
            if (to == CookingMasterRhythmGameManager.State.PlacementNotes) _turnPromptSpoken = false;

            if (from == CookingMasterRhythmGameManager.State.Playing) SpeakRoundSummary();
        }

        /// <summary>
        /// Reads the judge frame of every step and the note steps of this bar. The
        /// step frames come from the game's own <c>CalcJustFrame</c>, so the beat
        /// is rounded exactly like the notes; the notes' own frames are compared
        /// against it and any mismatch is logged.
        /// </summary>
        private static void BuildBar(CookingMasterRhythmGameManager rhythm)
        {
            int steps = StepsPerBar();
            var frames = new int[steps + 1];
            try
            {
                for (int k = 0; k <= steps; k++) frames[k] = rhythm.CalcJustFrame(k);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"Cooking: CalcJustFrame failed ({ex.Message}), deriving the bar from the notes.");
                frames = null;
            }

            _noteSteps.Clear();
            var notes = rhythm.currentNotes;
            int count = notes?.Count ?? 0;
            var parts = new List<string>();
            float stepSize = 0f;
            for (int i = 0; i < count; i++)
            {
                var n = notes[i];
                if (n == null) continue;
                int no = n.NotesNo;
                _noteSteps.Add(no);
                parts.Add($"{no}@{n.JustFrame}");
                if (no > 0) stepSize = (float)n.JustFrame / no;
                if (frames != null && no >= 0 && no <= steps && frames[no] != n.JustFrame)
                    DebugLogger.LogState($"Cooking: note {no} judge frame {n.JustFrame} differs from step frame {frames[no]}.");
            }

            if (frames == null && stepSize > 0f)
            {
                frames = new int[steps + 1];
                for (int k = 0; k <= steps; k++) frames[k] = (int)Math.Round(k * stepSize);
            }

            _stepFrames = frames;
            DebugLogger.LogState($"Cooking: bar built, {steps} steps, beat every {StepsPerBeat()}, "
                + $"frames=[{(frames == null ? "none" : string.Join(",", frames))}] notes=[{string.Join(" ", parts)}].");
        }

        /// <summary>Highest step whose judge frame has been reached, -1 before step 0.</summary>
        private static int StepAt(int frame)
        {
            int step = -1;
            for (int k = 0; k < _stepFrames.Length; k++)
            {
                if (_stepFrames[k] > frame) break;
                step = k;
            }
            return step;
        }

        private static int StepsPerBar()
        {
            try
            {
                int max = CookingMasterRhythmGameManager.maxNotesNo;
                if (max >= BeatsPerBar && max <= 64) return max;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"Cooking: maxNotesNo unreadable: {ex.Message}");
            }
            return DefaultStepsPerBar;
        }

        private static int StepsPerBeat() => Math.Max(1, StepsPerBar() / BeatsPerBar);

        private static int SafeFrame(CookingMasterRhythmGameManager rhythm)
        {
            try { return rhythm.CurrentFrame; } catch { return -1; }
        }

        private static int CurrentRhythmFrame()
        {
            var rhythm = CookingMasterManager.Instance?.RhythmGameManager;
            return rhythm == null ? -1 : SafeFrame(rhythm);
        }

        #endregion

        #region Round summary

        /// <summary>
        /// "4 of 5 cooked: 2 perfect, 2 early, 1 missed. Plus 95. Made Amoeba Soup,
        /// Slimy Gelatin." Early and late tell the player which way to correct.
        /// </summary>
        private static void SpeakRoundSummary()
        {
            if (_roundResults.Count == 0) return;

            int perfect = 0, early = 0, late = 0, miss = 0, points = 0;
            var dishes = new List<string>();
            foreach (var r in _roundResults)
            {
                switch (r.Type)
                {
                    case NotesResultType.JUST_SUCCESS:
                    case NotesResultType.LATE_JUST_SUCCESS:
                        perfect++; break;
                    case NotesResultType.VERY_FAST_SUCCESS:
                    case NotesResultType.FAST_SUCCESS:
                        early++; break;
                    case NotesResultType.LATE_SUCCESS:
                    case NotesResultType.VERY_LATE_SUCCESS:
                        late++; break;
                    default:
                        miss++; break;
                }
                if (!CookingMasterManager.IsSuccess(r.Type)) continue;
                points += r.Points;
                // The card on screen shows only the points ("+40"), so the name comes
                // from the item text; ResolveItemName never returns a bare id.
                string dish = TextUtil.ResolveItemName(r.ItemId);
                if (string.IsNullOrEmpty(dish))
                {
                    DebugLogger.LogState($"Cooking: no name for dish item {r.ItemId}.");
                    dish = Loc.Get("cook_unknown_dish");
                }
                if (!dishes.Contains(dish)) dishes.Add(dish);
            }

            var counts = new List<string>();
            if (perfect > 0) counts.Add(Loc.Get("cook_sum_perfect", perfect));
            if (early > 0) counts.Add(Loc.Get("cook_sum_early", early));
            if (late > 0) counts.Add(Loc.Get("cook_sum_late", late));
            if (miss > 0) counts.Add(Loc.Get("cook_sum_miss", miss));

            var parts = new List<string>
            {
                Loc.Get("cook_round_summary", _roundResults.Count - miss, _roundResults.Count,
                    string.Join(", ", counts), points),
            };
            if (dishes.Count > 0) parts.Add(Loc.Get("cook_round_dishes", string.Join(", ", dishes)));
            if (_perfectBonus) parts.Add(Loc.Get("cook_perfect_bonus"));

            ScreenReader.Say(TextUtil.JoinSentences(parts));
            _roundResults.Clear();
            _perfectBonus = false;
        }

        private static bool IsAllDigits(string text)
        {
            foreach (char c in text) if (c < '0' || c > '9') return false;
            return text.Length > 0;
        }

        /// <summary>Screen left the rhythm game: speak anything not yet spoken and reset the pass.</summary>
        private static void EndRhythmScreen()
        {
            SpeakRoundSummary();
            ResetRhythm();
        }

        /// <summary>Forgets the bar and the pass state.</summary>
        private static void ResetRhythm()
        {
            _rhythmState = CookingMasterRhythmGameManager.State.Invalid;
            _stepFrames = null;
            _noteSteps.Clear();
            _lastStep = -1;
            _roundResults.Clear();
            _perfectBonus = false;
        }

        #endregion
    }
}
