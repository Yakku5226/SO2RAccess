# Code Index: LoadGameHandler.cs

## Top-level comments

- Namespace: `SO2RAccess`
- File-level XML doc on the class describes two Harmony patches:
  - `UISaveLoadSelector.Show` — announces "Load game." or "Save game." when the screen opens
  - `UISaveLoadListItemPresenter.OnSelected` — announces focused save slot details on navigation

---

## Class: LoadGameHandler (line 18)

### Fields

- `private bool _patchesApplied` (line 22)

### Methods

- `public void ApplyPatches(HarmonyLib.Harmony harmony)` (line 33)
  Note: Safe to call multiple times; guards with `_patchesApplied`. Registers two Harmony postfixes and force-initialises IL2CPP types via `RuntimeHelpers.RunClassConstructor` before patching.

- `private static void SaveLoadSelector_Show_Postfix(UISaveLoadSelector __instance)` (line 75)
  Note: Harmony postfix for `UISaveLoadSelector.Show`. Reads `SaveLoadState` to decide whether to announce "Save game." or "Load game." Falls back to "Load game." if the state read throws.

- `private static void SaveLoadListItem_OnSelected_Postfix(UISaveLoadListItemPresenter __instance, ListItemDataBase itemData)` (line 99)
  Note: Harmony postfix for `UISaveLoadListItemPresenter.OnSelected`. Casts `itemData` to `UISaveLoadListItemData`, resolves list position from the parent `UISaveLoadSelector`, then delegates to `AnnounceSlot`.

- `private static void AnnounceSlot(UISaveLoadListItemData data, int position, int total)` (line 138)
  Note: Builds and speaks the save-slot announcement. Auto-save slots use the "Auto save" locale key; normal slots use `data.slotText` (falling back to the numeric position). Empty slots announce "[label]. Empty."; filled slots include hero name, level, difficulty, location, and playtime via `Loc.Get("load_slot_with_data", ...)`.
