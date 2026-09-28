using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Text;

namespace SO2RAccess
{
    /// <summary>
    /// Speaks the Fun City arena reception screens on <c>UIColiseumWindow</c>:
    ///
    /// - Character picker (duel and survival): a choice list owned by the window,
    ///   not the conversation window, so nothing spoke there before (user report
    ///   2026-09-27). The title, the focused name with "cannot enter" for greyed
    ///   members, and the position are read while the cursor is polled.
    /// - Survival: challenger and level, every streak reward with "earned" where
    ///   the game shows its clear mark, the longest streak, then the ready rows.
    /// - Duel: each rank row with its cleared mark, followed by the rank's info
    ///   panel (recommended level, description, reward) once the game has
    ///   refilled it, then the ready rows.
    /// - Challenge: each battle row with locked / cleared, followed by its info
    ///   panel (recommended level, party rule, battle rule, description,
    ///   rewards). The party screen names the fixed party, then the ready rows.
    ///
    /// The ready-check rows (Yes / No / Prepare for Battle) and the rank and
    /// challenge rows are owned here so the universal list net cannot talk over
    /// the opening announcements. All cursors are native and fire no hooks, so
    /// the screen state and every cursor are polled each frame, the same pattern
    /// as <see cref="BunnyRaceHandler"/>.
    /// </summary>
    public class ColiseumHandler
    {
        #region Fields

        private static UIColiseumWindow _window;
        private static int _findCooldown;
        private static UIDefine.ColiseumState _state = UIDefine.ColiseumState.None;

        /// <summary>Opening announcement pending until the screen's data is populated.</summary>
        private static bool _openingPending;
        private static float _openingDeadline;
        private const float OpeningWaitSeconds = 1.5f;

        /// <summary>Last spoken cursor of the screen's main list (picker, rank list, challenge list).</summary>
        private static int _lastIndex = -1;

        /// <summary>Last spoken ready-check row, -1 while the ready check is not showing.</summary>
        private static int _lastReadyIndex = -1;

        /// <summary>Frames left before the info panel of the focused row is read (game refills it on the move).</summary>
        private static int _infoCountdown = -1;
        private const int InfoDelayFrames = 2;

        #endregion

        /// <summary>Called on scene change to drop cached references.</summary>
        public void OnSceneChanged()
        {
            _window = null;
            _findCooldown = 0;
            ResetScreenState(UIDefine.ColiseumState.None);
        }

        #region Update Loop

        /// <summary>Called every frame from Main.UpdateHandlers().</summary>
        public void Update()
        {
            DetectWindow();
            if (_window == null || _state == UIDefine.ColiseumState.None) return;

            try
            {
                switch (_state)
                {
                    case UIDefine.ColiseumState.DuelBattleSoloSelect:
                    case UIDefine.ColiseumState.SurvivalBattleSoloSelect:
                        UpdateCharacterPicker();
                        break;
                    case UIDefine.ColiseumState.SurvivalBattle:
                        UpdateSurvival();
                        break;
                    case UIDefine.ColiseumState.DuelBattle:
                        UpdateDuel();
                        break;
                    case UIDefine.ColiseumState.ChallengeBattle:
                        UpdateChallenge();
                        break;
                    case UIDefine.ColiseumState.ChallengeBattleMemberSelect:
                        UpdateParty();
                        break;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"ColiseumHandler.Update: {ex.Message}");
            }
        }

        /// <summary>
        /// Finds the coliseum window lazily (throttled; inactive while closed) and
        /// follows its open state.
        /// </summary>
        private void DetectWindow()
        {
            if (_window == null)
            {
                if (_findCooldown > 0) { _findCooldown--; return; }
                _findCooldown = 60;

                try
                {
                    var found = UnityEngine.Object.FindObjectsOfType<UIColiseumWindow>(true);
                    if (found != null && found.Length > 0) _window = found[0];
                    if (_window != null) DebugLogger.LogState("Coliseum: cached UIColiseumWindow reference.");
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"Coliseum: detection error: {ex.Message}");
                }
                return;
            }

