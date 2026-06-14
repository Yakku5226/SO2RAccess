# LoadGameHandler.cs (178 lines)

Announces load game (and save game) menu navigation to the screen reader.
Patches applied:
  UISaveLoadSelector.Show                — announces "Save game." or "Load game." on screen open.
  UISaveLoadListItemPresenter.OnSelected — announces focused save slot details (label, hero,
                                           level, difficulty, location, playtime, position).
namespace: SO2RAccess (line 8)
usings (non-System / notable only): HarmonyLib, Il2CppGame, MelonLoader

## class LoadGameHandler (line 19)
Announces load/save game menu navigation; two Harmony postfix hooks.

fields/properties (declaration order):
- _patchesApplied : bool (line 23)

methods (declaration order):
- void ApplyPatches(HarmonyLib.Harmony harmony) (line 33)
  - note: Applies two postfix patches: UISaveLoadSelector.Show and UISaveLoadListItemPresenter.OnSelected. Safe to call multiple times.
- void SaveLoadSelector_Show_Postfix(UISaveLoadSelector __instance) (line 75)
  - note: Postfix for UISaveLoadSelector.Show(). Reads __instance.SaveLoadState to announce "save_game_screen" or "load_game_screen".
- void SaveLoadListItem_OnSelected_Postfix(UISaveLoadListItemPresenter __instance, ListItemDataBase itemData) (line 99)
  - note: Postfix for UISaveLoadListItemPresenter.OnSelected(ListItemDataBase). Casts itemData to UISaveLoadListItemData, reads position from parent UISaveLoadSelector.currentIndex, then calls AnnounceSlot.
- void AnnounceSlot(UISaveLoadListItemData data, int position, int total) (line 138)
  - note: Builds screen reader announcement. Auto-saves use "load_slot_label_auto"; normal slots use data.slotText or position. Empty slots → "load_slot_empty"; filled slots → "load_slot_with_data" with heroName, heroLevel, difficultyLevel, fieldName, playTimeValue.
