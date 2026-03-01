using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
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
        private static int _statusLastPageIndex = -1;
        /// <summary>
        /// Cached talent announcement string built by UITalentPresenter.Set hook.
        /// The hook fires on status screen open (page 0) — not on page switch.
        /// We cache the string and announce it when pageIndex changes to 1 (talent page).
        /// </summary>
        private static string _cachedTalentAnnouncement = "";

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
                if (pageIdx == _statusLastPageIndex) return;

                int oldPage = _statusLastPageIndex;
                _statusLastPageIndex = pageIdx;

                // Skip initial page set (handled by UpdatePresenter heading).
                if (oldPage < 0) return;

                // Reset character index so switching characters on a new page
                // will re-trigger the UpdatePresenter announcement.
                _statusLastIndex = -1;

                DebugLogger.LogState($"CampStatus: page changed {oldPage} → {pageIdx}.");

                // Page 0 = stats (UpdatePresenter hooks will fire and announce).
                // Page 1 = talents (announce cached data from UITalentPresenter.Set hook).
                if (pageIdx == 1 && !string.IsNullOrEmpty(_cachedTalentAnnouncement))
                {
                    ScreenReader.Say(_cachedTalentAnnouncement);
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
        /// Data sources: _statusPlayerName (UpdateName hook), _statusLevelData
        /// (LevelPresenter.Setup hook), _statusParamData (StatusParamPresenter.Setup hook).
        /// </summary>
        private static void AnnounceStatusCharacter(int index, int total)
        {
            try
            {
                var sb = new StringBuilder();

                // Character name (captured by UpdateName hook).
                if (!string.IsNullOrEmpty(_statusPlayerName))
                    sb.Append(_statusPlayerName + ". ");

                // Level, HP, MP, EXP (captured by LevelPresenter.Setup hook).
                if (_statusLevelData != null)
                {
                    sb.Append(Loc.Get("camp_status_level_hp_mp",
                        _statusLevelData.level,
                        _statusLevelData.hp, _statusLevelData.maxHp,
                        _statusLevelData.mp, _statusLevelData.maxMp));
                    sb.Append(" ");
                    sb.Append(Loc.Get("camp_status_exp",
                        _statusLevelData.exp, _statusLevelData.nextExp));
                    sb.Append(" ");
                }

                // Combat stats and base attributes (captured by StatusParamPresenter.Setup hook).
                if (_statusParamData != null)
                {
                    sb.Append(Loc.Get("camp_status_combat",
                        _statusParamData.attack, _statusParamData.defence,
                        _statusParamData.magic, _statusParamData.hit,
                        _statusParamData.dodge, _statusParamData.critical));
                    sb.Append(" ");
                    sb.Append(Loc.Get("camp_status_attributes",
                        _statusParamData.str, _statusParamData.con,
                        _statusParamData.dex, _statusParamData.agl,
                        _statusParamData.intelligence, _statusParamData.luc));
                    sb.Append(" ");
                    sb.Append(Loc.Get("camp_status_stamina_guts",
                        _statusParamData.stm, _statusParamData.guts));
                    sb.Append(" ");
                }

                // Position in party.
                sb.Append(Loc.Get("camp_status_position", index + 1, total));

                DebugLogger.LogGameValue("CampStatus.char",
                    $"name='{_statusPlayerName}' idx={index} ({index + 1}/{total}) " +
                    $"levelData={(_statusLevelData != null ? "yes" : "null")} " +
                    $"paramData={(_statusParamData != null ? "yes" : "null")}");

                ScreenReader.Say(sb.ToString());
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

        #endregion
    }
}
