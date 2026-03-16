using Il2CppGame;
using MelonLoader;
using System;
using System.Text;

namespace SO2RAccess
{
    /// <summary>
    /// Quest list sub-screen accessibility (camp menu → Quests and Missions → Quests).
    ///
    /// The quest window (UIQuestWindow) opens separately from the camp window.
    /// UIQuestSelector extends UIListSelectorBase — polling-based navigation.
    /// Data: UIQuestListItemData with missionName, missionState, isNew, isEnd.
    /// Description: UIQuestDescriptionPresenter with questTitle and questDescription.
    /// </summary>
    public partial class CampMenuHandler
    {
        #region Quest Fields

        private static UIQuestWindow _questWindow;
        private static UIQuestSelector _questSelector;
        private static UIListSelectorBase _questListBase;
        private static readonly SubScreenState _questState = new SubScreenState();

        #endregion

        #region Quest Hook

        /// <summary>
        /// Postfix for GameUIManager.OpenQuestWindow — captures the UIQuestWindow reference.
        /// </summary>
        private static void OpenQuestWindow_Postfix(UIQuestWindow __result)
        {
            if (__result == null) return;
            _questWindow = __result;
            _questSelector = __result.questSelector;
            _questListBase = null;
            DebugLogger.LogState("Quest: captured UIQuestWindow from hook.");
        }

        #endregion

        #region Quest Update

        /// <summary>
        /// Polls the quest list selector and announces entries.
        /// Gated on _lastRootMenuItemName == "QuestList" (camp sub-menu).
        /// </summary>
        private void UpdateQuestList()
        {
            if (_lastRootMenuItemName != "QuestList") return;
            if (_questSelector == null)
            {
                TryFindQuestWindow();
                if (_questSelector == null) return;
            }

            try
            {
                if (_questListBase == null)
                {
                    _questListBase = _questSelector.TryCast<UIListSelectorBase>();
                    if (_questListBase == null) return;
                }

                bool isActive = _questSelector.gameObject.activeInHierarchy;
                if (!_questState.CheckEntry(isActive,
                    () => ScreenReader.Say(Loc.Get("quest_screen")),
                    "CampQuest"))
                    return;

                int idx = _questListBase.currentIndex;
                if (idx == _questState.LastIndex)
                {
                    CheckQuestDetailPress(idx);
                    return;
                }
                _questState.LastIndex = idx;

                var list = _questListBase.currentDataList;
                if (list == null) return;
                int total = list.Count;
                if (total == 0 || idx < 0 || idx >= total) return;

                var item = list[idx].TryCast<UIQuestListItemData>();
                if (item == null) return;

                string name = item.missionName;
                if (string.IsNullOrEmpty(name))
                {
                    ScreenReader.Say(Loc.Get("quest_empty", idx + 1, total));
                    return;
                }

                string status = GetQuestStatusText(item);
                if (item.isNew)
                    ScreenReader.Say(Loc.Get("quest_item_new", name, status, idx + 1, total));
                else
                    ScreenReader.Say(Loc.Get("quest_item", name, status, idx + 1, total));

                DebugLogger.LogGameValue("CampQuest.item",
                    $"{name} [{status}] ({idx + 1}/{total})");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CampMenuHandler.UpdateQuestList: {ex.Message}");
                _questListBase = null;
            }
        }

