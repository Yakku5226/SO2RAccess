using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine.InputSystem;

namespace SO2RAccess
{
    /// <summary>
    /// Speaks the Fun City cooking contest on <c>UICookingMasterWindow</c>:
    ///
    /// - Cook picker (state CookSelect): a choice list owned by the window, silent
    ///   before. Title, focused name and position while the cursor is polled.
    /// - Signals (state Signal): the theme, "Begin!", "Time's up!" and the
    ///   knockouts, from the signal presenter's Set hook.
    /// - Match screen (state Information): the clock at 2:00 / 1:00 / 0:30 / 0:10,
    ///   the opponent's score gains, and both pressure gauges at every quarter,
    ///   from the HUD presenters' Set hooks. The status key (P, or L3 on the
    ///   pad) reads time, both scores, both pressures and the basket on demand.
    /// - Ingredient list (state FoodSelect): each row with the dishes it can make
    ///   and their points (the recipe panel), and the cooking count while it is
    ///   being chosen. Rows are owned here, so the universal list net stays quiet.
    /// - Rhythm game (state RhythmGame): a steady beat plus the preview pattern,
    ///   results after the bar; see CookingMasterHandler.Rhythm.cs.
    ///
    /// All cursors are native and fire no hooks, so screen state and cursors are
    /// polled each frame, the same pattern as <see cref="BunnyRaceHandler"/>.
    /// </summary>
    public partial class CookingMasterHandler
    {
        #region Fields

        private static UICookingMasterWindow _window;
        private static int _findCooldown;
        private static UIDefine.CookingMasterState _state = UIDefine.CookingMasterState.None;
        private bool _patchesApplied;

        private static bool _openingPending;
        private static float _openingDeadline;
        private const float OpeningWaitSeconds = 1.5f;
        private static int _lastIndex = -1;

        // Food select
        private static bool _wasSelectingCount;

        // HUD
        private static float _nextHudPollTime;
        private const float HudPollSeconds = 0.25f;
        private static int _lastTimerSeconds = -1;
        private static int _lastTimerThreshold = int.MaxValue;
        private static readonly int[] TimerThresholds = { 120, 60, 30, 10 };
        private static int _lastEnemyScore = -1;
        private static int _lastPlayerScore = -1;
        private static float _lastNoteResultTime = -10f;
        private static int _lastPlayerPressureStep = -1;
        private static int _lastEnemyPressureStep = -1;
        private const float PressureStepPercent = 25f;


        #endregion

        #region Patches

        /// <summary>
        /// Hooks the signal text, the HUD score and pressure setters, the cooking
        /// count, the judgement calls of the rhythm game, and the
        /// note reset that starts a new round.
        /// </summary>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(UICookingMasterWindow).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICookingMasterSignalPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICookingMasterScorePresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICookingMasterPressurePresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICookingMasterFoodActionPresenter).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICookingMasterRhythmGameSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(CookingMasterRhythmGameManager).TypeHandle);

                var signalState = typeof(UICookingMasterSignalSelector.SignalState);
                var self = typeof(CookingMasterHandler);

                harmony.Patch(AccessTools.Method(typeof(UICookingMasterSignalPresenter), "Set",
                        new[] { typeof(string), signalState }),
                    postfix: new HarmonyMethod(self, nameof(SignalOne_Postfix)));
                harmony.Patch(AccessTools.Method(typeof(UICookingMasterSignalPresenter), "Set",
                        new[] { typeof(string), typeof(string), signalState }),
                    postfix: new HarmonyMethod(self, nameof(SignalPair_Postfix)));

                harmony.Patch(AccessTools.Method(typeof(UICookingMasterScorePresenter),
                        nameof(UICookingMasterScorePresenter.SetScore),
                        new[] { typeof(int), typeof(float), typeof(bool) }),
                    postfix: new HarmonyMethod(self, nameof(SetScore_Postfix)));
                harmony.Patch(AccessTools.Method(typeof(UICookingMasterPressurePresenter),
                        nameof(UICookingMasterPressurePresenter.SetPlayerPressure),
                        new[] { typeof(float), typeof(float) }),
                    postfix: new HarmonyMethod(self, nameof(SetPlayerPressure_Postfix)));
                harmony.Patch(AccessTools.Method(typeof(UICookingMasterPressurePresenter),
                        nameof(UICookingMasterPressurePresenter.SetEnemyPressure),
                        new[] { typeof(float), typeof(float) }),
                    postfix: new HarmonyMethod(self, nameof(SetEnemyPressure_Postfix)));

