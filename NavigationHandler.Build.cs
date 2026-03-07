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

            var npcItems = new List<NavItem>();
            var interactItems = new List<NavItem>();
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
                bool isInteractable = IsInteractableNpcType(npc.npcType);
                var item = new NavItem
                {
                    Label         = label,
                    Distance      = dist,
                    Position      = pos,
                    LiveTransform = npc.transform,
                    IsCounterNpc  = isCounter,
                };
                DebugLogger.LogGameValue(isInteractable ? "NAV:INTERACT" : "NAV:NPC",
                    $"[{label}] type={npc.npcType} dist={dist:F1} pos={pos}");

                if (isInteractable)
                    interactItems.Add(item);
                else
                    npcItems.Add(item);
            }

            SortAndFilterUnreachable(npcItems, playerPos);
            SortAndFilterUnreachable(interactItems, playerPos);

            // Number any NPCs that still carry the generic "NPC" label.
            int npcNum = 1;
            for (int i = 0; i < npcItems.Count; i++)
            {
                var item = npcItems[i];
                if (item.Label == "NPC")
                {
                    item.Label = Loc.Get("nav_npc_n", npcNum++);
                    npcItems[i] = item;
                }
            }

            _categories[CAT_NPC].AddRange(npcItems);
            _categories[CAT_INTERACTABLE].AddRange(interactItems);
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

            SortAndFilterUnreachable(items, playerPos);

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

            SortAndFilterUnreachable(items, playerPos);

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

            SortAndFilterUnreachable(items, playerPos);

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
        /// Generic events (no type matched) are dropped — they have no content.
        /// PAs and sub-events with isDisableIcon are skipped (game hides them).
        /// Sub-events are annotated with "(reward)" or "(battle)" hints when applicable.
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

                    var scenario = evt.GetEnableScenarioEvent();
                    var pa       = evt.GetEnablePrivateActionEvent();
                    var sub      = evt.GetEnableSubEvent();

                    // Drop generic events — no script attached, nothing happens
                    if (scenario == null && pa == null && sub == null)
                        continue;

                    // Skip events the game itself marks as hidden
                    if (pa != null && pa.isDisableIcon) continue;
                    if (sub != null && sub.isDisableIcon) continue;

                    Vector3 pos  = evt.transform.position;
                    float   dist = Vector3.Distance(playerPos, pos);

                    string label;
                    if (scenario != null)
                    {
                        label = Loc.Get("nav_event_story");
                    }
                    else if (pa != null)
                    {
                        label = Loc.Get("nav_event_pa");
                    }
                    else
                    {
                        // Sub-event — add hints for reward or battle
                        bool hasReward = sub.treasureID > 0;
                        bool hasBattle = sub.enemyPartyID > 0;
                        if (hasReward && hasBattle)
                            label = Loc.Get("nav_event_side_reward_battle");
                        else if (hasReward)
                            label = Loc.Get("nav_event_side_reward");
                        else if (hasBattle)
                            label = Loc.Get("nav_event_side_battle");
                        else
                            label = Loc.Get("nav_event_side");
                    }

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

            SortAndFilterUnreachable(items, playerPos);

            // Number duplicates within each label type
            var counts = new Dictionary<string, int>();
            var totals = new Dictionary<string, int>();
            foreach (var item in items)
            {
                if (!totals.ContainsKey(item.Label))
                    totals[item.Label] = 0;
                totals[item.Label]++;
            }

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (totals[item.Label] > 1)
                {
                    if (!counts.ContainsKey(item.Label))
                        counts[item.Label] = 0;
                    counts[item.Label]++;
                    item.Label = $"{item.Label} {counts[item.Label]}";
                }
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
                try { recovery = sp.IsRecovery; }
                catch (Exception ex) { DebugLogger.LogState($"NAV BuildSavePoints: IsRecovery error: {ex.Message}"); }

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

            SortAndFilterUnreachable(items, playerPos);

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
                                                enemyName = TextUtil.ParseCharaNameID(nameKey);
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

        /// <summary>
        /// Scans for stairs on the current field map.
        /// Labels as "Stairs up" or "Stairs down" based on isUpperStage.
        /// Uses FieldManager.FieldStairsList (game-managed list).
        /// </summary>
        private void BuildStairs(
            Il2CppSystem.Collections.Generic.List<FieldStairs> list,
            Vector3 playerPos)
        {
            _categories[CAT_STAIRS].Clear();
            if (list == null) return;

            var items = new List<NavItem>();
            int upCount = 0, downCount = 0;

            for (int i = 0; i < list.Count; i++)
            {
                var stairs = list[i];
                if (stairs == null) continue;

                Vector3 pos  = stairs.transform.position;
                float   dist = Vector3.Distance(playerPos, pos);

                bool isUp = false;
                try { isUp = stairs.isUpperStage; }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"NAV BuildStairs: isUpperStage error: {ex.Message}");
                }

                string label = isUp
                    ? Loc.Get("nav_stairs_up")
                    : Loc.Get("nav_stairs_down");

                if (isUp) upCount++; else downCount++;

                items.Add(new NavItem
                {
                    Label         = label,
                    Distance      = dist,
                    Position      = pos,
                    LiveTransform = null,
                });

                DebugLogger.LogGameValue("NAV:STAIRS",
                    $"isUp={isUp} dist={dist:F1}");
            }

            SortAndFilterUnreachable(items, playerPos);

            if (upCount > 1 || downCount > 1)
            {
                int uNum = 1, dNum = 1;
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    if (item.Label == Loc.Get("nav_stairs_up"))
                    {
                        if (upCount > 1)
                            item.Label = Loc.Get("nav_stairs_up_n", uNum++);
                    }
                    else
                    {
                        if (downCount > 1)
                            item.Label = Loc.Get("nav_stairs_down_n", dNum++);
                    }
                    items[i] = item;
                }
            }

            _categories[CAT_STAIRS].AddRange(items);
        }

        /// <summary>
        /// Scans for stone doors on the current field map.
        /// Only includes doors with seType == StoneDoor.
        /// Labels as "Stone door, open" or "Stone door, closed" based on doorState.
        /// Uses FieldManager.FieldDoorList (game-managed list).
        /// </summary>
        private void BuildDoors(
            Il2CppSystem.Collections.Generic.List<FieldDoor> list,
            Vector3 playerPos)
        {
            _categories[CAT_DOOR].Clear();
            if (list == null) return;

            var items = new List<NavItem>();
            int openCount = 0, closedCount = 0;

            for (int i = 0; i < list.Count; i++)
            {
                var door = list[i];
                if (door == null) continue;

                try
                {
                    if (door.seType != FieldDoor.DoorSeType.StoneDoor) continue;
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"NAV BuildDoors: seType error: {ex.Message}");
                    continue;
                }

                Vector3 pos  = door.transform.position;
                float   dist = Vector3.Distance(playerPos, pos);

                bool isOpen = false;
                try { isOpen = door.doorState == FieldDoor.State.Open; }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"NAV BuildDoors: doorState error: {ex.Message}");
                }

                string label = isOpen
                    ? Loc.Get("nav_door_stone_open")
                    : Loc.Get("nav_door_stone_closed");

                if (isOpen) openCount++; else closedCount++;

                items.Add(new NavItem
                {
                    Label         = label,
                    Distance      = dist,
                    Position      = pos,
                    LiveTransform = null,
                });

                DebugLogger.LogGameValue("NAV:DOOR",
                    $"isOpen={isOpen} dist={dist:F1}");
            }

            SortAndFilterUnreachable(items, playerPos);

            if (openCount > 1 || closedCount > 1)
            {
                int oNum = 1, cNum = 1;
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    if (item.Label == Loc.Get("nav_door_stone_open"))
                    {
                        if (openCount > 1)
                            item.Label = Loc.Get("nav_door_stone_open_n", oNum++);
                    }
                    else
                    {
                        if (closedCount > 1)
                            item.Label = Loc.Get("nav_door_stone_closed_n", cNum++);
                    }
                    items[i] = item;
                }
            }

            _categories[CAT_DOOR].AddRange(items);
        }

        /// <summary>
        /// Scans for warp-related gimmicks: warp panels (Gimmick09), magic circles
        /// (Gimmick17), and moving platforms (Gimmick03). Iterates
        /// FieldGimmickManager.FieldGimmickList and uses TryCast to identify types.
        /// </summary>
        private void BuildWarpPoints(FieldManager fm, Vector3 playerPos)
        {
            _categories[CAT_WARP].Clear();

            try
            {
                var gimmickMgr = fm.FieldGimmickManager;
                if (gimmickMgr == null) return;

                var gimmickList = gimmickMgr.FieldGimmickList;
                if (gimmickList == null) return;

                var items = new List<NavItem>();
                int panelCount = 0, circleCount = 0, platformCount = 0;

                for (int i = 0; i < gimmickList.Count; i++)
                {
                    var gimmick = gimmickList[i];
                    if (gimmick == null) continue;

                    var panel = gimmick.TryCast<FieldGimmick09>();
                    if (panel != null)
                    {
                        Vector3 pos  = panel.transform.position;
                        float   dist = Vector3.Distance(playerPos, pos);
                        panelCount++;

                        items.Add(new NavItem
                        {
                            Label         = Loc.Get("nav_warp_panel"),
                            Distance      = dist,
                            Position      = pos,
                            LiveTransform = null,
                        });

                        DebugLogger.LogGameValue("NAV:WARP",
                            $"panel dist={dist:F1}");
                        continue;
                    }

                    var circle = gimmick.TryCast<FieldGimmick17>();
                    if (circle != null)
                    {
                        try
                        {
                            if (!circle.IsEnable()) continue;
                            if (circle.isDisableWarp) continue;
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.LogState(
                                $"NAV BuildWarpPoints: circle filter error: {ex.Message}");
                            continue;
                        }

                        Vector3 pos  = circle.transform.position;
                        float   dist = Vector3.Distance(playerPos, pos);
                        circleCount++;

                        items.Add(new NavItem
                        {
                            Label         = Loc.Get("nav_warp_circle"),
                            Distance      = dist,
                            Position      = pos,
                            LiveTransform = null,
                        });

                        DebugLogger.LogGameValue("NAV:WARP",
                            $"circle dist={dist:F1}");
                        continue;
                    }

                    var platform = gimmick.TryCast<FieldGimmick03>();
                    if (platform != null)
                    {
                        Vector3 pos  = platform.transform.position;
                        float   dist = Vector3.Distance(playerPos, pos);
                        platformCount++;

                        items.Add(new NavItem
                        {
                            Label         = Loc.Get("nav_warp_platform"),
                            Distance      = dist,
                            Position      = pos,
                            LiveTransform = null,
                        });

                        DebugLogger.LogGameValue("NAV:WARP",
                            $"platform dist={dist:F1}");
                        continue;
                    }
                }

                SortAndFilterUnreachable(items, playerPos);

                if (panelCount > 1 || circleCount > 1 || platformCount > 1)
                {
                    int pNum = 1, cNum = 1, plNum = 1;
                    for (int i = 0; i < items.Count; i++)
                    {
                        var item = items[i];
                        if (item.Label == Loc.Get("nav_warp_panel"))
                        {
                            if (panelCount > 1)
                                item.Label = Loc.Get("nav_warp_panel_n", pNum++);
                        }
                        else if (item.Label == Loc.Get("nav_warp_circle"))
                        {
                            if (circleCount > 1)
                                item.Label = Loc.Get("nav_warp_circle_n", cNum++);
                        }
                        else if (item.Label == Loc.Get("nav_warp_platform"))
                        {
                            if (platformCount > 1)
                                item.Label = Loc.Get("nav_warp_platform_n", plNum++);
                        }
                        items[i] = item;
                    }
                }

                _categories[CAT_WARP].AddRange(items);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV BuildWarpPoints error: {ex.Message}");
            }
        }

        /// <summary>
        /// Sorts items by distance and removes those unreachable via NavMesh.
        /// Items with IsCounterNpc=true skip the reachability check (they are
        /// behind counters but the game still allows interaction).
        /// If ALL items would be filtered out, the NavMesh is likely broken at the
        /// player's position (disconnected island / gap). In that case, keep
        /// everything — showing extra items is better than showing nothing.
        /// </summary>
        private void SortAndFilterUnreachable(List<NavItem> items, Vector3 playerPos)
        {
            items.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            var unreachableIndices = new List<int>();
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (items[i].IsCounterNpc) continue;
                if (!IsReachable(playerPos, items[i].Position))
                    unreachableIndices.Add(i);
            }

            // If every non-counter item would be removed, the player is likely on a
            // disconnected NavMesh fragment — skip filtering entirely.
            int nonCounterCount = 0;
            for (int i = 0; i < items.Count; i++)
                if (!items[i].IsCounterNpc) nonCounterCount++;

            if (unreachableIndices.Count > 0 && unreachableIndices.Count >= nonCounterCount)
            {
                DebugLogger.LogState(
                    $"NAV: all {unreachableIndices.Count} non-counter items unreachable — " +
                    "NavMesh gap suspected, skipping reachability filter");
                return;
            }

            // Remove genuinely unreachable items (indices already in descending order).
            foreach (int i in unreachableIndices)
            {
                DebugLogger.LogState(
                    $"NAV: filtered unreachable '{items[i].Label}' at dist={items[i].Distance:F1}");
                items.RemoveAt(i);
            }
        }

        #endregion
    }
}
