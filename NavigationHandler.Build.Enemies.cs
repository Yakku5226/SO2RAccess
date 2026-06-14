using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SO2RAccess
{
    // Partial class fragment of NavigationHandler.Build: Enemy scanning + type naming (BuildEnemies, GetEnemyTypeName).
    public partial class NavigationHandler
    {
        #region Private — Build
        /// <summary>
        /// Scans for FieldEnemy objects and builds the Enemies category.
        /// Resolves enemy names from party data via ParameterManager + TextManager.
        /// </summary>
        private void BuildEnemies(Vector3 playerPos)
        {
            _categories[CAT_ENEMY].Clear();

            var found = UnityEngine.Object.FindObjectsOfType<FieldEnemy>();
            if (found == null || found.Length == 0) return;

            var pm = ParameterManager.Instance;
            var tm = TextManager.Instance;
            var items = new List<NavItem>();

            for (int i = 0; i < found.Length; i++)
            {
                var enemy = found[i];
                if (enemy == null) continue;

                Vector3 pos  = enemy.transform.position;
                float   dist = Vector3.Distance(playerPos, pos);

                // World map: skip distant enemies.
                if (_isWorldmap && dist > WorldmapEnemyMaxDistance) continue;

                // Get symbol type for difficulty label
                string typeName = "";
                try
                {
                    var symbolType = enemy.EnemySymbolType;
                    typeName = GetEnemyTypeName(symbolType);
                }
                catch { }

                // Resolve enemy name via encounter chain:
                // FieldEnemy.EncountID → encounter params → partyID → enemy params → name
                string enemyName = "";
                try
                {
                    if (pm != null && tm != null)
                    {
                        int encountID = enemy.encountID;

                        if (encountID != 0)
                        {
                            // Step 1: encounter ID → encounter params (has enemy party ID)
                            var encParams = pm.GetFieldmapEncountParameter(encountID);

                            if (encParams != null && encParams.Count > 0)
                            {
                                int partyID = encParams[0].enemyPartyID;

                                if (partyID != 0)
                                {
                                    // Step 2: party ID → enemy parameters (has name key)
                                    var partyMembers =
                                        pm.GetEnemyParameterListByPartyID(partyID);

                                    if (partyMembers != null && partyMembers.Count > 0)
                                    {
                                        string nameKey = partyMembers[0].charaNameID;

                                        if (!string.IsNullOrEmpty(nameKey))
                                            enemyName = TextUtil.ResolveCharaNameKey(
                                                nameKey,
                                                TextManager.MessageType.System,
                                                TextManager.MessageType.Skill,
                                                TextManager.MessageType.Item);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState(
                        $"NAV:ENEMY name resolve failed: {ex.Message}");
                }

                // Build label: "Name, type" or "Type enemy" fallback
                string label;
                if (!string.IsNullOrEmpty(enemyName))
                {
                    label = string.IsNullOrEmpty(typeName)
                        ? enemyName
                        : Loc.Get("nav_enemy_named", enemyName, typeName);
                }
                else
                {
                    label = string.IsNullOrEmpty(typeName)
                        ? Loc.Get("nav_enemy_unknown")
                        : Loc.Get("nav_enemy_typed", typeName);
                }

                items.Add(new NavItem
                {
                    Label         = label,
                    Distance      = dist,
                    Position      = pos,
                    LiveTransform = enemy.transform,
                });

                DebugLogger.LogGameValue("NAV:ENEMY",
                    $"label='{label}' type={typeName} " +
                    $"partyID={enemy.EnemyPartyID} dist={dist:F1}");
            }

            SortAndFilterUnreachable(items, playerPos);

            // Number duplicates of the same base label
            var labelCounts = new Dictionary<string, int>();
            foreach (var item in items)
            {
                if (!labelCounts.ContainsKey(item.Label))
                    labelCounts[item.Label] = 0;
                labelCounts[item.Label]++;
            }

            var labelNums = new Dictionary<string, int>();
            for (int i = 0; i < items.Count; i++)
            {
                string baseLabel = items[i].Label;
                if (labelCounts[baseLabel] > 1)
                {
                    if (!labelNums.ContainsKey(baseLabel))
                        labelNums[baseLabel] = 1;
                    var item = items[i];
                    item.Label = $"{baseLabel} {labelNums[baseLabel]++}";
                    items[i] = item;
                }
            }

            _categories[CAT_ENEMY].AddRange(items);
        }


        /// <summary>Returns a friendly name for the enemy symbol type.</summary>
        private static string GetEnemyTypeName(FieldEnemySymbolType type)
        {
            switch (type)
            {
                case FieldEnemySymbolType.Weak:
                case FieldEnemySymbolType.SubspecificWeak:
                    return Loc.Get("nav_enemy_weak");
                case FieldEnemySymbolType.Medium:
                case FieldEnemySymbolType.SubspecificMedium:
                    return Loc.Get("nav_enemy_medium");
                case FieldEnemySymbolType.Strong:
                case FieldEnemySymbolType.SubspecificStrong:
                    return Loc.Get("nav_enemy_strong");
                case FieldEnemySymbolType.Raid:
                    return Loc.Get("nav_enemy_raid");
                default:
                    return "";
            }
        }
        #endregion
    }
}
