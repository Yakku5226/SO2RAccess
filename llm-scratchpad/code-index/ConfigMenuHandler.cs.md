# Code Index: ConfigMenuHandler.cs

## Top-level comments

- Namespace: `SO2RAccess`
- File-level XML doc (lines 10-19) lists all five Harmony patches applied by this class:
  - `UIConfigMenuSelector.Show` — announces first item when config menu opens
  - `UIConfigMenuSelector.OnMoveCursor` — announces focused category on navigation
  - `UIConfigGroupSelectorBase.MoveCursor` — announces focused setting on navigation
  - `UIConfigGroupSelectItemSelector.SetLabel` — caches voice-config row labels
  - `UIConfigGroupSelectItemSelector.OnMoveCursor` — announces new value on left/right

---

## Class: ConfigMenuHandler (line 20)

### Fields

- `private bool _patchesApplied` (line 24)
- `private static readonly Dictionary<IntPtr, string> _labelCache` (line 29)
  Note: Keyed by IL2CPP object pointer. Populated only by the `SetLabel` hook, which fires
  exclusively for voice-config rows. Display/audio rows are NOT cached here; their labels
  are read from the Unity hierarchy at runtime instead.

### Methods

#### Patch Application

- `public void ApplyPatches(HarmonyLib.Harmony harmony)` (line 41)
  Note: Guards against double-application via `_patchesApplied`. Also force-initializes
  IL2CppInterop class lookup tables for three types before any postfix runs, to prevent
  silent null returns from `TryCast<T>()`.

#### Harmony Patch Methods — Config Category Menu

- `private static void ConfigMenu_Show_Postfix(UIConfigMenuSelector __instance)` (line 106)
  Note: Postfix for `UIConfigMenuSelector.Show`. Delegates to `AnnounceConfigMenuItem`.

- `private static void ConfigMenu_OnMoveCursor_Postfix(UIConfigMenuSelector __instance)` (line 113)
  Note: Postfix for `UIConfigMenuSelector.OnMoveCursor`. Delegates to `AnnounceConfigMenuItem`.

- `private static void AnnounceConfigMenuItem(UIConfigMenuSelector selector)` (line 121)
  Note: Shared helper called by both Show and OnMoveCursor postfixes. Reads the current item
  from `currentDataList`, casts to `UICommonListItemData` (not `UIConfigMenuItemData` — the
  stored type differs from what the name suggests), and announces label + position via
  `ScreenReader.Say`.

#### Harmony Patch Methods — Settings Submenus

- `private static void ConfigGroup_MoveCursor_Postfix(UIConfigGroupSelectorBase __instance)` (line 148)
  Note: Postfix for `UIConfigGroupSelectorBase.MoveCursor(int)`. One patch covers all nine
  config submenus because `MoveCursor` is defined only on the base class and is never
  overridden. Emits a diagnostic log line then delegates to `AnnounceGroupItem`.

- `private static void AnnounceGroupItem(UIConfigGroupSelectorBase selector)` (line 155)
  Note: Reads `groupSelectorList[currentIndex]` to get the focused `UIConfigGroupSelectItemSelector`,
  then calls `GetItemLabel` and `GetItemValue` to build the announcement string. Announces
  label-only if value is empty, otherwise announces label + value + position.

- `private static void ConfigGroupItem_SetLabel_Postfix(UIConfigGroupSelectItemSelector __instance, string label)` (line 187)
  Note: Postfix for `UIConfigGroupSelectItemSelector.SetLabel(string)`. Stores the label
  string into `_labelCache` keyed by the object's IL2CPP pointer. Only fires for voice-config
  rows; all other row types set labels through prefab/localization paths that do not call
  `SetLabel`.

#### Harmony Patch Methods — Value Adjustment

- `private static void ConfigItem_OnMoveCursor_Postfix(UIConfigGroupSelectItemSelector __instance)` (line 199)
  Note: Postfix for `UIConfigGroupSelectItemSelector.OnMoveCursor`. Fires when the user
  presses left/right to change a setting value. Tries option-list path first
  (`GetCurrentData().text`); if that yields nothing, falls back to gauge/slider path
  (`TryCast<UIConfigGroupGaugeSelectItemSelector>().value.text`).

#### Helpers

- `private static string GetItemLabel(UIConfigGroupSelectItemSelector itemSelector)` (line 246)
  Note: Three-strategy label resolution. Strategy 1: pointer lookup in `_labelCache` (voice
  rows only). Strategy 2: scan `GetComponentsInChildren<GameText>` inside the selector's own
  hierarchy, skipping the gauge-value `GameText` if present. Strategy 3 (fallback): walk
  backward through sibling transforms searching for any non-empty `GameText`. Strategy 2 and 3
  both emit diagnostic `MelonLogger.Msg` lines — this method contains active diagnostics left
  in for investigation.

- `private static string GetItemValue(UIConfigGroupSelectItemSelector itemSelector)` (line 332)
  Note: Reads the current value for a setting row. Option-list path: `GetCurrentData().text`.
  Gauge/slider path: `TryCast<UIConfigGroupGaugeSelectItemSelector>().value.text`. Returns
  empty string on failure (exception silently swallowed via bare `catch {}`).