            try
            {
                // OpenColiseumState only records the first screen pushed (log 2026-09-28
                // 11:39: the duel list, survival panel and party screen never changed it);
                // the selector stack knows the screen on top.
                var state = _window.IsOpened
                    ? (UIDefine.ColiseumState)_window.GetCurrentState()
                    : UIDefine.ColiseumState.None;
                if (state == _state) return;

                DebugLogger.LogState($"Coliseum: screen {_state} -> {state}.");
                ResetScreenState(state);
                if (state != UIDefine.ColiseumState.None)
                {
                    _openingPending = true;
                    _openingDeadline = UnityEngine.Time.unscaledTime + OpeningWaitSeconds;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"Coliseum: detection error: {ex.Message}");
                _window = null;
                _findCooldown = 0;
                ResetScreenState(UIDefine.ColiseumState.None);
            }
        }

        private static void ResetScreenState(UIDefine.ColiseumState state)
        {
            _state = state;
            _openingPending = false;
            _lastIndex = -1;
            _lastReadyIndex = -1;
            _infoCountdown = -1;
        }

        /// <summary>True once the opening wait is over: data present, or the deadline passed.</summary>
        private static bool OpeningReady(bool hasData)
        {
            if (!_openingPending) return false;
            if (!hasData && UnityEngine.Time.unscaledTime < _openingDeadline) return false;
            _openingPending = false;
            return true;
        }

        #endregion

        #region Character picker

        /// <summary>Title and focused name on opening, then the name on every cursor move.</summary>
        private static void UpdateCharacterPicker()
        {
            var selector = _window.soloCharacterSelector;
            if (selector == null) return;

            var choices = selector.choiceDataList;
            int count = choices?.Count ?? 0;

            if (_openingPending)
            {
                if (!OpeningReady(count > 0)) return;
                int idx = selector.selectChoiceIndex;
                _lastIndex = idx;
                // titleLabel carries the rule name ("Duel Battle"); the question
                // ("Who will be entering?") is the choice presenter's own title.
                var sb = new StringBuilder();
                string rule = ReadText(selector.titleLabel);
                if (rule.Length > 0) sb.Append(rule).Append('.').Append(' ');
                string title = ReadText(selector.charaChoicePresenter?.title);
                sb.Append(title.Length > 0 ? title : Loc.Get("coliseum_pick_heading"));
                sb.Append(' ').Append(DescribeChoice(choices, idx, count));
                ScreenReader.Say(sb.ToString());
                DebugLogger.LogState($"Coliseum: picker opened (rule={selector.currentRule}, choices={count}, index={idx}).");
                return;
            }

            int current = selector.selectChoiceIndex;
            if (current == _lastIndex) return;
            _lastIndex = current;
            ScreenReader.Say(DescribeChoice(choices, current, count));
        }

        /// <summary>"Ernest, cannot enter. 8 of 8." for one row of a choice presenter list.</summary>
        private static string DescribeChoice(Il2CppSystem.Collections.Generic.List<UIChoiceData> choices, int index, int count)
        {
            if (index < 0 || index >= count) return Loc.Get("coliseum_no_choice");
            var d = choices[index];
            var sb = new StringBuilder();
            sb.Append(TextUtil.StripTags(d?.message ?? ""));
            if (d != null && !d.canDecision) sb.Append(", ").Append(Loc.Get("coliseum_cannot_enter"));
            TextUtil.AppendPosition(sb, index, count);
            return sb.ToString();
        }

        #endregion

        #region Survival

        /// <summary>Challenger, reward table and best streak on opening, then the ready rows.</summary>
        private static void UpdateSurvival()
        {
            var selector = _window.survivalBattleSelector;
            if (selector == null) return;

            if (_openingPending)
            {
                if (!OpeningReady(ReadText(selector.challengerName).Length > 0)) return;
                var sb = new StringBuilder();
                sb.Append(Loc.Get("coliseum_rule_survival"));
                AppendChallenger(sb, ReadText(selector.challengerName), ReadText(selector.challengerLevel));
                AppendSurvivalRewards(sb, selector.rewardInformationPresenter);
                AppendReadyRow(sb, selector.readyCheckSelector, true);
                ScreenReader.Say(sb.ToString());
                return;
            }

            PollReadyCheck(selector.readyCheckSelector);
        }

