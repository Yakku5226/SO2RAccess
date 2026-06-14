# ConfigMenuHandler.cs (335 lines)

Announces config menu navigation to the screen reader.
Patches: UIConfigMenuSelector.Show (first item on open), UIConfigMenuSelector.OnMoveCursor (category nav),
UIConfigGroupSelectorBase.MoveCursor (setting nav in all 9 submenus), UIConfigGroupSelectItemSelector.SetLabel
(caches voice-config row labels), UIConfigGroupSelectItemSelector.OnMoveCursor (value change left/right).
namespace: SO2RAccess (line 8)
usings (non-System / notable only): HarmonyLib, Il2CppGame, MelonLoader

## class ConfigMenuHandler (line 20)
Announces config menu category and settings navigation via Harmony patches.

fields/properties (declaration order):
- _patchesApplied : bool = false (line 24)
- _labelCache : static readonly Dictionary<IntPtr, string> (line 29)  — maps selector Pointer → cached label string for voice-config rows

methods (declaration order):
- void ApplyPatches(HarmonyLib.Harmony harmony) (line 41)
  - note: Safe to call multiple times. Patches Show, OnMoveCursor on UIConfigMenuSelector; MoveCursor on UIConfigGroupSelectorBase (covers all 9 submenus); SetLabel and OnMoveCursor on UIConfigGroupSelectItemSelector. Forces RuntimeHelpers.RunClassConstructor for 3 types.
- static void ConfigMenu_Show_Postfix(UIConfigMenuSelector __instance) (line 106)
  - note: Postfix for UIConfigMenuSelector.Show(). Delegates to AnnounceConfigMenuItem.
- static void ConfigMenu_OnMoveCursor_Postfix(UIConfigMenuSelector __instance) (line 113)
  - note: Postfix for UIConfigMenuSelector.OnMoveCursor(). Delegates to AnnounceConfigMenuItem.
- static void AnnounceConfigMenuItem(UIConfigMenuSelector selector) (line 121)
  - note: Reads currentDataList[CurrentIndex] as UICommonListItemData, announces item.text with position. Items stored as UICommonListItemData, not UIConfigMenuItemData.
- static void ConfigGroup_MoveCursor_Postfix(UIConfigGroupSelectorBase __instance) (line 148)
  - note: Postfix for UIConfigGroupSelectorBase.MoveCursor(int). Delegates to AnnounceGroupItem.
- static void AnnounceGroupItem(UIConfigGroupSelectorBase selector) (line 154)
  - note: Reads groupSelectorList[currentIndex], calls GetItemLabel + GetItemValue, announces with position. Uses config_setting_no_value key when value is empty.
- static void ConfigGroupItem_SetLabel_Postfix(UIConfigGroupSelectItemSelector __instance, string label) (line 185)
  - note: Postfix for UIConfigGroupSelectItemSelector.SetLabel(string). Caches label by Pointer for voice-config rows only.
- static void ConfigItem_OnMoveCursor_Postfix(UIConfigGroupSelectItemSelector __instance) (line 197)
  - note: Postfix for UIConfigGroupSelectItemSelector.OnMoveCursor(). Reads option-list value via GetCurrentData().text; falls back to gauge.currentIndex.ToString() for slider settings.
- static string GetItemLabel(UIConfigGroupSelectItemSelector itemSelector) (line 244)
  - note: Three strategies in order: (1) _labelCache by Pointer for voice rows; (2) any non-gauge-value GameText in hierarchy; (3) walk backward through sibling transforms for nearest GameText (known to return section headers as fallback).
- static string GetItemValue(UIConfigGroupSelectItemSelector itemSelector) (line 308)
  - note: Option-list: GetCurrentData().text; gauge/slider: TryCast<UIConfigGroupGaugeSelectItemSelector>().currentIndex.ToString().