                harmony.Patch(AccessTools.Method(typeof(UICookingMasterFoodActionPresenter),
                        nameof(UICookingMasterFoodActionPresenter.SetCreateCount), new[] { typeof(int) }),
                    postfix: new HarmonyMethod(self, nameof(SetCreateCount_Postfix)));

                harmony.Patch(AccessTools.Method(typeof(UICookingMasterRhythmGameSelector),
                        nameof(UICookingMasterRhythmGameSelector.SetupCookingResult),
                        new[] { typeof(int), typeof(int), typeof(int), typeof(NotesResultType) }),
                    postfix: new HarmonyMethod(self, nameof(CookingResult_Postfix)));
                harmony.Patch(AccessTools.Method(typeof(UICookingMasterRhythmGameSelector),
                        nameof(UICookingMasterRhythmGameSelector.SetupPerfectBonusResult), Type.EmptyTypes),
                    postfix: new HarmonyMethod(self, nameof(PerfectBonus_Postfix)));
                harmony.Patch(AccessTools.Method(typeof(CookingMasterRhythmGameManager),
                        nameof(CookingMasterRhythmGameManager.SettingNotes), Type.EmptyTypes),
                    postfix: new HarmonyMethod(self, nameof(SettingNotes_Postfix)));

                _patchesApplied = true;
                MelonLogger.Msg("[COOKING] Patches applied.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[COOKING] Patch error: {ex.Message}");
            }
        }

        private static void SignalOne_Postfix(string signal)
        {
            SpeakSignal(TextUtil.StripTags(signal ?? ""));
        }

        private static void SignalPair_Postfix(string left, string right)
        {
            SpeakSignal(TextUtil.JoinSentences(new[] { TextUtil.StripTags(left ?? ""), TextUtil.StripTags(right ?? "") }));
        }

        private static void SpeakSignal(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            ScreenReader.Say(text);
            DebugLogger.LogState($"Cooking: signal '{text}'.");
        }

        /// <summary>
        /// Opponent gains are spoken with the new total. The player's gains come
        /// out of the note judgements; only a gain outside one (the perfect bonus)
        /// is spoken here.
        /// </summary>
        private static void SetScore_Postfix(UICookingMasterScorePresenter __instance, int score)
        {
            try
            {
                var info = _window?.informationSelector;
                if (info == null || __instance == null) return;

                if (IsSame(__instance, info.enemyScorePresenter))
                {
                    if (_lastEnemyScore >= 0 && score > _lastEnemyScore)
                        ScreenReader.SayQueued(Loc.Get("cook_enemy_score", score - _lastEnemyScore, score));
                    _lastEnemyScore = score;
                }
                else if (IsSame(__instance, info.playerScorePresenter))
                {
                    bool fromNote = UnityEngine.Time.unscaledTime - _lastNoteResultTime < 1.5f;
                    if (_lastPlayerScore >= 0 && score > _lastPlayerScore && !fromNote)
                        ScreenReader.SayQueued(Loc.Get("cook_player_score", score - _lastPlayerScore, score));
                    _lastPlayerScore = score;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"Cooking: SetScore hook error: {ex.Message}");
            }
        }

        private static void SetPlayerPressure_Postfix(float playerPressure, float maxPressure)
        {
            AnnouncePressureStep(ref _lastPlayerPressureStep, playerPressure, maxPressure, "cook_player_pressure");
        }

        private static void SetEnemyPressure_Postfix(float enemyPressure, float maxPressure)
        {
            AnnouncePressureStep(ref _lastEnemyPressureStep, enemyPressure, maxPressure, "cook_enemy_pressure");
        }

        /// <summary>Speaks a gauge when it crosses a quarter, up or down; the gauge animates, so steps are compared, not values.</summary>
        private static void AnnouncePressureStep(ref int lastStep, float value, float max, string key)
        {
            try
            {
                if (max <= 0f) return;
                int percent = (int)Math.Round(value / max * 100f);
                int step = (int)(percent / PressureStepPercent);
                if (lastStep < 0) { lastStep = step; return; }
                if (step == lastStep) return;
                lastStep = step;
                ScreenReader.SayQueued(Loc.Get(key, (int)(step * PressureStepPercent)));
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"Cooking: pressure hook error: {ex.Message}");
            }
        }

        private static void SetCreateCount_Postfix(int count)
        {
            if (_state != UIDefine.CookingMasterState.FoodSelect) return;
            if (_window?.foodSelector == null || !_window.foodSelector.isSelectItemCount) return;
            ScreenReader.Say(count.ToString());
        }

        private static bool IsSame(Il2CppSystem.Object a, Il2CppSystem.Object b)
        {
            return a != null && b != null && a.Pointer == b.Pointer;
        }

        #endregion

        /// <summary>Called on scene change to drop cached references.</summary>
        public void OnSceneChanged()
        {
            _window = null;
            _findCooldown = 0;
            ResetScreenState(UIDefine.CookingMasterState.None);
            ResetMatch();
        }

        #region Update Loop

        /// <summary>Called every frame from Main.UpdateHandlers().</summary>
        public void Update()
        {
            DetectWindow();
            if (_window == null || _state == UIDefine.CookingMasterState.None) return;

            try
            {
                if (_state != UIDefine.CookingMasterState.CookSelect && StatusKeyPressed())
                {
                    AnnounceStatus();
                    return;
                }

                switch (_state)
                {
                    case UIDefine.CookingMasterState.CookSelect:
                        UpdateCookPicker();
                        break;
                    case UIDefine.CookingMasterState.FoodSelect:
                        UpdateFoodSelect();
                        break;
                    case UIDefine.CookingMasterState.Information:
                        UpdateHud();
                        break;
                    case UIDefine.CookingMasterState.RhythmGame:
                        UpdateRhythm();
                        break;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CookingMasterHandler.Update: {ex.Message}");
            }
        }

        /// <summary>Finds the window lazily (throttled; inactive while closed) and follows its state.</summary>
        private void DetectWindow()
        {
            if (_window == null)
            {
                if (_findCooldown > 0) { _findCooldown--; return; }
                _findCooldown = 60;

                try
                {
                    var found = UnityEngine.Object.FindObjectsOfType<UICookingMasterWindow>(true);
                    if (found != null && found.Length > 0) _window = found[0];
                    if (_window != null) DebugLogger.LogState("Cooking: cached UICookingMasterWindow reference.");
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"Cooking: detection error: {ex.Message}");
                }
                return;
            }

            try
            {
                // The selector stack knows the screen on top; the Open… property only
                // records the first screen pushed (coliseum log 2026-09-28 11:39).
                var state = _window.IsOpened
                    ? (UIDefine.CookingMasterState)_window.GetCurrentState()
                    : UIDefine.CookingMasterState.None;
                if (state == _state) return;

                DebugLogger.LogState($"Cooking: screen {_state} -> {state}.");
                var previous = _state;
                ResetScreenState(state);

                if (state == UIDefine.CookingMasterState.None || state == UIDefine.CookingMasterState.CookSelect)
                    ResetMatch();

                if (state == UIDefine.CookingMasterState.CookSelect || state == UIDefine.CookingMasterState.FoodSelect)
                {
                    _openingPending = true;
                    _openingDeadline = UnityEngine.Time.unscaledTime + OpeningWaitSeconds;
                }
                if (previous == UIDefine.CookingMasterState.RhythmGame) EndRhythmScreen();
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"Cooking: detection error: {ex.Message}");
                _window = null;
                _findCooldown = 0;
                ResetScreenState(UIDefine.CookingMasterState.None);
            }
        }

        private static void ResetScreenState(UIDefine.CookingMasterState state)
        {
            _state = state;
            _openingPending = false;
            _lastIndex = -1;
            _wasSelectingCount = false;
        }

        /// <summary>Forgets the per-match trackers (clock thresholds, scores, pressure steps, rhythm bar).</summary>
        private static void ResetMatch()
        {
            _lastTimerSeconds = -1;
            _lastTimerThreshold = int.MaxValue;
            _lastEnemyScore = -1;
            _lastPlayerScore = -1;
            _lastPlayerPressureStep = -1;
            _lastEnemyPressureStep = -1;
            ResetRhythm();
        }

        private static bool OpeningReady(bool hasData)
        {
            if (!_openingPending) return false;
            if (!hasData && UnityEngine.Time.unscaledTime < _openingDeadline) return false;
            _openingPending = false;
            return true;
        }

        #endregion

        #region Cook picker

        private static void UpdateCookPicker()
        {
            var selector = _window.cookListSelector;
            if (selector == null) return;
            var choices = selector.choiceDataList;
            int count = choices?.Count ?? 0;

            if (_openingPending)
            {
                if (!OpeningReady(count > 0)) return;
                int idx = selector.selectChoiceIndex;
                _lastIndex = idx;
                string title = ReadText(selector.cookChoicePresenter?.title);
                if (title.Length == 0) title = Loc.Get("cook_pick_heading");
                ScreenReader.Say(title + " " + DescribeChoice(choices, idx, count));
                DebugLogger.LogState($"Cooking: picker opened (state={selector.selectState}, choices={count}, index={idx}).");
                return;
            }

            int current = selector.selectChoiceIndex;
            if (current == _lastIndex) return;
            _lastIndex = current;
            ScreenReader.Say(DescribeChoice(choices, current, count));
        }

        private static string DescribeChoice(Il2CppSystem.Collections.Generic.List<UIChoiceData> choices, int index, int count)
        {
            if (index < 0 || index >= count) return "";
            var d = choices[index];
            var sb = new StringBuilder(TextUtil.StripTags(d?.message ?? ""));
            if (d != null && !d.canDecision) sb.Append(", ").Append(Loc.Get("cook_cannot_enter"));
            TextUtil.AppendPosition(sb, index, count);
            return sb.ToString();
        }

        #endregion

        #region Food select

        /// <summary>Heading and first row on opening, rows on cursor moves, "choose the count" when the count cursor appears.</summary>
        private static void UpdateFoodSelect()
        {
            var selector = _window.foodSelector;
            if (selector == null) return;

            if (_openingPending)
            {
                if (!OpeningReady(selector.DataCount > 0)) return;
                int idx = selector.CurrentIndex;
                _lastIndex = idx;
                ScreenReader.Say(Loc.Get("cook_food_heading") + " " + DescribeFoodRow(selector, idx));
                return;
            }

            bool selectingCount = selector.isSelectItemCount;
            if (selectingCount != _wasSelectingCount)
            {
                _wasSelectingCount = selectingCount;
                if (selectingCount)
                {
                    string count = ReadText(selector.actionPresenter?.createCount);
                    ScreenReader.Say(count.Length > 0 ? Loc.Get("cook_count_prompt", count) : Loc.Get("cook_count_prompt_plain"));
                    return;
                }
            }

            int current = selector.CurrentIndex;
            if (current == _lastIndex) return;
            _lastIndex = current;
            string text = DescribeFoodRow(selector, current);
            if (text.Length > 0) ScreenReader.Say(text);
        }

        /// <summary>"Gelatinous Slime, 62 owned. Makes Amoeba Soup plus 50, Slimy Gelatin plus 30, new. 1 of 3."</summary>
        private static string DescribeFoodRow(UICookingMasterFoodSelector selector, int index)
        {
            var list = selector.currentDataList;
            int count = list?.Count ?? 0;
            if (index < 0 || index >= count) return "";
            var data = list[index]?.TryCast<UICookingMasterFoodActionListItemData>();
            if (data == null) return "";

            var sb = new StringBuilder();
            sb.Append(Loc.Get("cook_food_row", TextUtil.StripTags(data.itemName ?? ""), data.count));
            if (data.isLuxury) sb.Append(' ').Append(Loc.Get("cook_food_luxury"));

            var dishes = new List<string>();
            var info = data.informationData?.dataList;
            int dishCount = info?.Count ?? 0;
            for (int i = 0; i < dishCount; i++)
            {
                var d = info[i];
                if (d == null) continue;
                string name = TextUtil.StripTags(d.itemName ?? "");
                if (name.Length == 0) continue;
                string entry = Loc.Get("cook_dish", name, d.score);
                if (!d.isAlreadyCreated) entry += " " + Loc.Get("cook_dish_new");
                dishes.Add(entry);
            }
            if (dishes.Count > 0) sb.Append(' ').Append(Loc.Get("cook_makes", string.Join(", ", dishes)));
            else sb.Append(' ').Append(Loc.Get("cook_makes_nothing"));

            TextUtil.AppendPosition(sb, index, count);
            return sb.ToString();
        }

        #endregion

        #region HUD

        /// <summary>Calls the clock at the fixed thresholds; everything else on the HUD arrives through hooks.</summary>
        private static void UpdateHud()
        {
            float now = UnityEngine.Time.unscaledTime;
            if (now < _nextHudPollTime) return;
            _nextHudPollTime = now + HudPollSeconds;

            int seconds = TimerSeconds();
            if (seconds < 0) return;

            // The clock was reset upwards: a new match.
            if (_lastTimerSeconds >= 0 && seconds > _lastTimerSeconds + 5) _lastTimerThreshold = int.MaxValue;
            _lastTimerSeconds = seconds;

            foreach (int threshold in TimerThresholds)
            {
                if (threshold >= _lastTimerThreshold || seconds > threshold) continue;
                _lastTimerThreshold = threshold;
                ScreenReader.SayQueued(threshold >= 60
                    ? Loc.Get("cook_time_minutes", threshold / 60)
                    : Loc.Get("cook_time_seconds", threshold));
                break;
            }
        }

        /// <summary>Seconds left from the "mm:ss" clock text, -1 when unreadable.</summary>
        private static int TimerSeconds()
        {
            string text = ReadText(_window?.informationSelector?.timerPresenter?.timerText);
            var parts = text.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int m) || !int.TryParse(parts[1], out int s)) return -1;
            return m * 60 + s;
        }

        /// <summary>The status key: P on the keyboard, or L3 without the L2 modifier on the pad.</summary>
        private static bool StatusKeyPressed()
        {
            try
            {
                var kb = Keyboard.current;
                if (kb != null && kb[ModKeys.MiniGameStatus].wasPressedThisFrame) return true;
                var gp = Gamepad.current;
                return gp != null && ModKeys.ModMenuChord(gp).wasPressedThisFrame && !ModKeys.NavModifier(gp).isPressed;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"Cooking: status key error: {ex.Message}");
                return false;
            }
        }

        /// <summary>"Time 2:33. You 0, opponent 1000. Your pressure 10 percent, opponent 0. Ingredients 62 of 80."</summary>
        private static void AnnounceStatus()
        {
            var info = _window.informationSelector;
            var parts = new List<string>();

            string time = ReadText(info?.timerPresenter?.timerText);
            if (time.Length > 0) parts.Add(Loc.Get("cook_status_time", time));

            int mine = info?.playerScorePresenter?.currentScore ?? -1;
            int theirs = info?.enemyScorePresenter?.currentScore ?? -1;
            if (mine >= 0 || theirs >= 0) parts.Add(Loc.Get("cook_status_scores", Math.Max(mine, 0), Math.Max(theirs, 0)));

            try
            {
                var manager = CookingMasterManager.Instance;
                float max = manager?.MaxPressure ?? 0f;
                if (manager != null && max > 0f)
                {
                    int p = (int)Math.Round((manager.PlayerParameter?.Pressure ?? 0f) / max * 100f);
                    int e = (int)Math.Round((manager.EnemyCook?.Parameter?.Pressure ?? 0f) / max * 100f);
                    parts.Add(Loc.Get("cook_status_pressure", p, e));
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"Cooking: pressure status unreadable: {ex.Message}");
            }

            string basket = ReadText(info?.basketPresenter?.basketText);
            if (basket.Length > 0) parts.Add(Loc.Get("cook_status_basket", basket.Replace("/", " " + Loc.Get("cook_of") + " ")));

            ScreenReader.Say(parts.Count > 0 ? TextUtil.JoinSentences(parts) : Loc.Get("cook_status_unavailable"));
        }

        #endregion

        #region Helpers

        /// <summary>Visible text of a GameText with rich-text tags stripped, "" when absent.</summary>
        private static string ReadText(GameText gt)
        {
            if (gt == null) return "";
            try
            {
                string raw = ((Il2CppTMPro.TMP_Text)gt)?.text;
                return TextUtil.StripTags(raw ?? "") ?? "";
            }
            catch
            {
                return "";
            }
        }

        #endregion
    }
}
