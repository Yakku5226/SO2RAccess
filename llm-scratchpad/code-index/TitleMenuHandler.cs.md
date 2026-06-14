# TitleMenuHandler.cs (147 lines)

Announces title menu navigation to the screen reader. Covers the "press any button" screen and the main title menu.

namespace: SO2RAccess (line 7)
usings (non-System / notable only): HarmonyLib, Il2CppGame, MelonLoader

## class TitleMenuHandler (line 17)
Announces title menu navigation to the screen reader.

Patches applied:
- UITitlePressAnyButtonSelector.Show — postfix — announces press-any-button screen
- UITitleMenuSelector.Show — postfix — announces menu on open and reads first item
- UITitleMenuSelector.OnInput — postfix — announces focused item when index changes

fields/properties (declaration order):
- _lastAnnouncedIndex : int (line 21)  — initialized to -1; prevents re-announcing same item
- _patchesApplied : bool (line 22)
- _instance : static TitleMenuHandler (line 25)  — back-reference for Harmony static patch methods

methods (declaration order):
- TitleMenuHandler() (line 34)
  - note: Sets _instance = this. Does not apply patches.
- void ApplyPatches(HarmonyLib.Harmony harmony) (line 48)
  - note: Patches UITitlePressAnyButtonSelector.Show (postfix), UITitleMenuSelector.Show (postfix), and UITitleMenuSelector.OnInput (postfix). Idempotent.
- static void PressAnyButton_Show_Postfix() (line 84)
  - note: Postfix for UITitlePressAnyButtonSelector.Show. Speaks "title_press_any_button" loc string.
- static void TitleMenu_Show_Postfix(UITitleMenuSelector __instance) (line 91)
  - note: Postfix for UITitleMenuSelector.Show. Resets _lastAnnouncedIndex to -1 then calls AnnounceCurrentItem so first item is always announced on open.
- static void TitleMenu_OnInput_Postfix(UITitleMenuSelector __instance) (line 101)
  - note: Postfix for UITitleMenuSelector.OnInput. Delegates to _instance.OnInputProcessed.
- void OnInputProcessed(UITitleMenuSelector selector) (line 110)
  - note: Reads selector.CurrentIndex; returns early if unchanged; updates _lastAnnouncedIndex and calls AnnounceCurrentItem.
- void AnnounceCurrentItem(UITitleMenuSelector selector) (line 121)
  - note: Reads menuItemList[CurrentIndex]; speaks "title_menu_item" with name, 1-based index, count if canDecision=true; else "title_menu_item_unavailable". Guards against null list and out-of-range index.
