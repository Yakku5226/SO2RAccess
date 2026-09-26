using Il2CppGame;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// The route proof's ANCHORS: where the player really stands after leaving a
    /// location. Until 2026-09-20 every proof started at the map jump's own
    /// position = the town symbol centre, inside the town's wall and fence
    /// colliders where the player can never stand; the start-side forgiveness
    /// that made such routes possible let fake proofs through (Hilton place 4,
    /// Arlia place 1). The game's map jump layout data holds the real point: a
    /// <c>MapjumpLayoutData</c> whose <c>ToFieldmapID</c> is the world map
    /// carries the world position the player appears at in <c>ToPosition</c>.
    /// The anchors are checked against exit positions taken from play logs
    /// before anything is proven from them.
    /// </summary>
    public static partial class WorldmapFishingStandBaker
    {
        /// <summary>Exits closer together than this (m, flat) are one anchor.</summary>
        private const float AnchorMergeMeters = 1f;

        /// <summary>A logged exit must have an anchor this close (m, flat) or the anchors are not trusted.</summary>
        private const float KnownExitToleranceMeters = 3f;

        /// <summary>Most layout records written to the log when the anchor check fails.</summary>
        private const int AnchorDumpLines = 120;

        /// <summary>
        /// Where the player appeared on a world map after leaving a town, per
        /// planet, read from play logs. Ground truth for the anchor check.
        /// Expel: logs of 2026-09-20. Nede: none yet — the first Nede exit seen
        /// in a play log belongs here before its stands bake is trusted.
        /// </summary>
        private static readonly Dictionary<WorldmapID, (string name, float x, float z)[]> KnownExitsByMap =
            new Dictionary<WorldmapID, (string name, float x, float z)[]>
            {
                [WorldmapID.EXPEL] = new[]
                {
                    ("Hilton", 750.8f, -170.7f),
                    ("Arlia", -46.8f, -404.5f),
                },
            };

        /// <summary>
        /// Every world position a map jump puts the player at on this world map,
        /// from the game's layout data (one anchor per distinct point, labelled
        /// "source fieldmap:map jump ID"). Also runs the shared entrance scan the
        /// gate pinch rule and the ring evidence need. Empty when the layout data
        /// is unavailable (logged).
        /// </summary>
        private static List<Anchor> CollectAnchors(FieldmapID worldFieldmap, WorldmapGridFormat.CachedGrid grid, int mask)
        {
            var anchors = new List<Anchor>();
            _proofMapjumps = WorldmapMapjumps.CollectAll();
            var pm = ParameterManager.Instance;
            if (pm == null)
            {
                Log("Anchors: ParameterManager unavailable — no exit anchors.");
                return anchors;
            }

            int records = 0, errors = 0, merged = 0;
            foreach (MapjumpID id in Enum.GetValues(typeof(MapjumpID)))
            {
                MapjumpLayoutData data;
                try
                {
                    data = pm.GetMapjumpLayoutParameter(id);
                    if (data == null) continue;
                    records++;
                    if (data.ToFieldmapID != worldFieldmap) continue;
                }
                catch (Exception ex)
                {
                    if (errors++ < 3) Log($"Anchors: layout record {id} unreadable: {ex.Message}");
                    continue;
                }

                Vector3 pos = data.ToPosition;
                grid.WorldToGrid(pos.x, pos.z, out int ax, out int az);
                float gameY = pos.y;
                pos.y = grid.GetHeightM(ax, az);
                if (anchors.Exists(a => FlatDistanceSq(a.Position, pos) <= AnchorMergeMeters * AnchorMergeMeters))
                {
                    merged++;
                    continue;
                }

                var anchor = new Anchor
                {
                    Label = $"{data.FieldmapID}:{id}",
                    Position = pos,
                    Region = WorldmapPathfinder.GetRegionId(pos, WorldmapTravelMode.Foot),
                };
                anchors.Add(anchor);
                Log($"Anchors: {anchor.Label} exit ({pos.x:F1},{pos.z:F1}) game y {gameY:F1} / grid y {pos.y:F1}, " +
                    $"foot region {anchor.Region}, {DescribeSymbolDistance(pos)}, " +
                    $"{WorldmapMapjumps.DescribeNearestRing(_proofMapjumps, pos)}, " +
                    (grid.IsPassable(ax, az, WorldmapGridFormat.CachedGrid.FlagFootBlocked) ? "grid cell walkable" : "GRID CELL BLOCKED") +
                    (BodyOverlapsWall(pos, mask, out Collider wall)
                        ? $", BODY OVERLAPS '{wall.name}' L{wall.gameObject.layer} ({NavigationHandler.ColliderChain(wall)})"
                        : ", body clear of the scene colliders") + ".");
            }
            Log($"Anchors: {anchors.Count} exit anchors from {records} layout records " +
                $"({merged} merged as the same point, {errors} unreadable); {_proofMapjumps.Count} entrance symbols in the scene.");
            return anchors;
        }

        /// <summary>Log fragment: distance from an exit point to the nearest entrance symbol centre (the old anchor).</summary>
        private static string DescribeSymbolDistance(Vector3 pos)
        {
            float best = float.MaxValue;
            string label = null;
            foreach (var (fieldmapID, position, _) in _proofMapjumps)
            {
                float d = FlatDistanceSq(position, pos);
                if (d >= best) continue;
                best = d;
                label = fieldmapID.ToString();
            }
            return label == null ? "no entrance symbol in the scene" : $"{Mathf.Sqrt(best):F1} m from symbol {label}";
        }

        /// <summary>
        /// True when every logged exit of this world map has an anchor within
        /// <see cref="KnownExitToleranceMeters"/>. On a miss the layout records
        /// around the world map are dumped so the right field can be found from
        /// the log. A map without logged exits passes (nothing to disprove it).
        /// </summary>
        private static bool AnchorsMatchKnownExits(WorldmapID wmID, FieldmapID worldFieldmap, List<Anchor> anchors)
        {
            if (!KnownExitsByMap.TryGetValue(wmID, out var exits) || exits.Length == 0)
            {
                Log($"Anchor check: WARNING — no logged exits for {WorldmapFishingStands.MapName(wmID) ?? wmID.ToString()}; " +
                    "anchors accepted unchecked. Add the first town exit from a play log to KnownExitsByMap " +
                    "before trusting this bake.");
                return true;
            }
            bool all = true;
            foreach (var (name, x, z) in exits)
            {
                var known = new Vector3(x, 0f, z);
                Anchor nearest = null;
                float best = float.MaxValue;
                foreach (var a in anchors)
                {
                    float d = FlatDistanceSq(a.Position, known);
                    if (d >= best) continue;
                    best = d;
                    nearest = a;
                }
                float meters = nearest == null ? float.MaxValue : Mathf.Sqrt(best);
                bool ok = meters <= KnownExitToleranceMeters;
                all &= ok;
                Log($"Anchor check: {name} logged exit ({x:F1},{z:F1}): " +
                    (nearest == null ? "no anchors at all"
                        : $"nearest anchor {nearest.Label} ({nearest.Position.x:F1},{nearest.Position.z:F1}) {meters:F1} m away") +
                    $" — {(ok ? "PASS" : "FAIL")}.");
            }
            if (!all) DumpLayoutRecords(worldFieldmap);
            return all;
        }

        /// <summary>Log-only: every layout record that touches the world map, so a failed anchor check can be diagnosed without another run.</summary>
        private static void DumpLayoutRecords(FieldmapID worldFieldmap)
        {
            var pm = ParameterManager.Instance;
            if (pm == null) return;
            int lines = 0;
            foreach (MapjumpID id in Enum.GetValues(typeof(MapjumpID)))
            {
                try
                {
                    var d = pm.GetMapjumpLayoutParameter(id);
                    if (d == null || (d.FieldmapID != worldFieldmap && d.ToFieldmapID != worldFieldmap)) continue;
                    if (lines++ >= AnchorDumpLines) continue;
                    Vector3 p = d.Position, t = d.ToPosition, y = d.PsynardPosition;
                    Log($"Anchor dump: {id} {d.FieldmapID} ({p.x:F1},{p.y:F1},{p.z:F1}) → {d.ToFieldmapID} " +
                        $"({t.x:F1},{t.y:F1},{t.z:F1}), psynard ({y.x:F1},{y.y:F1},{y.z:F1}).");
                }
                catch (Exception ex)
                {
                    if (lines++ < AnchorDumpLines) Log($"Anchor dump: {id} unreadable: {ex.Message}");
                }
            }
            Log($"Anchor dump: {lines} records touch {worldFieldmap}" +
                (lines > AnchorDumpLines ? $" (first {AnchorDumpLines} shown)" : "") + ".");
        }
    }
}
