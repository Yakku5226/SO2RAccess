# Code Index: GamepadMenuHandler.cs

## Top-Level Comments

The class XML doc (lines 9-19) describes the three Harmony patches applied:
- `UIKeyConfigSelectItemPresenter.OnSelected` — announces focused item on navigation; strips
  sprite tags and controller-type prefixes from button names so the user hears "Cross" not
  "PS4_Cross".
- `UIKeyConfigSelector.OnDecision` — announces the button-capture prompt when the user
  confirms an action.
- `UIKeyConfigSelector.UpdateAliasAction` — re-announces the item after a new button is
  assigned so the user hears the updated binding.

---

## Class: GamepadMenuHandler (line 20)

Namespace: `SO2RAccess`

### Fields

- `private bool _patchesApplied` (line 24)
  Guard flag; prevents patches from being registered more than once.

- `private static UIKeyConfigSelectItemPresenter _selectedPresenter` (line 28)
  Caches the most recently focused item presenter so `UpdateAliasAction` can re-read it
  after a binding is saved without needing the presenter passed in directly.

- `private static readonly string[] _spritePrefixes` (lines 32-35)
  Array of controller-type prefixes (`"PS5_"`, `"PS4_"`, `"Xbox_"`, `"Switch_"`, `"PC_"`,
  `"Gamepad_"`) that are stripped from sprite names before announcing to the user.

### Methods

- `public void ApplyPatches(HarmonyLib.Harmony harmony)` (line 46)
  Registers all three Harmony postfixes. Runs class constructors first via
  `RuntimeHelpers.RunClassConstructor` to ensure IL2CPP type metadata is initialised before
  patching. Safe to call multiple times — bails early if `_patchesApplied` is already true.

- `private static void GamepadItem_OnSelected_Postfix(UIKeyConfigSelectItemPresenter __instance, SelectItemDataBase itemData)` (line 98)
  Harmony postfix for `UIKeyConfigSelectItemPresenter.OnSelected`. Caches `__instance` into
  `_selectedPresenter` and calls `AnnouncePresenter`. The `itemData` parameter is received
  from Harmony but not used directly; data is read from the presenter's own fields instead.

- `private static void GamepadSelector_OnDecision_Postfix(UIKeyConfigSelector __instance)` (line 118)
  Harmony postfix for `UIKeyConfigSelector.OnDecision`. Announces a "press a button" prompt
  only when the selector's `currentState` is `CommonChanging` or `BattleChanging`, meaning
  the game is actively waiting for the user to press a physical button to assign.

- `private static void GamepadSelector_UpdateAliasAction_Postfix(UIKeyConfigSelector __instance, GameInputManager.InputAction action)` (line 139)
  Harmony postfix for `UIKeyConfigSelector.UpdateAliasAction`. The `action` parameter is
  received from Harmony but not used; the method re-reads `_selectedPresenter` (which now
  reflects the newly saved binding) and calls `AnnouncePresenter` to announce the result.

- `private static void AnnouncePresenter(UIKeyConfigSelectItemPresenter presenter, UIKeyConfigSelector selector)` (line 163)
  Reads `presenter.label.text` (action name) and `presenter.icon.text` (assigned button
  sprite tag) and formats a screen-reader announcement. Falls back to `presenter.pressKeyText`
  if `icon` is empty and the press-text looks like a sprite tag. Also reads position info
  (`currentIndex`, `presenterList.Count`) from the selector for "X of Y" context.
  Note: Despite the name, this method also handles the case where no button is assigned,
  using a different localization key (`"gamepad_item_no_button"`).

- `private static string ExtractButtonName(string raw)` (line 206)
  Parses a raw GameText string. If it is a sprite tag of the form `<sprite name=X>`, extracts
  the name and passes it to `StripControllerPrefix`. Otherwise returns the string trimmed.
  Returns an empty string for null/whitespace input.

- `private static string StripControllerPrefix(string spriteName)` (line 223)
  Iterates `_spritePrefixes` and removes the first matching prefix from the start of the
  sprite name (e.g. `"PS4_Cross"` → `"Cross"`, `"Xbox_A"` → `"A"`). Returns the original
  string unchanged if no prefix matches.
