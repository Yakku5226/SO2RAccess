using Il2CppGame;
using MelonLoader;
using System;

namespace SO2RAccess
{
    /// <summary>
    /// Mission list sub-screen accessibility (camp menu → Quests and Missions → Missions).
    ///
    /// The mission window (UIMissionWindow) opens separately from the camp window.
    /// UIMissionListSelector extends UIListSelectorBase — polling-based navigation.
    /// Data: UIMissionListItemData with missionName, stateMessage, isClear, isAchieved.
    /// Categories: Beginner, Expert, Specialist, Legend (via currentCategory on selector).
    /// </summary>
    public partial class CampMenuHandler
    {
        #region Mission Fields

        private static UIMissionWindow _campMissionWindow;
        private static UIMissionListSelector _missionSelector;
        private static UIListSelectorBase _missionListBase;
        private static readonly SubScreenState _missionState = new SubScreenState();
        private static int _missionLastCategory = -1;

        #endregion

        #region Mission Hook

        /// <summary>
        /// Postfix for GameUIManager.OpenMissionWindow — captures the UIMissionWindow
        /// reference when opened from camp.
        /// </summary>
        private static void OpenMissionWindow_Postfix(UIMissionWindow __result)
        {
            if (__result == null) return;
            if (!IsCampOpen) return; // Only capture when opened from camp
            _campMissionWindow = __result;
            _missionSelector = __result.missionListSelector;
            _missionListBase = null;
            DebugLogger.LogState("CampMission: captured UIMissionWindow from hook.");
        }

        #endregion

        #region Mission Update

        /// <summary>
        /// Polls the mission list selector and announces entries.
        /// Gated on _lastRootMenuItemName == "MissionList" (camp sub-menu).
        /// </summary>
        private void UpdateMissionList()
        {
            if (_lastRootMenuItemName != "MissionList") return;
            if (_missionSelector == null)
            {
                TryFindMissionWindow();
                if (_missionSelector == null) return;
            }

            try
            {
                if (_missionListBase == null)
                {
                    _missionListBase = _missionSelector.TryCast<UIListSelectorBase>();
                    if (_missionListBase == null) return;
                }

                bool isActive = _missionSelector.gameObject.activeInHierarchy;
                if (!_missionState.CheckEntry(isActive,
                    () => ScreenReader.Say(Loc.Get("mission_screen")),
                    "CampMission"))
                    return;

                // Announce category changes (Beginner, Expert, Specialist, Legend)
                int cat = (int)_missionSelector.currentCategory;
                if (cat != _missionLastCategory)
                {
                    _missionLastCategory = cat;
                    string catName = GetMissionCategoryName(cat);
                    if (_missionState.LastIndex >= 0) // Don't announce on first open
                    {
                        ScreenReader.Say(Loc.Get("mission_category", catName));
                        _missionState.LastIndex = -1; // Force re-announce of current item
                    }
                }

                int idx = _missionListBase.currentIndex;
                if (idx == _missionState.LastIndex) return;
                _missionState.LastIndex = idx;

                var list = _missionListBase.currentDataList;
                if (list == null) return;
                int total = list.Count;
                if (total == 0 || idx < 0 || idx >= total) return;

                var item = list[idx].TryCast<UIMissionListItemData>();
                if (item == null) return;

                string name = item.missionName;
                if (string.IsNullOrEmpty(name))
                {
                    ScreenReader.Say(Loc.Get("mission_empty", idx + 1, total));
                    return;
                }

                string status = GetMissionStatusText(item);
                string announcement = Loc.Get("mission_item", name, status, idx + 1, total);

                // Append the mission's reward (resolved names from the selector's own
                // reward panel, which the game refreshes for the highlighted mission).
                string reward = BuildMissionReward();
                if (!string.IsNullOrEmpty(reward))
                    announcement += " " + reward;

                ScreenReader.Say(announcement);

                DebugLogger.LogGameValue("CampMission.item",
                    $"{name} [{status}] ({idx + 1}/{total}) reward='{reward}'");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateMissionList: {ex.Message}");
                _missionListBase = null;
            }
        }

