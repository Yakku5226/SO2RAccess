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
