using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Bake-time survey of the CURRENT world map (debug only). Runs before the
    /// F9 grid bake and stand-alone from F6. It reads every fact the bake
    /// depends on and refuses the bake when one of them is missing or
    /// contradictory — the honesty rule: a grid baked on wrong assumptions is
    /// fiction the pathfinder cannot detect later.
    /// What it records (log prefix <c>[GridSurvey]</c>):
    /// - the game's own painted map rectangle (<c>worldGridData.RootData.Rect</c>),
    /// - the streamed-collision unit list coverage (X/Z and Y) and pool count,
    /// - every town entrance trigger with its height class and ground,
    /// - every location symbol with its position,
    /// - the world-map loop members and any lowered terrain copies that carry
    ///   colliders (the "land sinks in the distance" copies),
    /// - the psynard altitude caps,
    /// - the bounds the bake would derive from the data.
    /// It also writes <c>worldmap_{map}.survey.txt</c>, a probe list for the
    /// offline audit tool (<c>tools\GridAnalysis.cs</c>).
    /// </summary>
    public static class WorldmapGridSurvey
    {
        /// <summary>Metres added around the measured map extents before snapping.</summary>
        public const float BoundsMarginMeters = 16f;

        /// <summary>
        /// Hard cap on derived grid cells (1.5 x Expel's 37.5 M). Height + flags +
        /// regions cost about 7 bytes per cell; a bigger derived grid means the
        /// data was misread, not that the planet is huge.
        /// </summary>
        public const long MaxCells = 56_000_000;

        /// <summary>Unit coverage span may exceed the painted rect by this factor before it counts as loop copies.</summary>
        private const float MaxCoverageToRectRatio = 2f;

        /// <summary>Most per-trigger and per-location lines written to the log.</summary>
        private const int MaxDetailLines = 80;

        /// <summary>Extents of the streamed unit list, measured from each unit's bounds.</summary>
        public struct UnitCoverage
        {
            public int Count;
            public int WithLayout;
            public int[] PerLayer;
            public float MinX, MaxX, MinZ, MaxZ, MinY, MaxY;
            /// <summary>True when at least one unit with a prefab was measured.</summary>
            public bool Any => WithLayout > 0;
            public float SpanX => MaxX - MinX;
            public float SpanZ => MaxZ - MinZ;
        }

        /// <summary>Everything the survey learned; <see cref="CanBake"/> is the verdict.</summary>
        public sealed class Result
        {
            public WorldmapID WmID;
            public string MapName;
            public Vector3 PlayerPos;
            public bool HasRect;
            public Rect Rect;
            public float GameGridSize;
            public bool HasUnits;
            public UnitCoverage Units;
            public int PoolCount;
            public int TriggersTotal, TriggersSmall, TriggersLarge;
            public int LocationCount;
            public int LoopCreatorColliders, LoopCloneCollidersOutsideCoverage;
            public bool HasDerivedBounds;
            public float MinX, MinZ, MaxX, MaxZ;
            public long DerivedCells;
            /// <summary>Red flags. Empty = the bake may run.</summary>
            public readonly List<string> Abort = new List<string>();
            /// <summary>Probe points for the offline tool: kind, label, x, z.</summary>
            public readonly List<(string kind, string label, float x, float z)> ProbePoints =
                new List<(string, string, float, float)>();
            public bool CanBake => Abort.Count == 0;
            public string AbortSummary => string.Join("; ", Abort);
        }

        /// <summary>
        /// Runs the survey and logs it. Never throws; a failed section becomes a
        /// red flag or a logged "unavailable". <paramref name="writeProbeFile"/>
        /// writes the offline probe list next to the grid files.
        /// </summary>
        public static Result Run(FieldManager fm, WorldmapID wmID, Vector3 playerPos, bool writeProbeFile)
        {
            var r = new Result
            {
                WmID = wmID,
                MapName = WorldmapFishingStands.MapName(wmID) ?? "unknown",
                PlayerPos = playerPos,
            };
            var sb = new StringBuilder();
            sb.AppendLine("[GridSurvey] === WORLD MAP SURVEY ===");
            string fieldmap = "?";
            try { fieldmap = fm.currentFieldmapID.ToString(); } catch { }
            sb.AppendLine(
                $"[GridSurvey] map={wmID} ({r.MapName}) fieldmap={fieldmap} " +
                $"player=({playerPos.x:F1},{playerPos.y:F1},{playerPos.z:F1})");
            if (WorldmapFishingStands.MapName(wmID) == null)
                r.Abort.Add($"world map id {wmID} is not a known planet");

            SurveyWorldGrid(fm, r, sb);
            SurveyCulling(r, sb);
            SurveyTriggers(r, sb);
            SurveyLocations(wmID, r, sb);
            SurveyLoop(fm, r, sb);
            SurveyPsynard(fm, sb);
            DeriveBounds(r, sb);

            if (r.Abort.Count == 0)
                sb.AppendLine("[GridSurvey] VERDICT: no red flags — the bake may run.");
            else
                foreach (var reason in r.Abort)
                    sb.AppendLine($"[GridSurvey] ABORT: {reason}");
            sb.Append("[GridSurvey] === END SURVEY ===");
            MelonLogger.Msg(sb.ToString());

            if (writeProbeFile) WriteProbeFile(r);
            return r;
        }

        /// <summary>Measures the streamed unit list: counts, layers and bounds extents.</summary>
        public static UnitCoverage MeasureUnitCoverage(CullingData data)
        {
            var c = new UnitCoverage
            {
                PerLayer = new int[32],
                MinX = float.MaxValue, MaxX = float.MinValue,
                MinZ = float.MaxValue, MaxZ = float.MinValue,
                MinY = float.MaxValue, MaxY = float.MinValue,
            };
            var units = data?.unitList;
            if (units == null) return c;
            c.Count = units.Count;
            for (int i = 0; i < c.Count; i++)
            {
                var u = units[i];
                if (u == null) continue;
                int layer = u.layer;
                if (layer >= 0 && layer < 32) c.PerLayer[layer]++;
                if (u.layoutItem == null) continue;
                c.WithLayout++;
                var b = u.unitBounds;
                if (b.min.x < c.MinX) c.MinX = b.min.x;
                if (b.max.x > c.MaxX) c.MaxX = b.max.x;
                if (b.min.z < c.MinZ) c.MinZ = b.min.z;
                if (b.max.z > c.MaxZ) c.MaxZ = b.max.z;
                if (b.min.y < c.MinY) c.MinY = b.min.y;
                if (b.max.y > c.MaxY) c.MaxY = b.max.y;
            }
            return c;
        }

        /// <summary>The per-layer counts as "L0=123, L24=45".</summary>
        public static string DescribeLayers(UnitCoverage c)
        {
            var parts = new List<string>();
            if (c.PerLayer != null)
                for (int l = 0; l < 32; l++)
                    if (c.PerLayer[l] > 0) parts.Add($"L{l}={c.PerLayer[l]}");
            return string.Join(", ", parts);
        }

        // --------------------------------------------------------------------
        // Sections
        // --------------------------------------------------------------------

        private static void SurveyWorldGrid(FieldManager fm, Result r, StringBuilder sb)
        {
            try
            {
                if (!fm.IsExistWorldGridData())
                {
                    r.Abort.Add("the game reports no world grid data for this map");
                    sb.AppendLine("[GridSurvey] worldGridData: IsExistWorldGridData=false");
                    return;
                }
                var data = fm.worldGridData;
                if (data == null)
                {
                    r.Abort.Add("worldGridData is null");
                    sb.AppendLine("[GridSurvey] worldGridData: null");
                    return;
                }
                var root = data.RootData;
                if (root == null)
                {
                    r.Abort.Add("worldGridData.RootData is null");
                    sb.AppendLine("[GridSurvey] worldGridData: RootData null");
                    return;
                }
                r.Rect = root.Rect;
                r.GameGridSize = data.GetGridSize();
                r.HasRect = r.Rect.width > 0f && r.Rect.height > 0f;
                sb.AppendLine(
                    $"[GridSurvey] rect X[{r.Rect.xMin:F1},{r.Rect.xMax:F1}] Z[{r.Rect.yMin:F1},{r.Rect.yMax:F1}] " +
                    $"(w={r.Rect.width:F0} h={r.Rect.height:F0}) gameGridSize={r.GameGridSize:F2} m");
                if (!r.HasRect)
                {
                    r.Abort.Add("the game's world grid rect is empty");
                    return;
                }
                // The player stands on this map, so the rect (read as X/Z)
                // must contain them — otherwise the axis reading is wrong.
                bool inside = r.Rect.Contains(new Vector2(r.PlayerPos.x, r.PlayerPos.z));
                sb.AppendLine($"[GridSurvey] player inside rect (x/z reading): {inside}");
                if (!inside)
                    r.Abort.Add("the player stands outside the world grid rect — rect axis reading is wrong");
            }
            catch (Exception ex)
            {
                r.Abort.Add($"world grid data unreadable: {ex.Message}");
                sb.AppendLine($"[GridSurvey] worldGridData read error: {ex.Message}");
            }
        }

        private static void SurveyCulling(Result r, StringBuilder sb)
        {
            try
            {
                var mgr = CullingManager.Instance;
                var data = mgr != null ? mgr.cullingData : null;
                if (data == null)
                {
                    r.Abort.Add("CullingManager culling data is unavailable (no streamed chunk list)");
                    sb.AppendLine("[GridSurvey] culling: manager or cullingData null");
                    return;
                }
                r.Units = MeasureUnitCoverage(data);
                r.HasUnits = r.Units.Any;
                try { r.PoolCount = data.poolInfoList != null ? data.poolInfoList.Count : 0; }
                catch { r.PoolCount = -1; }
                sb.AppendLine(
                    $"[GridSurvey] units={r.Units.Count} withPrefab={r.Units.WithLayout} pools={r.PoolCount} " +
                    $"layers: {DescribeLayers(r.Units)}");
                if (!r.HasUnits)
                {
                    r.Abort.Add("the culling unit list is empty — the chunk loader would have nothing to load");
                    return;
                }
                sb.AppendLine(
                    $"[GridSurvey] unit coverage X[{r.Units.MinX:F0},{r.Units.MaxX:F0}] " +
                    $"Z[{r.Units.MinZ:F0},{r.Units.MaxZ:F0}] Y[{r.Units.MinY:F0},{r.Units.MaxY:F0}]");

                // The bake's ground ray starts at RaycastStartY + RaycastMaxDist
                // (CalcHeight's parameter is a start offset).
                float rayStart = WorldmapGridProbe.RaycastStartY + WorldmapGridProbe.RaycastMaxDist;
                if (r.Units.MaxY > rayStart - 10f)
                    r.Abort.Add($"terrain units reach Y={r.Units.MaxY:F0} m, the bake ray starts at {rayStart:F0} m");

                if (r.HasRect)
                {
                    bool tooWide = r.Units.SpanX > r.Rect.width * MaxCoverageToRectRatio ||
                                   r.Units.SpanZ > r.Rect.height * MaxCoverageToRectRatio;
                    sb.AppendLine(
                        $"[GridSurvey] coverage vs rect: spanX {r.Units.SpanX:F0}/{r.Rect.width:F0} " +
                        $"spanZ {r.Units.SpanZ:F0}/{r.Rect.height:F0}");
                    if (tooWide)
                        r.Abort.Add("the unit list spans more than twice the painted rect (loop copies or a shared list?)");
                }
            }
            catch (Exception ex)
            {
                r.Abort.Add($"culling data unreadable: {ex.Message}");
                sb.AppendLine($"[GridSurvey] culling read error: {ex.Message}");
            }
        }

        private static void SurveyTriggers(Result r, StringBuilder sb)
        {
            try
            {
                var mapjumps = UnityEngine.Object.FindObjectsOfType<FieldMapjumpCollision>();
                int lines = 0;
                var buckets = new int[5]; // <=5, <=10, <=20, <=50, >50 m tall
                if (mapjumps != null)
                {
                    for (int m = 0; m < mapjumps.Length; m++)
                    {
                        var mj = mapjumps[m];
                        if (mj == null) continue;
                        var cols = mj.GetComponents<Collider>();
                        if (cols == null) continue;
                        for (int ci = 0; ci < cols.Length; ci++)
                        {
                            var col = cols[ci];
                            if (col == null || !col.isTrigger) continue;
                            var b = col.bounds;
                            float sy = b.size.y;
                            r.TriggersTotal++;
                            bool small = sy <= WorldmapMapjumps.GroundLevelMaxHeight;
                            if (small) r.TriggersSmall++; else r.TriggersLarge++;
                            buckets[sy <= 5f ? 0 : sy <= 10f ? 1 : sy <= 20f ? 2 : sy <= 50f ? 3 : 4]++;

                            var pos = mj.transform.position;
                            float groundY = GameUtility.CalcHeight(pos, out bool ok, 50f);
                            string fieldmap = "?";
                            try { fieldmap = mj.fieldmapID.ToString(); } catch { }
                            if (small)
                                r.ProbePoints.Add(("gate", fieldmap, b.center.x, b.center.z));
                            if (lines++ < MaxDetailLines)
                                sb.AppendLine(
                                    $"[GridSurvey] trigger {fieldmap} at ({pos.x:F1},{pos.y:F1},{pos.z:F1}) " +
                                    $"box X[{b.min.x:F1},{b.max.x:F1}] Z[{b.min.z:F1},{b.max.z:F1}] sizeY={sy:F1} " +
                                    $"{(small ? "gate" : "town-wide")} calcHeight={(ok ? groundY.ToString("F1") : "fail")}");
                        }
                    }
                }
                sb.AppendLine(
                    $"[GridSurvey] triggers total={r.TriggersTotal} gates(<= {WorldmapMapjumps.GroundLevelMaxHeight:F0} m)={r.TriggersSmall} " +
                    $"town-wide={r.TriggersLarge}; height classes <=5:{buckets[0]} <=10:{buckets[1]} <=20:{buckets[2]} <=50:{buckets[3]} >50:{buckets[4]}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[GridSurvey] trigger scan error: {ex.Message}");
            }
        }

        private static void SurveyLocations(WorldmapID wmID, Result r, StringBuilder sb)
        {
            try
            {
                var pm = ParameterManager.Instance;
                var symbols = pm?.GetWorldmapSymbolParameter(wmID);
                if (symbols == null)
                {
                    sb.AppendLine("[GridSurvey] locations: symbol list unavailable");
                    return;
                }
                var tm = TextManager.Instance;
                int lines = 0;
                for (int i = 0; i < symbols.Count; i++)
                {
                    var sym = symbols[i];
                    if (sym == null) continue;
                    var p = sym.position;
                    string name = NavigationHandler.ResolveWorldmapSymbolName(pm, tm, sym, i);
                    string icon = "?";
                    try { icon = sym.mapIconType.ToString(); } catch { }
                    bool inRect = r.HasRect && r.Rect.Contains(new Vector2(p.x, p.z));
                    bool inUnits = r.HasUnits && p.x >= r.Units.MinX && p.x <= r.Units.MaxX &&
                                   p.z >= r.Units.MinZ && p.z <= r.Units.MaxZ;
                    r.LocationCount++;
                    r.ProbePoints.Add(("loc", name, p.x, p.z));
                    if (lines++ < MaxDetailLines)
                        sb.AppendLine(
                            $"[GridSurvey] location \"{name}\" {icon} at ({p.x:F1},{p.y:F1},{p.z:F1}) " +
                            $"inRect={inRect} inUnits={inUnits}");
                }
                sb.AppendLine($"[GridSurvey] locations: {r.LocationCount} symbols");
                if (r.LocationCount > 0 && r.TriggersSmall == 0)
                    r.Abort.Add("locations exist but no gate-sized entrance trigger was found (town colliders not spawned, or the 20 m gate rule is wrong here)");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[GridSurvey] location scan error: {ex.Message}");
            }
        }

        private static void SurveyLoop(FieldManager fm, Result r, StringBuilder sb)
        {
            try
            {
                Vector3 total = fm.worldMapLoopTotalTranslate;
                Vector3 outside = fm.experOutsidePoint;
                string outsideArea = "?";
                try
                {
                    var player = fm.GetControlPlayer();
                    if (player != null)
                    {
                        bool isOutside = fm.IsWorldMapOutsideArea(player, out Vector3 dir);
                        outsideArea = $"{isOutside} dir=({dir.x:F1},{dir.y:F1},{dir.z:F1})";
                    }
                }
                catch (Exception ex) { outsideArea = $"error: {ex.Message}"; }
                sb.AppendLine(
                    $"[GridSurvey] loop totalTranslate=({total.x:F1},{total.y:F1},{total.z:F1}) " +
                    $"experOutsidePoint=({outside.x:F1},{outside.y:F1},{outside.z:F1}) playerOutsideArea={outsideArea}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[GridSurvey] loop members unreadable: {ex.Message}");
            }

            // Terrain copies drawn beyond the map edge, lowered so they read as
            // the horizon. Colliders under their creators would let the bake
            // sample a copy as ground — refuse, and let the data decide.
            try
            {
                var creators = UnityEngine.Object.FindObjectsOfType<ExperLoopObjectCreator>(true);
                int count = creators != null ? creators.Length : 0;
                sb.AppendLine($"[GridSurvey] ExperLoopObjectCreator: {count} instance(s)");
                for (int i = 0; creators != null && i < count; i++)
                {
                    var c = creators[i];
                    if (c == null) continue;
                    int manual = 0;
                    try { manual = c.manualLoopObjects != null ? c.manualLoopObjects.Count : 0; } catch { }
                    var cols = c.GetComponentsInChildren<Collider>(true);
                    int colCount = cols != null ? cols.Length : 0;
                    r.LoopCreatorColliders += colCount;
                    sb.AppendLine(
                        $"[GridSurvey]   '{c.gameObject.name}' active={c.gameObject.activeInHierarchy} " +
                        $"nearWidth={c.copyNearObjWidth:F0} nearYOffset={c.copyNearObjHeightOffset:F1} " +
                        $"farWidth={c.copyFarObjWidth:F0} farYOffset={c.copyFarObjHeightOffset:F1} " +
                        $"boundsYMin={c.copyObjBoundsYMin:F1} manualObjects={manual} childColliders={colCount}");
                }
                var silhouettes = UnityEngine.Object.FindObjectsOfType<WorldMapSilhouetteObjectCreator>(true);
                int sCount = silhouettes != null ? silhouettes.Length : 0;
                int sCols = 0;
                for (int i = 0; silhouettes != null && i < sCount; i++)
                {
                    var s = silhouettes[i];
                    if (s == null) continue;
                    var cols = s.GetComponentsInChildren<Collider>(true);
                    sCols += cols != null ? cols.Length : 0;
                }
                r.LoopCreatorColliders += sCols;
                Vector3 experOutside = Vector3.zero;
                try { experOutside = WorldMapSilhouetteObjectCreator.EXPER_OUTSIDE_POINT; } catch { }
                sb.AppendLine(
                    $"[GridSurvey] WorldMapSilhouetteObjectCreator: {sCount} instance(s), childColliders={sCols}, " +
                    $"EXPER_OUTSIDE_POINT=({experOutside.x:F1},{experOutside.y:F1},{experOutside.z:F1})");
                if (r.LoopCreatorColliders > 0)
                    r.Abort.Add($"{r.LoopCreatorColliders} collider(s) live under the loop/silhouette copy creators — the bake could sample lowered copies");

                // Clones with colliders OUTSIDE the unit coverage are suspects too
                // (logged, not fatal: town models are prefab clones as well).
                if (r.HasUnits)
                {
                    var all = UnityEngine.Object.FindObjectsOfType<Collider>(true);
                    int shown = 0;
                    for (int i = 0; all != null && i < all.Length; i++)
                    {
                        var col = all[i];
                        if (col == null || col.isTrigger) continue;
                        var go = col.gameObject;
                        if (go == null) continue;
                        var t = go.transform;
                        bool clone = false;
                        for (int d = 0; t != null && d < 4 && !clone; d++, t = t.parent)
                            clone = t.gameObject.name.EndsWith("(Clone)", StringComparison.Ordinal);
                        if (!clone) continue;
                        var b = col.bounds;
                        bool outsideCoverage = b.max.x < r.Units.MinX || b.min.x > r.Units.MaxX ||
                                               b.max.z < r.Units.MinZ || b.min.z > r.Units.MaxZ;
                        if (!outsideCoverage) continue;
                        r.LoopCloneCollidersOutsideCoverage++;
                        if (shown++ < 8)
                            sb.AppendLine(
                                $"[GridSurvey]   clone collider outside coverage: '{go.name}' L{go.layer} " +
                                $"center=({b.center.x:F0},{b.center.y:F0},{b.center.z:F0}) ext=({b.extents.x:F0},{b.extents.y:F0},{b.extents.z:F0})");
                    }
                    sb.AppendLine(
                        $"[GridSurvey] clone colliders outside the unit coverage: {r.LoopCloneCollidersOutsideCoverage}" +
                        (r.LoopCloneCollidersOutsideCoverage > 0 ? " (WARNING: check they are not lowered copies)" : ""));
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[GridSurvey] loop copy scan error: {ex.Message}");
            }
        }

        private static void SurveyPsynard(FieldManager fm, StringBuilder sb)
        {
            try
            {
                // PsynardSettings is a struct in the interop (no null check possible);
                // an unset struct reads as all zeros, which the log then shows.
                var s = fm.psynardSettings;
                sb.AppendLine(
                    $"[GridSurvey] psynard limitY min={s.minLimitY:F1} expel={s.maxExpelLimitY:F1} " +
                    $"nede={s.maxNedeLimitY:F1} riseFromGround={s.riseFromGround:F1}" +
                    (s.maxNedeLimitY == 0f && s.maxExpelLimitY == 0f ? " (all zero — settings not loaded?)" : ""));
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[GridSurvey] psynard settings unavailable: {ex.Message}");
            }
            try
            {
                int mask = GameRenderManager.LayerMaskPsynardWall;
                sb.AppendLine($"[GridSurvey] psynard wall mask=0x{mask:X8} → {WorldmapGridDiagnostics.DescribeMask(mask)}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[GridSurvey] psynard wall mask unreadable: {ex.Message}");
            }
        }

        /// <summary>
        /// Bounds the bake would use for a data-derived map: the union of the
        /// painted rect and the unit coverage, plus <see cref="BoundsMarginMeters"/>,
        /// snapped OUTWARD to the bake tile lattice anchored at the world origin.
        /// Both inputs are static game assets, so a rebake is cell-identical.
        /// </summary>
        private static void DeriveBounds(Result r, StringBuilder sb)
        {
            if (!r.HasRect && !r.HasUnits)
            {
                sb.AppendLine("[GridSurvey] derived bounds: none (no rect, no units)");
                return;
            }
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            void Include(float x0, float x1, float z0, float z1)
            {
                if (x0 < minX) minX = x0;
                if (x1 > maxX) maxX = x1;
                if (z0 < minZ) minZ = z0;
                if (z1 > maxZ) maxZ = z1;
            }
            if (r.HasRect) Include(r.Rect.xMin, r.Rect.xMax, r.Rect.yMin, r.Rect.yMax);
            if (r.HasUnits) Include(r.Units.MinX, r.Units.MaxX, r.Units.MinZ, r.Units.MaxZ);

            float snap = WorldmapGridGenerator.TileSizeMeters;
            r.MinX = Mathf.Floor((minX - BoundsMarginMeters) / snap) * snap;
            r.MinZ = Mathf.Floor((minZ - BoundsMarginMeters) / snap) * snap;
            r.MaxX = Mathf.Ceil((maxX + BoundsMarginMeters) / snap) * snap;
            r.MaxZ = Mathf.Ceil((maxZ + BoundsMarginMeters) / snap) * snap;
            long w = (long)((r.MaxX - r.MinX) / WorldmapGridGenerator.CellSize) + 1;
            long h = (long)((r.MaxZ - r.MinZ) / WorldmapGridGenerator.CellSize) + 1;
            r.DerivedCells = w * h;
            r.HasDerivedBounds = true;
            sb.AppendLine(
                $"[GridSurvey] derived bounds X[{r.MinX:F0},{r.MaxX:F0}] Z[{r.MinZ:F0},{r.MaxZ:F0}] " +
                $"→ {w}x{h} ({r.DerivedCells} cells at {WorldmapGridGenerator.CellSize} m; margin {BoundsMarginMeters:F0} m, snap {snap:F0} m)");
            if (r.DerivedCells > MaxCells)
                r.Abort.Add($"derived grid would have {r.DerivedCells} cells, cap is {MaxCells}");
        }

        /// <summary>Path of the offline probe list for a map name.</summary>
        public static string ProbeFilePath(string mapName) =>
            Path.Combine(WorldmapGridFormat.UserDir, $"worldmap_{mapName}.survey.txt");

        private static void WriteProbeFile(Result r)
        {
            try
            {
                string path = ProbeFilePath(r.MapName);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var sb = new StringBuilder();
                sb.AppendLine($"# {r.WmID} survey {DateTime.Now:yyyy-MM-dd HH:mm}; kind<TAB>label<TAB>x<TAB>z");
                sb.AppendLine($"player\tbake spot\t{r.PlayerPos.x:F1}\t{r.PlayerPos.z:F1}");
                foreach (var (kind, label, x, z) in r.ProbePoints)
                    sb.AppendLine($"{kind}\t{label.Replace('\t', ' ')}\t{x:F1}\t{z:F1}");
                File.WriteAllText(path, sb.ToString());
                MelonLogger.Msg($"[GridSurvey] probe list written: {path} ({r.ProbePoints.Count + 1} lines)");
            }
            catch (Exception ex)
            {
                MelonLogger.Msg($"[GridSurvey] probe list not written: {ex.Message}");
            }
        }
    }
}
