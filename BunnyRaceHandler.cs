using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace SO2RAccess
{
    /// <summary>
    /// Speaks the Fun City bunny race screens on <c>UIBunnyRaceWindow</c>:
    ///
    /// - Medal shop (state MedalShop): a digit entry, not a list, so nothing spoke
    ///   there before. Opening reads the game's question, the medals and Fol owned
    ///   and the price; each press reads the new count and its cost, or the limit
    ///   when the game refuses (log 2026-09-27 21:15, count 3 refused at limit 2).
    /// - Bet screen (state Bet): the medal count on opening, the paddock bunny
    ///   (number, name, speed, stamina, trait, and its condition only while the
    ///   game shows the condition icon) on every L1/R1 trigger press, and the bet
    ///   rows as "Bunnies 1 and 2: prize x count" with the tipster's mark. The rows
    ///   are owned here, so the universal list net stays quiet on them.
    /// - Race (state NumberFlag): the running order every few seconds and at once
    ///   when the leader changes, read from the race manager's per-bunny progress.
    ///   A sighted player sees the bunnies run, so this is fair information. The
    ///   commentary lines are spoken by the dialogue handler as before.
    ///
    /// Cursor movement on these screens is native and fires no hooks, so screen
    /// state and the bet cursor are polled each frame, the same pattern as
    /// <see cref="FishingBaitHandler"/>. The medal count and the paddock switch
    /// have hookable methods and are announced from postfixes.
    /// </summary>
    public class BunnyRaceHandler
    {
        #region Fields

        private static UIBunnyRaceWindow _window;
        private static int _findCooldown;
        private static UIDefine.BunnyRaceState _state = UIDefine.BunnyRaceState.None;
        private bool _patchesApplied;

        // Medal shop
        private static int _shopLastCount = -1;
        private static float _shopLimitSpokenTime = -1f;
        private const float ShopLimitRepeatSeconds = 0.6f;

        // Bet screen
        private static bool _betOpeningPending;
        private static float _betOpeningDeadline;
        private static float _betSettleUntil = -1f;
        private static int _betLastIndex = -1;
        private const float BetOpeningWaitSeconds = 1.5f;

        /// <summary>
        /// The game moves the cursor by itself right after the list appears (log
        /// 2026-09-28 11:25:41: row 1 then row 12 within 16 ms), so the opening read
        /// waits this long after the list is populated before reading the cursor.
        /// </summary>
        private const float BetSettleSeconds = 0.3f;
        private static bool _betDataLogged;

        // Race standings
        private static float _raceNextSpeakTime;
        private static float _raceLastSpeakTime = -100f;
        private static string _raceLastOrder = "";
        private static int _raceLastLeader = -1;
        private const float RaceFirstDelaySeconds = 3f;
        private const float RaceRepeatSeconds = 8f;
        private const float RaceChangeGapSeconds = 2f;
        private static float _raceNextPollTime;
        private const float RacePollSeconds = 0.5f;

        #endregion

        #region Patches

        /// <summary>
        /// Hooks the medal shop's count change and the bet screen's paddock switch.
        /// Both were proven to fire in the 2026-09-27 survey.
        /// </summary>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(UIBunnyRaceWindow).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIBunnyRaceMedalShopSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIBunnyRaceBetSelector).TypeHandle);

                harmony.Patch(
                    AccessTools.Method(typeof(UIBunnyRaceMedalShopSelector),
                        nameof(UIBunnyRaceMedalShopSelector.UpdateMedalCount),
                        new[] { typeof(int), typeof(bool) }),
                    postfix: new HarmonyMethod(typeof(BunnyRaceHandler), nameof(UpdateMedalCount_Postfix)));

                harmony.Patch(
                    AccessTools.Method(typeof(UIBunnyRaceBetSelector),
                        nameof(UIBunnyRaceBetSelector.SelectCharaInfo),
                        new[] { typeof(bool) }),
                    postfix: new HarmonyMethod(typeof(BunnyRaceHandler), nameof(SelectCharaInfo_Postfix)));

                _patchesApplied = true;
                MelonLogger.Msg("[BUNNYRACE] Patches applied.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[BUNNYRACE] Patch error: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for UpdateMedalCount: the game has already clamped the request
        /// into <c>buyMedalCount</c>. A refused step speaks the limit; an accepted
        /// step speaks the count and its cost. Holding a key repeats the call with
        /// the same value, which stays silent.
        /// </summary>
        private static void UpdateMedalCount_Postfix(UIBunnyRaceMedalShopSelector __instance,
            int newMedalCount, bool isUp)
        {
            try
            {
                if (__instance == null) return;
                int accepted = __instance.buyMedalCount;
                float now = UnityEngine.Time.unscaledTime;

                if (newMedalCount != accepted)
                {
                    // Refused: above the purchase limit or below zero.
                    if (now - _shopLimitSpokenTime < ShopLimitRepeatSeconds) return;
                    _shopLimitSpokenTime = now;
                    ScreenReader.Say(newMedalCount > accepted
                        ? Loc.Get("bunny_shop_max", accepted)
                        : Loc.Get("bunny_shop_min"));
                    DebugLogger.LogState($"BunnyRace: medal count {newMedalCount} refused, stays {accepted}.");
                    return;
                }

                if (accepted == _shopLastCount) return;
                _shopLastCount = accepted;
                ScreenReader.Say(Loc.Get("bunny_shop_count", accepted, accepted * MedalPrice()));
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BunnyRace: UpdateMedalCount hook error: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for SelectCharaInfo (L1/R1 on the bet screen): the paddock panel
        /// has already been refilled for the new bunny, so it is read right away.
        /// </summary>
        private static void SelectCharaInfo_Postfix(UIBunnyRaceBetSelector __instance, bool isRight)
        {
            try
            {
                if (__instance == null || _state != UIDefine.BunnyRaceState.Bet) return;
                string text = DescribePaddock(__instance);
                if (text.Length == 0) return;
                ScreenReader.Say(text);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BunnyRace: SelectCharaInfo hook error: {ex.Message}");
            }
        }

        #endregion

        /// <summary>Called on scene change to drop cached references.</summary>
        public void OnSceneChanged()
        {
            _window = null;
            _findCooldown = 0;
            ResetScreenState(UIDefine.BunnyRaceState.None);
        }

        #region Update Loop

        /// <summary>Called every frame from Main.UpdateHandlers().</summary>
        public void Update()
        {
            DetectWindow();
            if (_window == null || _state == UIDefine.BunnyRaceState.None) return;

            try
            {
                switch (_state)
                {
                    case UIDefine.BunnyRaceState.Bet:
                        UpdateBet();
                        break;
                    case UIDefine.BunnyRaceState.NumberFlag:
                        UpdateRace();
                        break;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"BunnyRaceHandler.Update: {ex.Message}");
            }
        }

        /// <summary>
        /// Finds the bunny race window lazily (throttled; it only exists in the Fun
        /// City scenes and is inactive while closed) and follows its open state.
        /// </summary>
        private void DetectWindow()
        {
            if (_window == null)
            {
                if (_findCooldown > 0) { _findCooldown--; return; }
                _findCooldown = 60;

                try
                {
                    var found = UnityEngine.Object.FindObjectsOfType<UIBunnyRaceWindow>(true);
                    if (found != null && found.Length > 0) _window = found[0];
                    if (_window != null) DebugLogger.LogState("BunnyRace: cached UIBunnyRaceWindow reference.");
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"BunnyRace: detection error: {ex.Message}");
                }
                return;
            }

            try
            {
                // The selector stack knows the screen on top; the Open… property only
                // records the first screen pushed (coliseum log 2026-09-28 11:39).
                var state = _window.IsOpened
                    ? (UIDefine.BunnyRaceState)_window.GetCurrentState()
                    : UIDefine.BunnyRaceState.None;
                if (state == _state) return;

                DebugLogger.LogState($"BunnyRace: screen {_state} -> {state}.");
                ResetScreenState(state);
                switch (state)
                {
                    case UIDefine.BunnyRaceState.MedalShop:
                        AnnounceShopOpened();
                        break;
                    case UIDefine.BunnyRaceState.Bet:
                        _betOpeningPending = true;
                        _betOpeningDeadline = UnityEngine.Time.unscaledTime + BetOpeningWaitSeconds;
                        break;
                    case UIDefine.BunnyRaceState.NumberFlag:
                        _raceNextSpeakTime = UnityEngine.Time.unscaledTime + RaceFirstDelaySeconds;
                        break;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BunnyRace: detection error: {ex.Message}");
                _window = null;
                _findCooldown = 0;
                ResetScreenState(UIDefine.BunnyRaceState.None);
            }
        }

        private static void ResetScreenState(UIDefine.BunnyRaceState state)
        {
            _state = state;
            _shopLastCount = -1;
            _shopLimitSpokenTime = -1f;
            _betOpeningPending = false;
            _betSettleUntil = -1f;
            _betLastIndex = -1;
            _raceLastOrder = "";
            _raceLastLeader = -1;
            _raceNextPollTime = 0f;
        }

        #endregion

        #region Medal shop

        /// <summary>
        /// "Me-ow many medals? (2 left)" as the game words it, then the medals and
        /// Fol owned and the price per medal.
        /// </summary>
        private static void AnnounceShopOpened()
        {
            var selector = _window.medalShopSelector;
            var p = selector?.medalShopPresenter;
            if (p == null)
            {
                DebugLogger.LogState("BunnyRace: medal shop presenter missing, opening not spoken.");
                ScreenReader.Say(Loc.Get("bunny_shop_heading"));
                return;
            }

            _shopLastCount = selector.buyMedalCount;
            string label = ReadText(p.buyMedalLabelText);
            if (label.Length == 0) label = Loc.Get("bunny_shop_heading");
            ScreenReader.Say(Loc.Get("bunny_shop_open", label,
                ReadText(p.retainedMedalText), ReadText(p.retainedMoneyText), MedalPrice()));
        }

        /// <summary>Price of one medal in Fol from the race manager (1000 in the survey), 0 when unreadable.</summary>
        private static int MedalPrice()
        {
            try
            {
                return BunnyRaceManager.Instance?.GetBunnyRaceMedalPrice() ?? 0;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BunnyRace: medal price unreadable: {ex.Message}");
                return 0;
            }
        }

        #endregion

        #region Bet screen

        /// <summary>
        /// Waits for the bet list to populate, speaks the opening (medals, paddock
        /// bunny, first row), then follows the cursor.
        /// </summary>
        private static void UpdateBet()
        {
            var selector = _window.betSelector;
            if (selector == null) return;

            if (_betOpeningPending)
            {
                float now = UnityEngine.Time.unscaledTime;
                bool ready = selector.DataCount > 0 && ReadText(selector.paddockPresenter?.bunnyNameText).Length > 0;
                if (!ready && now < _betOpeningDeadline) return;
                if (_betSettleUntil < 0f)
                {
                    _betSettleUntil = now + BetSettleSeconds;
                    LogPrediction(selector);
                    return;
                }
                if (now < _betSettleUntil) return;
                _betOpeningPending = false;

                var sb = new StringBuilder();
                sb.Append(Loc.Get("bunny_bet_open", ReadText(selector.medalText)));
                string paddock = DescribePaddock(selector);
                if (paddock.Length > 0) sb.Append(' ').Append(paddock);
                int idx = selector.CurrentIndex;
                string row = DescribeBetRow(selector, idx);
                if (row.Length > 0) sb.Append(' ').Append(row);
                _betLastIndex = idx;
                ScreenReader.Say(sb.ToString());
                DebugLogger.LogState($"BunnyRace: bet opened (ready={ready}, rows={selector.DataCount}, index={idx}).");
                return;
            }

            int current = selector.CurrentIndex;
            if (current == _betLastIndex) return;
            _betLastIndex = current;
            string text = DescribeBetRow(selector, current);
            if (text.Length > 0) ScreenReader.Say(text);
        }

        /// <summary>
        /// "Bunnies 1 and 2: Luxury Grape Juice x1, tipster's pick. 3 of 12." The
        /// bunny numbers come from the row's bunny ids through the race manager;
        /// the row's own pair text ("1-2") is the fallback.
        /// </summary>
        private static string DescribeBetRow(UIBunnyRaceBetSelector selector, int index)
        {
            var list = selector.currentDataList;
            int count = list?.Count ?? 0;
            if (index < 0 || index >= count) return "";

            var data = list[index]?.TryCast<UIBunnyRaceListBetItemData>();
            if (data == null)
            {
                DebugLogger.LogState($"BunnyRace: bet row {index} has no UIBunnyRaceListBetItemData.");
                return "";
            }

            if (!_betDataLogged)
            {
                _betDataLogged = true;
                DebugLogger.LogState($"BunnyRace: bet row sample message='{data.message}' item='{data.itemName}' "
                    + $"count={data.itemCount} ids={data.firstBunnyID}/{data.secondBunnyID} predict={data.isPredictPair}");
            }

            string first = BunnyNumber(data.firstBunnyID);
            string second = BunnyNumber(data.secondBunnyID);
            string pair;
            if (first != null && second != null)
                pair = Loc.Get("bunny_pair", first, second);
            else
            {
                string raw = TextUtil.StripTags(data.message ?? "");
                var parts = raw.Split('-');
                pair = parts.Length == 2
                    ? Loc.Get("bunny_pair", parts[0].Trim(), parts[1].Trim())
                    : raw;
            }

            string item = TextUtil.StripTags(data.itemName ?? "");
            if (item.Length == 0) item = Loc.Get("ic_unknown_item");

            var sb = new StringBuilder();
            sb.Append(Loc.Get("bunny_bet_row", pair, item, data.itemCount));
            if (data.isPredictPair && PredictionBought()) sb.Append(' ').Append(Loc.Get("bunny_bet_predict"));
            TextUtil.AppendPosition(sb, index, count);
            return sb.ToString();
        }

        /// <summary>
        /// True when the race manager says a prediction was obtained for this race
        /// (the tipster was paid). The row flag alone is not trusted: the mark must
        /// never appear for a player who did not ask, so both must agree.
        /// </summary>
        private static bool PredictionBought()
        {
            try
            {
                return BunnyRaceManager.Instance?.IsExpected ?? false;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BunnyRace: IsExpected unreadable: {ex.Message}");
                return false;
            }
        }

        /// <summary>Debug record of the prediction state at bet opening, to verify the tipster gate in play.</summary>
        private static void LogPrediction(UIBunnyRaceBetSelector selector)
        {
            if (!Main.DebugMode) return;
            try
            {
                var manager = BunnyRaceManager.Instance;
                var numbers = manager?.ExpectBunnyNumbers;
                var expected = new List<string>();
                int n = numbers?.Length ?? 0;
                for (int i = 0; i < n; i++) expected.Add(numbers[i].ToString());

                var flagged = new List<int>();
                var list = selector.currentDataList;
                int count = list?.Count ?? 0;
                for (int i = 0; i < count; i++)
                    if (list[i]?.TryCast<UIBunnyRaceListBetItemData>()?.isPredictPair == true) flagged.Add(i + 1);

                DebugLogger.LogState($"BunnyRace: prediction state IsExpected={manager?.IsExpected} "
                    + $"expectNumbers=[{string.Join(",", expected)}] rowsFlagged=[{string.Join(",", flagged)}]");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BunnyRace: prediction state unreadable: {ex.Message}");
            }
        }

        /// <summary>The race number shown on a bunny for its id, or null when the manager cannot tell.</summary>
        private static string BunnyNumber(int bunnyID)
        {
            try
            {
                var param = BunnyRaceManager.Instance?.GetBunnyParameter(bunnyID);
                return param == null ? null : param.Number.ToString();
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BunnyRace: bunny number for id {bunnyID} unreadable: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// "Bunny 2, Ilia Bond. Speed slow, stamina plenty, trait relaxed." plus the
        /// condition while the game shows its icon (a bought tip). The icon has no
        /// text, so its sprite name is mapped to words and logged when unknown.
        /// </summary>
        private static string DescribePaddock(UIBunnyRaceBetSelector selector)
        {
            var p = selector?.paddockPresenter;
            if (p == null) return "";

            string name = ReadText(p.bunnyNameText);
            if (name.Length == 0) return "";

            var sb = new StringBuilder();
            sb.Append(Loc.Get("bunny_paddock", ReadText(p.bunnyIndexText), name,
                ReadText(p.bunnySpeedText), ReadText(p.bunnyStaminaText), ReadText(p.bunnyPersonalityText)));

            string condition = DescribeCondition(p);
            if (condition.Length > 0) sb.Append(' ').Append(condition);
            return sb.ToString();
        }

        /// <summary>
        /// The condition words for the paddock icon, "" while the icon is hidden
        /// (the icon only shows once the tipster has been paid). The icon has no
        /// text; its place in the presenter's sprite list is the level, lower is
        /// better (log 2026-09-28 12:03: the tipster's best bunny showed
        /// bunny_condition_02, his next two _03, the unmentioned one _04).
        /// </summary>
        private static string DescribeCondition(UIBunnyRacePaddockPresenter p)
        {
            try
            {
                if (p.conditionObj == null || !p.conditionObj.activeInHierarchy) return "";

                string shown = p.conditionIcon?.sprite?.name ?? "";
                var sprites = p.conditionSprites;
                int count = sprites?.Length ?? 0;
                int level = -1;
                for (int i = 0; i < count; i++)
                {
                    if (sprites[i] != null && sprites[i].name == shown) { level = i; break; }
                }

                DebugLogger.LogState($"BunnyRace: condition sprite '{shown}' = level {level} of {count}.");
                if (level < 0) return Loc.Get("bunny_condition_unknown");
                if (count == ConditionWordKeys.Length) return Loc.Get(ConditionWordKeys[level]);
                return Loc.Get("bunny_condition_level", level + 1, count);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BunnyRace: condition unreadable: {ex.Message}");
                return "";
            }
        }

        /// <summary>Words for a five-picture condition scale, best first.</summary>
        private static readonly string[] ConditionWordKeys =
        {
            "bunny_condition_excellent",
            "bunny_condition_good",
            "bunny_condition_average",
            "bunny_condition_poor",
            "bunny_condition_bad",
        };

        #endregion

        #region Race standings

        /// <summary>
        /// Twice a second reads every bunny's lap count and route progress, keeps
        /// the finished bunnies in their finishing order, and speaks the running
        /// order on a leader change or every few seconds.
        /// </summary>
        private static void UpdateRace()
        {
            float now = UnityEngine.Time.unscaledTime;
            if (now < _raceNextPollTime) return;
            _raceNextPollTime = now + RacePollSeconds;

            var manager = BunnyRaceManager.Instance;
            if (manager == null) return;

            var order = ComputeOrder(manager);
            if (order.Count == 0) return;

            int leader = order[0];
            string orderText = string.Join(", ", order);
            bool leaderChanged = _raceLastLeader >= 0 && leader != _raceLastLeader
                && now - _raceLastSpeakTime >= RaceChangeGapSeconds;
            bool due = now >= _raceNextSpeakTime;
            if (!leaderChanged && !due) return;
            if (!leaderChanged && orderText == _raceLastOrder)
            {
                // Nothing moved since the last call: wait another round.
                _raceNextSpeakTime = now + RaceRepeatSeconds;
                return;
            }

            _raceLastLeader = leader;
            _raceLastOrder = orderText;
            _raceLastSpeakTime = now;
            _raceNextSpeakTime = now + RaceRepeatSeconds;

            var rest = order.GetRange(1, order.Count - 1);
            ScreenReader.Say(rest.Count == 0
                ? Loc.Get("bunny_standings_leader", leader)
                : Loc.Get("bunny_standings", leader, string.Join(", ", rest)));
        }

        /// <summary>
        /// Bunny numbers from first to last: finished bunnies first in goal order,
        /// then the runners by laps and route progress.
        /// </summary>
        private static List<int> ComputeOrder(BunnyRaceManager manager)
        {
            var result = new List<int>();
            var seen = new HashSet<int>();

            var goals = manager.GoalBunnyList;
            int goalCount = goals?.Count ?? 0;
            for (int i = 0; i < goalCount; i++)
            {
                var b = goals[i];
                int number = b?.BunnyParameter?.Number ?? -1;
                if (number < 0 || !seen.Add(number)) continue;
                result.Add(number);
            }

            var runners = new List<(int number, int laps, int progress)>();
            var bunnies = manager.BunnyList;
            int count = bunnies?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                var b = bunnies[i];
                var param = b?.BunnyParameter;
                if (param == null) continue;
                int number = param.Number;
                if (seen.Contains(number)) continue;
                runners.Add((number, b.LoopCount, param.Progress));
            }

            runners.Sort((x, y) =>
            {
                int c = y.laps.CompareTo(x.laps);
                if (c != 0) return c;
                c = y.progress.CompareTo(x.progress);
                return c != 0 ? c : x.number.CompareTo(y.number);
            });

            if (Main.DebugMode && runners.Count > 0)
            {
                var parts = new List<string>();
                foreach (var r in runners) parts.Add($"{r.number}:lap{r.laps}/p{r.progress}");
                DebugLogger.LogState($"BunnyRace: standings goal=[{string.Join(",", result)}] run=[{string.Join(" ", parts)}]");
            }

            foreach (var r in runners) result.Add(r.number);
            return result;
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
