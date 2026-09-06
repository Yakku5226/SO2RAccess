using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Beacon half of <see cref="ManualNavHandler"/>: the nearest few objects the
    /// navigation list knows about each get a looping voice, panned to where the
    /// object is relative to the camera and louder the closer it is. Objects
    /// behind the player are muffled or just quieter (mod menu choice) because
    /// plain stereo cannot tell front from back.
    /// </summary>
    public partial class ManualNavHandler
    {
        #region Tuning

        /// <summary>Most beacons sounding at once (nearest win).</summary>
        private const int MaxBeaconVoices = 6;

        /// <summary>A beacon this close (m) or closer plays at the cue's full volume.</summary>
        private const float BeaconFullDistance = 1.5f;

        /// <summary>Volume kept by a beacon straight behind the player, per rear mode.</summary>
        private const float MuffledRearGain = 0.6f;
        private const float QuietRearGain = 0.5f;

        #endregion

        #region Fields

        private readonly List<NavigationHandler.BeaconTarget> _targets = new List<NavigationHandler.BeaconTarget>();
        private readonly List<(float dist, int index)> _inRange = new List<(float, int)>();
        private readonly Dictionary<int, MixerVoice> _beaconVoices = new Dictionary<int, MixerVoice>();
        private readonly HashSet<int> _selected = new HashSet<int>();
        private readonly List<int> _toRelease = new List<int>();

        #endregion

        /// <summary>
        /// Refreshes the target list, picks the nearest enabled ones within range and
        /// steers a voice for each; voices whose object dropped out fade away.
        /// </summary>
        private void UpdateBeacons(Vector3 playerPos, Vector3 camForward)
        {
            if (!_nav.TryGetBeaconTargets(_targets))
            {
                StopBeacons();
                return;
            }

            float range = ModSettings.BeaconRangeMeters;
            _inRange.Clear();
            for (int i = 0; i < _targets.Count; i++)
            {
                var t = _targets[i];
                if (!ModSettings.NavCue(t.Kind).Enabled) continue;

                Vector3 pos = LivePosition(t);
                float dist = Vector3.Distance(playerPos, pos);
                if (dist > range) continue;
                _inRange.Add((dist, i));
            }
            _inRange.Sort((a, b) => a.dist.CompareTo(b.dist));

            Vector3 camRight = new Vector3(camForward.z, 0f, -camForward.x);
            _selected.Clear();
            int voices = 0;
            for (int n = 0; n < _inRange.Count && voices < MaxBeaconVoices; n++)
            {
                var t = _targets[_inRange[n].index];
                string file = NavCues.FileName(t.Kind);
                if (!LoopMixer.IsCueAvailable(file)) continue; // e.g. the stairs placeholder

                ComputeBeacon(playerPos, LivePosition(t), _inRange[n].dist, range, camForward, camRight,
                    out float pan, out float rear);
                var cue = ModSettings.NavCue(t.Kind);
                float gain = cue.Volume * Mathf.Clamp01(1f - (_inRange[n].dist - BeaconFullDistance) / (range - BeaconFullDistance));
                float muffle = 0f;
                if (ModSettings.BeaconRear == BeaconRearMode.Muffled)
                {
                    muffle = rear;
                    gain *= 1f - (1f - MuffledRearGain) * rear;
                }
                else
                {
                    gain *= 1f - (1f - QuietRearGain) * rear;
                }

                _selected.Add(t.Id);
                voices++;
                if (_beaconVoices.TryGetValue(t.Id, out var voice) && voice.IsActive)
                {
                    voice.Set(gain, pan, muffle);
                }
                else
                {
                    voice = LoopMixer.Play(file, gain, pan); // random phase: same-kind beacons do not hit together
                    if (voice == null) continue;
                    voice.Set(gain, pan, muffle);
                    _beaconVoices[t.Id] = voice;
                    DebugLogger.LogState($"[BEACON] start {t.Kind} '{t.Label}' {_inRange[n].dist:F1} m pan {pan:F2} rear {rear:F2}");
                }
            }

            // Release voices for objects no longer selected.
            _toRelease.Clear();
            foreach (var pair in _beaconVoices)
                if (!_selected.Contains(pair.Key) || !pair.Value.IsActive) _toRelease.Add(pair.Key);
            foreach (int id in _toRelease)
            {
                _beaconVoices[id].Stop();
                _beaconVoices.Remove(id);
            }
        }

        /// <summary>
        /// Camera-relative direction to a beacon: pan is the sideways component
        /// (-1 left .. +1 right, constant-power in the mixer), rear is how far
        /// behind the camera it is (0 = level with or ahead, 1 = straight behind).
        /// </summary>
        private static void ComputeBeacon(Vector3 playerPos, Vector3 targetPos, float dist, float range,
            Vector3 camForward, Vector3 camRight, out float pan, out float rear)
        {
            Vector3 to = targetPos - playerPos;
            to.y = 0f;
            float flat = to.magnitude;
            if (flat < 0.05f)
            {
                pan = 0f;
                rear = 0f;
                return;
            }
            to /= flat;
            float forwardPart = Vector3.Dot(to, camForward);
            float rightPart = Vector3.Dot(to, camRight);
            pan = Mathf.Clamp(rightPart, -1f, 1f);
            rear = Mathf.Clamp01(-forwardPart);
        }

        private static Vector3 LivePosition(NavigationHandler.BeaconTarget t)
        {
            if (t.Live == null) return t.Position;
            try { return t.Live.position; }
            catch { return t.Position; }
        }

        private void StopBeacons()
        {
            if (_beaconVoices.Count == 0) return;
            foreach (var voice in _beaconVoices.Values) voice.Stop();
            _beaconVoices.Clear();
        }
    }
}
