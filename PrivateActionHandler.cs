using System;
using Il2CppGame;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Detects when a private action can be started in the current town and plays
    /// an audio cue. PAs in SO2R are a town-wide mode triggered by pressing Square,
    /// not collision-based triggers.
    /// Availability comes from the game's own gate, GameManager.CanChangeToPrivateAction,
    /// which also honours the per-town scenario windows where PAs are switched off
    /// (e.g. Kurik during the disaster chapter). The static locality flag alone is not
    /// enough: it only says a town supports PAs at all, never whether they are open now.
    /// </summary>
    public class PrivateActionHandler
    {
        #region Fields

        /// <summary>Last polled result. The cue plays on every false-to-true transition.</summary>
        private bool _available;
        private float _pollTimer;
        private const float PollInterval = 1.0f;

        #endregion

        #region Public Methods

        /// <summary>
        /// Called each frame from Main.UpdateHandlers().
        /// Polls PA availability once per second and plays the cue each time it becomes available,
        /// so a town that opens up mid-visit (after a blocking event ends) is announced too.
        /// </summary>
        public void Update()
        {
            if (!FieldState.IsFieldFree()) return;

            _pollTimer -= Time.deltaTime;
            if (_pollTimer > 0f) return;
            _pollTimer = PollInterval;

            bool available = CanChangeToPrivateAction();
            if (available == _available) return;
            _available = available;

            if (!available)
            {
                DebugLogger.LogState("PA notification: private action no longer available.");
                return;
            }

            if (ModSettings.PrivateActionSoundVolume > 0.001f)
                AudioCuePlayer.PlayPrivateActionCue();

            DebugLogger.LogState("PA notification: private action available.");
        }

        /// <summary>
        /// Resets state on scene change so entering a new town announces again.
        /// </summary>
        public void OnSceneChanged()
        {
            _available = false;
            _pollTimer = 2.0f;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Asks the game whether the player could switch to private action mode on the
        /// current field map right now. This is the same gate the game uses for the Square
        /// prompt, covering the locality flag, party size and scenario disable windows.
        /// </summary>
        private bool CanChangeToPrivateAction()
        {
            try
            {
                var fieldMgr = FieldManager.Instance;
                if (fieldMgr == null) return false;

                return GameManager.CanChangeToPrivateAction(fieldMgr.FieldmapID);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"PA: CanChangeToPrivateAction error: {ex.Message}");
                return false;
            }
        }

        #endregion
    }
}
