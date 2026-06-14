# CampMenuHandler.Party.cs (578 lines)

NOTE: This is a partial class fragment of CampMenuHandler. It adds party formation,
assist formation, and tactics sub-screen support. The other partial files contain
the root menu, items, status, skills, etc.
namespace: SO2RAccess (line 10)
usings (non-System / notable only): HarmonyLib, Il2CppGame, MelonLoader

## partial class CampMenuHandler (line 12)

### Fields — Party Formation (lines 14-23)
- _selectCharSelector : static UICampSelectCharacterSelector (line 21)
  — party formation selector; UICampSelectCharacterSelector extends UISelectorBase (NOT UIListSelectorBase); currentIndex unavailable from managed code
- _selectCharState : static readonly SubScreenState (line 22)
- _selectCharSlotData : static readonly Dictionary<int, CampCharacterStatusParameterData> (line 23)
  — maps slot index → character data; populated by PartyMemberPresenter_SetData_Postfix

### Fields — Assist Formation (lines 25-38)
- _assistSelector : static UICampAssistSettingSelector (line 32)
- _assistState : static readonly SubScreenState (line 33)
- _assistEquipListBase : static UIListSelectorBase (line 34)
- _assistEquipLastIndex : static int (line 35)
- _assistCharListBase : static UIListSelectorBase (line 36)
- _assistCharLastIndex : static int (line 37)
- _assistLastState : static int (line 38)  — tracks Equip(0) vs SelectAssistCharacter(1)

### Fields — Tactics (lines 40-54)
- _operationSelector : static UICampOperationSelector (line 49)
- _operationState : static readonly SubScreenState (line 50)
- _operationCharLastIndex : static int (line 51)
- _operationSelectListBase : static UIListSelectorBase (line 52)
- _operationSelectLastIndex : static int (line 53)
- _operationLastState : static int (line 54)  — tracks SelectCharacter(0) vs SelectOperation(1)

methods (declaration order):
- void UpdatePartyFormationSelector() (line 67)
  - note: polls UICampSelectCharacterSelector; navigation is 100% native so cursor slot detection uses pointer comparison of cursorPresenter.followTask.target / moveTask.cursorTarget against each slot's cursorTarget; falls back to distance-based position comparison; announces character name, level, HP/MP, role, position; gated on _lastRootMenuItemName == "PartyFormation"
- static void ForceReannounceCurrentSlot() (line 210)
  - note: resets _selectCharState.LastIndex to -1 to force the next poll to re-announce the current slot (used after data changes like toggling battle/reserve)
- void UpdateAssistSettingSelector() (line 219)
  - note: polls UICampAssistSettingSelector; two states — Equip(0): announces button label, assigned character, assist name, position; SelectAssistCharacter(1): announces character name and "currently set" status; gated on _lastRootMenuItemName == "AssistFormation"
- void UpdateTacticsSelector() (line 362)
  - note: polls UICampOperationSelector; two states — SelectCharacter(0): polled via TryCast<UIListSelectorBase>().currentIndex, announces name + current tactic; SelectOperation(1): position tracking only, details come from OperationInfoPresenter_Set_Postfix hook; gated on _lastRootMenuItemName == "Tactics"
- static void CharacterStatusPresenter_SetStatus_Postfix(Il2CppSystem.Collections.Generic.List<CampCharacterStatusParameterData>) (line 473)
  - note: Harmony postfix on UICampCharacterStatusPresenter.SetStatus; fires when party formation screen updates character status; calls ForceReannounceCurrentSlot so user hears updated data
- static void PartyMemberPresenter_SetData_Postfix(int index, UICampPartyMemberSelectItemData data) (line 498)
  - note: Harmony postfix on UICampPartyMemberPresenter.SetData(int, UICampPartyMemberSelectItemData); caches statusParameterData by slot index into _selectCharSlotData; calls ForceReannounceCurrentSlot
- static void OperationInfoPresenter_Set_Postfix(string name, string description, string prefabPath) (line 527)
  - note: Harmony postfix on UICampOperationInformationPresenter.Set(string, string, string); fires on each navigation in the operation list; gated to SelectOperation state (state==1); reads position and isSetting flag from _operationSelectListBase; announces name, description, "currently set", and position