        /// <summary>
        /// Reads quest description on confirm press.
        /// </summary>
        private void CheckQuestDetailPress(int idx)
        {
            var gim = GameInputManager.Instance;
            if (gim == null || !gim.IsDown(GameInputManager.InputAction.Decision)) return;
            if (_questListBase == null) return;

            var list = _questListBase.currentDataList;
            if (list == null) return;
            int total = list.Count;
            if (idx < 0 || idx >= total) return;

            var item = list[idx].TryCast<UIQuestListItemData>();
            if (item == null) return;
            if (string.IsNullOrEmpty(item.missionName)) return;

            try
            {
                // Try reading from the description presenter
                var descPresenter = _questSelector.descriptionPresenter;
                if (descPresenter != null)
                {
                    var sb = new StringBuilder();
                    string title = descPresenter.questTitle?.text;
                    string desc = descPresenter.questDescription?.text;

                    if (!string.IsNullOrEmpty(title))
                        AppendSentence(sb, title);
                    if (!string.IsNullOrEmpty(desc))
                        AppendSentence(sb, desc);

                    // Try reading rewards from presenters
                    var rewards = descPresenter.rewardElementPresenterList;
                    if (rewards != null && rewards.Count > 0)
                    {
                        var rewardNames = new System.Collections.Generic.List<string>();
                        for (int i = 0; i < rewards.Count; i++)
                        {
                            var reward = rewards[i];
                            if (reward == null) continue;
                            var go = reward.gameObject;
                            if (go == null || !go.activeInHierarchy) continue;

                            string rName = reward.rewardName?.itemName?.text;
                            string rValue = reward.rewardValue?.text;
                            if (!string.IsNullOrEmpty(rName))
                            {
                                if (!string.IsNullOrEmpty(rValue) && rValue != "0")
                                    rewardNames.Add($"{rName} x{rValue}");
                                else
                                    rewardNames.Add(rName);
                            }
                        }
                        if (rewardNames.Count > 0)
                            AppendSentence(sb, Loc.Get("quest_rewards",
                                string.Join(", ", rewardNames)));
                    }

                    string result = sb.ToString().Trim();
                    if (!string.IsNullOrEmpty(result))
                    {
                        ScreenReader.Say(result);
                        return;
                    }
                }

                // Fallback: just re-read the item data
                string status = GetQuestStatusText(item);
                ScreenReader.Say($"{item.missionName}. {status}.");
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CampQuest detail error: {ex.Message}");
            }
        }

        /// <summary>
        /// Maps MissionState enum to a user-friendly status string.
        /// </summary>
        private static string GetQuestStatusText(UIQuestListItemData item)
        {
            if (item.isEnd)
                return Loc.Get("quest_status_completed");
            if (item.isReportable)
                return Loc.Get("quest_status_reportable");
            if (item.isReceived)
                return Loc.Get("quest_status_received");

            // Fallback to enum
            var state = item.missionState;
            switch (state)
            {
                case UIMissionStatePresenter.MissionState.Completed:
                    return Loc.Get("quest_status_completed");
                case UIMissionStatePresenter.MissionState.Achieved:
                    return Loc.Get("quest_status_completed");
                case UIMissionStatePresenter.MissionState.Reportable:
                    return Loc.Get("quest_status_reportable");
                case UIMissionStatePresenter.MissionState.Received:
                    return Loc.Get("quest_status_received");
                case UIMissionStatePresenter.MissionState.NotAchieved:
                    return Loc.Get("quest_status_not_achieved");
                case UIMissionStatePresenter.MissionState.NotReceived:
                    return Loc.Get("quest_status_available");
                default:
                    return Loc.Get("quest_status_available");
            }
        }

        /// <summary>
        /// Fallback to find the quest window via GameUIManager or FindObjectOfType.
        /// </summary>
        private static void TryFindQuestWindow()
        {
            try
            {
                var guiMgr = GameUIManager.Instance;
                if (guiMgr != null)
                {
                    var wc = guiMgr.GetWindow(UIDefine.WindowType.Quest);
                    if (wc != null)
                    {
                        _questWindow = wc.TryCast<UIQuestWindow>();
                        if (_questWindow != null)
                        {
                            _questSelector = _questWindow.questSelector;
                            DebugLogger.LogState("Quest: found window via GetWindow.");
                            return;
                        }
                    }
                }

                var found = UnityEngine.Object.FindObjectOfType<UIQuestWindow>();
                if (found != null)
                {
                    _questWindow = found;
                    _questSelector = found.questSelector;
                    DebugLogger.LogState("Quest: found window via FindObjectOfType.");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"Quest find error: {ex.Message}");
            }
        }

        #endregion
    }
}