        /// <summary>"Rewards: 10 win streak, Rainbow Diamond. 20 win streak, Ring of the General, earned. Longest win streak 0."</summary>
        private static void AppendSurvivalRewards(StringBuilder sb, UISurvivalBattleRewardInformationPresenter info)
        {
            if (info == null) return;

            var rows = new List<string>();
            var presenters = info.presenters;
            int count = presenters?.Length ?? 0;
            for (int i = 0; i < count; i++)
            {
                var p = presenters[i];
                if (p == null || p.gameObject == null || !p.gameObject.activeInHierarchy) continue;
                string streak = ReadText(p.consecutiveWinText);
                string reward = ReadText(p.rewardName);
                if (reward.Length == 0) continue;
                string row = Loc.Get("coliseum_reward_row", streak, reward);
                if (p.clearIconObj != null && p.clearIconObj.activeInHierarchy)
                    row += ", " + Loc.Get("coliseum_reward_earned");
                rows.Add(row);
            }

            if (rows.Count > 0)
                sb.Append(' ').Append(Loc.Get("coliseum_rewards", string.Join(". ", rows)));

            string best = ReadText(info.maxConsecutiveWinCount);
            if (best.Length > 0) sb.Append(' ').Append(Loc.Get("coliseum_best_streak", best));
        }

        #endregion

        #region Duel

        /// <summary>Rank rows with their info panel while choosing, ready rows afterwards.</summary>
        private static void UpdateDuel()
        {
            var selector = _window.duelBattleSelector;
            if (selector == null) return;

            bool readyCheck = selector.currentState == UIDuelBattleSelector.State.ReadyCheck;

            if (_openingPending)
            {
                if (!OpeningReady(selector.DataCount > 0)) return;
                int idx = selector.CurrentIndex;
                _lastIndex = idx;
                var sb = new StringBuilder();
                sb.Append(Loc.Get("coliseum_rule_duel"));
                AppendChallenger(sb, ReadText(selector.challengerName), ReadText(selector.challengerLevel));
                sb.Append(' ').Append(DescribeRankRow(selector, idx));
                sb.Append(' ').Append(DescribeRankInfo(selector.rankInformationPresenter));
                ScreenReader.Say(sb.ToString());
                return;
            }

            if (readyCheck)
            {
                PollReadyCheck(selector.readyCheckSelector);
                return;
            }
            _lastReadyIndex = -1;

            if (_infoCountdown > 0) { _infoCountdown--; return; }
            if (_infoCountdown == 0)
            {
                _infoCountdown = -1;
                ScreenReader.SayQueued(DescribeRankInfo(selector.rankInformationPresenter));
                return;
            }

            int current = selector.CurrentIndex;
            if (current == _lastIndex) return;
            _lastIndex = current;
            ScreenReader.Say(DescribeRankRow(selector, current));
            _infoCountdown = InfoDelayFrames;
        }

        /// <summary>"Rank C, cleared. 3 of 5."</summary>
        private static string DescribeRankRow(UIDuelBattleSelector selector, int index)
        {
            var list = selector.currentDataList;
            int count = list?.Count ?? 0;
            if (index < 0 || index >= count) return "";
            var data = list[index]?.TryCast<UIDuelBattleListItemData>();
            if (data == null) return "";

            string rank = "";
            try { rank = TextUtil.StripTags(selector.GetRankText(data.battleRank) ?? ""); }
            catch (Exception ex) { DebugLogger.LogState($"Coliseum: GetRankText failed: {ex.Message}"); }
            if (rank.Length == 0) rank = data.battleRank.ToString();

            var sb = new StringBuilder(rank);
            if (data.isCleared) sb.Append(", ").Append(Loc.Get("coliseum_cleared"));
            TextUtil.AppendPosition(sb, index, count);
            return sb.ToString();
        }

