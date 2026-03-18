using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace SO2RAccess
{
    public partial class CampMenuHandler
    {
        #region Status Sub-screen Fields

        // Status sub-screen (statusSelector on UICampWindow)
        // Detection: hook-driven. Both activeInHierarchy and root-menu-hidden approaches
        // failed (both stay true). Instead, UICampStatusSelector.UpdatePresenter hook fires
        // when the status screen opens or character tab changes — use it as the trigger.
        // Data captured from hooks that fire just before UpdatePresenter:
        //   UpdateName → _statusPlayerName
        //   LevelPresenter.Setup → _statusLevelData
        //   StatusParamPresenter.Setup → _statusParamData
        private static UICampStatusSelector _statusSelector = null;
        private static bool _statusScreenOpen = false;
        private static int _statusLastIndex = -1;
        private static UICampStatusParameterData _statusParamData = null;
        private static UICampStatusLevelData _statusLevelData = null;
        private static string _statusPlayerName = "";
        private static PlayerID _statusPlayerID = PlayerID.INVALID;
        private static int _statusLastPageIndex = -1;
        /// <summary>
        /// Cached talent announcement string built by UITalentPresenter.Set hook.
        /// The hook fires on status screen open (page 0) — not on page switch.
        /// We cache the string and announce it when pageIndex changes to 1 (talent page).
        /// </summary>
        private static string _cachedTalentAnnouncement = "";
        /// <summary>
        /// Cached elemental affinities string built by UIElementalGroupPresenter.Set hook
        /// when the status screen is active. Cleared on status close and camp reopen.
        /// </summary>
        private static string _cachedStatusElementalAnnouncement = "";
        /// <summary>
        /// Individual elemental resistance lines for virtual cursor navigation.
        /// Each entry is one element's resistance (e.g. "Fire: weak").
        /// </summary>
        private static List<string> _cachedStatusElementalLines = new List<string>();
        /// <summary>
        /// Cached friendship announcement built by UICampStatusPresenter.SetEmotion hook.
        /// Shows each party member's favorability rating. Cleared on status close and camp reopen.
        /// </summary>
        private static string _cachedFriendshipAnnouncement = "";
        /// <summary>
        /// Individual friendship lines for virtual cursor navigation.
        /// Each entry is one party member's friendship level.
        /// </summary>
        private static List<string> _cachedFriendshipLines = new List<string>();

        // Virtual cursor for line-by-line navigation on page 0.
        // Built by AnnounceStatusCharacter, navigated by Up/Down in UpdateStatusSelector.
        private static List<string> _statusVirtualLines = new List<string>();
        private static int _statusVirtualIndex = -1;

        #endregion

        #region Status Sub-screen Update

        /// <summary>
        /// Polls pageIndex to detect L1/R1 page switches on the status screen.
        /// Main status detection is hook-driven via UpdatePresenter — this method
        /// only handles page changes (native-only, no hooks fire for page navigation).
        /// </summary>
        private void UpdateStatusSelector()
        {
            // Poll pageIndex to detect page switches (L1/R1 on status screen).
            // Page navigation is native-only; no hooks fire for page change itself.
            if (!_statusScreenOpen || _statusSelector == null) return;

            try
            {
                int pageIdx = _statusSelector.pageIndex;

                // Detect page change.
                if (pageIdx != _statusLastPageIndex)
                {
                    int oldPage = _statusLastPageIndex;
                    _statusLastPageIndex = pageIdx;

                    // Skip initial page set (handled by UpdatePresenter heading).
                    if (oldPage >= 0)
                    {
                        // Reset character index so switching characters on a new page
                        // will re-trigger the UpdatePresenter announcement.
                        _statusLastIndex = -1;

                        DebugLogger.LogState($"CampStatus: page changed {oldPage} → {pageIdx}.");

                        // Clear virtual cursor on page switch.
                        _statusVirtualLines.Clear();
                        _statusVirtualIndex = -1;

                        // Page 0 = stats (UpdatePresenter hooks will fire and announce).
                        // Page 1 = talents (announce cached data from UITalentPresenter.Set hook).
                        if (pageIdx == 1 && !string.IsNullOrEmpty(_cachedTalentAnnouncement))
                        {
                            ScreenReader.Say(_cachedTalentAnnouncement);
                        }
                    }

                    return;
                }

                // Virtual cursor: Up/Down navigates status lines on page 0.
                if (pageIdx == 0 && _statusVirtualLines.Count > 0)
                {
                    var gim = GameInputManager.Instance;
                    if (gim == null) return;

                    int newIdx = _statusVirtualIndex;
                    if (gim.IsDown(GameInputManager.InputAction.Down))
                    {
                        newIdx = Math.Min(_statusVirtualIndex + 1, _statusVirtualLines.Count - 1);
                    }
                    else if (gim.IsDown(GameInputManager.InputAction.Up))
                    {
                        newIdx = Math.Max(_statusVirtualIndex - 1, 0);
                    }

                    if (newIdx == _statusVirtualIndex) return;
                    _statusVirtualIndex = newIdx;

                    ScreenReader.Say(_statusVirtualLines[newIdx]);
                    DebugLogger.LogGameValue("CampStatus.vcursor",
                        $"{newIdx + 1}/{_statusVirtualLines.Count}: {_statusVirtualLines[newIdx]}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateStatusSelector: {ex.Message}");
            }
        }

        /// <summary>
        /// Builds and announces character status from hook-captured data.
        /// Called from the UpdatePresenter hook (fires last in the hook chain).
        /// Also populates _statusVirtualLines for Up/Down line-by-line navigation.
        /// Data sources: _statusPlayerName (UpdateName hook), _statusLevelData
        /// (LevelPresenter.Setup hook), _statusParamData (StatusParamPresenter.Setup hook),
        /// age/food (direct presenter read), elementals/friendship (cached from hooks).
        /// </summary>
        private static void AnnounceStatusCharacter(int index, int total)
        {
            try
            {
                var lines = new List<string>();

                // Character name (captured by UpdateName hook).
                if (!string.IsNullOrEmpty(_statusPlayerName))
                    lines.Add(_statusPlayerName);

                // Level, HP, MP (captured by LevelPresenter.Setup hook).
                if (_statusLevelData != null)
                {
                    lines.Add(Loc.Get("camp_status_level_hp_mp",
                        _statusLevelData.level,
                        _statusLevelData.hp, _statusLevelData.maxHp,
                        _statusLevelData.mp, _statusLevelData.maxMp));
                    lines.Add(Loc.Get("camp_status_exp",
                        _statusLevelData.exp, _statusLevelData.nextExp));
                }

                // Combat stats — one line per stat for virtual cursor navigation.
                if (_statusParamData != null)
                {
                    lines.Add($"{Loc.Get("camp_status_stat_attack")}: {_statusParamData.attack}");
                    lines.Add($"{Loc.Get("camp_status_stat_defence")}: {_statusParamData.defence}");
                    lines.Add($"{Loc.Get("camp_status_stat_magic")}: {_statusParamData.magic}");
                    lines.Add($"{Loc.Get("camp_status_stat_hit")}: {_statusParamData.hit}");
                    lines.Add($"{Loc.Get("camp_status_stat_dodge")}: {_statusParamData.dodge}");
                    lines.Add($"{Loc.Get("camp_status_stat_critical")}: {_statusParamData.critical}");
                    lines.Add($"{Loc.Get("camp_status_stat_str")}: {_statusParamData.str}");
                    lines.Add($"{Loc.Get("camp_status_stat_con")}: {_statusParamData.con}");
                    lines.Add($"{Loc.Get("camp_status_stat_dex")}: {_statusParamData.dex}");
                    lines.Add($"{Loc.Get("camp_status_stat_agl")}: {_statusParamData.agl}");
                    lines.Add($"{Loc.Get("camp_status_stat_int")}: {_statusParamData.intelligence}");
                    lines.Add($"{Loc.Get("camp_status_stat_luc")}: {_statusParamData.luc}");
                    lines.Add($"{Loc.Get("camp_status_stat_stamina")}: {_statusParamData.stm}");
                    lines.Add($"{Loc.Get("camp_status_stat_guts")}: {_statusParamData.guts}");
                }

                // Age (read directly from presenter — CallerCount 0, not hookable).
                try
                {
                    var agePresenter = _statusSelector?.statusPresenter?.agePresenter;
                    var valuePresenter = agePresenter?.valuePresenter;
                    if (valuePresenter != null)
                    {
                        string ageText = valuePresenter.age?.gameText?.text;
                        if (!string.IsNullOrEmpty(ageText))
                            lines.Add(Loc.Get("camp_status_age", ageText));
                    }
                }
                catch (Exception ageEx)
                {
                    DebugLogger.LogState($"CampStatus: age read failed: {ageEx.Message}");
                }

                // Favorite food — the likeFood GameText is the label "Favorite Food".
                // The actual food name is set by native SetLikeFood(string) into the same
                // GameText. When a food hasn't been discovered yet, the text just says
                // "Favorite Food" (the label default). Only announce when the text differs
                // from the label, meaning an actual food has been discovered.
                try
                {
                    var likeFoodGT = _statusSelector?.statusPresenter?.likeFood;
                    if (likeFoodGT != null)
                    {
                        string foodText = likeFoodGT.text;
                        if (!string.IsNullOrEmpty(foodText) &&
                            foodText != "Favorite Food" && foodText != "Favourite Food")
                        {
                            lines.Add(Loc.Get("camp_status_favorite_food", foodText));
                            DebugLogger.LogGameValue("CampStatus.food", foodText);
                        }
                    }
                }
                catch (Exception foodEx)
                {
                    DebugLogger.LogState($"CampStatus: food read failed: {foodEx.Message}");
                }

                // Elemental affinities — individual lines per element for virtual cursor.
                if (_cachedStatusElementalLines.Count > 0)
                {
                    foreach (var el in _cachedStatusElementalLines)
                        lines.Add(el);
                }
                else if (!string.IsNullOrEmpty(_cachedStatusElementalAnnouncement))
                {
                    lines.Add(_cachedStatusElementalAnnouncement);
                }

                // Friendship levels — individual lines per party member for virtual cursor.
                if (_cachedFriendshipLines.Count > 0)
                {
                    foreach (var fl in _cachedFriendshipLines)
                        lines.Add(fl);
                }
                else if (!string.IsNullOrEmpty(_cachedFriendshipAnnouncement))
                {
                    lines.Add(_cachedFriendshipAnnouncement);
                }

                // Position in party.
                lines.Add(Loc.Get("camp_status_position", index + 1, total));

                // Store for virtual cursor navigation.
                _statusVirtualLines = lines;
                _statusVirtualIndex = -1;

                DebugLogger.LogGameValue("CampStatus.char",
                    $"name='{_statusPlayerName}' idx={index} ({index + 1}/{total}) " +
                    $"lines={lines.Count} " +
                    $"levelData={(_statusLevelData != null ? "yes" : "null")} " +
                    $"paramData={(_statusParamData != null ? "yes" : "null")}");

                // Announce everything at once on entry.
                ScreenReader.Say(string.Join(" ", lines));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.AnnounceStatusCharacter: {ex.Message}");
            }
        }

        #endregion

        #region Status Postfix Hooks

        /// <summary>
        /// Postfix for UICampStatusParameterPresenter.Setup(UICampStatusParameterData).
        /// Fires whenever the status parameter panel updates — on screen open and character
        /// tab changes. Captures the stat data so UpdateStatusSelector can include all stats
        /// (attack, defence, magic, hit, dodge, critical, str, con, dex, agl, int, luc,
        /// stamina, guts) in the announcement.
        /// </summary>
        private static void StatusParamPresenter_Setup_Postfix(UICampStatusParameterData data)
        {
            _statusParamData = data;
            DebugLogger.LogGameValue("CampStatus.paramHook",
                $"attack={data?.attack} defence={data?.defence} str={data?.str}");
        }

        #endregion

        #region Status Screen Hooks (hook-driven detection)

        /// <summary>
        /// Postfix for UICampStatusSelector.UpdatePresenter(int, int, bool).
        /// Fires LAST in the hook chain when the status screen opens or character tab changes.
        /// By this point, UpdateName, LevelPresenter.Setup, and StatusParamPresenter.Setup
        /// have already captured all the data we need. This is the announcement trigger.
        /// </summary>
        private static void Diag_StatusSelector_UpdatePresenter(int index, int difference, bool isDelay)
        {
            try
            {
                // Announce heading on first open.
                if (!_statusScreenOpen)
                {
                    _statusScreenOpen = true;
                    _statusLastIndex = -1;
                    _statusLastPageIndex = _statusSelector?.pageIndex ?? 0;
                    ScreenReader.Say(Loc.Get("camp_status_screen"));
                    DebugLogger.LogState("CampStatus: screen opened (hook-driven).");
                }

                // Get total from tab presenter's data list.
                int total = 1;
                if (_statusSelector != null)
                {
                    var tabPresenter = _statusSelector.statusPresenter?.characterTabPresenter;
                    var tabList = tabPresenter?.itemTabDataList;
                    if (tabList != null)
                        total = tabList.Count;
                }

                // Skip if same index (shouldn't happen often since game fires on change).
                if (index == _statusLastIndex) return;
                _statusLastIndex = index;

                // Only announce stats on the stats page (page 0). Other pages
                // have their own hooks (e.g. UITalentPresenter.Set for talents).
                int currentPage = _statusSelector?.pageIndex ?? 0;
                if (currentPage == 0)
                {
                    AnnounceStatusCharacter(index, total);
                }
                else
                {
                    DebugLogger.LogState($"CampStatus: UpdatePresenter on page {currentPage}, skipping stats announcement.");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.StatusUpdatePresenter: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for UICampStatusSelector.UpdateName(PlayerID, ConstPlayerParameter).
        /// Fires when the character name updates — captures the name for announcement.
        /// </summary>
        private static void Diag_StatusSelector_UpdateName(PlayerID playerID, ConstPlayerParameter playerParam)
        {
            try
            {
                var pm = ParameterManager.Instance;
                _statusPlayerID = playerID;
                _statusPlayerName = pm != null ? (pm.GetCharacterFirstName(playerID) ?? "") : playerID.ToString();
                DebugLogger.LogGameValue("CampStatus.nameHook", $"playerID={playerID} name='{_statusPlayerName}'");
            }
            catch (Exception ex)
            {
                _statusPlayerName = playerID.ToString();
                MelonLogger.Warning($"CampMenuHandler.StatusUpdateName: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for UICampStatusLevelPresenter.Setup(UICampStatusLevelData).
        /// Fires when the level/HP/MP presenter updates — captures the data for announcement.
        /// </summary>
        private static void Diag_StatusLevelPresenter_Setup(UICampStatusLevelData data)
        {
            _statusLevelData = data;
            DebugLogger.LogGameValue("CampStatus.levelHook",
                data != null ? $"lv={data.level} hp={data.hp}/{data.maxHp} mp={data.mp}/{data.maxMp}" : "null");
        }

        /// <summary>
        /// Postfix for UITalentPresenter.Set(List&lt;UITalentData&gt;).
        /// Fires when the status screen initializes (page 0), NOT on page switch.
        /// Caches the talent announcement string. If already on the talent page (page 1),
        /// announces immediately (handles character tab change while viewing talents).
        /// </summary>
        private static void TalentPresenter_Set_Postfix(
            Il2CppSystem.Collections.Generic.List<UITalentData> dataList)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append(Loc.Get("camp_status_talents_screen"));

                if (dataList != null && dataList.Count > 0)
                {
                    for (int i = 0; i < dataList.Count; i++)
                    {
                        var talent = dataList[i];
                        if (talent == null) continue;
                        string name = talent.talentName ?? "";
                        if (string.IsNullOrEmpty(name)) continue;
                        sb.Append(" ");
                        sb.Append(name);
                        if (i < dataList.Count - 1) sb.Append(",");
                    }
                }
                else
                {
                    sb.Append(" ");
                    sb.Append(Loc.Get("camp_status_talents_none"));
                }

                _cachedTalentAnnouncement = sb.ToString();
                DebugLogger.LogGameValue("CampStatus.talents",
                    $"count={dataList?.Count ?? 0} cached='{_cachedTalentAnnouncement}'");

                // If already on the talent page, announce immediately.
                // This handles character tab changes while viewing talents.
                int pageIdx = _statusSelector?.pageIndex ?? 0;
                if (_statusScreenOpen && pageIdx == 1)
                {
                    ScreenReader.Say(_cachedTalentAnnouncement);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.TalentPresenter_Set_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for UICampStatusPresenter.SetEmotion(List&lt;UICampStatusFavorabilityRatingItemListData&gt;).
        /// Fires when the friendship panel updates on the status screen (CallerCount 1).
        /// Caches a formatted friendship string for inclusion in the page 0 announcement.
        /// Character identification via faceIcon sprite name with fallback to index.
        /// </summary>
        private static void StatusPresenter_SetEmotion_Postfix(
            Il2CppSystem.Collections.Generic.List<UICampStatusFavorabilityRatingItemListData> dataList)
        {
            if (_lastRootMenuItemName != "Status") return;

            try
            {
                if (dataList == null || dataList.Count == 0)
                {
                    _cachedFriendshipAnnouncement = Loc.Get("camp_status_friendship_none");
                    _cachedFriendshipLines.Clear();
                    DebugLogger.LogGameValue("CampStatus.friendship", "no data");
                    return;
                }

                // Build party member name list excluding the currently selected character.
                // The friendship list contains entries for all OTHER party members in order.
                // Use _statusPlayerID (set by UpdateName hook which fires before SetEmotion)
                // rather than _statusLastIndex (set by UpdatePresenter which fires AFTER).
                var otherNames = new System.Collections.Generic.List<string>();
                try
                {
                    var tabList = _statusSelector?.statusPresenter?.characterTabPresenter?.itemTabDataList;
                    if (tabList != null)
                    {
                        var pm = ParameterManager.Instance;
                        for (int t = 0; t < tabList.Count; t++)
                        {
                            var tabItem = tabList[t]?.TryCast<UICharacterTabItemData>();
                            if (tabItem == null) continue;
                            if (tabItem.playerID == _statusPlayerID) continue;
                            string name = pm?.GetCharacterFirstName(tabItem.playerID);
                            otherNames.Add(!string.IsNullOrEmpty(name) ? name : tabItem.playerID.ToString());
                        }
                    }
                }
                catch (Exception tabEx)
                {
                    DebugLogger.LogState($"CampStatus: friendship tab lookup failed: {tabEx.Message}");
                }

                var friendLines = new List<string>();
                for (int i = 0; i < dataList.Count; i++)
                {
                    var entry = dataList[i];
                    if (entry == null) continue;

                    string charName = (i < otherNames.Count) ? otherNames[i] : $"Character {i + 1}";
                    int percentage = (int)(entry.favorabilityRating * 100);

                    DebugLogger.LogGameValue($"CampStatus.friendship[{i}]",
                        $"name='{charName}' rating={entry.favorabilityRating:F2} pct={percentage} ending={entry.isEnding}");

                    string line = entry.isEnding
                        ? Loc.Get("camp_status_friendship_ending", charName, percentage)
                        : Loc.Get("camp_status_friendship_entry", charName, percentage);
                    friendLines.Add(line);
                }

                _cachedFriendshipLines = friendLines;
                _cachedFriendshipAnnouncement = Loc.Get("camp_status_friendship") + " " +
                    string.Join(". ", friendLines);
                DebugLogger.LogGameValue("CampStatus.friendship",
                    $"count={dataList.Count} cached='{_cachedFriendshipAnnouncement}'");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.StatusPresenter_SetEmotion_Postfix: {ex.Message}");
            }
        }

        #endregion
    }
}
