# CampMenuHandler.BattleSkill.cs (897 lines)

Partial class fragment of CampMenuHandler covering battle skill and combat skill
sub-screens: leveling (Enhance menu), tactical readout (root BattleSkill menu),
and button assignment setting screen (BattleSkill → equip).
namespace: SO2RAccess (line 8)
usings (non-System / notable only): HarmonyLib, Il2CppGame, MelonLoader

## partial class CampMenuHandler (line 10)

Battle skill leveling sub-screen (battleSkillSelector on UICampWindow).
UICampBattleSkillSelector wraps UISelectBattleSkillSelector (battle skills, BP)
and UICampCombatSkillSelector (combat skills, BP). State drives which inner
selector is active. Both share UIBattleSkillInformationPresenter.Set hook.

fields/properties (declaration order):
- _battleSkillOuterSelector : UICampBattleSkillSelector (line 23)
- _battleSkillInnerSelector : UISelectBattleSkillSelector (line 24)
- _battleSkillListBase : UIListSelectorBase (line 25)
- _combatSkillInnerSelector : UICampCombatSkillSelector (line 26)
- _combatSkillListBase : UIListSelectorBase (line 27)
- _battleSkillWasActive : bool (line 28)
- _battleSkillHeadingPending : bool (line 33)  — deferred to hook because activeInHierarchy is always true; set true when root menu item changes
- _lastBattleSkillMenuItem : string (line 38)  — tracks current sub-item name to detect switching between BattleSkillPoint/CombatPoint
- _combatSkillLastState : UICampCombatSkillSelector.State (line 42)
- _combatSkillToggleLastIndex : int (line 44)
- _combatSkillToggleLastIsUse : bool (line 45)
- _battleSkillSettingSelector : UICampBattleSkillSettingSelector (line 50)  — button assignment screen (root BattleSkill only)
- _battleSkillEquipListSel : UICampBattleSkillEquipListSelector (line 51)
- _battleSkillEquipListBase : UIListSelectorBase (line 52)
- _battleSkillPickerListBase : UIListSelectorBase (line 53)
- _battleSkillEquipLastIndex : int (line 54)
- _battleSkillSettingState : SubScreenState (line 55)  — readonly

methods (declaration order):

- bool IsRootBattleSkillMenu() (line 63)
  - note: gate — returns true when _lastRootMenuItemName == "BattleSkill"

- bool IsEnhanceBattleSkillMenu() (line 73)
  - note: gate — returns true when _lastRootMenuItemName is "BattleSkillPoint" or "CombatPoint"

- void UpdateBattleSkillSelector() (line 89)
  - note: instance method, called each frame from UpdateHandlers. activeInHierarchy unusable; uses _battleSkillHeadingPending flag. Also calls UpdateCombatSkillToggleMode() for Enhance/CombatPoint.

- void UpdateBattleSkillSettingSelector() (line 174)
  - note: polls button-assignment screen (Equip state only); caches sub-selectors on genuine entry via SubScreenState.CheckEntry.

- void UpdateBattleSkillEquipSlotList() (line 242)
  - note: called by UpdateBattleSkillSettingSelector; reads GetCurrentData() from _battleSkillEquipListSel to announce slot button + assigned skill name.

- void BattleSkillInfoPresenter_Set_Postfix(UIBattleSkillInformationData data) (line 289)
  - note: Postfix on UIBattleSkillInformationPresenter.Set. Routes to BuildRootBattleSkillAnnouncement (root), BuildEnhanceBattleSkillAnnouncement (Enhance), or picker announcement (setting screen SelectBattleSkill state). First hook fire after menu item change triggers CacheBattleSkillInnerSelectors().

- string BuildRootBattleSkillAnnouncement(UIBattleSkillInformationData data, string targetTypeStr) (line 445)
  - note: assembles Name, MP, damage type, target, elements, range, effect, description, level. Omits zero/empty fields.

- string BuildEnhanceBattleSkillAnnouncement(UIBattleSkillInformationData data, int pointCost, bool isMaxLevel, string balance, bool isCombatSkill, int listSkillLevel, int listSkillLevelMax) (line 511)
  - note: upgrade-focused; combat vs battle skill ordering differs. Uses list item data for level because info panel shows 0/0 for combat skills. Omits upgrade section at max level.

- void CacheBattleSkillInnerSelectors() (line 589)
  - note: called once per root menu item change (from hook). Caches inner selector and announces heading; determines which selector based on _lastRootMenuItemName.

- void UpdateCombatSkillToggleMode() (line 626)
  - note: polls currentState for ChangeOnOff (Square button toggle); tracks index and isUse changes to announce each skill's active/inactive status. Enhance menu only.

- string ResolveCurrentSkillTargetType() (line 691)
  - note: reads battleSkillID from inner selector's current item, queries ParameterManager.GetBattleSkillParameter, maps TargetType enum via MapTargetType.

- string MapDamageSpeciallyType(DamageSpeciallyType type) (line 722)
  - note: switch expression mapping DamageSpeciallyType → Loc key string.

- string MapTargetType(TargetType type) (line 737)
  - note: switch expression mapping TargetType → Loc key string.

- string MapElementName(ElementID id) (line 754)
  - note: switch expression; returns empty for INVALID/NOTHING/MAX.

- string MapBonusType(BattleSkillEnhanceBonusType type) (line 774)
  - note: switch expression mapping BattleSkillEnhanceBonusType → Loc key string.

- string BuildBonusList(Il2CppSystem.Collections.Generic.List\<UIEnhanceBonusData\> bonusDataList) (line 800)
  - note: filters to isUp==true entries, prefers bonusName over mapped bonusType, returns comma-separated list.

- void AppendSkillInfo(StringBuilder sb, UIBattleSkillInformationData data, int pointCost, bool isMaxLevel, string balance) (line 834)
  - note: used only by the assignment screen skill picker; appends name, level, BP cost, MP, description, effect.

- void AppendSentence(StringBuilder sb, string text) (line 862)
  - note: trims trailing ". " from text then appends ". " — prevents double punctuation.

- string ReadSkillPointBalance(object selector) (line 872)
  - note: reads skillPointValue GameText.text via type-check on UISelectBattleSkillSelector, UICampCombatSkillSelector, or UICampSkillSelector; returns raw text (e.g. "100").
