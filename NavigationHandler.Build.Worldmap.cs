using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SO2RAccess
{
    // Partial class fragment of NavigationHandler.Build: World-map location scanning (BuildWorldmapLocations + mapjump diagnostic).
    public partial class NavigationHandler
    {
        #region Private — Build
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

            // Per-travel-mode reachability inputs, resolved ONCE per list
            // build: current mode, the player's start-region SET in that
            // mode (every region the pathfinder's start-clearing disc can
            // bridge onto — not just the exact cell), and the
            // entrance-trigger cache (one scene scan). Verdicts only
            // ANNOTATE items ("unreachable on foot / by bunny") — nothing
            // is hidden, and the walk attempt with its honest refusal
            // messages is never blocked.
            var travelMode = WorldmapTravel.CurrentMode();
            var playerRegions = new List<int>();
            WorldmapPathfinder.GetStartRegionIds(
                playerPos, travelMode, playerRegions);
            RefreshWmMapjumpCache();

            var items = new List<NavItem>();

            for (int i = 0; i < symbols.Count; i++)
            {
                try
                {
                    var sym = symbols[i];
                    if (sym == null) continue;

                    var iconType = sym.mapIconType;
                    int start = sym.StartScenarioProgress;
                    int end   = sym.EndScenarioProgress;
                    bool inProgressWindow =
                        progress >= start && (end <= 0 || progress <= end);

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

                    // Only cities and dungeons become navigable list items.
                    // EVERY symbol goes to the debug log first — the survey
                    // that tells us which icon types the data actually
                    // contains, so list coverage is decided from evidence.
                    bool listed = iconType == MapIconType.CITY
                        || iconType == MapIconType.DUNGEON;
                    DebugLogger.LogGameValue("NAV:WM:SYMBOL",
                        $"[{name}] icon={iconType} progress=[{start},{end}] " +
                        (listed
                            ? (inProgressWindow ? "LISTED" : "skip: progress window")
                            : "skip: icon type not listed"));
                    if (!listed || !inProgressWindow)
                        continue;

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

                    // Honest per-mode reachability annotation. Only a PROVEN
                    // disconnection annotates; every unknown stays plain
                    // (treated as reachable). Every verdict is logged with
                    // its reason so false annotations are diagnosable.
                    var verdict = ResolveLocationReachability(
                        pos, playerPos, playerRegions, travelMode,
                        out string reachReason);
                    if (verdict == WmReachability.Unreachable)
                    {
                        label = Loc.Get(
                            travelMode == WorldmapTravelMode.Bunny
                                ? "nav_wm_unreachable_bunny"
                                : "nav_wm_unreachable_foot",
                            label);
                    }
                    MelonLoader.MelonLogger.Msg(
                        $"[WMReach] {name}: {verdict} ({travelMode}) — " +
                        $"{reachReason}");

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

        /// <summary>
        /// Collects world-map fishing spots from the game's
        /// ConstFishingWaterPlaceParameter database. The world map has NO
        /// FieldFishingWaterPlace objects — its spots are painted into the
        /// native world grid (proven by the 2026-07-11 survey log:
        /// FieldFishingWaterPlaceList is empty there), so the parameter
        /// database is the only truthful source. Walk target AND list
        /// distance = the nearest walkable shore cell on the PLAYER's side
        /// of the water (falling back to the center snap + water-edge
        /// distance when no same-side shore exists); the center is the
        /// face-on-arrival point.
        /// </summary>
        private List<NavItem> CollectWorldmapFishingSpots(Vector3 playerPos)
        {
            var items = new List<NavItem>();

            var pm = ParameterManager.Instance;
            var fm = FieldManager.Instance;
            if (pm == null || fm == null) return items;

            Il2CppSystem.Collections.Generic.List<ConstFishingWaterPlaceParameter> spots = null;
            try { spots = pm.GetFishingWaterPlaceParameterList(fm.currentFieldmapID); }
            catch (Exception ex)
            {
                DebugLogger.LogState(
                    $"NAV: GetFishingWaterPlaceParameterList error: {ex.Message}");
                return items;
            }

            if (spots == null || spots.Count == 0)
            {
                DebugLogger.LogState(
                    $"NAV: no fishing water place parameters for " +
                    $"fieldmap {fm.currentFieldmapID}.");
                // Survey fallback: dump the whole database once so a wrong
                // map-ID assumption shows up as evidence, not silence.
                if (Main.DebugMode)
                {
                    try
                    {
                        var all = pm.GetFishingWaterPlaceParameterList();
                        if (all != null)
                        {
                            for (int i = 0; i < all.Count; i++)
                            {
                                var s = all[i];
                                if (s == null) continue;
                                var p = s.Position;
                                DebugLogger.LogGameValue("NAV:FISHING:DB",
                                    $"id={s.WaterPlaceID} map={s.FieldmapID} " +
                                    $"pos=({p.x:F0},{p.y:F0},{p.z:F0}) " +
                                    $"placement={s.IsPlacementFishingSpot}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.LogState(
                            $"NAV: fishing DB dump error: {ex.Message}");
                    }
                }
                return items;
            }

            var mode = WorldmapTravel.CurrentMode();

            // Player's connected regions, for keeping the reachability
            // filter honest on rivers (see the far-bank re-pick below).
            // Empty = unknown → no region checks (fail open).
            var startRegions = new List<int>();
            WorldmapPathfinder.GetStartRegionIds(playerPos, mode, startRegions);

            for (int i = 0; i < spots.Count; i++)
            {
                try
                {
                    var spot = spots[i];
                    if (spot == null) continue;

                    Vector3 center = spot.Position;
                    var size = spot.Size;
                    var waterBox = new Bounds(center, size);

                    // Shore point: nearest grid cell walkable in the current
                    // travel mode. If even the ~50m snap finds nothing, keep
                    // the center — the walk attempt will refuse honestly.
                    if (!WorldmapPathfinder.TryGetNearestWalkableWorld(
                            center, mode, out Vector3 walkTarget))
                        walkTarget = center;

                    // Target + distance: the nearest walkable SHORE CELL on
                    // the player's side of the water — where the walk will
                    // actually end, so the only honest number. The box-edge
                    // metric read "0 meters" while standing inside these
                    // land-spanning AABBs (Krosse exit), and the center-snap
                    // fallback read "40 meters" while standing AT the fishable
                    // stand (both proven 2026-08-29). This also subsumes the
                    // far-bank rescue: a center snap on the wrong river bank
                    // is replaced by a same-side cell, keeping the
                    // reachability filter honest.
                    bool playerSideShore = false;
                    if (startRegions.Count > 0 &&
                        TryFindShoreOnPlayerSide(waterBox, mode,
                            startRegions, playerPos, out Vector3 nearShore))
                    {
                        playerSideShore = true;
                        walkTarget = nearShore;
                    }
                    else if (startRegions.Count > 0)
                    {
                        DebugLogger.LogState(
                            $"NAV WM fishing list: id={spot.WaterPlaceID} has " +
                            "no walkable shore cell on the player's side — " +
                            "keeping center snap (reachability filter judges).");
                    }

                    float dist;
                    if (playerSideShore)
                    {
                        dist = Vector3.Distance(playerPos, walkTarget);
                    }
                    else
                    {
                        // No same-side shore (far-bank-only spot, or regions
                        // unknown): water-edge distance, with the center-snap
                        // fallback when the player stands inside the box.
                        dist = Vector3.Distance(playerPos,
                            waterBox.ClosestPoint(playerPos));
                        if (dist < 0.01f)
                            dist = Vector3.Distance(playerPos, walkTarget);
                    }

                    DebugLogger.LogGameValue("NAV:FISHING:BUILD",
                        $"id={spot.WaterPlaceID} " +
                        $"center=({center.x:F1},{center.y:F1},{center.z:F1}) " +
                        $"size=({size.x:F1},{size.y:F1},{size.z:F1}) " +
                        $"placement={spot.IsPlacementFishingSpot} " +
                        $"walkTarget=({walkTarget.x:F1},{walkTarget.y:F1},{walkTarget.z:F1}) " +
                        $"shore={playerSideShore} dist={dist:F1}");

                    items.Add(new NavItem
                    {
                        Label        = Loc.Get("nav_fishing"),
                        Distance     = dist,
                        Position     = walkTarget,
                        FacePosition = center,
                        // Water volume, so the walk-start code can search the
                        // box perimeter for a game-verified fishable stand.
                        TriggerBounds = waterBox,
                    });
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState(
                        $"NAV: worldmap fishing spot {i}: {ex.Message}");
                }
            }

            return items;
        }

        /// <summary>
        /// Nearest-to-player walkable shore cell around a water box that lies
        /// in one of the player's connected regions, or false when the whole
        /// shoreline is on other landmasses. Grid-only (no game water probes
        /// — the walk start does those); used to keep the list's coarse
        /// target, and with it the reachability filter, on the player's side
        /// of rivers.
        /// </summary>
        private static bool TryFindShoreOnPlayerSide(Bounds waterBox,
            WorldmapTravelMode mode, List<int> startRegions,
            Vector3 playerPos, out Vector3 shore)
        {
            Vector3 best = Vector3.zero;
            float bestDistSq = float.MaxValue;

            ForEachWaterBoxShoreCell(waterBox, mode, cell =>
            {
                int region = WorldmapPathfinder.GetRegionId(cell, mode);
                if (region != 0 && !startRegions.Contains(region)) return;
                float distSq = (cell - playerPos).sqrMagnitude;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = cell;
                }
            });

            shore = best;
            return bestDistSq < float.MaxValue;
        }

        /// <summary>
        /// Debug-mode survey of world-map objects the nav list does not
        /// (yet) cover: counts of the game's per-map object lists plus
        /// positions of any discovery location points. Logged on every
        /// world-map list open so candidate POI types show up as log
        /// evidence instead of being guessed at. No-op outside debug mode.
        /// </summary>
        private void LogWorldmapObjectSurvey(FieldManager fm)
        {
            if (!Main.DebugMode || fm == null) return;

            try
            {
                DebugLogger.LogGameValue("NAV:WM:SURVEY",
                    $"fishingSpots={fm.FieldFishingWaterPlaceList?.Count ?? -1} " +
                    $"locationPoints={fm.FieldLocationPointList?.Count ?? -1} " +
                    $"savePoints={fm.FieldSavePointList?.Count ?? -1} " +
                    $"stairs={fm.FieldStairsList?.Count ?? -1} " +
                    $"doors={fm.FieldDoorList?.Count ?? -1} " +
                    $"minimapAreas={fm.FieldMinimapAreaList?.Count ?? -1}");

                // Discovery location points would be nav-list candidates
                // (fields list them as Markers) — log where they are.
                var lps = fm.FieldLocationPointList;
                if (lps != null)
                {
                    for (int i = 0; i < lps.Count; i++)
                    {
                        var lp = lps[i];
                        if (lp == null) continue;
                        var p = lp.transform.position;
                        DebugLogger.LogGameValue("NAV:WM:SURVEY:LOCPOINT",
                            $"#{i} pos=({p.x:F0},{p.y:F0},{p.z:F0})");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState(
                    $"NAV WM survey error: {ex.Message}");
            }
        }
        #endregion
    }
}
