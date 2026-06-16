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
                    string catName = MissionReadout.GetCategoryName(cat);
                    if (_missionState.LastIndex >= 0) // Don't announce on first open
                    {
                        ScreenReader.Say(Loc.Get("mission_category", catName));
                        _missionState.LastIndex = -1; // Force re-announce of current item
                    }
                }

                int idx = _missionListBase.currentIndex;
                if (idx == _missionState.LastIndex) return;
                _missionState.LastIndex = idx;

                string announcement = MissionReadout.BuildItemAnnouncement(
                    _missionSelector, _missionListBase, idx, out string name, out string status);
                if (announcement == null) return;

                ScreenReader.Say(announcement);

                DebugLogger.LogGameValue("CampMission.item",
                    $"{name} [{status}] ({idx + 1})");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateMissionList: {ex.Message}");
                _missionListBase = null;
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
