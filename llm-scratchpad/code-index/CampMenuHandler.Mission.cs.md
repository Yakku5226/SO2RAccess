# CampMenuHandler.Mission.cs (210 lines)

Partial class fragment — mission list sub-screen accessibility for the camp menu
(camp → Quests and Missions → Missions). Uses UIMissionWindow / UIMissionListSelector
with polling-based navigation (native C++ wall, same pattern as other sub-screens).
namespace: SO2RAccess (line 5)
usings (non-System / notable only): Il2CppGame, MelonLoader

## partial class CampMenuHandler (line 15)
Mission list sub-screen: UIMissionWindow (opened separately from UICampWindow).
UIMissionListSelector extends UIListSelectorBase — polling currentIndex.
Data: UIMissionListItemData (missionName, stateMessage, isClear, isAchieved, missionState).
Categories: Beginner(0), Expert(1), Specialist(2), Legend(3) via currentCategory.

fields/properties (declaration order):
- _campMissionWindow : static UIMissionWindow (line 19)
- _missionSelector : static UIMissionListSelector (line 20)
- _missionListBase : static UIListSelectorBase (line 21)  — cast of _missionSelector, used to read currentIndex/currentDataList
- _missionState : static readonly SubScreenState (line 22)
- _missionLastCategory : static int (line 23)  — tracks last announced category to detect changes; -1 = not yet set

methods (declaration order):

- static void OpenMissionWindow_Postfix(UIMissionWindow __result) (line 33)
  - note: Postfix for GameUIManager.OpenMissionWindow. Captures UIMissionWindow only when IsCampOpen is true (guards against non-camp openings).

- void UpdateMissionList() (line 51)
  - note: Per-frame poller. Gated on _lastRootMenuItemName == "MissionList". Calls TryFindMissionWindow if selector is null. Announces category changes (resets LastIndex to force re-announce). Reads currentIndex from UIListSelectorBase; casts item to UIMissionListItemData; announces name + status + position via Loc.Get("mission_item").

- static string GetMissionStatusText(UIMissionListItemData item) (line 122)
  - note: Priority: isClear flag → stateMessage (game's own localized text) → isAchieved flag → missionState enum (Completed/Achieved/Reportable/Received/NotAchieved/NotReceived). Returns Loc.Get key for each.

- static string GetMissionCategoryName(int category) (line 159)
  - note: Maps 0-3 to Loc.Get("mission_cat_beginner/expert/specialist/legend"); falls back to category.ToString().

- static void TryFindMissionWindow() (line 174)
  - note: Fallback finder. Tries GameUIManager.Instance.GetWindow(UIDefine.WindowType.Mission) first, then FindObjectOfType<UIMissionWindow>. Sets _campMissionWindow and _missionSelector.
