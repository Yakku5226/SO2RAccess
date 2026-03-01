using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SO2RAccess
{
    public partial class NavigationHandler
    {
        #region Data Model

        private struct NavItem
        {
            public string    Label;
            public float     Distance;
            public Vector3   Position;
            /// <summary>
            /// Live transform of the target object (NPCs, chests, markers).
            /// Updated each frame during auto-walk so moving NPCs are tracked.
            /// Null for exits — their position in the world does not change.
            /// </summary>
            public Transform LiveTransform;
            /// <summary>
            /// True for functional NPCs (shops, inns, guilds) that are commonly
            /// behind counters. These skip the NavMesh reachability filter because
            /// the game allows interaction over the counter.
            /// </summary>
            public bool      IsCounterNpc;
        }

        #endregion

        #region Private — Build

        /// <summary>
        /// Scans for NPCs in the current field.
        /// Resolves each NPC's display name via ConstNpcParameter position matching
        /// and code name parsing. Falls back to NPC type label if no match.
        /// Generic NPCs (no specific type) are numbered by distance: NPC 1, NPC 2, etc.
        /// Party members (within 2 units of the player) are filtered out.
        /// </summary>
        private void BuildNpcs(Vector3 playerPos, FieldmapID mapID)
        {
            _categories[CAT_NPC].Clear();

            var npcParams = TryGetNpcParams(mapID);
            DebugLogger.LogState(
                $"NAV: npcParams for map {mapID}: " +
                (npcParams == null ? "null" : $"{npcParams.Count} entries"));

            var found = UnityEngine.Object.FindObjectsOfType<FieldNpcCharacter>();
            if (found == null) return;

            var items = new List<NavItem>();
            foreach (var npc in found)
            {
                if (npc == null) continue;

                // Skip enemies — they have their own category
                if (npc.TryCast<FieldEnemy>() != null) continue;

                Vector3 pos  = npc.transform.position;
                float   dist = Vector3.Distance(playerPos, pos);

                if (dist < 2.0f) continue; // party members walk alongside the player

                string label = ResolveNpcName(npc, npcParams);
                bool isCounter = IsFunctionalNpcType(npc.npcType);
                items.Add(new NavItem
                {
                    Label         = label,
                    Distance      = dist,
                    Position      = pos,
                    LiveTransform = npc.transform,
                    IsCounterNpc  = isCounter,
                });
                DebugLogger.LogGameValue("NAV:NPC",
                    $"[{label}] type={npc.npcType} dist={dist:F1} pos={pos}");
            }

            items.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            // Filter out NPCs that are unreachable via NavMesh.
            // Functional NPCs (shops, inns, guilds) skip this check because
            // they are commonly behind counters — the game allows interaction
            // over the counter even though a walkable path does not exist.
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (items[i].IsCounterNpc)
                {
                    DebugLogger.LogState(
                        $"NAV: keeping counter NPC '{items[i].Label}' (skip reachability)");
                    continue;
                }
                if (!IsReachable(playerPos, items[i].Position))
                {
                    DebugLogger.LogState($"NAV: filtered unreachable NPC '{items[i].Label}'");
                    items.RemoveAt(i);
                }
            }

            // Number any NPCs that still carry the generic "NPC" label.
            int npcNum = 1;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Label == "NPC")
                {
                    item.Label = Loc.Get("nav_npc_n", npcNum++);
                    items[i]   = item;
                }
            }

            _categories[CAT_NPC].AddRange(items);
        }

        /// <summary>
        /// Resolves a human-readable name for an NPC.
        ///
        /// Steps:
        ///   1. Check DialogueHandler.NpcDisplayNames — if the player has already spoken
        ///      to this NPC the real display name is returned, qualified with the NPC's
        ///      functional role when relevant (e.g. "Equipment shop (Hahn)").
        ///   2. Try matching the NPC's initial position against ConstNpcParameter entries.
        ///   3. If a persistent dialogue name is found, return it qualified as above.
        ///   4. Parse the code name (e.g. NPC_..._GIRL1 → "Girl 1").
        ///   5. Fall back to the NPC type category label (e.g. "Item shop").
        ///   6. Final fallback: return "NPC" so the caller can number it.
        /// </summary>
        private static string ResolveNpcName(
            FieldNpcCharacter npc,
            Il2CppSystem.Collections.Generic.List<ConstNpcParameter> npcParams)
        {
            // Resolve the NPC's functional category up front — used to qualify dialogue names.
            // e.g. NpcType.SHOP_EQUIPMENT → "Equipment shop", NpcType.NPC → "NPC"
            string category = GetNpcCategory(npc.npcType);

            // Prefer the real dialogue name if we've already talked to this NPC.
            int instanceID = npc.GetInstanceID();
            if (DialogueHandler.NpcDisplayNames.TryGetValue(instanceID, out string knownName))
            {
                string qualified = QualifyNpcName(knownName, category);
                DebugLogger.LogState(
                    $"NAV: NPC id={instanceID} → '{qualified}' (from dialogue map)");
                return qualified;
            }

            if (npcParams != null && npcParams.Count > 0)
            {
                try
                {
                    Vector3 spawn = npc.InitialPosition;
                    for (int i = 0; i < npcParams.Count; i++)
                    {
                        var param = npcParams[i];
                        if (param == null) continue;
                        if (Vector3.Distance(spawn, param.Position) < 2.0f)
                        {
                            string codeName = param.Name;
                            if (!string.IsNullOrEmpty(codeName))
                            {
                                // Prefer a real name learned from dialogue (persists across sessions).
                                if (DialogueHandler.PersistentNpcNames.TryGetValue(
                                        codeName, out string persistedName))
                                {
                                    string qualified = QualifyNpcName(persistedName, category);
                                    DebugLogger.LogState(
                                        $"NAV: NPC '{codeName}' → '{qualified}' (persistent)");
                                    return qualified;
                                }

                                string readable = ParseNpcCodeName(codeName);
                                if (!string.IsNullOrEmpty(readable))
                                {
                                    DebugLogger.LogState(
                                        $"NAV: NPC '{codeName}' → '{readable}'");
                                    return readable;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"NAV: ResolveNpcName error: {ex.Message}");
                }
            }

            // Fall back to the NPC type category (e.g. "Item shop", "Innkeeper").
            return category;
        }

        /// <summary>
        /// Combines a dialogue display name with a functional NPC category.
        /// Returns "[category] ([displayName])" for functional NPCs (shop, inn, guild, etc.)
        /// and just "[displayName]" for plain NPCs with no specific role.
        /// This ensures "Hahn" becomes "Equipment shop (Hahn)" while "Elderly person"
        /// stays as "Elderly person".
        /// </summary>
        private static string QualifyNpcName(string displayName, string category)
        {
            if (category == "NPC") return displayName;
            return $"{category} ({displayName})";
        }

        /// <summary>
        /// Parses an NPC internal code name into a human-readable label.
        ///
        /// Format: NPC_{mapArea}_{mapSub}_{orderNum}_{DESCRIPTOR}{num}
        /// e.g. NPC_0003_01a_18_GIRL1 → "Girl 1"
        ///      NPC_0003_01a_17_GRANDFATHER2 → "Grandfather 2"
        ///      NPC_0003_01a_26_WEAPONSHOP1  → "Weaponshop 1"
        ///
        /// The last underscore segment is extracted, trailing digits are split from
        /// the descriptor text, and the text is title-cased.
        /// Returns null if the code name cannot be parsed.
        /// </summary>
        private static string ParseNpcCodeName(string codeName)
        {
            if (string.IsNullOrEmpty(codeName)) return null;

            // Take the last segment after the final underscore.
            int lastUnder = codeName.LastIndexOf('_');
            if (lastUnder < 0 || lastUnder >= codeName.Length - 1) return null;

            string suffix = codeName.Substring(lastUnder + 1); // e.g. "GIRL1"

            // Split trailing digits from the descriptor text.
            int numStart = suffix.Length;
            while (numStart > 0 && char.IsDigit(suffix[numStart - 1]))
                numStart--;

            string text = suffix.Substring(0, numStart);       // e.g. "GIRL"
            string num  = suffix.Substring(numStart);           // e.g. "1"

            if (string.IsNullOrEmpty(text)) return null;

            // Title-case: first letter uppercase, rest lowercase.
            string readable = char.ToUpper(text[0]) + text.Substring(1).ToLower();

            return string.IsNullOrEmpty(num) ? readable : $"{readable} {num}";
        }

        /// <summary>
        /// Scans for treasure chests and labels each by opened/unopened status,
        /// numbered separately by type in distance order.
        /// </summary>
        private void BuildChests(Vector3 playerPos)
        {
            _categories[CAT_CHEST].Clear();

            var found = UnityEngine.Object.FindObjectsOfType<FieldTreasureBox>();
            if (found == null) return;

            var items = new List<NavItem>();
            foreach (var chest in found)
            {
                if (chest == null) continue;

                Vector3 pos   = chest.transform.position;
                float   dist  = Vector3.Distance(playerPos, pos);
                string  label = chest.isAcquired
                    ? Loc.Get("nav_chest_opened")
                    : Loc.Get("nav_chest_unopened");

                items.Add(new NavItem
                {
                    Label         = label,
                    Distance      = dist,
                    Position      = pos,
                    LiveTransform = chest.transform,
                });
            }

            items.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            // Filter out chests that are unreachable via NavMesh.
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (!IsReachable(playerPos, items[i].Position))
                {
                    DebugLogger.LogState($"NAV: filtered unreachable chest at dist={items[i].Distance:F1}");
                    items.RemoveAt(i);
                }
            }

            int unopenedNum = 1;
            int openedNum   = 1;
            for (int i = 0; i < items.Count; i++)
            {
                var  item     = items[i];
                bool isOpened = item.Label == Loc.Get("nav_chest_opened");
                item.Label = isOpened
                    ? Loc.Get("nav_chest_opened_n",   openedNum++)
                    : Loc.Get("nav_chest_unopened_n", unopenedNum++);
                items[i] = item;
                DebugLogger.LogGameValue("NAV:CHEST", $"[{item.Label}] dist={item.Distance:F1}");
            }

            _categories[CAT_CHEST].AddRange(items);
        }

        /// <summary>
        /// Scans for map exits and labels each by icon type and destination.
        /// DOOR = "Building entrance to [dest]", GATE = "Town gate to [dest]".
        /// Destinations resolved via game data (ConstFieldParameter + TextManager).
        /// </summary>
        private void BuildExits(Vector3 playerPos)
        {
            _categories[CAT_EXIT].Clear();

            var found = UnityEngine.Object.FindObjectsOfType<FieldMapjumpCollision>();
            if (found == null) return;

            var items = new List<NavItem>();
            foreach (var exit in found)
            {
                if (exit == null) continue;
                try
                {
                    Vector3    pos      = exit.transform.position;
                    float      dist     = Vector3.Distance(playerPos, pos);
                    string     icon     = exit.iconType.ToString();
                    FieldmapID destId   = exit.fieldmapID;
                    string     destName = ResolveMapName(destId);
                    string     typeLabel = icon == "GATE"
                        ? Loc.Get("nav_exit_gate")
                        : Loc.Get("nav_exit_door");
                    string     label    = Loc.Get("nav_exit_with_dest", typeLabel, destName);

                    items.Add(new NavItem { Label = label, Distance = dist, Position = pos });
                    DebugLogger.LogGameValue("NAV:EXIT",
                        $"[{label}] dest={destId} dist={dist:F1}");
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"NAV:EXIT error: {ex.Message}");
                }
            }

            items.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            // Filter out exits that are unreachable via NavMesh.
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (!IsReachable(playerPos, items[i].Position))
                {
                    DebugLogger.LogState($"NAV: filtered unreachable exit '{items[i].Label}'");
                    items.RemoveAt(i);
                }
            }

            _categories[CAT_EXIT].AddRange(items);
        }

        /// <summary>
        /// Reads quest markers from FieldManager.FieldLocationPointList.
        /// Numbers markers if more than one is present.
        /// </summary>
        private void BuildMarkers(
            Il2CppSystem.Collections.Generic.List<FieldLocationPoint> list,
            Vector3 playerPos)
        {
            _categories[CAT_MARKER].Clear();
            if (list == null) return;

            var items = new List<NavItem>();
            for (int i = 0; i < list.Count; i++)
            {
                var marker = list[i];
                if (marker == null) continue;

                Vector3 pos  = marker.transform.position;
                float   dist = Vector3.Distance(playerPos, pos);
                items.Add(new NavItem
                {
                    Label         = Loc.Get("nav_marker"),
                    Distance      = dist,
                    Position      = pos,
                    LiveTransform = marker.transform,
                });
                DebugLogger.LogGameValue("NAV:MARKER",
                    $"id={marker.locationPointID} dist={dist:F1}");
            }

            items.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            // Filter out markers that are unreachable via NavMesh.
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (!IsReachable(playerPos, items[i].Position))
                {
                    DebugLogger.LogState($"NAV: filtered unreachable marker at dist={items[i].Distance:F1}");
                    items.RemoveAt(i);
                }
            }

            if (items.Count > 1)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var item   = items[i];
                    item.Label = Loc.Get("nav_marker_n", i + 1);
                    items[i]   = item;
                }
            }

            _categories[CAT_MARKER].AddRange(items);
        }

        /// <summary>
        /// Scans for active event triggers (story, private action, sub-event).
        /// Only includes triggers whose conditions are currently satisfied.
        /// </summary>
        private void BuildEvents(Vector3 playerPos)
        {
            _categories[CAT_EVENT].Clear();

            var found = UnityEngine.Object.FindObjectsOfType<FieldEventCollision>();
            if (found == null) return;

            var items = new List<NavItem>();
            foreach (var evt in found)
            {
                if (evt == null) continue;
                try
                {
                    if (!evt.IsEventActivate()) continue;

                    Vector3 pos  = evt.transform.position;
                    float   dist = Vector3.Distance(playerPos, pos);

                    string label;
                    var scenario = evt.GetEnableScenarioEvent();
                    var pa       = evt.GetEnablePrivateActionEvent();
                    var sub      = evt.GetEnableSubEvent();

                    if (scenario != null)
                        label = Loc.Get("nav_event_story");
                    else if (pa != null)
                        label = Loc.Get("nav_event_pa");
                    else if (sub != null)
                        label = Loc.Get("nav_event_side");
                    else
                        label = Loc.Get("nav_event_generic");

                    items.Add(new NavItem
                    {
                        Label         = label,
                        Distance      = dist,
                        Position      = pos,
                        LiveTransform = null,
                    });
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"NAV:EVENT error: {ex.Message}");
                }
            }

            items.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (!IsReachable(playerPos, items[i].Position))
                {
                    DebugLogger.LogState($"NAV: filtered unreachable event at dist={items[i].Distance:F1}");
                    items.RemoveAt(i);
                }
            }

            int storyNum = 1, paNum = 1, sideNum = 1, genericNum = 1;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Label == Loc.Get("nav_event_story"))
                    item.Label = Loc.Get("nav_event_story_n", storyNum++);
                else if (item.Label == Loc.Get("nav_event_pa"))
                    item.Label = Loc.Get("nav_event_pa_n", paNum++);
                else if (item.Label == Loc.Get("nav_event_side"))
                    item.Label = Loc.Get("nav_event_side_n", sideNum++);
                else
                    item.Label = Loc.Get("nav_event_generic_n", genericNum++);
                items[i] = item;
                DebugLogger.LogGameValue("NAV:EVENT", $"[{item.Label}] dist={item.Distance:F1}");
            }

            _categories[CAT_EVENT].AddRange(items);
        }

        /// <summary>
        /// Scans for save points on the current field map.
        /// Labels as "Save point" or "Recovery save point" based on IsRecovery.
        /// Uses FieldManager.FieldSavePointList (game-managed list).
        /// </summary>
        private void BuildSavePoints(
            Il2CppSystem.Collections.Generic.List<FieldSavePoint> list,
            Vector3 playerPos)
        {
            _categories[CAT_SAVE].Clear();
            if (list == null) return;

            var items = new List<NavItem>();
            int saveCount = 0, recoveryCount = 0;

            for (int i = 0; i < list.Count; i++)
            {
                var sp = list[i];
                if (sp == null) continue;

                Vector3 pos  = sp.transform.position;
                float   dist = Vector3.Distance(playerPos, pos);

                bool recovery = false;
                try { recovery = sp.IsRecovery; } catch { }

                string label = recovery
                    ? Loc.Get("nav_save_recovery")
                    : Loc.Get("nav_save");

                if (recovery) recoveryCount++;
                else          saveCount++;

                items.Add(new NavItem
                {
                    Label         = label,
                    Distance      = dist,
                    Position      = pos,
                    LiveTransform = sp.transform,
                });

                DebugLogger.LogGameValue("NAV:SAVE",
                    $"recovery={recovery} dist={dist:F1}");
            }

            items.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (!IsReachable(playerPos, items[i].Position))
                {
                    DebugLogger.LogState($"NAV: filtered unreachable save point at dist={items[i].Distance:F1}");
                    items.RemoveAt(i);
                }
            }

            // Number items if there are multiples of either type.
            if (saveCount > 1 || recoveryCount > 1)
            {
                int sNum = 1, rNum = 1;
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    if (item.Label == Loc.Get("nav_save_recovery"))
                    {
                        if (recoveryCount > 1)
                            item.Label = Loc.Get("nav_save_recovery_n", rNum++);
                    }
                    else
                    {
                        if (saveCount > 1)
                            item.Label = Loc.Get("nav_save_n", sNum++);
                    }
                    items[i] = item;
                }
            }

            _categories[CAT_SAVE].AddRange(items);
        }

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
                                        {
                                            // Try all known MessageTypes
                                            enemyName = tm.GetMessage(
                                                nameKey, TextManager.MessageType.System);
                                            if (string.IsNullOrEmpty(enemyName))
                                                enemyName = tm.GetMessage(
                                                    nameKey, TextManager.MessageType.Skill);
                                            if (string.IsNullOrEmpty(enemyName))
                                                enemyName = tm.GetMessage(
                                                    nameKey, TextManager.MessageType.Item);

                                            // Fallback: parse the key into a readable name
                                            // e.g. "CHARA_LIZARDAXE" → "Lizardaxe"
                                            if (string.IsNullOrEmpty(enemyName))
                                                enemyName = ParseCharaNameID(nameKey);
                                        }
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

            // Sort by distance
            items.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            // Filter unreachable
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (!IsReachable(playerPos, items[i].Position))
                {
                    DebugLogger.LogState(
                        $"NAV: filtered unreachable enemy at dist={items[i].Distance:F1}");
                    items.RemoveAt(i);
                }
            }

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

        /// <summary>
        /// Parses a charaNameID key into a readable enemy name.
        /// e.g. "CHARA_LIZARDAXE" → "Lizardaxe", "CHARA_VOPALBUNNY" → "Vopalbunny"
        /// Strips the "CHARA_" prefix and converts to title case.
        /// </summary>
        private static string ParseCharaNameID(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";

            // Strip common prefixes
            string name = key;
            if (name.StartsWith("CHARA_", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(6);
            else if (name.StartsWith("MON_", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(4);

            if (string.IsNullOrEmpty(name)) return key;

            // Convert: "LIZARDAXE" → "Lizardaxe", "KILLERRABI" → "Killerrabi"
            return char.ToUpper(name[0]) + name.Substring(1).ToLower();
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