        /// <summary>"Recommended level 20. Description. Reward: Item."</summary>
        private static string DescribeRankInfo(UIDuelBattleRankInformationPresenter info)
        {
            if (info == null) return "";
            var parts = new List<string>();
            string level = ReadText(info.recommendLevel);
            if (level.Length > 0) parts.Add(Loc.Get("coliseum_recommended_level", level));
            string desc = ReadText(info.rankDescription);
            if (desc.Length > 0) parts.Add(desc);
            string reward = ReadText(info.rewardItemName);
            if (reward.Length > 0) parts.Add(Loc.Get("coliseum_reward", reward));
            return TextUtil.JoinSentences(parts);
        }

        #endregion

        #region Challenge

        /// <summary>Challenge rows with their info panel.</summary>
        private static void UpdateChallenge()
        {
            var selector = _window.challengeBattleSelector;
            if (selector == null) return;

            if (_openingPending)
            {
                if (!OpeningReady(selector.DataCount > 0)) return;
                int idx = selector.CurrentIndex;
                _lastIndex = idx;
                var sb = new StringBuilder();
                sb.Append(Loc.Get("coliseum_rule_challenge"));
                sb.Append(' ').Append(DescribeChallengeRow(selector, idx));
                sb.Append(' ').Append(DescribeChallengeInfo(selector.battleInformationPresenter));
                ScreenReader.Say(sb.ToString());
                return;
            }

            if (_infoCountdown > 0) { _infoCountdown--; return; }
            if (_infoCountdown == 0)
            {
                _infoCountdown = -1;
                ScreenReader.SayQueued(DescribeChallengeInfo(selector.battleInformationPresenter));
                return;
            }

            int current = selector.CurrentIndex;
            if (current == _lastIndex) return;
            _lastIndex = current;
            ScreenReader.Say(DescribeChallengeRow(selector, current));
            _infoCountdown = InfoDelayFrames;
        }

        /// <summary>"Battle name, locked. 2 of 6."</summary>
        private static string DescribeChallengeRow(UIChallengeBattleSelector selector, int index)
        {
            var list = selector.currentDataList;
            int count = list?.Count ?? 0;
            if (index < 0 || index >= count) return "";
            var data = list[index]?.TryCast<UIChallengeBattleListItemData>();
            if (data == null) return "";

            var sb = new StringBuilder(TextUtil.StripTags(data.battleName ?? ""));
            if (data.isLocked) sb.Append(", ").Append(Loc.Get("coliseum_locked"));
            else if (data.isCleared) sb.Append(", ").Append(Loc.Get("coliseum_cleared"));
            TextUtil.AppendPosition(sb, index, count);
            return sb.ToString();
        }

        /// <summary>"Recommended level 40. Party: females only. Rule: … Description. Rewards: A, B."</summary>
        private static string DescribeChallengeInfo(UIChallengeBattleInformationPresenter info)
        {
            if (info == null) return "";
            var parts = new List<string>();
            string level = ReadText(info.recommendLevel);
            if (level.Length > 0) parts.Add(Loc.Get("coliseum_recommended_level", level));
            string party = ReadText(info.partyRule);
            if (party.Length > 0) parts.Add(Loc.Get("coliseum_party_rule", party));
            string rule = ReadText(info.battleRule);
            if (rule.Length > 0) parts.Add(Loc.Get("coliseum_battle_rule", rule));
            string desc = ReadText(info.description);
            if (desc.Length > 0) parts.Add(desc);

            var rewards = new List<string>();
            if (info.firstRewardObj == null || info.firstRewardObj.activeInHierarchy)
            {
                string r = ReadText(info.firstRewardName);
                if (r.Length > 0) rewards.Add(r);
            }
            if (info.secondRewardObj == null || info.secondRewardObj.activeInHierarchy)
            {
                string r = ReadText(info.secondRewardName);
                if (r.Length > 0) rewards.Add(r);
            }
            if (rewards.Count > 0) parts.Add(Loc.Get("coliseum_reward", string.Join(", ", rewards)));
            return TextUtil.JoinSentences(parts);
        }

        /// <summary>The fixed party on opening, then the ready rows.</summary>
        private static void UpdateParty()
        {
            var selector = _window.challengeBattlePartyMemberSelector;
            if (selector == null) return;

            if (_openingPending)
            {
                string party = DescribeParty();
                if (!OpeningReady(party.Length > 0)) return;
                var sb = new StringBuilder();
                if (party.Length > 0) sb.Append(Loc.Get("coliseum_party", party));
                AppendReadyRow(sb, selector.readyCheckSelector, true);
                if (sb.Length > 0) ScreenReader.Say(sb.ToString());
                return;
            }

            PollReadyCheck(selector.readyCheckSelector);
        }

