using Il2CppGame;
using System;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Battle resume for spoken directions. A scene change while directions
    /// run (a battle scene loading, most of the time) remembers the destination
    /// so the directions come back on their own once the battle is over. Mirrors
    /// the auto-walk battle resume — only a battle resumes; anything else
    /// (a cutscene, a menu) is dropped after a short grace period.
    /// </summary>
    public partial class NavigationHandler
    {
        #region State

        /// <summary>True when directions were interrupted by a scene change and may resume.</summary>
        private bool _guideResumePending;
        /// <summary>True once a battle was detected during the pending window.</summary>
        private bool _guideResumeBattleSeen;
        /// <summary>Seconds the field has been free without a battle having been seen.</summary>
        private float _guideResumeFreeTimer;
        private Vector3   _guideResumeTarget;
        private string    _guideResumeLabel;
        private Transform _guideResumeTransform;
        private int       _guideResumeCategoryIndex;
        private bool      _guideResumeIsCounter;
        private FieldmapID _guideResumeMapId;

        // World map identity (2026-09-06). Without the ring goal a resumed
        // location route would aim at the town centre inside the walls, and
        // without the water point a fishing resume would lose its arrival rule —
        // the same two lessons auto-walk's resume learned.
        private bool     _guideResumeOnWorldmap;
        private Vector3  _guideResumeWmGoal;
        private Vector3  _guideResumeWmCentre;
        private Vector3? _guideResumeWmFace;
        private bool     _guideResumeWmFishing;
        private bool     _guideResumeWmFloorTier;

        #endregion

        /// <summary>
        /// Scene change while directions run: remembers the destination so the
        /// directions come back on their own once the battle is over, then stops.
        /// </summary>
        public void OnSceneChangeGuidance()
        {
            if (_guideActive)
            {
                _guideResumePending       = true;
                _guideResumeBattleSeen    = false;
                _guideResumeFreeTimer     = 0f;
                _guideResumeTarget        = _guideTarget;
                _guideResumeLabel         = _guideLabel;
                _guideResumeTransform     = _guideTransform;
                _guideResumeCategoryIndex = _guideCategoryIndex;
                _guideResumeIsCounter     = _guideIsCounter;
                _guideResumeOnWorldmap    = _guideOnWorldmap;
                _guideResumeWmGoal        = _guideWmGoal;
                _guideResumeWmCentre      = _guideWmCentre;
                _guideResumeWmFace        = _guideWmFace;
                _guideResumeWmFishing     = _guideWmFishing;
                _guideResumeWmFloorTier   = _guideWmFloorTier;
                try { _guideResumeMapId = FieldManager.Instance?.currentFieldmapID ?? FieldmapID.INVALID; }
                catch { _guideResumeMapId = FieldmapID.INVALID; }
                DebugLogger.LogState($"NAV guidance: scene change, saving potential resume for '{_guideLabel}'.");
            }
            StopGuidance("scene change");
        }

        /// <summary>Drops a pending directions resume.</summary>
        private void ClearGuideResume()
        {
            _guideResumePending    = false;
            _guideResumeBattleSeen = false;
            _guideResumeFreeTimer  = 0f;
            _guideResumeTransform  = null;
            _guideResumeOnWorldmap = false;
            _guideResumeWmFace     = null;
            _guideResumeWmFishing  = false;
        }

        /// <summary>
        /// Per-frame handler for a pending directions resume (called from Update
        /// while nothing else is guiding or walking). Resumes once a battle was
        /// seen and the field is free again on the same map; a non-battle
        /// interruption is dropped after a short grace period.
        /// </summary>
        private void UpdateGuideResume()
        {
            try
            {
                if (IsBattleActive())
                {
                    _guideResumeBattleSeen = true;
                    _guideResumeFreeTimer = 0f;
                }
                if (!IsFieldFree())
                {
                    _guideResumeFreeTimer = 0f;
                    return;
                }

                var fm = FieldManager.Instance;
                if (fm != null && _guideResumeMapId != FieldmapID.INVALID
                    && fm.currentFieldmapID != _guideResumeMapId)
                {
                    DebugLogger.LogState("NAV guidance resume: map changed, discarding.");
                    ClearGuideResume();
                    return;
                }

                if (_guideResumeBattleSeen)
                {
                    ResumeGuidance();
                    return;
                }

                _guideResumeFreeTimer += Time.deltaTime;
                if (_guideResumeFreeTimer >= GuideResumeDiscardDelay)
                {
                    DebugLogger.LogState("NAV guidance resume: non-battle interruption, discarding.");
                    ClearGuideResume();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV guidance resume error: {ex.Message}");
                ClearGuideResume();
            }
        }

        /// <summary>Re-routes from the player's current position and restarts the directions.</summary>
        private void ResumeGuidance()
        {
            Vector3 target = _guideResumeTarget;
            if (_guideResumeTransform != null)
            {
                try { target = _guideResumeTransform.position; }
                catch { _guideResumeTransform = null; }
            }
            string label = _guideResumeLabel;
            var transform = _guideResumeTransform;
            int category = _guideResumeCategoryIndex;
            bool isCounter = _guideResumeIsCounter;
            bool onWorldmap = _guideResumeOnWorldmap;
            var wmResume = new WorldmapGuideResume
            {
                Goal      = _guideResumeWmGoal,
                Centre    = _guideResumeWmCentre,
                Face      = _guideResumeWmFace,
                Fishing   = _guideResumeWmFishing,
                FloorTier = _guideResumeWmFloorTier
            };
            ClearGuideResume();

            if (!TryGetPlayerPosition(out Vector3 playerPos)) return;

            if (onWorldmap)
            {
                if (!ResumeWorldmapGuidance(playerPos, target, label, transform, category, wmResume))
                    DebugLogger.LogState($"NAV guidance resume: no world map route to '{label}' after battle, discarding.");
                return;
            }

            if (!TryRouteForGuidance(playerPos, target, category, isCounter, out bool unverified))
            {
                DebugLogger.LogState($"NAV guidance resume: no route to '{label}' after battle, discarding.");
                return;
            }
            StartGuidance(playerPos, target, label, transform, category, isCounter, unverified,
                onWorldmap: false, Loc.Get("nav_guide_resuming", label));
        }

        /// <summary>What a world map resume needs besides the target itself.</summary>
        private struct WorldmapGuideResume
        {
            public Vector3  Goal;
            public Vector3  Centre;
            public Vector3? Face;
            public bool     Fishing;
            public bool     FloorTier;
        }
    }
}
