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
            /// <summary>
            /// Reference to the FieldEventCollision for event targets.
            /// Used to call StartEvent() directly when the NavMesh path
            /// ends short of the trigger zone (transform.position bypasses
            /// Unity physics, so OnTriggerEnter never fires).
            /// Null for non-event targets.
            /// </summary>
            public FieldEventCollision EventRef;
            /// <summary>
            /// Collider bounds of the event trigger zone. Used to verify
            /// the player is near the trigger edge before calling StartEvent().
            /// Null if unavailable or for non-event targets.
            /// </summary>
            public Bounds?   TriggerBounds;
            /// <summary>
            /// Optional world position to face on arrival (e.g. water center for
            /// fishing spots). Used instead of LiveTransform for facing when the
            /// target object is off the NavMesh and shouldn't drive distance checks.
            /// </summary>
            public Vector3?  FacePosition;
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
            _categories[CAT_EVENT].Clear(); // PA NPCs go here; BuildEvents appends later

            var npcParams = TryGetNpcParams(mapID);
            DebugLogger.LogState(
                $"NAV: npcParams for map {mapID}: " +
                (npcParams == null ? "null" : $"{npcParams.Count} entries"));

            var found = UnityEngine.Object.FindObjectsOfType<FieldNpcCharacter>();
            if (found == null) return;

            var npcItems = new List<NavItem>();
            var interactItems = new List<NavItem>();
            var paItems = new List<NavItem>();
            foreach (var npc in found)
            {
                if (npc == null) continue;

                // Skip enemies — they have their own category
                if (npc.TryCast<FieldEnemy>() != null) continue;

                Vector3 pos  = npc.transform.position;
                float   dist = Vector3.Distance(playerPos, pos);

                // Skip player character and party members — not interactable on field
                if (npc.TryCast<FieldPlayer>() != null) continue;
                if (npc.TryCast<FieldFollowCharacter>() != null) continue;

                // Skip INVALID-type NPCs — background/decoration NPCs not meant for interaction
                if (npc.NpcType == NpcType.INVALID) continue;

                string label = ResolveNpcName(npc, npcParams, out string codeName);
                bool isCounter = IsFunctionalNpcType(npc.npcType);
                bool isInteractable = IsInteractableNpcType(npc.npcType);

                // Read contactDistance to detect counter NPCs that aren't a
                // functional type (e.g. castle receptionist behind a desk).
                // High contactDistance (>= 1.0) means the game expects interaction
                // from farther away — typical of behind-counter NPCs.
                float contactDist = 0.5f;
                try
                {
                    contactDist = npc.ContactDistance;
                    DebugLogger.LogGameValue("NAV:NPC:CONTACT",
                        $"[{label}] contactDistance={contactDist:F2} codeName={codeName}");
                }
                catch
                {
                    DebugLogger.LogGameValue("NAV:NPC:CONTACT",
                        $"[{label}] contactDistance=READ_ERROR codeName={codeName}");
                }

                if (!isCounter && contactDist >= 1.0f)
                    isCounter = true;

                // Private action NPCs have code names starting with "pa_"
                bool isPrivateAction = codeName != null
                    && codeName.StartsWith("pa_", StringComparison.OrdinalIgnoreCase);

                // For PA NPCs, extract the character name from the code name
                // (e.g. "pa_04_c_001_01_RENA" → "Rena") because the dialogue-
                // derived name may be the wrong character (the first speaker).
                string paLabel = null;
                if (isPrivateAction)
                {
                    string paName = ParseNpcCodeName(codeName);
                    paLabel = string.IsNullOrEmpty(paName)
                        ? Loc.Get("nav_event_pa")
                        : $"{Loc.Get("nav_event_pa")} ({paName})";
                }

                var item = new NavItem
                {
                    Label         = isPrivateAction
                        ? paLabel
                        : label,
                    Distance      = dist,
                    Position      = pos,
                    LiveTransform = npc.transform,
                    IsCounterNpc  = isCounter,
                };

                if (isPrivateAction)
                {
                    DebugLogger.LogGameValue("NAV:EVENT",
                        $"[{item.Label}] (PA NPC) dist={dist:F1} pos={pos}");
                    paItems.Add(item);
                }
                else
                {
                    DebugLogger.LogGameValue(isInteractable ? "NAV:INTERACT" : "NAV:NPC",
                        $"[{label}] type={npc.npcType} dist={dist:F1} pos={pos}");
                    if (isInteractable)
                        interactItems.Add(item);
                    else
                        npcItems.Add(item);
                }
            }

            SortAndFilterUnreachable(npcItems, playerPos);
            SortAndFilterUnreachable(interactItems, playerPos);
            SortAndFilterUnreachable(paItems, playerPos);

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
            _categories[CAT_EVENT].AddRange(paItems);
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
            Il2CppSystem.Collections.Generic.List<ConstNpcParameter> npcParams,
            out string resolvedCodeName)
        {
            resolvedCodeName = null;

            // Always try to resolve the code name via position matching,
            // even if we already know the display name from dialogue.
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
                            resolvedCodeName = param.Name;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"NAV: ResolveNpcName codeName lookup error: {ex.Message}");
                }
            }

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

            if (!string.IsNullOrEmpty(resolvedCodeName))
            {
                // Prefer a real name learned from dialogue (persists across sessions).
                if (DialogueHandler.PersistentNpcNames.TryGetValue(
                        resolvedCodeName, out string persistedName))
                {
                    string qualified = QualifyNpcName(persistedName, category);
                    DebugLogger.LogState(
                        $"NAV: NPC '{resolvedCodeName}' → '{qualified}' (persistent)");
                    return qualified;
                }

                string readable = ParseNpcCodeName(resolvedCodeName);
                if (!string.IsNullOrEmpty(readable))
                {
                    DebugLogger.LogState(
                        $"NAV: NPC '{resolvedCodeName}' → '{readable}'");
                    return readable;
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

                // World map: skip distant chests (they're likely across ocean/mountains).
                if (_isWorldmap && dist > WorldmapChestMaxDistance) continue;

                // Use PascalCase property (IsAcquired) not backing field (isAcquired).
                // IL2CPP backing fields can return stale/wrong values for distant objects.
                bool isOpened = chest.IsAcquired;

                string label = isOpened
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
                bool isOpened = item.Label.StartsWith(Loc.Get("nav_chest_opened"));
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

                // Skip discovered markers. The effectComponent (sparkle) is
                // removed after discovery; IsEnd and isEnd stay false.
                try
                {
                    if (marker.effectComponent == null) continue;
                }
                catch { /* property unavailable — include the marker */ }

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
            // NOTE: do not clear CAT_EVENT here — BuildNpcs may have already
            // added private-action NPCs to this category earlier in the scan.

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

                    Bounds? triggerBounds = null;
                    try
                    {
                        var col = evt.GetComponent<Collider>();
                        if (col != null) triggerBounds = col.bounds;
                    }
                    catch (Exception colEx)
                    {
                        DebugLogger.LogState($"NAV:EVENT collider bounds: {colEx.Message}");
                    }

                    items.Add(new NavItem
                    {
                        Label         = label,
                        Distance      = dist,
                        Position      = pos,
                        LiveTransform = null,
                        EventRef      = evt,
                        TriggerBounds = triggerBounds,
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
        /// Scans for FieldFishingWaterPlace objects and adds them to the
        /// Interactables category. Position is set to the nearest walkable
        /// shore point (NavMesh sample from collider center). LiveTransform
        /// is set to the BoxCollider transform so the arrival code can face
        /// the player toward the water.
        /// </summary>
        private void BuildFishingSpots(Vector3 playerPos)
        {
            var found = UnityEngine.Object.FindObjectsOfType<FieldFishingWaterPlace>();
            if (found == null || found.Length == 0) return;

            var items = new List<NavItem>();
            foreach (var spot in found)
            {
                if (spot == null) continue;

                var col = spot.boxCollider;
                if (col == null) continue;

                Bounds bounds = col.bounds;
                Vector3 center = bounds.center;

                // Walk target: nearest NavMesh point to the collider center.
                // This puts the player at the water's edge (close enough to interact).
                Vector3 walkTarget;
                if (NavMesh.SamplePosition(center, out NavMeshHit hit, 10f, NavMesh.AllAreas))
                    walkTarget = hit.position;
                else
                    walkTarget = spot.transform.position;

                float dist = Vector3.Distance(playerPos, walkTarget);

                DebugLogger.LogGameValue("NAV:FISHING:BUILD",
                    $"center={center} walkTarget={walkTarget} " +
                    $"bounds=({bounds.size.x:F2},{bounds.size.y:F2},{bounds.size.z:F2}) " +
                    $"dist={dist:F1}");

                items.Add(new NavItem
                {
                    Label         = Loc.Get("nav_fishing"),
                    Distance      = dist,
                    Position      = walkTarget,
                    // Face the water center on arrival, but don't track
                    // LiveTransform — the collider center is off NavMesh
                    // and would cause arrival distance to be too large.
                    FacePosition  = center,
                });
            }

            SortAndFilterUnreachable(items, playerPos);

            // Number if multiple fishing spots on the same map.
            if (items.Count > 1)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    item.Label = Loc.Get("nav_fishing_n", i + 1);
                    items[i] = item;
                }
            }

            _categories[CAT_INTERACTABLE].AddRange(items);

            foreach (var item in items)
                DebugLogger.LogGameValue("NAV:FISHING",
                    $"[{item.Label}] dist={item.Distance:F1} pos={item.Position}");
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
        /// <summary>
        /// Builds the Locations category for world map navigation.
        /// Uses the game's ConstWorldmapSymbolParameter database, filtered by
        /// current scenario progress. Resolves display names from locality data.
        /// Matches runtime WorldmapSymbol objects for LiveTransform tracking.
        /// </summary>
        private void BuildWorldmapLocations(Vector3 playerPos, WorldmapID wmID)
        {
            _categories[CAT_LOCATION].Clear();

            if (wmID == WorldmapID.INVALID) return;

            var pm = ParameterManager.Instance;
            if (pm == null) return;

            Il2CppSystem.Collections.Generic.List<ConstWorldmapSymbolParameter> symbols = null;
            try { symbols = pm.GetWorldmapSymbolParameter(wmID); }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV: GetWorldmapSymbolParameter error: {ex.Message}");
                return;
            }

            if (symbols == null || symbols.Count == 0) return;

            int progress = 0;
            try { progress = pm.UserParameter.MainScenarioProgress; }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV: MainScenarioProgress error: {ex.Message}");
            }

            var tm = TextManager.Instance;

            // Collect runtime WorldmapSymbol objects for LiveTransform matching.
            WorldmapSymbol[] runtimeSymbols = null;
            try { runtimeSymbols = UnityEngine.Object.FindObjectsOfType<WorldmapSymbol>(); }
            catch { }

            var items = new List<NavItem>();

            for (int i = 0; i < symbols.Count; i++)
            {
                try
                {
                    var sym = symbols[i];
                    if (sym == null) continue;

                    var iconType = sym.mapIconType;
                    // Only show cities and dungeons as navigable locations.
                    if (iconType != MapIconType.CITY && iconType != MapIconType.DUNGEON)
                        continue;

                    // Filter by scenario progress.
                    int start = sym.StartScenarioProgress;
                    int end   = sym.EndScenarioProgress;
                    if (progress < start || (end > 0 && progress > end))
                        continue;

                    // Resolve display name: localityID → locality parameter → name.
                    string name = null;
                    var localityID = sym.localityID;
                    try
                    {
                        var localityParam = pm.GetLocalityParameter(localityID);
                        if (localityParam != null)
                        {
                            string nameKey = localityParam.localityNameID;
                            if (!string.IsNullOrEmpty(nameKey) && tm != null)
                                name = tm.GetMessage(nameKey, TextManager.MessageType.System);
                        }
                    }
                    catch { }

                    // Fallback: use symbolName if locality resolution failed.
                    if (string.IsNullOrEmpty(name))
                    {
                        name = sym.SymbolName;
                        if (string.IsNullOrEmpty(name))
                            name = $"Location {i}";
                    }

                    // Label: plain name for cities, "(Dungeon)" suffix for dungeons.
                    string label = iconType == MapIconType.DUNGEON
                        ? Loc.Get("nav_location_dungeon", name)
                        : name;

                    // Position from game data (static, not subject to wrapping).
                    Vector3 pos = sym.Position;
                    float dist = Vector3.Distance(playerPos, pos);

                    // Find matching runtime WorldmapSymbol for LiveTransform.
                    Transform liveTransform = null;
                    if (runtimeSymbols != null)
                    {
                        try
                        {
                            foreach (var rs in runtimeSymbols)
                            {
                                if (rs != null && rs.LocalityID == localityID)
                                {
                                    liveTransform = rs.transform;
                                    pos = liveTransform.position;
                                    dist = Vector3.Distance(playerPos, pos);
                                    break;
                                }
                            }
                        }
                        catch { }
                    }

                    items.Add(new NavItem
                    {
                        Label         = label,
                        Distance      = dist,
                        Position      = pos,
                        LiveTransform = liveTransform,
                    });

                    DebugLogger.LogGameValue("NAV:LOCATION",
                        $"[{label}] dist={dist:F0} icon={iconType} " +
                        $"progress=[{start},{end}] locality={localityID}");
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"NAV: BuildWorldmapLocations item {i}: {ex.Message}");
                }
            }

            items.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            _categories[CAT_LOCATION].AddRange(items);
        }

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
                    "keeping them (auto-walk will report unreachable on attempt)");
            }
            else
            {
                // Remove genuinely unreachable items (indices already in descending order).
                foreach (int i in unreachableIndices)
                {
                    DebugLogger.LogState(
                        $"NAV: filtered unreachable '{items[i].Label}' at dist={items[i].Distance:F1}");
                    items.RemoveAt(i);
                }
            }

            // Label items on different floors with "(above)" or "(below)" so the user
            // knows before selecting. Uses the same FloorChangeThreshold as auto-walk.
            LabelFloorDifferences(items, playerPos);
        }

        /// <summary>
        /// Appends "(above)" or "(below)" to item labels when the target is on a
        /// different floor (Y difference exceeds FloorChangeThreshold).
        /// Helps the user understand vertical positioning before attempting auto-walk.
        /// </summary>
        private void LabelFloorDifferences(List<NavItem> items, Vector3 playerPos)
        {
            for (int i = 0; i < items.Count; i++)
            {
                float yDiff = items[i].Position.y - playerPos.y;
                if (Mathf.Abs(yDiff) >= FloorChangeThreshold)
                {
                    var item = items[i];
                    item.Label = Loc.Get(
                        yDiff > 0 ? "nav_label_above" : "nav_label_below",
                        item.Label);
                    items[i] = item;
                }
            }
        }

        #endregion
    }
}
