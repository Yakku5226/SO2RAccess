# EquipWizardHandler.cs (355 lines)

Announces equipment wizard navigation (auto-equip suggestion overlay that appears when
better gear is acquired). Hosted on UISystemWindow, not UICampWindow. Detection via
FindObjectOfType (lazy/throttled) + polling IsShowingEquipWizard. All navigation is
native C++; cursor polling is the only approach.

namespace: SO2RAccess (line 4)
usings (non-System / notable only): Il2CppGame, MelonLoader

## class EquipWizardHandler (line 24)
Announces equipment wizard navigation to the screen reader.

fields/properties (declaration order):
- _patchesApplied : bool (line 27)
- _window : static UISystemWindow (line 29)
- _findCooldown : static int (line 30)
- _isShowing : static bool (line 32)
- _selector : static UIEquipWizardSelector (line 33)
- _selectorBase : static UIListSelectorBase (line 34)
- _lastMenuIndex : static int (line 35)
- _lastDataIndex : static int (line 36)
- _menuNames : static string[] (line 39)  — lazy-initialized from Loc; matches UIEquipWizardSelector.Menu enum order (Yes/No/Reject All)

methods (declaration order):
- void ApplyPatches(HarmonyLib.Harmony harmony) (line 49)
  - note: Registers RuntimeHelpers.RunClassConstructor for UISystemWindow, UIEquipWizardSelector, UIEquipWizardPresenter, EquipWizardData, UIEquipWithFactorListItemData, UIEquipListItemData. No Harmony hooks — all announcements are polling-based.
- void OnSceneChanged() (line 73)
  - note: Resets _window, _findCooldown, _isShowing, _selector, _selectorBase, and index tracking to -1.
- void Update() (line 93)
  - note: Calls DetectWindow(), then UpdateMenu() if _isShowing.
- void DetectWindow() (line 105)  [private]
  - note: Lazily finds UISystemWindow (60-frame cooldown). Once found, polls IsShowingEquipWizard to detect open (calls AnnounceWizardEntry, caches selector) and close (clears references) transitions.
- void UpdateMenu() (line 164)  [private]
  - note: Polls _selectorBase.currentIndex for Yes/No/Reject All cursor changes; also detects character advance via equipWizardDataIndex change (resets menu index and calls AnnounceWizardEntry).
- void AnnounceWizardEntry() (line 221)  [private]
  - note: Announces heading, presenter description text, equipment comparison (via AnnounceEquipmentChanges), and current menu option. Called on open and on character advance.
- void AnnounceEquipmentChanges(StringBuilder sb) (line 284)  [private]
  - note: Reads pre/post equip data lists via _selector.GetPreEquipDataList / GetPostEquipDataList, then appends changed slots as "[slot]: [old] to [new]" for each slot where postItem.isChanged is true.
