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
            /// <summary>
            /// True for targets that are still listed but no longer worth a beacon:
            /// an opened chest. Set by the builder so beacon code never parses labels.
            /// </summary>
            public bool      Consumed;
            /// <summary>
            /// What the target is when it is an interactable (fishing spot, gathering
            /// point, switch…); None for everything else. Set by the builder so the
            /// beacon code and the arrival logic never parse labels.
            /// </summary>
            public InteractableKind Kind;
            /// <summary>True for fishing spots on any map.</summary>
            public bool      IsFishing => Kind == InteractableKind.Fishing;
            /// <summary>True for world map dungeon symbols; cities are the other Location kind.</summary>
            public bool      IsDungeon;
            /// <summary>
            /// True when a PROVEN region lookup says the target cannot be reached in
            /// the current travel mode (world map fishing stands). The builder
            /// annotates the label; the item stays listed.
            /// </summary>
            public bool      Unreachable;
            /// <summary>
            /// World map fishing only: the lake's nearest bake-PROVEN stand, set when
            /// it differs from the listed nearest-shore stand. The route planner
            /// retargets to it once, silently, when the nearest stand's route is
            /// refused by the body sweep (user decision 2026-09-09).
            /// </summary>
            public Vector3?  FishingFallback;
            /// <summary>Water point to face at <see cref="FishingFallback"/>.</summary>
            public Vector3?  FishingFallbackFace;
            /// <summary>World map fishing only: the listed stand's proof swept nothing (see <see cref="FishingStandEntry.GridOnlyProof"/>).</summary>
            public bool      FishingGridOnlyProof;
            /// <summary>World map fishing only: the same for <see cref="FishingFallback"/>.</summary>
            public bool      FishingFallbackGridOnly;
            /// <summary>
            /// Identity for stable numbering: something that names the same
            /// thing after the field is rebuilt (chests use their save flag,
            /// fishing spots their water place ID). Null = the resting position
            /// rounded to half a metre is used. Never a Unity object or instance
            /// ID: a battle reloads the field and every object comes back with a
            /// new ID, which used to renumber every chest after every fight.
            /// </summary>
            public string Identity;
        }

        #endregion

        #region Private — Build

        /// <summary>
        /// Scans for treasure chests and labels each by opened/unopened status.
        /// All chests on a map share ONE number sequence, handed out in distance
        /// order the first time each chest is seen: opening "Unopened chest 3"
        /// turns it into "Opened chest 3" and no other chest is ever called 3.
        /// (Separate opened/unopened sequences used to collide the moment a
        /// chest changed state — two "Opened chest 1" in the same list.)
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

                // The save flag that records "opened" is unique per chest and
                // survives the field being rebuilt after a battle. 0 = no flag
                // (some scripted chests): fall back to the resting position.
                int flag = chest.Flag;

                items.Add(new NavItem
                {
                    Label         = isOpened
                        ? Loc.Get("nav_chest_opened")
                        : Loc.Get("nav_chest_unopened"),
                    Distance      = dist,
                    Position      = pos,
                    LiveTransform = chest.transform,
                    Consumed      = isOpened,
                    Identity      = flag > 0 ? "chest:" + flag : null,
                });
            }

            // Number first, then mark the ones without a path: the "no path" and
            // floor suffixes wrap the numbered name.
            items.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            for (int i = 0; i < items.Count; i++)
            {
                var item      = items[i];
                int stableNum = GetStableNumber("chests", item);

                item.Label = item.Consumed
                    ? Loc.Get("nav_chest_opened_n",   stableNum)
                    : Loc.Get("nav_chest_unopened_n", stableNum);
                items[i] = item;
            }

            SortAndFilterUnreachable(items, playerPos, keepUnreachable: true);

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                DebugLogger.LogGameValue("NAV:CHEST",
                    $"[{item.Label}] dist={item.Distance:F1} id={item.Identity ?? PositionKey(item.Position)}");
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
            if (found == null)
            {
                DebugLogger.LogState("NAV:EXITDIAG: FindObjectsOfType<FieldMapjumpCollision> returned null.");
                return;
            }

            DebugLogger.LogState($"NAV:EXITDIAG: {found.Length} FieldMapjumpCollision in scene.");

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

                    // DIAGNOSTIC (debug-only): log BOTH icon fields raw. The red "!"
                    // story marker is MapIconType.SCENARIO_EVENT and may ride on the
                    // exit's subIconType (overlaid on a GATE/DOOR) — which the label
                    // logic above ignores. This reveals whether a story-objective exit
                    // is being shown as a plain entrance.
                    string subIcon;
                    try { subIcon = exit.subIconType.ToString(); }
                    catch { subIcon = "?"; }
                    DebugLogger.LogGameValue("NAV:EXIT",
                        $"[{label}] dest={destId} dist={dist:F1} "
                        + $"iconType={icon} subIconType={subIcon}");
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

            // DIAGNOSTIC (debug-only): dump every location point defined for this
            // map from game data, with world position and discovered flag. This is
            // independent of whether the live sparkle object currently exists, so it
            // reveals points the live list omits (e.g. far ones) and recovers their
            // coordinates. See IsLocationPointDiscovered for why effectComponent is
            // NOT a reliable "discovered" signal.
            LogLocationPointDiagnostics(playerPos);

            if (list == null) return;

            var items = new List<NavItem>();
            for (int i = 0; i < list.Count; i++)
            {
                var marker = list[i];
                if (marker == null) continue;

                // The sparkle (effectComponent) is distance-gated: the game only
                // spawns it within the point's visibleDistance, so a null sparkle
                // does NOT mean "discovered" — it also happens when the player is
                // simply too far away. The reliable discovered signal is the
                // persistent released flag (IsLocationPointDiscovered).
                bool hasSparkle;
                try { hasSparkle = marker.effectComponent != null; }
                catch { hasSparkle = true; }
                bool discovered = IsLocationPointDiscovered(marker.locationPointID);

                Vector3 pos  = marker.transform.position;
                float   dist = Vector3.Distance(playerPos, pos);

                DebugLogger.LogGameValue("NAV:MARKER:LIVE",
                    $"id={marker.locationPointID} dist={dist:F1} " +
                    $"sparkle={hasSparkle} discovered={discovered}");

                // Hide only points the player has ALREADY discovered (persistent
                // released flag). Do NOT gate on the sparkle: it is distance-gated,
                // so a far undiscovered point (e.g. the Old Lighthouse, ~65 m away
                // and ~12 m up a tower) has no sparkle yet but must still be listed.
                // Confirmed via NAV:MARKER:LIVE log: sparkle=False / discovered=False
                // at the town entrance was wrongly filtered out before this change.
                if (discovered) continue;

                items.Add(new NavItem
                {
                    Label         = Loc.Get("nav_marker"),
                    Distance      = dist,
                    Position      = pos,
                    LiveTransform = marker.transform,
                });
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
        /// True if the given location point has already been discovered by the
        /// player. Reads the persistent "released" flag from save data
        /// (<see cref="UserParameter.GetReleasedLocationPointFlag"/>) — the reliable
        /// discovered-state source. NOTE: the sparkle (effectComponent) is NOT a
        /// reliable discovered signal because it is distance-gated (only spawned
        /// within the point's visibleDistance), so a distant undiscovered point and
        /// an already-discovered point both report a null sparkle.
        /// Returns false (treat as undiscovered) if the data is unavailable.
        /// </summary>
        private bool IsLocationPointDiscovered(LocationPointID id)
        {
            try
            {
                var user = ParameterManager.Instance?.UserParameter;
                if (user == null) return false;
                return user.GetReleasedLocationPointFlag(id);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState(
                    $"NAV: GetReleasedLocationPointFlag failed for {id}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// DIAGNOSTIC (debug-only): logs every location point defined for the
        /// current field map from game data — ID, name key, world position,
        /// distance from the player, and whether it has already been discovered.
        /// Sourced from <see cref="ParameterManager.GetLocationPointParameterList"/>,
        /// which is complete and position-bearing regardless of how far away the
        /// player is — unlike the live sparkle list. Used to diagnose why a
        /// discoverable location (e.g. the Old Lighthouse) does or does not appear
        /// in the navigation list, and to recover its coordinates.
        /// </summary>
        private void LogLocationPointDiagnostics(Vector3 playerPos)
        {
            try
            {
                var fm = FieldManager.Instance;
                var pm = ParameterManager.Instance;
                if (fm == null || pm == null)
                {
                    DebugLogger.LogState(
                        "NAV:LOCDIAG: FieldManager/ParameterManager unavailable.");
                    return;
                }

                FieldmapID mapID = fm.currentFieldmapID;
                var paramList = pm.GetLocationPointParameterList(mapID);
                if (paramList == null)
                {
                    DebugLogger.LogState(
                        $"NAV:LOCDIAG: map {mapID} defines no location points.");
                    return;
                }

                var user = pm.UserParameter;
                DebugLogger.LogState(
                    $"NAV:LOCDIAG: map={mapID} defines {paramList.Count} location " +
                    $"point(s). player=({playerPos.x:F1},{playerPos.y:F1},{playerPos.z:F1})");

                for (int i = 0; i < paramList.Count; i++)
                {
                    var p = paramList[i];
                    if (p == null) continue;

                    Vector3 pos  = p.position;
                    float   dist = Vector3.Distance(playerPos, pos);
                    bool discovered = false;
                    try
                    {
                        if (user != null)
                            discovered = user.GetReleasedLocationPointFlag(p.locationPointID);
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.LogState(
                            $"NAV:LOCDIAG: released-flag read failed for " +
                            $"{p.locationPointID}: {ex.Message}");
                    }

                    DebugLogger.LogState(
                        $"NAV:LOCDIAG: [{p.locationPointID}] nameID='{p.locationNameID}' " +
                        $"pos=({pos.x:F1},{pos.y:F1},{pos.z:F1}) dist={dist:F1} " +
                        $"discovered={discovered}");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV:LOCDIAG error: {ex.Message}");
            }
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
            if (found == null)
            {
                DebugLogger.LogState("NAV:EVENTDIAG: FindObjectsOfType<FieldEventCollision> returned null.");
                return;
            }

            // DIAGNOSTIC (debug-only): log how many event-collision triggers exist and
            // why each is included or dropped. Without this only SURVIVING events were
            // logged, so a story-event marker that fails a filter was invisible in the
            // log (could not tell "no trigger present" from "trigger rejected").
            DebugLogger.LogState($"NAV:EVENTDIAG: {found.Length} FieldEventCollision in scene.");

            var items = new List<NavItem>();
            foreach (var evt in found)
            {
                if (evt == null) continue;
                try
                {
                    Vector3 evtPos  = evt.transform.position;
                    float   evtDist = Vector3.Distance(playerPos, evtPos);

                    if (!evt.IsEventActivate())
                    {
                        DebugLogger.LogState(
                            $"NAV:EVENTDIAG: drop '{evt.name}' dist={evtDist:F1} — IsEventActivate=false.");
                        continue;
                    }

                    var scenario = evt.GetEnableScenarioEvent();
                    var pa       = evt.GetEnablePrivateActionEvent();
                    var sub      = evt.GetEnableSubEvent();

                    // Drop generic events — no script attached, nothing happens
                    if (scenario == null && pa == null && sub == null)
                    {
                        DebugLogger.LogState(
                            $"NAV:EVENTDIAG: drop '{evt.name}' dist={evtDist:F1} — "
                            + "no scenario/PA/sub event enabled (generic).");
                        continue;
                    }

                    // Skip events the game itself marks as hidden
                    if (pa != null && pa.isDisableIcon)
                    {
                        DebugLogger.LogState(
                            $"NAV:EVENTDIAG: drop '{evt.name}' dist={evtDist:F1} — PA isDisableIcon.");
                        continue;
                    }
                    if (sub != null && sub.isDisableIcon)
                    {
                        DebugLogger.LogState(
                            $"NAV:EVENTDIAG: drop '{evt.name}' dist={evtDist:F1} — sub isDisableIcon.");
                        continue;
                    }

                    DebugLogger.LogState(
                        $"NAV:EVENTDIAG: keep '{evt.name}' dist={evtDist:F1} "
                        + $"scenario={scenario != null} pa={pa != null} sub={sub != null} "
                        + "(pre-reachability).");

                    Vector3 pos  = evtPos;
                    float   dist = evtDist;

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
                DebugLogger.LogGameValue("NAV:EVENT",
                    $"[{item.Label}] dist={item.Distance:F1} " +
                    $"pos=({item.Position.x:F1},{item.Position.y:F1},{item.Position.z:F1})");
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
        /// Builds the fishing spot entries of the Interactables category.
        /// Fields scan live FieldFishingWaterPlace objects and drop unreachable
        /// ones; the world map has NONE (its spots are painted into the game's
        /// native world grid data), so spots come from the baked stands of the
        /// ConstFishingWaterPlaceParameter database there, and a proven-unreachable
        /// stand stays listed with the same per-mode suffix towns get. Both paths
        /// share numbering and the stand/face-water arrival contract.
        /// </summary>
        private void BuildFishingSpots(Vector3 playerPos)
        {
            var items = _isWorldmap
                ? CollectWorldmapFishingSpots(playerPos)
                : CollectFieldFishingSpots(playerPos);
            if (items.Count == 0) return;

            if (_isWorldmap)
                items.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            else
                SortAndFilterUnreachable(items, playerPos);

            // Mark them for the beacon system, number them if there are several,
            // then annotate the proven-unreachable ones (world map only).
            bool bunny = _isWorldmap && WorldmapTravel.CurrentMode() == WorldmapTravelMode.Bunny;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                item.Kind = InteractableKind.Fishing;
                if (items.Count > 1)
                {
                    item.Label = Loc.Get("nav_fishing_n", GetStableNumber("fishing", item));
                }
                if (item.Unreachable)
                    item.Label = Loc.Get(bunny ? "nav_wm_unreachable_bunny" : "nav_wm_unreachable_foot", item.Label);
                items[i] = item;
            }

            _categories[CAT_INTERACTABLE].AddRange(items);

            foreach (var item in items)
                DebugLogger.LogGameValue("NAV:FISHING",
                    $"[{item.Label}] dist={item.Distance:F1} pos={item.Position}");
        }

        /// <summary>
        /// Collects field-map fishing spots from live FieldFishingWaterPlace
        /// objects. Walk target is the nearest NavMesh point to the collider
        /// center (the water's edge); the center itself becomes the
        /// face-on-arrival point.
        /// </summary>
        private List<NavItem> CollectFieldFishingSpots(Vector3 playerPos)
        {
            var items = new List<NavItem>();

            var found = UnityEngine.Object.FindObjectsOfType<FieldFishingWaterPlace>();
            if (found == null) return items;

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
                    // No identity: the water place's resting position names it.
                });
            }

            return items;
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
        /// Sorts items by distance and removes those unreachable via NavMesh.
        /// Items with IsCounterNpc=true skip the reachability check (they are
        /// behind counters but the game still allows interaction).
        /// If ALL items would be filtered out, the NavMesh is likely broken at the
        /// player's position (disconnected island / gap). In that case, keep
        /// everything — showing extra items is better than showing nothing.
        /// With <paramref name="keepUnreachable"/> the unreachable items stay
        /// listed, marked <see cref="NavItem.Unreachable"/> and labelled
        /// "…, no path" (chests, pickup points, contact points: a first visit to a
        /// dungeon floor the NavMesh does not connect is walked by hand, and the
        /// beacon and spoken directions need the target to exist for that).
        /// </summary>
        private void SortAndFilterUnreachable(List<NavItem> items, Vector3 playerPos,
            bool keepUnreachable = false)
        {
            items.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            // Reachability is decided by IsReachable() (complete NavMesh path,
            // else a recorded traversal route).
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

            if (keepUnreachable)
            {
                foreach (int i in unreachableIndices)
                {
                    var item = items[i];
                    DebugLogger.LogState(
                        $"NAV: no path to '{item.Label}' at dist={item.Distance:F1} " +
                        $"pos=({item.Position.x:F1},{item.Position.y:F1},{item.Position.z:F1}) — kept, marked.");
                    item.Unreachable = true;
                    item.Label       = Loc.Get("nav_label_nopath", item.Label);
                    items[i] = item;
                }
            }
            else if (unreachableIndices.Count > 0 && unreachableIndices.Count >= nonCounterCount)
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
                        $"NAV: filtered unreachable '{items[i].Label}' at dist={items[i].Distance:F1} " +
                        $"pos=({items[i].Position.x:F1},{items[i].Position.y:F1},{items[i].Position.z:F1})");
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

        /// <summary>
        /// Gets or assigns the stable number of an item within a numbering group
        /// ("chests", "fishing", "gather" — one sequence each, so a category that
        /// holds two kinds of things does not interleave their numbers).
        /// First time an identity is seen on this map: the next FREE number
        /// (one more than the numbers already handed out, never a loop counter
        /// that could repeat a number already in use). Afterwards: the same
        /// number, for as long as the map stays loaded — through battles, from
        /// every side of the map, whatever the item's state. The map change
        /// reset lives in the list builder. Same design as the Eiyuden mod.
        /// </summary>
        private int GetStableNumber(string group, NavItem item)
        {
            string identity = item.Identity ?? PositionKey(item.Position);
            if (!_stableNumbers.TryGetValue(group, out var numbers))
            {
                numbers = new Dictionary<string, int>();
                _stableNumbers[group] = numbers;
            }

            if (!numbers.TryGetValue(identity, out int number))
            {
                number = numbers.Count + 1;
                numbers[identity] = number;
                DebugLogger.LogState(
                    $"NAV numbering: {group} '{identity}' = {number} ({numbers.Count} known on this map)");
            }
            return number;
        }

        /// <summary>
        /// Position rounded to half a metre: the identity of anything that does
        /// not move, stable across the field being rebuilt after a battle.
        /// </summary>
        private static string PositionKey(Vector3 p) =>
            $"{Mathf.RoundToInt(p.x * 2f)},{Mathf.RoundToInt(p.y * 2f)},{Mathf.RoundToInt(p.z * 2f)}";

        #endregion
    }
}
