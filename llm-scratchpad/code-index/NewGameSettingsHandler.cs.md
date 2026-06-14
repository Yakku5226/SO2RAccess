# NewGameSettingsHandler.cs (284 lines)

Announces new game settings screen navigation (follows protagonist selection).
Settings rows: Name, Voice Language, Voice Type, Difficulty, BGM Version,
Display Event Art, Return to defaults, Confirm.
Patches: UITitleSelectVoiceSelector.Show (postfix), OnUp (postfix), OnDown (postfix),
         UpdateCurrentPresenter (postfix), OnDecision (postfix).
namespace: SO2RAccess (line 7)
usings (non-System / notable only): HarmonyLib, Il2CppGame, MelonLoader

## class NewGameSettingsHandler (line 23)
Announces new game settings screen navigation to the screen reader.

fields/properties (declaration order):
- _patchesApplied : bool (line 26)
- _instance : NewGameSettingsHandler (line 29)  — static back-reference so Harmony static methods can call instance logic
- _lastMenuIndex : int (line 31)
- _showComplete : bool (line 35)  — true after Show() finishes init calls to UpdateCurrentPresenter; prevents spurious announcements during init flood

methods (declaration order):
- NewGameSettingsHandler() (line 45)
  - note: Sets static _instance = this. Call once in Main.InitializeHandlers().
- void ApplyPatches(HarmonyLib.Harmony) (line 58)
  - note: Patches UITitleSelectVoiceSelector: Show, OnUp, OnDown, UpdateCurrentPresenter, OnDecision. Pre-initializes UITitleSelectVoiceMenuSelectItemPresenter and UICommonSelectTextPresenter type tables via RuntimeHelpers. Safe to call multiple times.
- void VoiceSelector_Show_Postfix(UITitleSelectVoiceSelector) (line 107)
  - note: Harmony postfix for UITitleSelectVoiceSelector.Show. Resets _showComplete and _lastMenuIndex, announces "newgame_screen", calls AnnounceCurrentItem, then sets _showComplete=true.
- void VoiceSelector_OnUp_Postfix(UITitleSelectVoiceSelector) (line 122)
  - note: Harmony postfix for UITitleSelectVoiceSelector.OnUp. Delegates to OnNavigated.
- void VoiceSelector_OnDown_Postfix(UITitleSelectVoiceSelector) (line 127)
  - note: Harmony postfix for UITitleSelectVoiceSelector.OnDown. Delegates to OnNavigated.
- void VoiceSelector_UpdateCurrentPresenter_Postfix(UITitleSelectVoiceSelector, UITitleSelectVoiceSelector.Menu) (line 135)
  - note: Harmony postfix for UITitleSelectVoiceSelector.UpdateCurrentPresenter. Fires on left/right value change. Guards on _showComplete. Reads preferNewValue=true (nextText animating in beats currentText fading out). Announces new value only.
- void VoiceSelector_OnDecision_Postfix(UITitleSelectVoiceSelector) (line 157)
  - note: Harmony postfix for UITitleSelectVoiceSelector.OnDecision. Announces name-editing mode if __instance.isEditName is true.
- void OnNavigated(UITitleSelectVoiceSelector) (line 174)
  - note: Checks selector.menuIndex against _lastMenuIndex; announces current item on change only.
- void AnnounceCurrentItem(UITitleSelectVoiceSelector) (line 185)
  - note: Gets presenter for current menu row, reads label (falls back to GetFallbackLabel), reads value via GetSettingValueText(preferNewValue:false), announces "label: value" or label alone.
- UITitleSelectVoiceMenuSelectItemPresenter GetPresenter(UITitleSelectVoiceSelector, UITitleSelectVoiceSelector.Menu) (line 217)
  - note: Returns presenter from menuPresneterList[menu index] via TryCast; null if out of range.
- string GetSettingValueText(UITitleSelectVoiceSelector, UITitleSelectVoiceSelector.Menu, UITitleSelectVoiceMenuSelectItemPresenter, bool) (line 234)
  - note: Returns value text for a menu row. EditName: reads fullNamePresenter.fullName.text. Initialize/Decision: returns "". Others: reads presenter.textPresenter; if preferNewValue=true reads nextText first (animating in), else currentText.
- string GetFallbackLabel(UITitleSelectVoiceSelector.Menu) (line 266)
  - note: Returns Loc-keyed English label string for each Menu enum value; used when presenter.label.text is empty.
