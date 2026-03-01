# Code Index: KeyboardMenuHandler.cs

## Top-Level Comments

File-level XML doc (lines 9-19) on the class describes three patches applied:
- `UIConfigKeyboardListItemPresenter.OnSelected` — fires on cursor navigation, reads actionName and pressKeyText from GameText fields (not the data label, which lacks the assigned key).
- `UIConfigKeyboardListSelector.OnDecision` — fires when user confirms an action, announces key-capture prompt.
- `UIConfigKeyboardListSelector.UpdateAfterKeyboard` — fires after key assignment is saved, re-announces the item with the new key.

---

## Class: KeyboardMenuHandler (line 20)

Namespace: `SO2RAccess`

### Fields

- `private bool _patchesApplied` (line 24)
- `private static UIConfigKeyboardListItemPresenter _selectedPresenter` (line 28)
  Note: Caches the currently focused presenter so `UpdateAfterKeyboard_Postfix` can re-read `pressKeyText` after the binding is saved without needing an instance reference.

### Methods

- `public void ApplyPatches(HarmonyLib.Harmony harmony)` (line 39)
  Note: Patches all three hooks described in the class XML doc. Guards with `_patchesApplied` so it is safe to call multiple times. Runs IL2CPP class constructors for `UIConfigKeyboardListItemData` and `UIConfigKeyboardListItemPresenter` before patching to avoid IL2CPP type-initialisation races.

- `private static void KeyboardListItem_OnSelected_Postfix(UIConfigKeyboardListItemPresenter __instance, ListItemDataBase itemData)` (line 92)
  Note: Harmony postfix for `UIConfigKeyboardListItemPresenter.OnSelected`. Caches `__instance` into `_selectedPresenter` then delegates to `AnnouncePresenter`. The `itemData` parameter is received from Harmony but not used directly — the live GameText fields on the presenter are read instead.

- `private static void KeyboardList_OnDecision_Postfix(UIConfigKeyboardListSelector __instance)` (line 113)
  Note: Harmony postfix for `UIConfigKeyboardListSelector.OnDecision`. Only speaks when `currentState` has transitioned to `KeyboardChanging`, meaning the game is now waiting for the user to press a key to bind.

- `private static void KeyboardList_UpdateAfterKeyboard_Postfix(UIConfigKeyboardListSelector __instance)` (line 132)
  Note: Harmony postfix for `UIConfigKeyboardListSelector.UpdateAfterKeyboard`. Called after the new binding is written and the list refreshed. Uses the cached `_selectedPresenter` (set by `OnSelected_Postfix`) to re-announce the item with its updated key name.

- `private static void AnnouncePresenter(UIConfigKeyboardListItemPresenter presenter, UIConfigKeyboardListSelector selector)` (line 156)
  Note: Core helper. Reads `actionName` from `presenter.actionName.text`. Finds the assigned key by scanning all child `GameText` components for one named "Icon" and stripping the `<sprite name=KEYNAME>` TMP sprite tag to get a plain string. Uses `selector.currentIndex` and `selector.currentDataList.Count` for position. Calls `Loc.Get("keyboard_item_no_key", ...)` if no key is assigned, or `Loc.Get("keyboard_item", ...)` if one is. Logs via `DebugLogger.LogGameValue`.
