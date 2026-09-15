using Il2CppGame;
using System;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// The world-map grid bake's per-cell probe, as ONE shared, replayable
    /// function: the ground height query and the obstacle test that decide a
    /// cell's foot/bunny flags and its clearance record. The F9 bake
    /// (<see cref="WorldmapGridGenerator"/>) and the F10 grid-truth probe
    /// (<see cref="WorldmapGridDiagnostics.LogGridTruthProbe"/>) both call it,
    /// so a live replay at a cell is an exact replay of what the bake did there
    /// — the only variable is the physics world at the time (2026-09-13: the
    /// Arlia town wall is open ground in the July grid; this is how we learn
    /// whether the wall was absent at bake time or the probe cannot see it).
    /// </summary>
    public static class WorldmapGridProbe
    {
        /// <summary>
        /// Height of the CalcHeight probe point. Must be above the highest
        /// terrain point. Map heights range -33m to 98m.
        /// </summary>
        public const float RaycastStartY = 150f;

        /// <summary>
        /// Passed as CalcHeight's ray-start offset above the probe point (the
        /// game's parameter is a start height, not a length): the ray starts
        /// 300 m above the probe point and runs to the default distance.
        /// </summary>
        public const float RaycastMaxDist = 300f;

        /// <summary>
        /// The layer index of CharacterWall (region boundary walls with
        /// designed road gaps 1.8m-5.1m wide). Within the foot mask this
        /// layer gets the fine 5x5 sub-cell clearance scan; every other
        /// foot-mask layer uses the simple radius threshold. (The bunny
        /// mask contains no CharacterWall — the mount ignores region walls,
        /// measured in Phase A.)
        /// </summary>
        public const int CharaWallLayer = 23;

        /// <summary>
        /// Hard minimum clearance for a cell to be FOOT-passable. Set to the
        /// player capsule radius (0.50m) so any theoretically passable
        /// gap stays in the grid. The continuous clearance penalty in
        /// the A* pathfinder steers away from tight cells — the hard
        /// threshold just prevents truly impassable ones.
        /// </summary>
        public const float MinPassableClearance = 0.50f;

        /// <summary>
        /// Hard minimum clearance for a cell to be BUNNY-passable. The
        /// FieldBunny capsule measured IDENTICAL to the foot player
        /// (0.50m radius) in the Phase A investigation, so the floors match.
        /// </summary>
        public const float BunnyMinClearance = 0.50f;

        /// <summary>
        /// Sub-cell resolution for CharaWall gap detection. Each 0.5m
        /// cell near a CharaWall is checked at 25 sub-positions (5x5
        /// at 0.125m spacing). A cell is blocked only if NONE of the
        /// sub-positions have >= MinPassableClearance from all walls.
        /// The best position is stored as a clearance offset so the
        /// pathfinder guides the player through the widest part of gaps.
        /// </summary>
        public const int SubCellSteps = 2; // -2..+2 = 5 points per axis

        /// <summary>
        /// Search radius for OverlapSphere when finding solid obstacles.
        /// Must cover player collision radius (0.5m) plus margin.
        /// </summary>
        public const float ObstacleSearchRadius = 1.0f;

        /// <summary>Height above the ground at which the obstacle sphere is centred.</summary>
        public const float ProbeHeightAboveGround = 0.5f;

        /// <summary>Solid foot obstacles closer than this get a clearance record (A* penalty input).</summary>
        public const float ClearanceRecordMeters = 2.0f;

        /// <summary>Result of one cell's obstacle probe (see <see cref="ProbeObstacles"/>).</summary>
        public struct ProbeResult
        {
            /// <summary>The cell gets the foot-blocked flag.</summary>
            public bool FootBlocked;
            /// <summary>The cell gets the bunny-blocked flag.</summary>
            public bool BunnyBlocked;
            /// <summary>Nearest solid foot-mask (non-CharaWall) collider distance at the probe point; MaxValue = none within the search radius.</summary>
            public float NearestFootSolidDist;
            /// <summary>A solid CharaWall lies within the search radius, so the 5x5 sub-cell pass ran.</summary>
            public bool HasCharaWall;
            /// <summary>Best sub-cell clearance from the CharaWall pass; -1 when it did not run.</summary>
            public float CharaWallBestClearance;
            /// <summary>Sub-cell offset (m) the bake stores; valid when <see cref="HasClearanceOffset"/>.</summary>
            public float BestOffX, BestOffZ;
            /// <summary>True when the bake stores a clearance offset for this cell.</summary>
            public bool HasClearanceOffset;
            /// <summary>Clearance the bake stores in the value table; MaxValue = no entry.</summary>
            public float ClearanceValue;
            /// <summary>Diagnostics: the nearest solid collider either query saw (null = none) and its distance.</summary>
            public Collider NearestCollider;
            /// <summary>Diagnostics: distance to <see cref="NearestCollider"/>.</summary>
            public float NearestDist;
        }

        /// <summary>The bake's ground query at a cell, with the bake's constants.</summary>
        public static float BakeGroundHeight(float worldX, float worldZ, out bool hasGround)
        {
            var probe = new Vector3(worldX, RaycastStartY, worldZ);
            return GameUtility.CalcHeight(probe, out hasGround, RaycastMaxDist);
        }

        /// <summary>
        /// The bake's obstacle test for one cell standing on <paramref name="groundY"/>:
        /// one sphere query serves both modes (each hit collider's own layer decides
        /// which mode it blocks), then the CharaWall 5x5 sub-cell pass for foot
        /// travel. Pure function of the physics world — no bake state.
        /// </summary>
        public static ProbeResult ProbeObstacles(float worldX, float worldZ, float groundY,
            int footSolidMask, int charaWallMask, int bunnyMask)
        {
            var r = new ProbeResult
            {
                NearestFootSolidDist = float.MaxValue,
                CharaWallBestClearance = -1f,
                ClearanceValue = float.MaxValue,
                NearestDist = float.MaxValue,
            };
            int unionSolidMask = footSolidMask | bunnyMask;
            Vector3 checkPos = new Vector3(worldX, groundY + ProbeHeightAboveGround, worldZ);

            var cols = UnityEngine.Physics.OverlapSphere(checkPos, ObstacleSearchRadius, unionSolidMask);
            if (cols != null)
            {
                for (int c = 0; c < cols.Length; c++)
                {
                    if (cols[c] == null || cols[c].isTrigger) continue;
                    int layerBit = 1 << cols[c].gameObject.layer;
                    float dist = Vector3.Distance(checkPos, cols[c].ClosestPoint(checkPos));
                    if (dist < r.NearestDist) { r.NearestDist = dist; r.NearestCollider = cols[c]; }
                    if ((layerBit & footSolidMask) != 0)
                    {
                        if (dist < r.NearestFootSolidDist) r.NearestFootSolidDist = dist;
                        if (dist < MinPassableClearance) r.FootBlocked = true;
                    }
                    if ((layerBit & bunnyMask) != 0 && dist < BunnyMinClearance)
                        r.BunnyBlocked = true;
                }
            }

            // CharaWall (foot only) with sub-cell precision: scan 5x5
            // sub-positions (0.125m spacing); the cell is foot-blocked ONLY
            // if NONE has >= 0.50m clearance from all solid walls. This
            // gives 0.1m accuracy for gap detection while keeping the 0.5m
            // grid format.
            if (!r.FootBlocked && charaWallMask != 0)
            {
                var cols23 = UnityEngine.Physics.OverlapSphere(checkPos, ObstacleSearchRadius, charaWallMask);
                bool hasSolidWall = false;
                if (cols23 != null)
                {
                    for (int c = 0; c < cols23.Length; c++)
                    {
                        if (cols23[c] == null || cols23[c].isTrigger) continue;
                        hasSolidWall = true;
                        float dist = Vector3.Distance(checkPos, cols23[c].ClosestPoint(checkPos));
                        if (dist < r.NearestDist) { r.NearestDist = dist; r.NearestCollider = cols23[c]; }
                    }
                }

                if (hasSolidWall)
                {
                    r.HasCharaWall = true;
                    // Track the sub-position with maximum minimum clearance
                    // from all walls — this becomes the optimal walk-through
                    // point for narrow gaps. Considers BOTH CharaWalls and
                    // the other solid foot layers so the offset doesn't push
                    // the player toward rocks.
                    float subStep = WorldmapGridGenerator.CellSize / 4f; // 0.125m
                    float bestClearance = -1f;
                    float bestOffX = 0f, bestOffZ = 0f;
                    for (int sx = -SubCellSteps; sx <= SubCellSteps; sx++)
                    {
                        for (int sz = -SubCellSteps; sz <= SubCellSteps; sz++)
                        {
                            Vector3 subPos = new Vector3(
                                worldX + sx * subStep, groundY + ProbeHeightAboveGround, worldZ + sz * subStep);
                            float minDist = MinSolidDistance(cols23, subPos, ~0);
                            if (cols != null)
                            {
                                float d2 = MinSolidDistance(cols, subPos, footSolidMask);
                                if (d2 < minDist) minDist = d2;
                            }
                            if (minDist > bestClearance)
                            {
                                bestClearance = minDist;
                                bestOffX = sx * subStep;
                                bestOffZ = sz * subStep;
                            }
                        }
                    }
                    r.CharaWallBestClearance = bestClearance;
                    if (bestClearance < MinPassableClearance)
                    {
                        r.FootBlocked = true;
                    }
                    else
                    {
                        if (Math.Abs(bestOffX) > 0.01f || Math.Abs(bestOffZ) > 0.01f)
                        {
                            r.HasClearanceOffset = true;
                            r.BestOffX = bestOffX;
                            r.BestOffZ = bestOffZ;
                        }
                        r.ClearanceValue = bestClearance;
                    }
                }
            }

            // For foot-passable cells near solid obstacles, record the
            // clearance value if it is the tightest constraint (a CharaWall
            // value may already be stored and be tighter).
            if (!r.FootBlocked && r.NearestFootSolidDist < ClearanceRecordMeters
                && r.NearestFootSolidDist < r.ClearanceValue)
                r.ClearanceValue = r.NearestFootSolidDist;

            return r;
        }

        /// <summary>Minimum ClosestPoint distance from <paramref name="pos"/> to the solid colliders on the given layers.</summary>
        private static float MinSolidDistance(Collider[] cols, Vector3 pos, int layerMask)
        {
            float minDist = float.MaxValue;
            if (cols == null) return minDist;
            for (int c = 0; c < cols.Length; c++)
            {
                if (cols[c] == null || cols[c].isTrigger) continue;
                if (((1 << cols[c].gameObject.layer) & layerMask) == 0) continue;
                float d = Vector3.Distance(pos, cols[c].ClosestPoint(pos));
                if (d < minDist) minDist = d;
            }
            return minDist;
        }

        /// <summary>
        /// One-line collider evidence for logs: name, layer, parent chain, Y extent
        /// and whether it is active, enabled and a trigger.
        /// </summary>
        public static string DescribeCollider(Collider col)
        {
            if (col == null) return "(none)";
            try
            {
                var b = col.bounds;
                return $"'{col.name}' L{col.gameObject.layer} {NavigationHandler.ColliderChain(col)} " +
                    $"y=[{b.min.y:F1},{b.max.y:F1}] active={col.gameObject.activeInHierarchy} " +
                    $"enabled={col.enabled} trigger={col.isTrigger}";
            }
            catch (Exception ex)
            {
                return $"'{col.name}' (describe failed: {ex.Message})";
            }
        }
    }
}
