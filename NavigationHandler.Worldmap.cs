using Il2CppGame;
using System;
using UnityEngine;

namespace SO2RAccess
{
    public partial class NavigationHandler
    {
        /// <summary>
        /// True when the current field is a world map (Expel/Nede overworld).
        /// Set at the start of ScanAndOpenList, persists during auto-walk,
        /// cleared when auto-walk ends or the nav list closes.
        /// </summary>
        private bool _isWorldmap;

        /// <summary>
        /// Cached AIPathFinder for world map pathfinding.
        /// Retrieved from the player's AI controller chain on first use.
        /// </summary>
        private AIPathFinder<FieldCharacter> _wmPathFinder;

        /// <summary>Timer for world map stuck detection during auto-walk.</summary>
        private float _wmStuckTimer;

        /// <summary>Player position at the last stuck check, for distance comparison.</summary>
        private Vector3 _wmLastStuckCheckPos;

        /// <summary>
        /// Gets the world map pathfinder from the player's AI controller chain.
        /// Caches the result for subsequent calls within the same session.
        /// </summary>
        private AIPathFinder<FieldCharacter> GetWorldmapPathFinder()
        {
            if (_wmPathFinder != null) return _wmPathFinder;

            try
            {
                var player = FieldManager.Instance?.GetControlPlayer();
                if (player == null) return null;

                var aiCtrl = player.FieldAIController;
                if (aiCtrl == null) return null;

                var aiParam = aiCtrl.aiParameter;
                if (aiParam == null) return null;

                _wmPathFinder = aiParam.aiPathFinder;
                return _wmPathFinder;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV WorldmapPathFinder chain: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Checks whether a target is reachable on the world map by sampling
        /// CalcHeight at evenly spaced points along the line from player to target.
        /// If any sample has no ground (success=false), there is ocean between
        /// the player and target — the target is unreachable.
        /// Returns true as a fallback if CalcHeight throws.
        /// </summary>
        private bool WorldmapIsReachableViaCalcHeight(Vector3 playerPos, Vector3 targetPos)
        {
            try
            {
                for (int s = 1; s <= WorldmapCalcHeightSamples; s++)
                {
                    float t = s / (float)WorldmapCalcHeightSamples;
                    Vector3 samplePos = new Vector3(
                        playerPos.x + (targetPos.x - playerPos.x) * t,
                        playerPos.y + (targetPos.y - playerPos.y) * t,
                        playerPos.z + (targetPos.z - playerPos.z) * t);

                    GameUtility.CalcHeight(samplePos, out bool success);
                    if (!success)
                    {
                        DebugLogger.LogState(
                            $"NAV worldmap: ocean barrier at sample {s}/{WorldmapCalcHeightSamples} " +
                            $"toward ({targetPos.x:F1},{targetPos.z:F1})");
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV WorldmapCalcHeight: {ex.Message}");
                return true; // fallback — don't filter
            }
        }

        /// <summary>
        /// On the world map, stored waypoints are not used (coordinate wrapping
        /// makes them stale). Instead, the Update loop calls WorldmapFindPath
        /// each frame. This method just validates and sets up for auto-walk.
        /// </summary>
        private bool WorldmapCalculateAndStorePath(Vector3 playerPos, Vector3 targetPos)
        {
            // No stored waypoints on world map — the Update loop handles
            // per-frame pathfinding via WorldmapFindPath.
            _pathCorners = new Vector3[] { targetPos };
            _pathCornerIndex = 0;
            _pathRecalcTimer = 0f;
            return true;
        }

        /// <summary>
        /// Clears cached world map pathfinder when leaving the world map.
        /// </summary>
        private void ClearWorldmapCache()
        {
            _wmPathFinder = null;
        }

        /// <summary>
        /// Finds the nearest FieldMapjumpCollision to the auto-walk target and
        /// triggers it to enter the location. On the world map, location entry
        /// is handled by trigger colliders that fire OnTriggerEnter when the
        /// player walks over them. Since auto-walk uses transform.position
        /// (bypassing Unity physics triggers), we invoke ChangeFieldmap()
        /// directly on the nearest matching collider.
        /// Returns true if a mapjump was found and triggered.
        /// </summary>
        private bool TryEnterWorldmapLocation()
        {
            try
            {
                var collisions = UnityEngine.Object
                    .FindObjectsOfType<FieldMapjumpCollision>();
                if (collisions == null || collisions.Length == 0)
                {
                    DebugLogger.LogState(
                        "NAV worldmap enter: no FieldMapjumpCollision objects found.");
                    return false;
                }

                FieldMapjumpCollision nearest = null;
                float nearestDist = float.MaxValue;

                for (int i = 0; i < collisions.Length; i++)
                {
                    var c = collisions[i];
                    if (c == null) continue;
                    float dist = Vector3.Distance(
                        c.transform.position, _autoWalkTarget);
                    if (dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearest = c;
                    }
                }

                if (nearest == null)
                {
                    DebugLogger.LogState(
                        "NAV worldmap enter: no valid FieldMapjumpCollision.");
                    return false;
                }

                DebugLogger.LogState(
                    $"NAV worldmap enter: triggering mapjump " +
                    $"dist={nearestDist:F1} fieldmap={nearest.fieldmapID} " +
                    $"pos=({nearest.transform.position.x:F1}," +
                    $"{nearest.transform.position.z:F1})");

                return nearest.ChangeFieldmap();
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV worldmap enter error: {ex.Message}");
                return false;
            }
        }
    }
}
