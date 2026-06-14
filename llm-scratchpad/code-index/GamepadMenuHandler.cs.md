# GamepadMenuHandler.cs (235 lines)

Announces gamepad (controller) binding menu navigation to the screen reader.
Patches: UIKeyConfigSelectItemPresenter.OnSelected (postfix, cursor navigation),
         UIKeyConfigSelector.OnDecision (postfix, button-capture prompt),
         UIKeyConfigSelector.UpdateAliasAction (postfix, re-announce after assignment).
Button sprite tags (e.g. "<sprite name=PS4_Cross>") are stripped of controller-type prefix so user hears "Cross".
namespace: SO2RAccess (line 7)
usings: HarmonyLib, Il2CppGame, MelonLoader, System.Runtime.CompilerServices

## class GamepadMenuHandler (line 21)

fields/properties (declaration order):
- _patchesApplied : bool (line 24)
- _selectedPresenter : UIKeyConfigSelectItemPresenter (line 28)  [— static; cached reference to currently focused item presenter; used by UpdateAliasAction to re-read after binding]
- _spritePrefixes : string[] (line 32)  [— static readonly; controller-type prefixes to strip: PS5_, PS4_, Xbox_, Switch_, PC_, Gamepad_]

methods (declaration order):
- void ApplyPatches(HarmonyLib.Harmony harmony) (line 46)
  - note: Applies three Harmony patches (OnSelected postfix, OnDecision postfix, UpdateAliasAction postfix). Calls RuntimeHelpers.RunClassConstructor to ensure IL2CPP types are initialised before patching. Safe to call multiple times.
- static void GamepadItem_OnSelected_Postfix(UIKeyConfigSelectItemPresenter __instance, SelectItemDataBase itemData) (line 98)
  - note: Harmony postfix on UIKeyConfigSelectItemPresenter.OnSelected. Caches _selectedPresenter and calls AnnouncePresenter.
- static void GamepadSelector_OnDecision_Postfix(UIKeyConfigSelector __instance) (line 118)
  - note: Harmony postfix on UIKeyConfigSelector.OnDecision. Announces "press a button" prompt when state becomes CommonChanging or BattleChanging.
- static void GamepadSelector_UpdateAliasAction_Postfix(UIKeyConfigSelector __instance, GameInputManager.InputAction action) (line 139)
  - note: Harmony postfix on UIKeyConfigSelector.UpdateAliasAction. Re-reads cached _selectedPresenter (now showing new binding) and announces it.
- static void AnnouncePresenter(UIKeyConfigSelectItemPresenter presenter, UIKeyConfigSelector selector) (line 163)
  - note: Reads actionName from presenter.label.text and button name from presenter.icon.text (or pressKeyText fallback). Strips sprite tags and controller prefix. Announces with position (idx+1/count).
- static string ExtractButtonName(string raw) (line 206)
  - note: Extracts plain button name from raw GameText. Sprite tags ("<sprite name=X>") → extracts X and strips prefix. Plain text returned trimmed.
- static string StripControllerPrefix(string spriteName) (line 220)
  - note: Iterates _spritePrefixes; returns spriteName with matching prefix removed.