        /// <summary>
        /// Builds the reward clause for the highlighted mission from the selector's
        /// rewardItemDataList — the same data the game shows in its reward panel, with
        /// names already resolved (so it works for items whose IDs we can't resolve
        /// ourselves, e.g. gems). Returns null when no reward data is available.
        /// </summary>
        private string BuildMissionReward()
        {
            try
            {
                var rewards = _missionSelector?.rewardItemDataList;
                if (rewards == null || rewards.Count == 0) return null;

                var parts = new System.Collections.Generic.List<string>();
                for (int i = 0; i < rewards.Count; i++)
                {
                    var r = rewards[i];
                    if (r == null) continue;

                    string rName = TextUtil.StripTags(r.rewardName ?? "");
                    if (string.IsNullOrEmpty(rName)) continue;

                    int count = r.itemCount;
                    parts.Add(count > 1
                        ? Loc.Get("overflow_item_multi", rName, count)
                        : Loc.Get("overflow_item", rName));
                }

                if (parts.Count == 0) return null;
                return Loc.Get("mission_reward", string.Join(", ", parts));
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"BuildMissionReward error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Maps mission data flags to a user-friendly status string.
        /// </summary>
        private static string GetMissionStatusText(UIMissionListItemData item)
        {
            if (item.isClear)
                return Loc.Get("mission_status_complete");

            // stateMessage is the game's own localized status text
            string stateMsg = item.stateMessage;
            if (!string.IsNullOrEmpty(stateMsg))
                return stateMsg;

            // Fallback to flags/enum
            if (item.isAchieved)
                return Loc.Get("mission_status_achieved");

            var state = item.missionState;
            switch (state)
            {
                case UIMissionStatePresenter.MissionState.Completed:
                    return Loc.Get("mission_status_complete");
                case UIMissionStatePresenter.MissionState.Achieved:
                    return Loc.Get("mission_status_achieved");
                case UIMissionStatePresenter.MissionState.Reportable:
                    return Loc.Get("mission_status_reportable");
                case UIMissionStatePresenter.MissionState.Received:
                    return Loc.Get("mission_status_in_progress");
                case UIMissionStatePresenter.MissionState.NotAchieved:
                    return Loc.Get("mission_status_incomplete");
                case UIMissionStatePresenter.MissionState.NotReceived:
                    return Loc.Get("mission_status_incomplete");
                default:
                    return Loc.Get("mission_status_incomplete");
            }
        }

        /// <summary>
        /// Returns a friendly name for the mission category.
        /// </summary>
        private static string GetMissionCategoryName(int category)
        {
            switch (category)
            {
                case 0: return Loc.Get("mission_cat_beginner");
                case 1: return Loc.Get("mission_cat_expert");
                case 2: return Loc.Get("mission_cat_specialist");
                case 3: return Loc.Get("mission_cat_legend");
                default: return category.ToString();
            }
        }

        /// <summary>
        /// Fallback to find the mission window via GameUIManager or FindObjectOfType.
        /// </summary>
        private static void TryFindMissionWindow()
        {
            try
            {
                var guiMgr = GameUIManager.Instance;
                if (guiMgr != null)
                {
                    var wc = guiMgr.GetWindow(UIDefine.WindowType.Mission);
                    if (wc != null)
                    {
                        _campMissionWindow = wc.TryCast<UIMissionWindow>();
                        if (_campMissionWindow != null)
                        {
                            _missionSelector = _campMissionWindow.missionListSelector;
                            DebugLogger.LogState("CampMission: found window via GetWindow.");
                            return;
                        }
                    }
                }

                var found = UnityEngine.Object.FindObjectOfType<UIMissionWindow>();
                if (found != null)
                {
                    _campMissionWindow = found;
                    _missionSelector = found.missionListSelector;
                    DebugLogger.LogState("CampMission: found window via FindObjectOfType.");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampMission find error: {ex.Message}");
            }
        }

        #endregion
    }
}
