using System;
using Il2CppGame;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SO2RAccess
{
    /// <summary>
    /// Manual navigation by ear on field, town and dungeon maps: four looping wall
    /// tones (front / right / behind / left of the camera) that grow louder as a
    /// wall gets closer, plus looping beacons that pan toward nearby objects
    /// (see <c>ManualNavHandler.Beacons.cs</c>). Everything plays through
    /// <see cref="LoopMixer"/>; every cue has its own on/off and volume in the
    /// mod menu and there is no master switch.
    ///
    /// Walls come from <see cref="WallProbe"/>, whose slope-safe rules were gated
    /// on the breadcrumb audit (F11). Wall tones are muted while auto-walk steers
    /// the player; beacons keep playing. Everything fades out whenever the field
    /// is not free (menus, dialogue, battle, cutscenes) or on the world map.
    /// Polling only — no Harmony patches.
    /// </summary>
    public partial class ManualNavHandler
    {
        #region Tuning

        /// <summary>Seconds between probe / beacon updates. Gains glide in the mixer between ticks.</summary>
        private const float TickInterval = 0.1f;

        /// <summary>A wall this close (m) or closer plays at the cue's full volume.</summary>
        private const float WallFullDistance = 0.6f;

        /// <summary>A wall voice that has been silent this long (s) is released.</summary>
        private const float WallVoiceLinger = 2f;

        /// <summary>Seconds between debug-mode readouts of the four wall distances.</summary>
        private const float DebugLogInterval = 0.5f;

        #endregion

        #region Fields

        private readonly NavigationHandler _nav;
        private readonly MixerVoice[] _wallVoices = new MixerVoice[4];
        private readonly float[] _wallSilentFor = new float[4];
        private float _tickTimer;
        private float _debugTimer;

        #endregion

        /// <param name="navigationHandler">Source of beacon targets and the auto-walk state.</param>
        public ManualNavHandler(NavigationHandler navigationHandler)
        {
            _nav = navigationHandler;
            SoundBank.Preload(Array.ConvertAll(NavCues.All, NavCues.FileName));
        }

        #region Public Methods

        /// <summary>Called every frame from Main.UpdateHandlers().</summary>
        public void Update()
        {
            if (!AnyCueEnabled())
            {
                StopAll();
                return;
            }

            if (!FieldState.IsFieldFree() || IsWorldmap())
            {
                StopAll();
                return;
            }

            _tickTimer += Time.deltaTime;
            if (_tickTimer < TickInterval) return;
            float dt = _tickTimer;
            _tickTimer = 0f;

            if (!TryGetPlayerPosition(out Vector3 playerPos))
            {
                StopAll();
                return;
            }

            Vector3 camForward = WallProbe.CameraForwardFlat();
            UpdateWalls(playerPos, camForward, dt);
            UpdateBump(playerPos, camForward, dt);
            UpdateBeacons(playerPos, camForward);
        }

        /// <summary>Called on scene change: silences everything.</summary>
        public void OnSceneChanged() => StopAll();

        /// <summary>Polling-only handler — no patches. Kept for handler consistency.</summary>
        public void ApplyPatches(HarmonyLib.Harmony harmony) { }

        #endregion

        #region Walls

        /// <summary>
        /// Probes the four camera-relative directions and drives one voice each:
        /// gain rises linearly from silent at <see cref="ModSettings.WallRangeMeters"/> to the
        /// cue's volume at <see cref="WallFullDistance"/>. Left/right tones are
        /// panned hard to their side; front and behind sit in the centre and are
        /// told apart by pitch (the cues are tuned notes).
        /// </summary>
        private void UpdateWalls(Vector3 playerPos, Vector3 camForward, float dt)
        {
            bool anyWall = false;
            foreach (var kind in NavCues.Walls)
                if (ModSettings.NavCue(kind).Enabled) { anyWall = true; break; }

            if (!anyWall || _nav.IsAutoWalking)
            {
                for (int i = 0; i < 4; i++) DriveWall(i, 0f, dt);
                return;
            }

            bool describe = Main.DebugMode;
            float range = Mathf.Max(ModSettings.WallRangeMeters, WallFullDistance + 0.5f);
            WallProbe.Reading[] readings = WallProbe.ProbeAround(playerPos, camForward, range, describe);

            for (int i = 0; i < 4; i++)
            {
                var cue = ModSettings.NavCue(NavCues.Walls[i]);
                float gain = 0f;
                if (cue.Enabled && readings[i].HasObstacle)
                {
                    float t = (range - readings[i].Distance) / (range - WallFullDistance);
                    gain = cue.Volume * Mathf.Clamp01(t);
                }
                DriveWall(i, gain, dt);
            }

            if (describe)
            {
                _debugTimer += dt;
                if (_debugTimer >= DebugLogInterval)
                {
                    _debugTimer = 0f;
                    DebugLogger.LogState(
                        $"[WALLS] F {readings[WallProbe.Front]} | R {readings[WallProbe.Right]} | " +
                        $"B {readings[WallProbe.Behind]} | L {readings[WallProbe.Left]}");
                }
            }
        }

        /// <summary>Starts, steers or (after lingering silent) releases one wall voice.</summary>
        private void DriveWall(int index, float gain, float dt)
        {
            float pan = index == WallProbe.Right ? 1f : index == WallProbe.Left ? -1f : 0f;
            var voice = _wallVoices[index];

            if (gain > 0f)
            {
                _wallSilentFor[index] = 0f;
                if (voice == null || !voice.IsActive)
                    _wallVoices[index] = LoopMixer.Play(NavCues.FileName(NavCues.Walls[index]), gain, pan, phase01: 0f);
                else
                    voice.Set(gain, pan);
                return;
            }

            if (voice == null) return;
            voice.Set(0f, pan);
            _wallSilentFor[index] += dt;
            if (_wallSilentFor[index] >= WallVoiceLinger)
            {
                voice.Stop();
                _wallVoices[index] = null;
            }
        }

        #endregion

        #region Wall bump

        // A one-shot cue for the wall you are actually touching: the player pushes
        // the stick (or WASD / arrows) yet the character does not move. Unlike the
        // wall tones this cannot be wrong, because it reports being blocked instead
        // of predicting a wall (idea from the reference mod's collision sound).
        // FRAMEWORK ONLY (2026-09-06): NavBump.wav is not chosen yet, the kind is
        // hidden from the menu (NavCues.All) and disabled by default, so this code
        // is inert until a sound is added. TODO: pick the sound, add Bump to All.

        /// <summary>Stick deflection (or a held key) that counts as trying to walk.</summary>
        private const float BumpIntent = 0.5f;
        /// <summary>Below this speed (m/s) a pushing player counts as blocked.</summary>
        private const float BumpStuckSpeed = 0.3f;
        /// <summary>Blocked this long (s) before the first bump, so a stumble is not a wall.</summary>
        private const float BumpHoldSeconds = 0.15f;
        /// <summary>Repeat while still pushing into the same wall (s).</summary>
        private const float BumpRepeatSeconds = 0.6f;
        /// <summary>A push direction this different (degrees) bumps again at once.</summary>
        private const float BumpTurnDegrees = 25f;
        /// <summary>The cue plays this long at most (s); the loop mixer stops it.</summary>
        private const float BumpPlaySeconds = 0.5f;

        private Vector3 _bumpLastPos;
        private bool _bumpHavePos;
        private float _bumpBlockedFor;
        private float _bumpLastPlayTime = -10f;
        private Vector3 _bumpLastDir;
        private bool _bumpMissingLogged;

        /// <summary>
        /// Compares the walk the player is asking for with the distance actually
        /// covered this tick; pushing while standing still for
        /// <see cref="BumpHoldSeconds"/> plays the bump cue.
        /// </summary>
        private void UpdateBump(Vector3 playerPos, Vector3 camForward, float dt)
        {
            var cue = ModSettings.NavCue(NavCueKind.Bump);
            if (!cue.Enabled || _nav.IsAutoWalking || !TryGetMoveIntent(camForward, out Vector3 pushDir))
            {
                ResetBump(playerPos);
                return;
            }

            Vector3 flatMove = playerPos - _bumpLastPos;
            flatMove.y = 0f;
            float speed = _bumpHavePos && dt > 0f ? flatMove.magnitude / dt : BumpStuckSpeed + 1f;
            _bumpLastPos = playerPos;
            _bumpHavePos = true;

            if (speed > BumpStuckSpeed)
            {
                _bumpBlockedFor = 0f;
                return;
            }

            _bumpBlockedFor += dt;
            if (_bumpBlockedFor < BumpHoldSeconds) return;

            bool turned = Vector3.Angle(pushDir, _bumpLastDir) > BumpTurnDegrees;
            if (!turned && Time.time - _bumpLastPlayTime < BumpRepeatSeconds) return;

            _bumpLastDir = pushDir;
            _bumpLastPlayTime = Time.time;

            string file = NavCues.FileName(NavCueKind.Bump);
            if (!LoopMixer.IsCueAvailable(file))
            {
                if (!_bumpMissingLogged)
                {
                    _bumpMissingLogged = true;
                    DebugLogger.LogState($"[BUMP] blocked while pushing, but {file} is not available — cue skipped.");
                }
                return;
            }

            LoopMixer.Play(file, cue.Volume, 0f, phase01: 0f, autoStopSeconds: BumpPlaySeconds);
            DebugLogger.LogState($"[BUMP] blocked for {_bumpBlockedFor:F2} s pushing ({pushDir.x:F2}, {pushDir.z:F2}).");
        }

        private void ResetBump(Vector3 playerPos)
        {
            _bumpLastPos = playerPos;
            _bumpHavePos = true;
            _bumpBlockedFor = 0f;
        }

        /// <summary>
        /// The direction the player is trying to walk, camera-relative, from the
        /// gamepad left stick or the game's WASD / arrow keys. False when idle.
        /// </summary>
        private static bool TryGetMoveIntent(Vector3 camForward, out Vector3 dir)
        {
            dir = Vector3.zero;
            Vector3 fwd = camForward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);

            try
            {
                var gp = Gamepad.current;
                if (gp != null)
                {
                    Vector2 stick = gp.leftStick.ReadValue();
                    if (stick.magnitude >= BumpIntent)
                        dir = right * stick.x + fwd * stick.y;
                }

                if (dir == Vector3.zero)
                {
                    var kb = Keyboard.current;
                    if (kb != null)
                    {
                        if (kb.wKey.isPressed || kb.upArrowKey.isPressed) dir += fwd;
                        if (kb.sKey.isPressed || kb.downArrowKey.isPressed) dir -= fwd;
                        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) dir += right;
                        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) dir -= right;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"[BUMP] input read failed: {ex.Message}");
                return false;
            }

            if (dir.sqrMagnitude < 1e-4f) return false;
            dir.Normalize();
            return true;
        }

        #endregion

        #region Helpers

        private void StopAll()
        {
            for (int i = 0; i < 4; i++)
            {
                _wallVoices[i]?.Stop();
                _wallVoices[i] = null;
                _wallSilentFor[i] = 0f;
            }
            StopBeacons();
        }

        private static bool AnyCueEnabled()
        {
            foreach (var kind in NavCues.All)
                if (ModSettings.NavCue(kind).Enabled) return true;
            return false;
        }

        private static bool IsWorldmap()
        {
            try
            {
                var fm = FieldManager.Instance;
                return fm == null || fm.IsWorldmap();
            }
            catch
            {
                return true;
            }
        }

        private static bool TryGetPlayerPosition(out Vector3 pos)
        {
            pos = Vector3.zero;
            try
            {
                var player = FieldManager.Instance?.GetControlPlayer();
                if (player == null) return false;
                pos = player.transform.position;
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"ManualNav: player position unavailable ({ex.Message})");
                return false;
            }
        }

        #endregion
    }
}
