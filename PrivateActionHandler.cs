using System;
using Il2CppGame;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Detects when a private action is available in the current town and plays
    /// an audio cue. PAs in SO2R are a town-wide
    /// mode triggered by pressing Square — not collision-based triggers.
    /// Availability is determined by the locality parameter's IsPrivateAction flag.
    /// </summary>
    public class PrivateActionHandler
    {
        #region Fields

        private bool _announced;
        private float _pollTimer;
        private const float PollInterval = 1.0f;

        #endregion

        #region Public Methods

        /// <summary>
        /// Called each frame from Main.UpdateHandlers().
        /// Checks if PAs are available in the current location and announces once per visit.
        /// </summary>
        public void Update()
        {
            if (_announced) return;
            if (!FieldState.IsFieldFree()) return;

            _pollTimer -= Time.deltaTime;
            if (_pollTimer > 0f) return;
            _pollTimer = PollInterval;

            if (!IsPALocation()) return;

            if (ModSettings.PrivateActionSoundVolume > 0.001f)
                AudioCuePlayer.PlayPrivateActionCue();

            DebugLogger.LogState("PA notification: private action available.");
            _announced = true;
        }

        /// <summary>
        /// Resets state on scene change so entering a new town announces again.
        /// </summary>
        public void OnSceneChanged()
        {
            _announced = false;
            _pollTimer = 2.0f;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Checks if the current field location supports private actions
        /// via the locality parameter.
        /// </summary>
        private bool IsPALocation()
        {
            try
            {
                var fieldMgr = FieldManager.Instance;
                if (fieldMgr == null) return false;

                var paramMgr = ParameterManager.Instance;
                if (paramMgr == null) return false;

                var localityParam = paramMgr.GetLocalityParameter(fieldMgr.FieldmapID);
                return localityParam != null && localityParam.IsPrivateAction;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"PA: IsPALocation error: {ex.Message}");
                return false;
            }
        }

        #endregion
    }
}
