# BattleResultHandler.cs (327 lines)

Announces post-battle results to the screen reader.
Patch: UIBattleResultSelector.Set(BattleResultInfo) — postfix; fires once when battle result
screen is populated.
Data sources: BattleResultInfo.Exp/Money/SkillPoint/CombatSkillPoint (totals);
characterDataList (per-character level-up, BSP, learned skills);
itemIDList (item IDs dropped, resolved via OverflowResourceData);
character names via ParameterManager.GetCharacterFirstName;
skill names via UICommon.CreateBattleSkillInformationData → fallback TextManager → fallback GetBattleSkillMessage;
bonuses: chainBonusRatio, per-character trainingBonusRatio/openEyesBonusRatio.
namespace: SO2RAccess (line 9)
usings (non-System / notable only): HarmonyLib, Il2CppGame, MelonLoader

## class BattleResultHandler (line 31)
Announces post-battle results to the screen reader.

fields/properties (declaration order):
- _patchesApplied : bool (line 32)

methods (declaration order):
- void ApplyPatches(HarmonyLib.Harmony) (line 38)
  - note: Patches UIBattleResultSelector.Set(BattleResultInfo) with postfix. Pre-initializes IL2CPP type tables for UIBattleResultSelector, BattleResultInfo, BattleResultInfo.BattleResultCharacterData, OverflowResourceData, UICommon, UIBattleSkillInformationData via RuntimeHelpers. Safe to call multiple times.
- void BattleResultSelector_Set_Postfix(BattleResultInfo) (line 73)
  - note: Static Harmony postfix for UIBattleResultSelector.Set(BattleResultInfo). Builds announcement: heading, EXP, FOL, SP (if >0), BSP (if >0), chain bonus, per-character training/open-eyes bonuses, level-ups (new level + BSP gained + learned skills), items (grouped by ID with counts, resolved via OverflowResourceData). Announces via ScreenReader.Say.
- List<string> ResolveBattleSkillNamesAndDescriptions(Il2CppSystem.Collections.Generic.List<BattleSkillID>, PlayerID) (line 242)
  - note: For each BattleSkillID: tries UICommon.CreateBattleSkillInformationData (name+description), falls back to TextManager.GetMessage(nameId, Skill), then ParameterManager.GetBattleSkillMessage, finally Loc.Get("battle_result_skill_unknown"). Returns list of localized announcement strings (with or without description).
