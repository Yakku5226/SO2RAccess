# Code Index: HeroSelectHandler.cs

## Top-Level Comments

Class-level XML doc (lines 8-17): Announces protagonist selection screen navigation to the
screen reader. This is the screen after choosing "New Game" from the title menu, where the
player chooses between Claude and Rena. Documents two Harmony patches applied:
- UITitleSelectHeroSelector.Show — announces the screen heading on open
- UITitleSelectHeroSelector.OnSelected — announces the focused protagonist and their
  description on open and navigation

---

## Class: HeroSelectHandler (line 18)

Namespace: SO2RAccess

### Fields

- `private bool _patchesApplied` (line 22)
- `private static HeroSelectHandler _instance` (line 25)
  Note: Static back-reference so Harmony static patch methods can call instance logic.
- `private PlayerID _lastAnnouncedHero` (line 29)
  Note: Initialized to `PlayerID.INVALID`. Prevents re-announcing if OnSelected fires
  multiple times for the same hero without a real selection change.

### Methods

- `public HeroSelectHandler()` (line 39)
  Note: Constructor only. Sets `_instance = this`. Does not apply patches.

- `public void ApplyPatches(HarmonyLib.Harmony harmony)` (line 53)
  Note: Patches UITitleSelectHeroSelector.Show and UITitleSelectHeroSelector.OnSelected
  with postfix hooks. Guards against double application via `_patchesApplied`. Errors
  are caught and logged via MelonLogger.

- `private static void HeroSelector_Show_Postfix()` (line 84)
  Note: Harmony postfix for UITitleSelectHeroSelector.Show(). Resets `_lastAnnouncedHero`
  to INVALID so the first OnSelected after Show always announces, then speaks the screen
  heading via ScreenReader.

- `private static void HeroSelector_OnSelected_Postfix(UITitleSelectHeroSelector __instance, PlayerID playerID)` (line 97)
  Note: Harmony postfix for UITitleSelectHeroSelector.OnSelected(). Thin relay — delegates
  immediately to `_instance?.OnHeroFocused(...)`.

- `private void OnHeroFocused(UITitleSelectHeroSelector selector, PlayerID playerID)` (line 106)
  Note: Core announcement logic. Guards on null selector and duplicate playerID. Reads
  `selector.heroDescription?.text`, collapses newlines to spaces, then speaks either the
  name alone or a combined name+description string via Loc.Get(). Handles both Claude and
  Rena via PlayerID.CLAUDE branch check.
