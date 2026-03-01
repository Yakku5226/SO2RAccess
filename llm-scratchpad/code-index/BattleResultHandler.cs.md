# Code Index: BattleResultHandler.cs

## Top-Level Comments
- Class summary (lines 11–24): Announces post-battle results to the screen reader.
  Patches UIBattleResultSelector.Set(BattleResultInfo), which fires once when the
  battle result screen is populated. Announces EXP, FOL, items obtained, and level-ups.
  Data sources documented: BattleResultInfo.Exp, .Money, .characterDataList, .itemIDList.
  Character names resolved via ParameterManager.GetCharacterFirstName(playerID).

---

## Class: BattleResultHandler (line 25)

### Fields
- private bool _patchesApplied (line 27)

### Methods

- public void ApplyPatches(HarmonyLib.Harmony harmony) (line 33)
  Note: Guards against double-patching via _patchesApplied. Runs IL2CPP class
  constructors for UIBattleResultSelector, BattleResultInfo,
  BattleResultInfo.BattleResultCharacterData, and OverflowResourceData before
  patching to ensure types are initialized. Patches
  UIBattleResultSelector.Set(BattleResultInfo) with BattleResultSelector_Set_Postfix.

- private static void BattleResultSelector_Set_Postfix(BattleResultInfo resultInfo) (line 65)
  Note: Harmony postfix — not called directly. Builds a single announcement string
  containing: heading, total EXP, total FOL, any level-up messages (character name +
  new level, skipped if levelUpCount <= 0), and item names with counts. Items are
  grouped by ID using a local Dictionary to collapse duplicates before name resolution.
  Item names resolved by constructing OverflowResourceData(id, count, false, default(FactorID))
  and reading its .name property. Each item resolve is individually try-caught to avoid
  one bad item ID silencing the rest. Final string is trimmed and sent to ScreenReader.Say().
