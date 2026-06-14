# KeyboardMenuHandler.cs (202 lines)

Announces keyboard binding menu navigation to the screen reader.
Patches applied:
  UIConfigKeyboardListItemPresenter.OnSelected    — announces focused item (action: key, N of total).
  UIConfigKeyboardListSelector.OnDecision         — announces key-capture prompt when entering binding mode.
  UIConfigKeyboardListSelector.UpdateAfterKeyboard — re-announces item with new key after assignment.
Reads actionName and pressKeyText from presenter GameText fields (not data.label, which
lacks the assigned key). Key name is stored as a sprite tag "<sprite name=KEYNAME>" in
the "Icon" child GameText; tag is stripped to get the plain key name.
namespace: SO2RAccess (line 8)
usings (non-System / notable only): HarmonyLib, Il2CppGame, MelonLoader

## class KeyboardMenuHandler (line 21)
Harmony-patched keyboard binding menu handler; three postfix patches.

fields/properties (declaration order):
- _patchesApplied : bool (line 24)
- _selectedPresenter : static UIConfigKeyboardListItemPresenter (line 28)  — cached from OnSelected postfix; used by UpdateAfterKeyboard to re-read pressKeyText after binding change

methods (declaration order):
- void ApplyPatches(HarmonyLib.Harmony harmony) (line 38)
  - note: Applies three postfix patches: OnSelected, OnDecision, UpdateAfterKeyboard. Safe to call multiple times.
- void KeyboardListItem_OnSelected_Postfix(UIConfigKeyboardListItemPresenter __instance, ListItemDataBase itemData) (line 92)
  - note: Postfix for UIConfigKeyboardListItemPresenter.OnSelected. Caches __instance as _selectedPresenter, resolves parent UIConfigKeyboardListSelector, calls AnnouncePresenter.
- void KeyboardList_OnDecision_Postfix(UIConfigKeyboardListSelector __instance) (line 113)
  - note: Postfix for UIConfigKeyboardListSelector.OnDecision. Announces Loc.Get("keyboard_press_key") only when currentState == State.KeyboardChanging.
- void KeyboardList_UpdateAfterKeyboard_Postfix(UIConfigKeyboardListSelector __instance) (line 132)
  - note: Postfix for UIConfigKeyboardListSelector.UpdateAfterKeyboard. Re-calls AnnouncePresenter with cached _selectedPresenter and the updated selector so the new key binding is announced.
- void AnnouncePresenter(UIConfigKeyboardListItemPresenter presenter, UIConfigKeyboardListSelector selector) (line 157)
  - note: Reads presenter.actionName.text; iterates GetComponentsInChildren<GameText>(true) for the "Icon" child and strips "<sprite name=KEYNAME>" to get plain key name. Announces "keyboard_item" (with key) or "keyboard_item_no_key" (without). Uses selector.currentIndex and currentDataList.Count for position.
