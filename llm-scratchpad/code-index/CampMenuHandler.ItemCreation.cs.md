# CampMenuHandler.ItemCreation.cs (1684 lines)

Partial class fragment of CampMenuHandler (see also CampMenuHandler.Items.cs).
File-level XML summary: Item Creation sub-screen accessibility (Camp → Item Creation).
Describes 4-screen flow: Skill Selection → Action List → Material Selection → Result.
All navigation is native-only (polling). Hooks capture info panel data.
namespace: SO2RAccess (line 7)
usings: Il2CppGame, MelonLoader

## partial class CampMenuHandler (line 33)
Item Creation sub-screen polling and Harmony hooks for Camp → Item Creation.

fields/properties (declaration order):
- _isFieldShortcutIC : bool (line 38)  — True when IC opened via field shortcut (D-pad Down), not camp root menu
- _icSkillSelector : UICampSelectSpecialSkillSelector (line 41)
- _icSkillState : SubScreenState (line 42)
- _icLastTab : int (line 43)
- _icAllSelectors : List<UICampSpecialSkillSelectorBase> (line 47)  — All special skill selectors from UICampWindow, for activeInHierarchy scanning
- _icActiveSelector : UICampSpecialSkillSelectorBase (line 48)
- _icActionListBase : UIListSelectorBase (line 49)
- _icActionState : SubScreenState (line 50)
- _icLastCharTab : int (line 51)
- _icActiveSkillCategory : string (line 54)  — Set by creation hook to gate Train/Scout dedicated polling
- _icTrainSwitchSelector : UICampSpecialSkillSwitchSelector (line 57)
- _icTrainSwitchLastIndex : int (line 58)
- _icScoutSelector : UICampSpecialSkillScoutSelector (line 61)
- _icScoutActionListBase : UIListSelectorBase (line 62)
- _icScoutLastIndex : int (line 63)
- _icActionSelectorBase : UICampSpecialSkillActionSelectorBase (line 66)
- _icActionPresenter : UICampSpecialSkillActionPresenter (line 67)
- _icLastCreateCount : int (line 68)
- _icResultSelector : UICampSpecialSkillResultSelector (line 71)
- _icResultState : SubScreenState (line 72)
- _icResultReadyTime : float (line 74)  — Time.time after which the result index reset takes effect (animation delay)
- _icPendingSkillName : string (line 77)
- _icPendingSkillDesc : string (line 78)
- _icPendingSkillLevel : int (line 79)
- _icPendingCreationData : UIItemCreationInformationData (line 80)
- _icCreationHookFired : bool (line 81)
- _icAddMaterialSelector : UICampSpecialSkillAddMaterialSelector (line 84)
- _icMaterialSetHookFired : bool (line 85)
- _icMaterialLastState : int (line 86)
- _icMaterialSelectState : SubScreenState (line 87)
- _icMaterialItemListState : SubScreenState (line 88)
- _icMaterialSelectListBase : UIListSelectorBase (line 89)
- _icMaterialItemListBase : UIListSelectorBase (line 90)
- _icDiagDone : bool (line 93)  — Diagnostics first-open flag
- _icActiveSetSig : string (line 99)  — Last logged active-selector signature for the action screen; change-gates per-frame diagnostic
- _icSelLastIndex : int[] (line 108)  — Per-selector last-seen action-list currentIndex, parallel to _icAllSelectors; -1 = not populated/unseen
- _icFocusedIdx : int (line 111)  — Index into _icAllSelectors of the focused skill's selector, or -1
- _icResultDiagSig : string (line 113)  — Last logged result-selector diagnostic signature (change-gated)
- _icResultSeenSig : string (line 122)  — Signature of result content last reacted to; detects freshly appeared result for appraisal

methods (declaration order):
- static void CacheItemCreationSelectors(UICampWindow) (line 132)
  - note: Called from CampWindow_Open_Postfix. Caches all IC selectors from camp window, seeds stale-open suppression, calls SeedActionFocusTracking() and CacheSuperSpecialtySelector().
- static void SeedActionFocusTracking() (line 309)
  - note: Allocates and seeds _icSelLastIndex from each selector's current action list state; clears _icFocusedIdx. Prevents stale open from being mistaken as fresh entry.
- static void TryAddSelector(Il2CppObjectBase) (line 331)
  - note: Casts selector to UICampSpecialSkillSelectorBase and appends to _icAllSelectors if non-null.
- static bool IsICActive() (line 346)
  - note: Returns true if last root menu item is "ItemCreation" OR field shortcut IC flag is set.
- void UpdateItemCreation() (line 353)
  - note: Top-level IC update; calls four sub-update methods. Called from Update() when IC is active.
