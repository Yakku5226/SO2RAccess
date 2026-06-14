# CampMenuHandler.Status.cs (511 lines)

Partial class fragment of CampMenuHandler covering the Status sub-screen.
Detection is fully hook-driven (both activeInHierarchy and root-menu-hidden
approaches failed — activeInHierarchy is always true for the status selector).
Hook chain: UpdateName → LevelPresenter.Setup → StatusParamPresenter.Setup →
UpdatePresenter (fires last, triggers announcement).
namespace: SO2RAccess (line 11)
usings (non-System / notable only): HarmonyLib, Il2CppGame, MelonLoader, System.Text.RegularExpressions

## partial class CampMenuHandler (line 13)

fields/properties (declaration order):
- _statusSelector : UICampStatusSelector (line 24)
- _statusScreenOpen : bool (line 25)
- _statusLastIndex : int (line 26)
- _statusParamData : UICampStatusParameterData (line 27)
- _statusLevelData : UICampStatusLevelData (line 28)
- _statusPlayerName : string (line 29)
- _statusPlayerID : PlayerID (line 30)
- _statusLastPageIndex : int (line 31)
- _cachedTalentAnnouncement : string (line 37)  — built by UITalentPresenter.Set hook on page 0 open; announced when page switches to 1
- _cachedStatusElementalAnnouncement : string (line 42)  — built by UIElementalGroupPresenter.Set hook; cleared on status close and camp reopen
- _cachedStatusElementalLines : List\<string\> (line 47)  — individual elemental resistance lines for virtual cursor navigation
- _cachedFriendshipAnnouncement : string (line 52)  — built by UICampStatusPresenter.SetEmotion hook; cleared on close and reopen
- _cachedFriendshipLines : List\<string\> (line 57)  — individual friendship lines for virtual cursor navigation
- _statusVirtualLines : List\<string\> (line 61)  — full line list built by AnnounceStatusCharacter for Up/Down navigation
- _statusVirtualIndex : int (line 62)

methods (declaration order):

- void UpdateStatusSelector() (line 73)
  - note: instance method, called each frame. Only handles page changes (L1/R1 — native, no hooks fire). Also drives virtual cursor (Up/Down on page 0). Main open/character-change detection is handled by the Diag_StatusSelector_UpdatePresenter hook.

- void AnnounceStatusCharacter(int index, int total) (line 151)
  - note: static; assembles full character readout from hook-captured _statusPlayerName, _statusLevelData, _statusParamData, plus direct presenter reads for age and favorite food, plus cached elemental and friendship lines. Populates _statusVirtualLines for navigation. Announces everything at once via ScreenReader.Say(string.Join(" ", lines)).

- void StatusParamPresenter_Setup_Postfix(UICampStatusParameterData data) (line 287)
  - note: Postfix on UICampStatusParameterPresenter.Setup. Captures stat data (attack, defence, magic, hit, dodge, critical, str, con, dex, agl, int, luc, stamina, guts) into _statusParamData.

- void Diag_StatusSelector_UpdatePresenter(int index, int difference, bool isDelay) (line 304)
  - note: Postfix on UICampStatusSelector.UpdatePresenter(int, int, bool). Fires last in hook chain. Announces heading on first open, then calls AnnounceStatusCharacter on index change (page 0 only).

- void Diag_StatusSelector_UpdateName(PlayerID playerID, ConstPlayerParameter playerParam) (line 354)
  - note: Postfix on UICampStatusSelector.UpdateName. Captures player name via ParameterManager.GetCharacterFirstName into _statusPlayerName and _statusPlayerID.

- void Diag_StatusLevelPresenter_Setup(UICampStatusLevelData data) (line 374)
  - note: Postfix on UICampStatusLevelPresenter.Setup. Captures level/HP/MP data into _statusLevelData.

- void TalentPresenter_Set_Postfix(Il2CppSystem.Collections.Generic.List\<UITalentData\> dataList) (line 387)
  - note: Postfix on UITalentPresenter.Set. Fires on status screen open (page 0), NOT on page switch. Caches _cachedTalentAnnouncement. If already on talent page (page 1), announces immediately to handle character tab changes while viewing talents.

- void StatusPresenter_SetEmotion_Postfix(Il2CppSystem.Collections.Generic.List\<UICampStatusFavorabilityRatingItemListData\> dataList) (line 438)
  - note: Postfix on UICampStatusPresenter.SetEmotion. CallerCount(1). Reads party tab list to build other-member names (excludes current _statusPlayerID). Builds _cachedFriendshipLines and _cachedFriendshipAnnouncement. Guarded by _lastRootMenuItemName == "Status".
