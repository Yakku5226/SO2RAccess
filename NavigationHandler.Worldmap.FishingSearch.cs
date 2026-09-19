using Il2CppGame;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// World map fishing arrival, part two: the creep to a remembered real bubble
    /// (<see cref="WorldmapBubbleMemory"/>) and, when the water creep and the facing
    /// sweep found nothing, a search along the shore in lanes beside the creep line.
    /// Why: the bake's stand test cannot use the game's player check, and at the
    /// Lacuer east lake the bubble zone lay 7 m BESIDE the stand's creep line with
    /// the same facing (log 2026-09-19 16:17). Whatever the search finds is saved by
    /// the bubble memory, so it runs at most once per spot.
    /// </summary>
    public partial class NavigationHandler
    {
        #region Remembered bubble

        /// <summary>A remembered bubble farther than this from the stand is not this stand's business.</summary>
        private const float WmFishRememberedMaxMeters = 25f;

        /// <summary>Flat distance (m) that counts as standing on the remembered point.</summary>
        private const float WmFishRememberedReachMeters = 0.5f;

        /// <summary>Metres the creep may overshoot the stand → remembered point distance.</summary>
        private const float WmFishRememberedOvershootMeters = 3f;

        /// <summary>Remembered bubble the water creep aims at; null = creep along the baked bearing.</summary>
        private BubblePoint _wmFishBubbleGoal;

        /// <summary>Looks up the remembered bubble nearest the walk's stand; null when none is close or the map is unreadable.</summary>
        private BubblePoint FindRememberedBubble()
        {
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null) return null;
                return WorldmapBubbleMemory.NearestWithin(fm.WorldmapID, _autoWalkTarget, WmFishRememberedMaxMeters);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV WM fishing: bubble memory lookup failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Water creep stop test while aiming at a remembered bubble: reached without the
        /// player check, or walked past it. Null = keep going. On a stop the fan base
        /// becomes the facing the bubble was seen with (when known).
        /// </summary>
        private string RememberedCreepStop(Vector3 playerPos, float fromStand)
        {
            Vector3 goal = _wmFishBubbleGoal.Position;
            float toGoal = FlatDistance(playerPos, goal);
            string stop = null;
            if (toGoal <= WmFishRememberedReachMeters)
                stop = "on the remembered bubble point, player check still false";
            else if (fromStand >= FlatDistance(_autoWalkTarget, goal) + WmFishRememberedOvershootMeters)
                stop = $"walked past the remembered bubble point ({toGoal:F1} m away)";
            if (stop != null && _wmFishBubbleGoal.Facing.sqrMagnitude > 0.5f)
                _wmFishWaterBearing = _wmFishBubbleGoal.Facing.normalized;
            return stop;
        }

        #endregion

        #region Shore search

        /// <summary>Sideways distance (m) between two search lanes. The Lacuer bubble strip was ~2 m wide.</summary>
        private const float WmFishSearchLaneSpacing = 3f;

        /// <summary>Lanes per side: 4 × 3 m = 12 m each way along the shore.</summary>
        private const int WmFishSearchLanesPerSide = 4;

        /// <summary>Lane length (m) from the stand line toward the water = the water creep's cap.</summary>
        private const float WmFishSearchLaneLength = WmFishWaterCreepMaxDist;

        /// <summary>Flat distance (m) that counts as reaching a search waypoint.</summary>
        private const float WmFishSearchReachMeters = 1f;

        /// <summary>Hard time limit (s) of the whole search.</summary>
        private const float WmFishSearchMaxSeconds = 45f;

        /// <summary>After a stand-still that produced no bubble, the player check is ignored until the player moved this far.</summary>
        private const float WmFishStillRearmMeters = 1f;

        private bool _wmFishSearchActive;
        /// <summary>True once a search ran in this arrival (one per arrival).</summary>
        private bool _wmFishSearchSpent;
        private float _wmFishSearchDeadline;
        private readonly List<Vector3> _wmFishSearchPoints = new List<Vector3>();
        private int _wmFishSearchIndex;
        /// <summary>Index of the first waypoint of the second side (stall escape target).</summary>
        private int _wmFishSearchSecondSide;
        private int _wmFishSearchStallsInRow;
        private Vector3 _wmFishSearchLastPos;
        private float _wmFishSearchLastCheck;
        /// <summary>Where the last stand-still ended without a bubble.</summary>
        private Vector3 _wmFishStillFailPos;

        /// <summary>Clears the remembered-goal and search state at the start of every arrival.</summary>
        private void ResetFishSearchState()
        {
            _wmFishBubbleGoal = null;
            _wmFishSearchActive = false;
            _wmFishSearchSpent = false;
            _wmFishSearchPoints.Clear();
        }

        /// <summary>True while a failed stand-still nearby forbids the next one (search only).</summary>
        private bool StandStillBlockedHere(Vector3 playerPos) =>
            _wmFishSearchActive && FlatDistance(playerPos, _wmFishStillFailPos) < WmFishStillRearmMeters;

        /// <summary>
        /// Starts the shore search: lanes parallel to the water bearing, 3 m apart, first
        /// on one side of the creep line and then on the other, walked back and forth
        /// (the player check ends it anywhere, via <see cref="UpdateStandStill"/>).
        /// False when it already ran in this arrival.
        /// </summary>
        private bool BeginShoreSearch(FieldPlayer player, Vector3 playerPos)
        {
            if (_wmFishSearchSpent) return false;
            _wmFishSearchSpent = true;

            Vector3 bearing = _wmFishWaterBearing.sqrMagnitude > 0.5f
                ? _wmFishWaterBearing : player.transform.forward;
            bearing.y = 0f;
            bearing = bearing.sqrMagnitude < 0.0001f ? Vector3.forward : bearing.normalized;
            Vector3 side = new Vector3(bearing.z, 0f, -bearing.x);
            Vector3 stand = _autoWalkTarget;

            // Begin each side at the lane end nearer the player, then alternate.
            bool startFar = Vector3.Dot(playerPos - stand, bearing) > WmFishSearchLaneLength * 0.5f;
            _wmFishSearchPoints.Clear();
            for (int s = 0; s < 2; s++)
            {
                if (s == 1) _wmFishSearchSecondSide = _wmFishSearchPoints.Count;
                float sign = s == 0 ? 1f : -1f;
                for (int lane = 1; lane <= WmFishSearchLanesPerSide; lane++)
                {
                    Vector3 laneFoot = stand + side * (sign * lane * WmFishSearchLaneSpacing);
                    Vector3 near = laneFoot;
                    Vector3 far = laneFoot + bearing * WmFishSearchLaneLength;
                    _wmFishSearchPoints.Add(startFar ? far : near);
                    _wmFishSearchPoints.Add(startFar ? near : far);
                    startFar = !startFar;
                }
            }

            _wmFishSearchActive      = true;
            _wmFishSearchIndex       = 0;
            _wmFishSearchStallsInRow = 0;
            _wmFishSearchDeadline    = Time.time + WmFishSearchMaxSeconds;
            _wmFishSearchLastPos     = playerPos;
            _wmFishSearchLastCheck   = Time.time;
            _wmFishHoldUntil         = 0f;
            _wmFishWaterCreep        = false;
            // The stand-still may run again during the search, but not on this very spot.
            _wmFishStillSpent        = false;
            _wmFishStillFailPos      = playerPos;

            DebugLogger.LogState(
                $"NAV WM fishing: SHORE SEARCH from ({playerPos.x:F1},{playerPos.z:F1}) — " +
                $"{WmFishSearchLanesPerSide} lanes per side, {WmFishSearchLaneSpacing:F0} m apart, " +
                $"{WmFishSearchLaneLength:F0} m long, bearing ({bearing.x:F2},{bearing.z:F2}), " +
                $"up to {WmFishSearchMaxSeconds:F0} s.");
            ScreenReader.Say(Loc.Get("nav_fish_searching_shore"));
            CreepToward(_wmFishSearchPoints[0], playerPos);
            return true;
        }

        /// <summary>One frame of the shore search: next waypoint when reached or stalled, verdict at the end.</summary>
        private void UpdateShoreSearch(FieldPlayer player, Vector3 playerPos)
        {
            if (Time.time >= _wmFishSearchDeadline)
            {
                EndShoreSearch(player, playerPos, "time limit");
                return;
            }

            if (FlatDistance(playerPos, _wmFishSearchPoints[_wmFishSearchIndex]) <= WmFishSearchReachMeters)
            {
                _wmFishSearchStallsInRow = 0;
                _wmFishSearchIndex++;
            }
            else if (Time.time - _wmFishSearchLastCheck >= WmFishWaterCreepStallSeconds)
            {
                float moved = FlatDistance(playerPos, _wmFishSearchLastPos);
                _wmFishSearchLastPos   = playerPos;
                _wmFishSearchLastCheck = Time.time;
                if (moved < WmFishWaterCreepStallDist)
                {
                    _wmFishSearchStallsInRow++;
                    int from = _wmFishSearchIndex;
                    // Two stalls in a row = this side is walled off: jump to the other side.
                    _wmFishSearchIndex = _wmFishSearchStallsInRow >= 2 && from < _wmFishSearchSecondSide
                        ? _wmFishSearchSecondSide
                        : from + 1;
                    if (_wmFishSearchStallsInRow >= 2 && from >= _wmFishSearchSecondSide)
                        _wmFishSearchIndex = _wmFishSearchPoints.Count;
                    DebugLogger.LogState(
                        $"NAV WM fishing: search stalled at ({playerPos.x:F1},{playerPos.z:F1}) on waypoint {from} " +
                        $"({_wmFishSearchStallsInRow} in a row) — continuing with waypoint {_wmFishSearchIndex}.");
                }
            }

            if (_wmFishSearchIndex >= _wmFishSearchPoints.Count)
            {
                EndShoreSearch(player, playerPos, "all lanes walked");
                return;
            }
            CreepToward(_wmFishSearchPoints[_wmFishSearchIndex], playerPos);
        }

        private void EndShoreSearch(FieldPlayer player, Vector3 playerPos, string why)
        {
            _wmFishSearchActive = false;
            DebugLogger.LogState(
                $"NAV WM fishing: shore search ended without a bubble ({why}) at ({playerPos.x:F1},{playerPos.z:F1}).");
            EndFishArrivalWithoutBubble(player, "shore search ended");
        }

        /// <summary>The honest end of a fishing arrival that never got the bubble.</summary>
        private void EndFishArrivalWithoutBubble(FieldPlayer player, string diagContext)
        {
            _wmFishCreepActive = false;
            string label = _autoWalkLabel;
            float shortBy = _wmFishStandShortMeters;
            LogFishingArrivalDiag(player, diagContext);
            StopAutoWalk();
            AnnounceArrival(Loc.Get(FishingNoPromptKey(shortBy), label, shortBy.ToString("F1")));
        }

        #endregion
    }
}