- void UpdateICSkillSelection() (line 374)
  - note: Polls skill selector (Screen 1). Announces heading on entry, tracks tab changes, delegates to TryPollSuperSpecialtyTab() or TryPollSkillSelectionFallback().
- void TryPollSkillSelectionFallback() (line 460)
  - note: Fallback for Screen 1 when hook hasn't fired; reads currentIndex from UIListSelectorBase and announces position.
- void UpdateICActionList() (line 491)
  - note: Polls action list (Screen 2). Routes Train/Scout to dedicated pollers; uses ResolveFocusedActionSelector() to find the active skill's selector; tracks character tab, create mode, and calls PollActionListFallback().
- static int ResolveFocusedActionSelector() (line 562)
  - note: Finds which skill selector the user is navigating by detecting newly populated or cursor-moved action lists. Updates _icSelLastIndex as side effect. "Moved" wins over "entry" if both occur same frame.
- static void LogActiveActionSelectorsDiag(UICampSpecialSkillSelectorBase) (line 622)
  - note: Debug-only; logs active selectors with count/pause/input flags as a change-gated signature. Zero overhead when DebugMode is off.
- void TrackCharacterTab() (line 663)
  - note: Detects character tab (L/R) changes on active selector; resolves character name via executablePlayerIDList and ParameterManager.
- bool PollTrainSwitchSelector() (line 702)
  - note: Polls Train switch selector (ON/OFF per party member). Scans _icAllSelectors for UICampSpecialSkillTrainingSelector; announces character name + state or "All On"/"All Off". Returns true if Train active.
- bool PollScoutActionSelector() (line 813)
  - note: Polls Scout action list (Search/Escape/Do Nothing) via cached _icScoutSelector. Returns true if Scout active and handled.
- void PollCreateMode() (line 884)
  - note: Tracks currentCreateCount transitions on action presenter. Announces Create mode entry (with success rate), count changes, and exit. On exit schedules result announcement with 1.5s delay.
- static string ReadSuccessRate() (line 954)
  - note: Reads successRate text from _icActionPresenter, strips "%" suffix for clean TTS.
- static void ResetCreateModeState() (line 977)
- void PollActionListFallback() (line 984)
  - note: Fallback action list poll; reads UISpecialSkillConsumeListItemData.actionName and canDecision.
- void UpdateICResult() (line 1030)
  - note: Polls result selector (Screen 4). Calls LogResultDiag(), DetectNewResult(), handles delayed index reset, announces result item (name, success/failure status, result text).
- void DetectNewResult() (line 1115)
  - note: Detects new appraisal result by watching first item's content signature; schedules announcement via _icResultReadyTime. Known limitation: identical consecutive appraisal results won't re-announce.
- void LogResultDiag() (line 1153)
  - note: Debug-only; change-gated diagnostic logging result selector state (activeInHierarchy, counts, specialSkillID, first item). Zero overhead when DebugMode off.
- static void AddMaterialSelector_Set_IC_Postfix() (line 1197)
  - note: Postfix on UICampSpecialSkillAddMaterialSelector.Set (CallerCount 1). Signals entry into material selection flow by setting _icMaterialSetHookFired.
- static void SkillInfoPresenter_Set_IC_Postfix(string, string, int) (line 1216)
  - note: Postfix on UISpecialSkillInformationPresenter.Set. Announces skill name, level, description, and position. Must match nameof() in ApplyPatches.
- static void CreationInfoPresenter_Set_IC_Postfix(UIItemCreationInformationData) (line 1256)
  - note: Postfix on UIItemCreationInformationPresenter.Set. For skills with no creation items (Train/Scout), sets _icActiveSkillCategory and returns early. Otherwise announces category, level, creation result, rate, have-count, factor, effect, and position.
- static string SanitizeItemName(string) (line 1369)
  - note: Replaces all-"?" item names (undiscovered recipes) with Loc.Get("ic_unknown_item").
- void UpdateICMaterialSelection() (line 1397)
  - note: Polls material selector (Screen 3b). Gated on _icMaterialSetHookFired (hook-only entry; all sub-selectors have stale activeInHierarchy). Routes sub-states 0/1 to PollMaterialSelectCursor/PollMaterialItemListCursor.
- void PollMaterialSelectCursor() (line 1482)
  - note: Polls material slot list; resolves item names via ResolveItemName, announces empty slot or "Create" button at end of list.
- void PollMaterialItemListCursor() (line 1565)
  - note: Polls inventory item picker; reads UIItemListItemData for name and quantity.
- void AnnounceMaterialFactorRate() (line 1623)
  - note: Reads targetPercentage from factorInformationPresenter.factorRate and announces it.
- static string ResolveItemName(int) (line 1647)
  - note: Resolves item name from itemID via ParameterManager → TextManager (MessageType.Item). Falls back to parsing the raw "ITEM_XXX" key to title case.
