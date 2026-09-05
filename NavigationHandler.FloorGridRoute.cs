using Il2CppGame;
using System;
using UnityEngine;

namespace SO2RAccess
{
    public partial class NavigationHandler
    {
        #region Floor-grid routes (discovery only — spoken, never driven)

        /// <summary>
        /// The floor grid for the current field map, built on first use and kept
        /// until the map changes. Null when no map is loaded or the build failed.
        /// </summary>
        private FloorProbeGrid _floorGrid;

        /// <summary>Map id the cached grid belongs to.</summary>
        private string _floorGridMapId;

        /// <summary>True once a build failed for this map, so we do not retry every second.</summary>
        private bool _floorGridBuildFailed;

        /// <summary>Drops the cached grid (map change).</summary>
        private void InvalidateFloorGrid()
        {
            _floorGrid = null;
            _floorGridMapId = null;
            _floorGridBuildFailed = false;
        }

        /// <summary>
        /// Returns the current map's floor grid, building it on first use (a few
        /// hundred milliseconds, logged). Field maps only.
        /// </summary>
        private FloorProbeGrid GetFloorGrid(Vector3 playerPos)
        {
            var fm = FieldManager.Instance;
            if (fm == null || fm.IsWorldmap()) return null;
            string mapId = fm.currentFieldmapID.ToString();

            if (_floorGrid != null && _floorGridMapId == mapId) return _floorGrid;
            if (_floorGridBuildFailed && _floorGridMapId == mapId) return null;

            _floorGridMapId = mapId;
            var grid = new FloorProbeGrid();
            if (!grid.Build(mapId, playerPos, out string why))
            {
                _floorGridBuildFailed = true;
                DebugLogger.LogState($"NAV floor grid: build failed on {mapId}: {why}");
                return null;
            }
            _floorGrid = grid;
            _floorGridBuildFailed = false;
            DebugLogger.LogState(
                $"NAV floor grid: built {mapId} nodes={grid.NodeCount} components={grid.ComponentCount} ms={grid.BuildMs:F0}");
            return grid;
        }

        /// <summary>
        /// A floor-grid route from the player to a target, honouring the map-exit
        /// barrier like every other route. Used only when breadcrumbs and the
        /// NavMesh have no answer. Sets <see cref="_lastPathBlockedByExit"/> when
        /// the only grid route would leave the map.
        /// </summary>
        private bool TryFloorGridRoute(Vector3 playerPos, Vector3 target,
            out Vector3[] corners, out float length)
        {
            corners = null;
            length = 0f;
            var grid = GetFloorGrid(playerPos);
            if (grid == null) return false;

            if (!grid.TryRoute(playerPos, target, out corners, out length))
            {
                DebugLogger.LogState("NAV floor grid: no route (no node under an end, or different components).");
                return false;
            }
            if (!_autoWalkAllowExit && PathCrossesMapExit(corners))
            {
                _lastPathBlockedByExit = true;
                DebugLogger.LogState("NAV floor grid: route rejected — it would cross a map exit.");
                corners = null;
                return false;
            }
            DebugLogger.LogState(
                $"NAV floor grid: UNVERIFIED route length={length:F0}m corners={corners.Length}");
            return true;
        }

        /// <summary>
        /// Spoken tail for the "could not reach, it is above/below you" message:
        /// whether the floor grid sees a route at all, how long it is, and where
        /// along it the climb (or descent) begins. Empty when the grid has no
        /// route — the message then stays exactly as before.
        /// </summary>
        private string FloorGridRouteHint(Vector3 playerPos, Vector3 target, bool targetAbove)
        {
            try
            {
                if (!TryFloorGridRoute(playerPos, target, out var corners, out float length))
                    return "";

                int meters = Mathf.RoundToInt(length);
                // First stretch that changes height in the target's direction by
                // at least a floor: that is where the player must find the ramp.
                for (int i = 0; i + 1 < corners.Length; i++)
                {
                    float dy = corners[i + 1].y - corners[i].y;
                    if (targetAbove ? dy < FloorChangeThreshold * 0.5f : dy > -FloorChangeThreshold * 0.5f)
                        continue;
                    float dxz = FlatDistance(corners[i], corners[i + 1]);
                    if (dxz > 0.5f && Mathf.Abs(dy) / dxz < FloorProbeGrid.RampMinRatio) continue;

                    Vector3 start = corners[i];
                    int dist = Mathf.RoundToInt(FlatDistance(playerPos, start));
                    string compass = GetCompassDirection(playerPos, start);
                    return Loc.Get(targetAbove ? "nav_autowalk_route_hint_up" : "nav_autowalk_route_hint_down",
                        meters, dist, compass);
                }
                return Loc.Get("nav_autowalk_route_hint", meters);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV floor grid hint error: {ex.Message}");
                return "";
            }
        }

        #endregion
    }
}
