using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Linq;
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
            if (_wmMapjumpCache.Count == 0)
                DebugLogger.LogState(
                    "NAV WM list build: mapjump scan found no map jumps (right after a map " +
                    "load the town colliders may not exist yet) — reachability verdicts fail " +
                    "open; the route planner rescans before its sweep.");

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
                        IsDungeon     = iconType == MapIconType.DUNGEON,
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
        /// ConstFishingWaterPlaceParameter database, one entry per water place
        /// with a BAKED stand (<see cref="WorldmapFishingStands"/>). The world map
        /// has NO FieldFishingWaterPlace objects — its spots are painted into the
        /// native world grid — so the parameter database is the only truthful
        /// source, and the stands were verified against the game's own bubble test
        /// at bake time. Walk target = the designated stand, or the first alternate
        /// whose connected region the player can reach in the current travel mode
        /// (O(1) region lookups, no scans); when none matches, the designated stand
        /// is kept and the item is marked unreachable. Face point = the stand's
        /// verified water point. Without a stands file the spots are skipped (logged).
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
                LogFishingDatabaseSurvey(pm);
                return items;
            }

            WorldmapID wmId;
            try { wmId = fm.WorldmapID; }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV WM fishing list: WorldmapID read failed: {ex.Message}");
                return items;
            }
            var file = WorldmapFishingStands.Load(wmId);
            if (file == null)
            {
                DebugLogger.LogState(
                    $"NAV WM fishing list: no stands file for {wmId} — " +
                    $"{spots.Count} water places skipped.");
                return items;
            }

            var mode = WorldmapTravel.CurrentMode();
            // Player's connected regions (empty = unknown → never reject).
            var startRegions = new List<int>();
            if (mode != WorldmapTravelMode.Psynard)
                WorldmapPathfinder.GetStartRegionIds(playerPos, mode, startRegions);

            int skipped = 0;
            for (int i = 0; i < spots.Count; i++)
            {
                try
                {
                    var spot = spots[i];
                    if (spot == null) continue;

                    var place = WorldmapFishingStands.TryGetPlace(file, spot.WaterPlaceID);
                    if (place == null || place.Stands.Count == 0)
                    {
                        skipped++;
                        DebugLogger.LogState(
                            $"NAV WM fishing list: id={spot.WaterPlaceID} skipped — " +
                            (place == null ? "not in the stands file" : "no verified stand") + ".");
                        continue;
                    }

                    // A remembered real bubble beats "nearest the player": the stand next
                    // to it is where the arrival creep can reach it from.
                    var remembered = WorldmapBubbleMemory.NearestForPlace(wmId, spot.WaterPlaceID, playerPos);
                    var stand = ChooseFishingStand(file, place, mode, startRegions,
                        remembered?.Position ?? playerPos,
                        out bool unreachable, out FishingStandEntry fallback, out string reason);
                    if (remembered != null)
                        reason += $"; chosen nearest the remembered bubble ({remembered.X:F1},{remembered.Z:F1})";
                    Vector3 pos = stand.Position;
                    Vector3 face = pos + stand.Facing * file.FrontDistance;
                    float dist = FlatDistance(playerPos, pos);

                    DebugLogger.LogGameValue("NAV:FISHING:BUILD",
                        $"id={spot.WaterPlaceID} stand=({pos.x:F1},{pos.y:F1},{pos.z:F1}) of {place.Stands.Count} " +
                        $"clearance={stand.Clearance:F2} floorTierOnly={place.FloorTierOnly} " +
                        $"proven={(stand.Proven ? $"{stand.ProvenFrom}/{stand.ProofTier}" : "-")} " +
                        $"dist={dist:F1} unreachable={unreachable} fallback=" +
                        (fallback != null ? $"({fallback.X:F1},{fallback.Z:F1}) {FlatDistance(playerPos, fallback.Position):F0} m" : "none") +
                        $" ({reason})");

                    items.Add(new NavItem
                    {
                        Label               = Loc.Get("nav_fishing"),
                        Distance            = dist,
                        Position            = pos,
                        FacePosition        = face,
                        Unreachable         = unreachable,
                        FishingFallback     = fallback?.Position,
                        FishingFallbackFace = fallback != null
                            ? fallback.Position + fallback.Facing * file.FrontDistance
                            : (Vector3?)null,
                        SourceObject        = spot,  // ConstFishingWaterPlaceParameter as stable source
                    });
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState(
                        $"NAV: worldmap fishing spot {i}: {ex.Message}");
                }
            }
            if (skipped > 0)
                DebugLogger.LogState(
                    $"NAV WM fishing list: {skipped} of {spots.Count} water places have no stand " +
                    "(the game refused every shoreline cell at bake time).");

            return items;
        }

        /// <summary>
        /// The stand to list for a water place: the one NEAREST the player among
        /// the stands the current travel mode can use whose region the player's
        /// start regions contain (region 0 = unknown, and unknown player regions,
        /// never reject). The file holds the whole verified shoreline, so this is
        /// what "the nearest fishing spot" means (2026-09-09: six ranked stands per
        /// lake put the Krosse stand 77 m away while the player fished 27 m from
        /// the gate). On foot, a place whose stands all failed the bake's route
        /// proof is annotated unreachable; a place with a proven stand lists its
        /// nearest stand and, when that one is unproven, carries the nearest proven
        /// stand as the walk's silent fallback. A file without proofs treats every
        /// stand as "proof unknown" (no annotation, no fallback). Bunny travel needs
        /// a bunny-passable cell (the proofs are foot sweeps, so no fallback); the
        /// psynard reaches everything. <paramref name="anchorPos"/> is the player, or the
        /// remembered real bubble of this water when there is one.
        /// </summary>
        private static FishingStandEntry ChooseFishingStand(FishingStandFile file,
            FishingPlaceStands place, WorldmapTravelMode mode, List<int> startRegions,
            Vector3 anchorPos, out bool unreachable, out FishingStandEntry fallback,
            out string reason)
        {
            unreachable = false;
            fallback = null;
            // Nearest first; predicates are only evaluated until the first match.
            var byDistance = place.Stands
                .OrderBy(s => (s.X - anchorPos.x) * (s.X - anchorPos.x) + (s.Z - anchorPos.z) * (s.Z - anchorPos.z))
                .ToList();

            if (mode == WorldmapTravelMode.Psynard)
            {
                reason = "psynard, nearest stand";
                return byDistance[0];
            }

            bool bunny = mode == WorldmapTravelMode.Bunny;
            bool ModeOk(FishingStandEntry s) => !bunny || s.BunnyOk;
            bool RegionOk(FishingStandEntry s)
            {
                if (startRegions.Count == 0) return true;
                int region = WorldmapPathfinder.GetRegionId(s.Position, mode);
                return region == 0 || startRegions.Contains(region);
            }

            bool useProofs = mode == WorldmapTravelMode.Foot && file.ProofsBaked && place.ProofAttempted;
            if (useProofs && !place.HasProvenStand)
            {
                unreachable = true;
                reason = $"no proven stand: none of {place.Stands.Count} stands passed the bake's " +
                    $"route sweep in {place.ProofAttempts} attempts from {file.ProofAnchors} entrances";
                return byDistance.Find(ModeOk) ?? byDistance[0];
            }

            var nearest = byDistance.Find(s => ModeOk(s) && RegionOk(s));
            if (nearest != null)
            {
                if (useProofs && !nearest.Proven)
                    fallback = byDistance.Find(s => s.Proven && ModeOk(s) && RegionOk(s));
                reason = (startRegions.Count == 0
                        ? "nearest stand, player regions unknown"
                        : "nearest stand in the player's regions")
                    + (!useProofs ? " (proof unknown)"
                        : nearest.Proven ? ", proven"
                        : fallback != null ? ", unproven with a proven fallback"
                        : ", unproven, no proven fallback in the player's regions");
                return nearest;
            }

            unreachable = true;
            var anyMode = byDistance.Find(ModeOk);
            reason = anyMode == null
                ? "no bunny-passable stand"
                : "every " + (bunny ? "bunny-passable " : "") + "stand off the player's regions";
            return anyMode ?? byDistance[0];
        }

        /// <summary>
        /// Debug survey when a map has no fishing parameters: dumps the whole
        /// database once so a wrong map-ID assumption shows up as evidence.
        /// </summary>
        private static void LogFishingDatabaseSurvey(ParameterManager pm)
        {
            if (!Main.DebugMode) return;
            try
            {
                var all = pm.GetFishingWaterPlaceParameterList();
                if (all == null) return;
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
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV: fishing DB dump error: {ex.Message}");
            }
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
