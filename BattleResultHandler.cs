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
    ///   screen is populated. Announces EXP, FOL, items obtained, and level-ups.
    ///
    /// Data sources:
    ///   BattleResultInfo.Exp — total experience earned (PascalCase property).
    ///   BattleResultInfo.Money — total fol earned.
    ///   BattleResultInfo.characterDataList — per-character data with levelUpCount and playerID.
    ///   BattleResultInfo.itemIDList — item IDs dropped; resolved to names via OverflowResourceData.
    ///   Character names: ParameterManager.GetCharacterFirstName(playerID).
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
        /// Announces total EXP, FOL, items obtained, and any level-ups.
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

                DebugLogger.LogGameValue("BattleResult",
                    $"exp={totalExp} money={totalMoney}");

                // Level-ups — iterate character data.
                var charList = resultInfo.characterDataList;
                if (charList != null)
                {
                    var pm = ParameterManager.Instance;
                    for (int i = 0; i < charList.Count; i++)
                    {
                        var cd = charList[i];
                        if (cd == null || cd.levelUpCount <= 0) continue;

                        string name = "";
                        if (pm != null)
                            name = pm.GetCharacterFirstName(cd.playerID) ?? "";

                        int newLevel = cd.preLevel + cd.levelUpCount;

                        sb.Append(Loc.Get("battle_result_levelup", name, newLevel));
                        sb.Append(" ");

                        DebugLogger.LogGameValue("BattleResult.levelup",
                            $"name='{name}' preLevel={cd.preLevel} " +
                            $"levelUpCount={cd.levelUpCount} newLevel={newLevel}");
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
    }
}
