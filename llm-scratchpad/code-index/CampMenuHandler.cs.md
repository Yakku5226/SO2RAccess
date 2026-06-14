# CampMenuHandler.cs (1062 lines)

NOTE: This is the ROOT partial class fragment. Other partial fragments exist in sibling files
(e.g. CampMenuHandler.Items.cs, CampMenuHandler.Status.cs, etc.). All Harmony patch
registration and the Update() polling dispatcher live here.

Announces camp menu navigation to the screen reader. Patches UICampWindow.Open,
UIItemInformationPresenter.Set, UIBattleSkillInformationPresenter.Set,
UICampStatusParameterPresenter.Setup, and ~15 more hooks. Navigation is native C++;
all sub-screen cursors are polled from Update(). Root menu type: UICampMenuSelector.

namespace: SO2RAccess (line 8)
usings (non-System / notable only): HarmonyLib, Il2CppGame, MelonLoader

## partial class CampMenuHandler (line 61)
Announces camp menu navigation to the screen reader.

fields/properties (declaration order):
- _patchesApplied : bool (line 65)
- _menuSelector : static UICampMenuSelector (line 72)
- _lastIndex : static int (line 73)
- _wasActive : static bool (line 74)
- IsCampOpen : static bool { get; private set; } (line 80)  — true while camp window is open; read by NavigationHandler
- _campOpenTime : static float (line 87)  — guards against IsOpened false-positive during opening animation
- _campWindow : static UICampWindow (line 90)  — cached for closure detection
- _lastRootMenuItemName : static string (line 93)  — tracks which root menu item is highlighted; used for sub-screen gating

methods (declaration order):
- void ApplyPatches(HarmonyLib.Harmony harmony) (line 104)
  - note: Registers RuntimeHelpers.RunClassConstructor for ~60 IL2CPP types, then applies 18 Harmony postfix patches covering camp window open, item/skill/status/formation/skill/operation info presenters, quest/mission windows, item creation, and super specialty sub-screens. Safe to call multiple times.
- void Update() (line 465)
  - note: Polls camp window closure via IsOpened (with 1s grace period), then dispatches to ~20 UpdateXxx() sub-screen polling methods each frame.
- void UpdateRootMenu() (line 522)  [private]
  - note: Polls _menuSelector.currentIndex; announces focused UICampMenuItemData name and availability. Resets status screen state when root menu index changes.
- static void CampWindow_Open_Postfix(UICampWindow __instance) (line 611)
  - note: Postfix for UICampWindow.Open. Sets IsCampOpen, caches _campWindow and all sub-screen selectors from __instance. Detects field shortcut IC (OpenCampState == SelectSpecialSkill). Applies stale-seed logic for selectors that have activeInHierarchy=true permanently.
- static void StaleSuppressIfActive(UnityEngine.GameObject go, SubScreenState state, string logLabel) (line 994)  [private]
  - note: If go.activeInHierarchy, calls state.SuppressNextHeading() to prevent spurious announcement on first activation. Called in Open postfix for each sub-screen.
- static void StaleSeedPictureBook(Il2CppInterop...Il2CppObjectBase selector, SubScreenState state, ref UIListSelectorBase listBase, string logLabel) (line 1016)  [private]
  - note: Eagerly casts selector to UIListSelectorBase and calls state.SeedOnOpen(currentIndex). For picture-book selectors that are permanently active.
- void OnSceneChanged() (line 1050)
  - note: Resets IsCampOpen and _campWindow if camp was open during scene transition.
- static string StripTags(string text) (line 987)  [private]
  - note: Delegates to TextUtil.StripTags(text).
