using Il2CppGame;
using System;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// World map auto-walk resume after a battle. Mirrors the field-map resume
    /// (NavigationHandler.AutoWalk.cs): an interrupted walk is only resumed when
    /// a battle was actually seen; any other interruption (fast-travel window,
    /// dialogue, menu) is discarded after a short grace period. Before this
    /// classification existed, confirming a fast travel mid-walk fired the resume
    /// during the map transition and spoke "grid not found" / "cannot reach"
    /// (2026-09-17 log).
    /// </summary>
    public partial class NavigationHandler
    {
        /// <summary>True once a battle was observed while the resume is pending.</summary>
        private bool _wmResumeBattleSeen;

        /// <summary>Seconds the field has been continuously free while the resume is pending.</summary>
        private float _wmResumeFreeTimer;

        /// <summary>Time.time of the last battle resume, for <see cref="WmResumeCarryWindow"/>.</summary>
        private float _wmLastBattleResumeTime = -999f;

        /// <summary>
        /// Seconds the field must stay free after a battle before the walk resumes.
        /// The post-battle return blocks the field again for ~0.2 s shortly after
        /// it first reads free (2026-09-17 log: resume at +0.19 s, re-interrupted
        /// at +0.5 s), which cancelled a walk resumed too early.
        /// </summary>
        private const float WmResumeSettleDelay = 0.5f;

        /// <summary>
        /// A walk interrupted within this many seconds of a battle resume still
        /// counts as battle-interrupted (the post-battle blip), not as a menu.
        /// </summary>
        private const float WmResumeCarryWindow = 3f;

        /// <summary>
        /// Saves the current world map auto-walk so it can be resumed after a
        /// battle, then cancels the walk. Called when the field stops being free.
        /// </summary>
        private void SaveWorldmapResume()
        {
            _wmResumeActive = true;
            // A blip right after a battle resume is the post-battle return
            // settling, not a new interruption — keep the battle credit.
            _wmResumeBattleSeen =
                Time.time - _wmLastBattleResumeTime < WmResumeCarryWindow;
            _wmResumeFreeTimer = 0f;
            _wmResumeTarget = _autoWalkTarget;
            _wmResumeLabel = _autoWalkLabel;
            _wmResumeCategoryIndex = _autoWalkCategoryIndex;
            _wmResumeTransform = _autoWalkTransform;
            // Fishing identity too — CancelAutoWalk clears both, and without
            // them the resumed walk skips the bubble-confirmed arrival
            // (false "Arrived").
            _wmResumeIsFishing = _autoWalkIsFishing;
            _wmResumeFacePosition = _autoWalkFacePosition;
            // Blocked positions are kept across battles on purpose.
            DebugLogger.LogState(
                $"NAV worldmap: interrupted, saving resume for '{_autoWalkLabel}'" +
                (_wmResumeBattleSeen ? " (within the post-battle carry window)." : "."));
            CancelAutoWalk();
        }

        /// <summary>Drops a pending world map resume.</summary>
        private void ClearWorldmapResume()
        {
            _wmResumeActive = false;
            _wmResumeBattleSeen = false;
            _wmResumeFreeTimer = 0f;
            _wmResumeTransform = null;
        }

        /// <summary>
        /// Per-frame handler for a pending world map resume. Called from Update()
        /// while a resume is pending and auto-walk is not running. Resumes once a
        /// battle has come and gone; discards non-battle interruptions after
        /// <see cref="FieldResumeDiscardDelay"/> and any interruption that leaves
        /// the world map.
        /// </summary>
        private void UpdateWorldmapResume()
        {
            try
            {
                if (IsBattleActive())
                {
                    _wmResumeBattleSeen = true;
                    _wmResumeFreeTimer = 0f;
                }

                if (!IsFieldFree())
                {
                    _wmResumeFreeTimer = 0f;
                    return;
                }

                var fm = FieldManager.Instance;
                if (fm == null || !fm.IsWorldmap())
                {
                    DebugLogger.LogState("NAV worldmap resume: left the world map, discarding.");
                    ClearWorldmapResume();
                    return;
                }

                _wmResumeFreeTimer += Time.deltaTime;

                if (_wmResumeBattleSeen)
                {
                    // Battle over. Let the post-battle return settle before
                    // walking, or the walk is cancelled again within frames.
                    if (_wmResumeFreeTimer >= WmResumeSettleDelay)
                        ResumeWorldmapAutoWalk(fm);
                    return;
                }

                // No battle seen yet. Wait out the encounter-transition gap before
                // concluding this was a menu/dialogue interruption and dropping it.
                if (_wmResumeFreeTimer >= FieldResumeDiscardDelay)
                {
                    DebugLogger.LogState("NAV worldmap resume: non-battle interruption, discarding.");
                    ClearWorldmapResume();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV worldmap resume error: {ex.Message}");
                ClearWorldmapResume();
            }
        }

        /// <summary>
        /// Restores the saved world map walk, re-plans from the player's current
        /// position to the same entrance ring point, and resumes with a spoken
        /// confirmation. Announces unreachable instead of walking blind when the
        /// post-battle position has no route.
        /// </summary>
        private void ResumeWorldmapAutoWalk(FieldManager fm)
        {
            DebugLogger.LogState(
                $"NAV worldmap: resuming auto-walk to '{_wmResumeLabel}'.");

            _autoWalkTarget = _wmResumeTarget;
            _autoWalkLabel = _wmResumeLabel;
            _autoWalkCategoryIndex = _wmResumeCategoryIndex;
            _autoWalkTransform = _wmResumeTransform;
            _autoWalkIsFishing = _wmResumeIsFishing;
            _autoWalkFacePosition = _wmResumeFacePosition;
            _isWorldmap = true;
            ClearWorldmapResume();
            _wmLastBattleResumeTime = Time.time;

            // Update target from live transform if available.
            if (_autoWalkTransform != null)
                _autoWalkTarget = _autoWalkTransform.position;

            // Resume goal: locations re-plan to the stored ring point (the
            // entrance), NOT the town-centre symbol — a centre-aimed resume
            // always collapses to a wall-hugging floor route (2026-07-10).
            Vector3 resumeGoal = _autoWalkCategoryIndex == CAT_LOCATION
                ? _wmPathGoal : _autoWalkTarget;

            var player = fm.GetControlPlayer();
            if (player == null)
            {
                DebugLogger.LogState("NAV worldmap resume: no control player, discarding.");
                return;
            }

            Vector3 playerPos = player.transform.position;
            bool resumePathFound = WorldmapCalculateAndStorePath(
                playerPos, resumeGoal, keepBlockedPositions: true);

            if (!resumePathFound)
            {
                // Post-battle position has no route to the target (e.g. pushed
                // into a sealed pocket). Announce instead of walking blind.
                ScreenReader.Say(Loc.Get("nav_autowalk_unreachable", _autoWalkLabel));
                DebugLogger.LogState(
                    $"NAV resume: no path to '{_autoWalkLabel}' after battle — resume abandoned.");
                return;
            }

            _isAutoWalking = true;
            _staticIsAutoWalking = true;
            _wmStuckTimer = 0f;
            _wmLastStuckCheckPos = playerPos;
            _wmDiagTimer = 0f;
            // _wmRecalcCount and _wmBlockedPositions are kept so the walk
            // remembers previously stuck areas.
            ScreenReader.Say(Loc.Get("nav_autowalk_resuming", _autoWalkLabel));
            DebugLogger.LogState(
                $"NAV auto-walk resumed. target={_autoWalkLabel} " +
                $"waypoints={_wmPathWaypoints?.Length ?? 0}");
        }
    }
}
