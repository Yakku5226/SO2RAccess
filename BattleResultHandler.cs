using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace SO2RAccess
{
    /// <summary>
    /// Announces post-battle results to the screen reader.
    ///
    /// Patches applied:
    ///   UIBattleResultSelector.Set(BattleResultInfo) — fires once when the battle result
    ///   screen is populated. Announces EXP, FOL, SP, BSP, items obtained, and level-ups.
    ///
    /// Data sources:
    ///   BattleResultInfo.Exp / Money / SkillPoint / CombatSkillPoint — totals.
    ///   BattleResultInfo.characterDataList — per-character level-up, BSP, learned skills.
    ///   BattleResultInfo.itemIDList — item IDs dropped; resolved via OverflowResourceData.
    ///   Character names: ParameterManager.GetCharacterFirstName(playerID).
    ///   Skill names: ParameterManager.GetBattleSkillParameter → battleSkillNameID →
    ///                TextManager.GetMessage(key, Skill).
    ///   Skill descriptions: UICommon.CreateBattleSkillInformationData(skillId, playerID) →
    ///                       battleSkillDescription.
    ///   Bonuses: BattleResultInfo.chainBonusRatio, per-character trainingBonusRatio /
    ///            openEyesBonusRatio.
    /// </summary>
    public class BattleResultHandler
    {
        private bool _patchesApplied = false;

        /// <summary>
        /// Applies Harmony patches for the battle result screen.
        /// Safe to call multiple times — patches are only applied once.
        /// </summary>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (_patchesApplied) return;

            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(UIBattleResultSelector).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(BattleResultInfo).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(BattleResultInfo.BattleResultCharacterData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(OverflowResourceData).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UICommon).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(UIBattleSkillInformationData).TypeHandle);

                harmony.Patch(
                    AccessTools.Method(typeof(UIBattleResultSelector), "Set",
                        new Type[] { typeof(BattleResultInfo) }),
                    postfix: new HarmonyMethod(typeof(BattleResultHandler),
                        nameof(BattleResultSelector_Set_Postfix))
                );

                _patchesApplied = true;
                MelonLogger.Msg("BattleResultHandler: patches applied.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"BattleResultHandler.ApplyPatches failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for UIBattleResultSelector.Set(BattleResultInfo).
        /// Fires once when the battle result screen receives its data.
        /// Announces total EXP, FOL, SP, BSP, items obtained, and any level-ups
        /// with per-character BSP changes and learned battle skills.
        /// </summary>
        private static void BattleResultSelector_Set_Postfix(BattleResultInfo resultInfo)
        {
            if (resultInfo == null) return;

            try
            {
                var sb = new StringBuilder();

                // Heading.
                sb.Append(Loc.Get("battle_result_heading"));
                sb.Append(" ");

                // EXP and FOL (use PascalCase properties per IL2CPP safety rule).
                int totalExp = resultInfo.Exp;
                int totalMoney = resultInfo.Money;

                sb.Append(Loc.Get("battle_result_exp", totalExp));
                sb.Append(" ");
                sb.Append(Loc.Get("battle_result_fol", totalMoney));
                sb.Append(" ");

                // SP and BSP totals.
                int totalSp = resultInfo.SkillPoint;
                int totalBsp = resultInfo.CombatSkillPoint;

                if (totalSp > 0)
                {
                    sb.Append(Loc.Get("battle_result_sp", totalSp));
                    sb.Append(" ");
                }
                if (totalBsp > 0)
                {
                    sb.Append(Loc.Get("battle_result_bsp", totalBsp));
                    sb.Append(" ");
                }

                // Bonuses — announce which bonus types are active.
                float chainBonus = resultInfo.chainBonusRatio;
                if (chainBonus > 0f)
                {
                    sb.Append(Loc.Get("battle_result_bonus_chain"));
                    sb.Append(" ");
                }

                DebugLogger.LogGameValue("BattleResult",
                    $"exp={totalExp} money={totalMoney} sp={totalSp} bsp={totalBsp}" +
                    $" chainBonus={chainBonus}");

                // Level-ups — iterate character data.
                var charList = resultInfo.characterDataList;
                if (charList != null)
                {
                    var pm = ParameterManager.Instance;
                    for (int i = 0; i < charList.Count; i++)
                    {
                        var cd = charList[i];
                        if (cd == null) continue;

                        string name = "";
                        if (pm != null)
                            name = pm.GetCharacterFirstName(cd.playerID) ?? "";

                        // Per-character bonuses (announce even without level-up).
                        if (cd.trainingBonusRatio > 0f)
                        {
                            sb.Append(Loc.Get("battle_result_bonus_training", name));
                            sb.Append(" ");
                        }
                        if (cd.openEyesBonusRatio > 0f)
                        {
                            sb.Append(Loc.Get("battle_result_bonus_openeyes", name));
                            sb.Append(" ");
                        }

                        if (cd.levelUpCount <= 0) continue;

                        int newLevel = cd.preLevel + cd.levelUpCount;
                        int bspGained = cd.increaseCombatSkillPoint;

                        sb.Append(Loc.Get("battle_result_levelup", name, newLevel));
                        sb.Append(" ");

                        if (bspGained > 0)
                        {
                            sb.Append(Loc.Get("battle_result_levelup_bsp", bspGained));
                            sb.Append(" ");
                        }

                        // Learned battle skills — resolve names and descriptions.
                        var skillIds = cd.learningBattleSkillList;
                        if (skillIds != null && skillIds.Count > 0)
                        {
                            var skillAnnouncements = ResolveBattleSkillNamesAndDescriptions(
                                skillIds, cd.playerID);
                            foreach (var announcement in skillAnnouncements)
                            {
                                sb.Append(announcement);
                                sb.Append(" ");
                            }
                        }

                        DebugLogger.LogGameValue("BattleResult.levelup",
                            $"name='{name}' preLevel={cd.preLevel} " +
                            $"levelUpCount={cd.levelUpCount} newLevel={newLevel} " +
                            $"bsp+={bspGained} skills={skillIds?.Count ?? 0}" +
                            $" training={cd.trainingBonusRatio} openEyes={cd.openEyesBonusRatio}");
                    }
                }

                // Items — resolve names from item IDs.
                var itemIdList = resultInfo.itemIDList;
                if (itemIdList != null && itemIdList.Count > 0)
                {
                    // Group duplicate IDs to get counts.
                    var itemCounts = new Dictionary<int, int>();
                    for (int i = 0; i < itemIdList.Count; i++)
                    {
                        int id = itemIdList[i];
                        if (itemCounts.ContainsKey(id))
                            itemCounts[id]++;
                        else
                            itemCounts[id] = 1;
                    }

                    foreach (var kvp in itemCounts)
                    {
                        try
                        {
                            // Create OverflowResourceData to resolve the item name.
                            var itemData = new OverflowResourceData(
                                kvp.Key, kvp.Value, false, default(FactorID));
                            string itemName = itemData.name ?? "";

                            if (!string.IsNullOrEmpty(itemName))
                            {
                                if (kvp.Value > 1)
                                    sb.Append(Loc.Get("battle_result_item_multi",
                                        itemName, kvp.Value));
                                else
                                    sb.Append(Loc.Get("battle_result_item", itemName));
                                sb.Append(" ");

                                DebugLogger.LogGameValue("BattleResult.item",
                                    $"id={kvp.Key} name='{itemName}' count={kvp.Value}");
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.LogState(
                                $"BattleResult: item name resolve failed for id={kvp.Key}: {ex.Message}");
                        }
                    }
                }

                string result = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(result))
                    ScreenReader.Say(result);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"BattleResultHandler.Set postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Resolves a list of BattleSkillID values to localized announcements
        /// with name and description. Uses UICommon.CreateBattleSkillInformationData
        /// for the description, falling back to name-only if unavailable.
        /// </summary>
        private static List<string> ResolveBattleSkillNamesAndDescriptions(
            Il2CppSystem.Collections.Generic.List<BattleSkillID> skillIds,
            PlayerID playerID)
        {
            var announcements = new List<string>();
            var pm = ParameterManager.Instance;
            var tm = TextManager.Instance;
            if (pm == null || tm == null) return announcements;

            for (int i = 0; i < skillIds.Count; i++)
            {
                try
                {
                    var skillId = skillIds[i];
                    var param = pm.GetBattleSkillParameter(skillId);
                    if (param == null) continue;

                    string nameId = param.battleSkillNameID;
                    if (string.IsNullOrEmpty(nameId)) continue;

                    string skillName = "";
                    string description = "";

                    // Try UICommon first — native code resolves both name and description.
                    try
                    {
                        var infoData = UICommon.CreateBattleSkillInformationData(
                            skillId, playerID, false);
                        if (infoData != null)
                        {
                            skillName = infoData.battleSkillName ?? "";
                            description = (infoData.battleSkillDescription ?? "").TrimEnd('.', ' ');
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.LogState(
                            $"BattleResult: UICommon skill info failed for '{nameId}': {ex.Message}");
                    }

                    // Fallback: TextManager with Skill message type.
                    if (string.IsNullOrEmpty(skillName))
                        skillName = tm.GetMessage(nameId, TextManager.MessageType.Skill) ?? "";

                    // Fallback: ParameterManager.GetBattleSkillMessage.
                    if (string.IsNullOrEmpty(skillName))
                    {
                        try
                        {
                            skillName = pm.GetBattleSkillMessage(nameId) ?? "";
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.LogState(
                                $"BattleResult: GetBattleSkillMessage failed for '{nameId}': {ex.Message}");
                        }
                    }

                    // Last resort: announce as "Learned new skill" so it's never silent.
                    if (string.IsNullOrEmpty(skillName))
                    {
                        DebugLogger.LogState($"BattleResult: skill name unresolved for '{nameId}', using fallback");
                        skillName = Loc.Get("battle_result_skill_unknown");
                    }

                    if (!string.IsNullOrEmpty(description))
                        announcements.Add(Loc.Get("battle_result_learned_skill",
                            skillName, description));
                    else
                        announcements.Add(Loc.Get("battle_result_learned_skill_noDesc",
                            skillName));

                    DebugLogger.LogGameValue("BattleResult.skill",
                        $"name='{skillName}' desc='{description}'");
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState(
                        $"BattleResult: skill resolve failed for id={skillIds[i]}: {ex.Message}");
                }
            }

            return announcements;
        }
    }
}