        /// <summary>"Claude, Rena, Celine" from the manager's chosen party, "" when unreadable.</summary>
        private static string DescribeParty()
        {
            try
            {
                var members = ColiseumManager.Instance?.GetChallengeBattlePartyMembers();
                int count = members?.Count ?? 0;
                var names = new List<string>();
                for (int i = 0; i < count; i++)
                {
                    string name = ParameterManager.Instance?.GetCharacterFirstName(members[i]);
                    if (!string.IsNullOrEmpty(name)) names.Add(name);
                }
                return string.Join(", ", names);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"Coliseum: party unreadable: {ex.Message}");
                return "";
            }
        }

        #endregion

        #region Shared pieces

        private static void AppendChallenger(StringBuilder sb, string name, string level)
        {
            if (name.Length == 0) return;
            sb.Append(' ').Append(level.Length > 0
                ? Loc.Get("coliseum_challenger", name, level)
                : Loc.Get("coliseum_challenger_no_level", name));
        }

        /// <summary>
        /// Speaks the focused ready-check row (Yes / No / Prepare for Battle) when
        /// the cursor moves. The list may be hidden while a camp window is open.
        /// </summary>
        private static void PollReadyCheck(UIColiseumReadyCheckSelector ready)
        {
            if (ready == null) return;
            bool showing;
            try { showing = ready.gameObject != null && ready.gameObject.activeInHierarchy; }
            catch { showing = false; }
            if (!showing) { _lastReadyIndex = -1; return; }

            int current = ready.CurrentIndex;
            if (current == _lastReadyIndex) return;
            bool first = _lastReadyIndex < 0;
            _lastReadyIndex = current;
            string text = DescribeReadyRow(ready, current);
            if (text.Length > 0) ScreenReader.Say(first ? Loc.Get("coliseum_ready_heading") + " " + text : text);
        }

        /// <summary>Appends the focused ready row to an opening announcement and marks it spoken.</summary>
        private static void AppendReadyRow(StringBuilder sb, UIColiseumReadyCheckSelector ready, bool withHeading)
        {
            if (ready == null) return;
            int idx = ready.CurrentIndex;
            string text = DescribeReadyRow(ready, idx);
            if (text.Length == 0) return;
            if (withHeading) sb.Append(' ').Append(Loc.Get("coliseum_ready_heading"));
            sb.Append(' ').Append(text);
            _lastReadyIndex = idx;
        }

        private static string DescribeReadyRow(UIColiseumReadyCheckSelector ready, int index)
        {
            var list = ready.currentDataList;
            int count = list?.Count ?? 0;
            if (index < 0 || index >= count) return "";
            var data = list[index]?.TryCast<UIColiseumReadyCheckListItemData>();
            string text = TextUtil.StripTags(data?.text ?? "");
            if (text.Length == 0) return "";
            var sb = new StringBuilder(text);
            TextUtil.AppendPosition(sb, index, count);
            return sb.ToString();
        }

        /// <summary>
        /// Visible text of a GameText with rich-text tags stripped, "" when absent.
        /// The game fills hidden fields of locked entries with question marks
        /// ("????", log 2026-09-28 11:38); those read as absent too.
        /// </summary>
        private static string ReadText(GameText gt)
        {
            if (gt == null) return "";
            try
            {
                string raw = ((Il2CppTMPro.TMP_Text)gt)?.text;
                string text = TextUtil.StripTags(raw ?? "") ?? "";
                return IsHiddenPlaceholder(text) ? "" : text;
            }
            catch
            {
                return "";
            }
        }

        /// <summary>True for a text made only of question marks, the game's "not yet revealed" filler.</summary>
        private static bool IsHiddenPlaceholder(string text)
        {
            if (text.Length == 0) return false;
            foreach (char c in text)
                if (c != '?' && c != '？' && c != ' ') return false;
            return true;
        }

        #endregion
    }
}
