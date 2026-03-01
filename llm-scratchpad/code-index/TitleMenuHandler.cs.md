# Code Index: TitleMenuHandler.cs

## Top-Level Comments

- File-level XML doc on the class (lines 8–16) describes the handler's purpose and lists
  the three Harmony patches it applies:
  - `UITitlePressAnyButtonSelector.Show` — press-any-button screen
  - `UITitleMenuSelector.Show` — menu open
  - `UITitleMenuSelector.OnInput` — focused item changes

## Namespace: SO2RAccess

---

## Class: TitleMenuHandler (line 17)

### Fields

- `private int _lastAnnouncedIndex` (line 21) — tracks the last announced menu index to
  avoid re-announcing the same item
- `private bool _patchesApplied` (line 22) — guard flag so patches are only registered once
- `private static TitleMenuHandler _instance` (line 25) — static back-reference that lets
  static Harmony patch methods call instance logic

### Constructor

- `public TitleMenuHandler()` (line 35)
  Note: Only assigns `_instance = this`. Patches are NOT applied here; caller must call
  `ApplyPatches()` separately.

### Methods

- `public void ApplyPatches(HarmonyLib.Harmony harmony)` (line 49)
  Note: Registers all three postfix patches. Guarded by `_patchesApplied` so it is safe to
  call multiple times. Wraps registration in a try-catch and logs errors via MelonLogger.

- `private static void PressAnyButton_Show_Postfix()` (line 84)
  Note: Harmony postfix for `UITitlePressAnyButtonSelector.Show`. Announces the
  press-any-button screen via `ScreenReader.Say` using the `"title_press_any_button"` locale
  key.

- `private static void TitleMenu_Show_Postfix(UITitleMenuSelector __instance)` (line 91)
  Note: Harmony postfix for `UITitleMenuSelector.Show`. Resets `_lastAnnouncedIndex` to -1
  so the first item is always announced fresh, then calls `AnnounceCurrentItem`.

- `private static void TitleMenu_OnInput_Postfix(UITitleMenuSelector __instance)` (line 101)
  Note: Harmony postfix for `UITitleMenuSelector.OnInput`. Delegates to instance method
  `OnInputProcessed`. Null-safe via `?.` operator.

- `private void OnInputProcessed(UITitleMenuSelector selector)` (line 110)
  Note: Reads `selector.CurrentIndex`, compares to `_lastAnnouncedIndex`, and calls
  `AnnounceCurrentItem` only when the index has actually changed. Acts as a change-detection
  filter between the raw Harmony postfix and the announcement logic.

- `private void AnnounceCurrentItem(UITitleMenuSelector selector)` (line 121)
  Note: Reads the current item from `selector.menuItemList` by index, checks
  `item.canDecision` for availability, and announces via `ScreenReader.Say` using locale keys
  `"title_menu_item"` (available) or `"title_menu_item_unavailable"` (grayed out). Includes
  position (1-based) and total count in the announcement.
