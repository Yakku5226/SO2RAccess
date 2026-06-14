# CampMenuHandler.Quest.cs (261 lines)

Partial class fragment of CampMenuHandler — quest list sub-screen accessibility.
Quest window (UIQuestWindow) opens separately from camp window. UIQuestSelector extends UIListSelectorBase — polling-based navigation.
Data: UIQuestListItemData (missionName, missionState, isNew, isEnd, isReportable, isReceived).
Description: UIQuestDescriptionPresenter (questTitle, questDescription, rewardElementPresenterList).
namespace: SO2RAccess (line 7)
usings: Il2CppGame, MelonLoader, System.Text

## partial class CampMenuHandler (line 17)

fields/properties (declaration order):
- _questWindow : UIQuestWindow (line 21)  [— static]
- _questSelector : UIQuestSelector (line 22)  [— static]
- _questListBase : UIListSelectorBase (line 23)  [— static; _questSelector cast to base for currentIndex/currentDataList access]
- _questState : SubScreenState (line 24)  [— static readonly; tracks active/inactive transitions and last index]

methods (declaration order):
- static void OpenQuestWindow_Postfix(UIQuestWindow __result) (line 32)
  - note: Harmony postfix on GameUIManager.OpenQuestWindow. Captures UIQuestWindow, questSelector references; clears _questListBase.
- void UpdateQuestList() (line 49)
  - note: Polling update called each frame. Gated on _lastRootMenuItemName == "QuestList". Uses SubScreenState.CheckEntry for open/close announcements. On index change announces item name, status (via GetQuestStatusText), isNew flag, and position. Calls CheckQuestDetailPress when index unchanged.
- void CheckQuestDetailPress(int idx) (line 114)
  - note: On Decision button press, reads UIQuestDescriptionPresenter (questTitle, questDescription, rewardElementPresenterList) and announces full detail. Formats rewards as "name xvalue". Falls back to item name + status string if presenter is empty.
- static string GetQuestStatusText(UIQuestListItemData item) (line 192)
  - note: Maps item flags (isEnd, isReportable, isReceived) and missionState enum to localised status strings. Priority: flags first, then enum switch.
- static void TryFindQuestWindow() (line 225)
  - note: Fallback discovery; tries GameUIManager.GetWindow(WindowType.Quest) first, then FindObjectOfType<UIQuestWindow>. Sets _questWindow and _questSelector.
