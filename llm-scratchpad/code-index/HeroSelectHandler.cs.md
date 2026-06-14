# HeroSelectHandler.cs (131 lines)

Announces protagonist selection screen navigation (Claude vs Rena) to the screen reader. Shown after "New Game" from the title menu.

namespace: SO2RAccess (line 7)
usings (non-System / notable only): HarmonyLib, Il2CppGame, MelonLoader

## class HeroSelectHandler (line 19)
Announces protagonist selection screen navigation to the screen reader.

Patches applied:
- UITitleSelectHeroSelector.Show — postfix — announces screen heading on open
- UITitleSelectHeroSelector.OnSelected — postfix — announces focused protagonist and description

fields/properties (declaration order):
- _patchesApplied : bool (line 22)
- _instance : static HeroSelectHandler (line 25)  — back-reference for Harmony static patch methods
- _lastAnnouncedHero : PlayerID (line 29)  — prevents re-announcing same hero if OnSelected fires multiple times; initialized to PlayerID.INVALID

methods (declaration order):
- HeroSelectHandler() (line 39)
  - note: Sets _instance = this. Does not apply patches.
- void ApplyPatches(HarmonyLib.Harmony harmony) (line 53)
  - note: Patches UITitleSelectHeroSelector.Show (postfix) and UITitleSelectHeroSelector.OnSelected (postfix). Idempotent.
- static void HeroSelector_Show_Postfix() (line 84)
  - note: Postfix for UITitleSelectHeroSelector.Show. Resets _lastAnnouncedHero to INVALID so first OnSelected always announces. Speaks "hero_select_screen" loc string.
- static void HeroSelector_OnSelected_Postfix(UITitleSelectHeroSelector __instance, PlayerID playerID) (line 97)
  - note: Postfix for UITitleSelectHeroSelector.OnSelected. Delegates to _instance.OnHeroFocused.
- void OnHeroFocused(UITitleSelectHeroSelector selector, PlayerID playerID) (line 106)
  - note: Skips if same hero as last announced. Reads heroDescription.text, collapses newlines to spaces, speaks name alone if no description, else "hero_select_item" loc string with name and description.
