# Code Index: NewGameSettingsHandler.cs

## Top-Level Comments

Lines 9-21: XML `<summary>` block on the class describing its purpose:
- Announces new game settings screen navigation to the screen reader.
- The screen follows protagonist selection and contains: Name, Voice Language, Voice Type, Difficulty, BGM Version, Display Event Art, Return to defaults, and Confirm.
- Lists all five Harmony patches applied and which method each targets.

---

## Class: NewGameSettingsHandler (line 22)

Namespace: `SO2RAccess`

### Fields

- `private bool _patchesApplied` (line 26)
  Guards `ApplyPatches` so patches are only registered once, even if called multiple times.
- `private static NewGameSettingsHandler _instance` (line 29)
  Singleton back-reference. Required because Harmony postfix methods must be static, so they delegate to this instance for state access.
- `private int _lastMenuIndex` (line 31)
  Tracks the previously announced menu row index to suppress repeat announcements when navigation does not actually change the focused item.
- `private bool _showComplete` (line 35)
  Set to `false` at screen open, then `true` after `Show()` finishes its own initialization calls to `UpdateCurrentPresenter`. Prevents the flood of initialization calls from being announced as user-driven value changes.

### Constructor

- `public NewGameSettingsHandler()` (line 44)
  Assigns `this` to the static `_instance` field so Harmony static patch methods can reach instance state.

### Methods

#### Patch Application

- `public void ApplyPatches(HarmonyLib.Harmony harmony)` (line 58)
  Registers all five Harmony postfix patches for `UITitleSelectVoiceSelector`. Pre-initializes IL2CPP type tables for `UITitleSelectVoiceMenuSelectItemPresenter` and `UICommonSelectTextPresenter` via `RuntimeHelpers.RunClassConstructor` so that `TryCast` works correctly inside the postfixes. Safe to call multiple times — skips if already applied.

#### Harmony Patch Methods (all private static)

- `private static void VoiceSelector_Show_Postfix(UITitleSelectVoiceSelector __instance)` (line 107)
  Postfix for `UITitleSelectVoiceSelector.Show`. Resets `_showComplete` and `_lastMenuIndex`, announces the screen heading via `ScreenReader.Say`, announces the currently focused item, then sets `_showComplete = true` to allow subsequent `UpdateCurrentPresenter` calls to be announced.

- `private static void VoiceSelector_OnUp_Postfix(UITitleSelectVoiceSelector __instance)` (line 122)
  Postfix for `UITitleSelectVoiceSelector.OnUp`. Delegates to `OnNavigated` to detect and announce a row change.

- `private static void VoiceSelector_OnDown_Postfix(UITitleSelectVoiceSelector __instance)` (line 128)
  Postfix for `UITitleSelectVoiceSelector.OnDown`. Delegates to `OnNavigated` to detect and announce a row change.

- `private static void VoiceSelector_UpdateCurrentPresenter_Postfix(UITitleSelectVoiceSelector __instance, UITitleSelectVoiceSelector.Menu menu)` (line 135)
  Postfix for `UITitleSelectVoiceSelector.UpdateCurrentPresenter`. Fires when the player presses left/right to change a setting value. Silenced during screen initialization via `_showComplete`. Reads the incoming (new) value via `GetNewValueText` and announces it.
  Note: The method is named "UpdateCurrentPresenter" in the game but it signals a value change, not a navigation change.

- `private static void VoiceSelector_OnDecision_Postfix(UITitleSelectVoiceSelector __instance)` (line 159)
  Postfix for `UITitleSelectVoiceSelector.OnDecision`. Announces "editing name" only when `__instance.isEditName` is true, meaning the player confirmed on the Name row specifically.

#### Internal Logic (all private)

- `private void OnNavigated(UITitleSelectVoiceSelector selector)` (line 174)
  Called by the OnUp and OnDown postfixes. Reads `selector.menuIndex`, compares to `_lastMenuIndex`, and calls `AnnounceCurrentItem` only when the index has changed.

- `private void AnnounceCurrentItem(UITitleSelectVoiceSelector selector)` (line 185)
  Reads the label and current value for the focused menu row and announces them via `ScreenReader.Say`. Uses `GetPresenter`, `GetFallbackLabel`, and `GetValueText` internally. Announces label + value together for rows that have a value, or label alone for button-only rows.

- `private static UITitleSelectVoiceMenuSelectItemPresenter GetPresenter(UITitleSelectVoiceSelector selector, UITitleSelectVoiceSelector.Menu menu)` (line 217)
  Looks up the presenter object for a given `Menu` enum value from `selector.menuPresneterList` by casting the enum to an integer index. Returns `null` if the list is null or the index is out of range.
  Note: `menuPresneterList` is a typo in the game's own field name (missing second 'e').

- `private static string GetValueText(UITitleSelectVoiceSelector selector, UITitleSelectVoiceSelector.Menu menu, UITitleSelectVoiceMenuSelectItemPresenter presenter)` (line 231)
  Returns the currently displayed value text for a menu row. Returns the full name string for the `EditName` row (from `fullNamePresenter`), empty string for button-only rows (`Initialize`, `Decision`), and `textPresenter.currentText.text` for all other rows.

- `private static string GetNewValueText(UITitleSelectVoiceSelector selector, UITitleSelectVoiceSelector.Menu menu, UITitleSelectVoiceMenuSelectItemPresenter presenter)` (line 259)
  Returns the value that is animating in after a left/right change. Reads `textPresenter.nextText.text` (the incoming value) and falls back to `textPresenter.currentText.text` if `nextText` is empty. Used by the `UpdateCurrentPresenter` postfix so the announced value is the new one, not the old one still displayed.
  Note: During the slide animation `currentText` holds the old value fading out and `nextText` holds the new value animating in.

- `private static string GetFallbackLabel(UITitleSelectVoiceSelector.Menu menu)` (line 286)
  Returns a localized fallback label string (via `Loc.Get`) for each `Menu` enum value, used when the presenter's own label `GameText` component is empty. Covers all eight menu rows: EditName, VoiceLanguage, VoiceVersion, Difficulty, BGMVersion, EnableEventStandingPicture, Initialize, Decision.
