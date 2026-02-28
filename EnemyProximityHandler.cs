using System;
using System.Collections.Generic;
using Il2CppGame;
using MelonLoader;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Plays a spatial audio cue when field enemies are nearby.
    /// Volume scales with distance, stereo pans with direction relative to the player.
    /// Scans for enemies periodically and tracks the closest one each frame.
    /// </summary>
    public class EnemyProximityHandler
    {
        #region Constants

        /// <summary>Distance (units) beyond which the cue is silent and stops.</summary>
        private const float MaxDistance = 25f;

        /// <summary>Distance (units) at or below which the cue plays at full volume.</summary>
        private const float MinDistance = 3f;

        /// <summary>Frames between full enemy scans (FindObjectsOfType is expensive).</summary>
        private const int ScanIntervalFrames = 60;

        #endregion

        #region Fields

        private int _scanTimer;
        private readonly List<Transform> _cachedEnemies = new List<Transform>();
        private bool _isPlaying;

        #endregion

        #region Public Methods

        /// <summary>
        /// Called every frame from Main.UpdateHandlers().
        /// Finds the closest enemy and drives SpatialAudioPlayer with volume/pan.
        /// </summary>
        public void Update()
        {
            if (!SpatialAudioPlayer.IsPlaying && !CanActivate())
            {
                // Not on the field — nothing to do.
                return;
            }

            // If we were playing but the field is no longer free, stop.
            if (!IsFieldFree())
            {
                if (_isPlaying)
                {
                    SpatialAudioPlayer.Stop();
                    _isPlaying = false;
                }
                return;
            }

            var fm = FieldManager.Instance;
            if (fm == null) return;

            var player = fm.GetControlPlayer();
            if (player == null) return;

            Vector3 playerPos;
            Vector3 playerForward;
            try
            {
                playerPos = player.transform.position;
                playerForward = player.transform.forward;
            }
            catch
            {
                return;
            }

            // --- Periodic full scan ---
            _scanTimer--;
            if (_scanTimer <= 0)
            {
                _scanTimer = ScanIntervalFrames;
                ScanEnemies();
            }

            // --- Find closest enemy from cache ---
            float closestDist = float.MaxValue;
            Vector3 closestPos = Vector3.zero;
            bool found = false;

            for (int i = _cachedEnemies.Count - 1; i >= 0; i--)
            {
                Transform t = _cachedEnemies[i];

                // Enemy may have been destroyed (entered battle, despawned, etc.)
                try
                {
                    if (t == null || t.gameObject == null)
                    {
                        _cachedEnemies.RemoveAt(i);
                        continue;
                    }
                }
                catch
                {
                    _cachedEnemies.RemoveAt(i);
                    continue;
                }

                try
                {
                    Vector3 enemyPos = t.position;
                    float dist = Vector3.Distance(playerPos, enemyPos);

                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closestPos = enemyPos;
                        found = true;
                    }
                }
                catch
                {
                    _cachedEnemies.RemoveAt(i);
                }
            }

            // --- No enemy in range ---
            if (!found || closestDist > MaxDistance)
            {
                if (_isPlaying)
                {
                    SpatialAudioPlayer.Stop();
                    _isPlaying = false;
                }
                return;
            }

            // --- Calculate volume (linear falloff) ---
            float volume;
            if (closestDist <= MinDistance)
                volume = 1f;
            else
                volume = 1f - (closestDist - MinDistance) / (MaxDistance - MinDistance);

            // --- Calculate panning (player-relative direction) ---
            Vector3 toEnemy = closestPos - playerPos;
            toEnemy.y = 0f; // horizontal only
            Vector3 forward = new Vector3(playerForward.x, 0f, playerForward.z);

            float pan = 0f;
            if (toEnemy.sqrMagnitude > 0.01f && forward.sqrMagnitude > 0.01f)
            {
                float angle = Vector3.SignedAngle(forward.normalized, toEnemy.normalized, Vector3.up);
                pan = Mathf.Clamp(angle / 90f, -1f, 1f);
            }

            // --- Drive audio ---
            SpatialAudioPlayer.SetVolumePan(volume, pan);

            if (!_isPlaying)
            {
                SpatialAudioPlayer.Start();
                _isPlaying = true;
            }
        }

        /// <summary>
        /// Called on scene change. Clears enemy cache and stops audio.
        /// </summary>
        public void OnSceneChanged()
        {
            _cachedEnemies.Clear();
            _scanTimer = 0; // force immediate scan next Update

            if (_isPlaying)
            {
                SpatialAudioPlayer.Stop();
                _isPlaying = false;
            }
        }

        /// <summary>
        /// Applies Harmony patches. This handler is polling-only — no patches needed.
        /// Included for interface consistency with other handlers.
        /// </summary>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            // No Harmony patches required — all data is read via polling.
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Full scan using FindObjectsOfType. Called on a timer to discover new enemies
        /// and remove stale ones.
        /// </summary>
        private void ScanEnemies()
        {
            _cachedEnemies.Clear();

            try
            {
                var found = UnityEngine.Object.FindObjectsOfType<FieldEnemy>();
                if (found == null || found.Length == 0) return;

                for (int i = 0; i < found.Length; i++)
                {
                    var enemy = found[i];
                    if (enemy == null) continue;

                    try
                    {
                        _cachedEnemies.Add(enemy.transform);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"EnemyProximityHandler.ScanEnemies error: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns true if the player is on the field with no menus blocking.
        /// </summary>
        private bool IsFieldFree()
        {
            try
            {
                if (FieldManager.Instance == null) return false;
                if (FieldManager.Instance.GetControlPlayer() == null) return false;
                if (CampMenuHandler.IsCampOpen) return false;
                if (ShopHandler.IsShopOpen) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Quick check whether the handler can potentially activate.
        /// Avoids expensive work when clearly not on the field.
        /// </summary>
        private bool CanActivate()
        {
            try
            {
                return FieldManager.Instance != null;
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}
