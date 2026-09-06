using System;

namespace SO2RAccess
{
    /// <summary>
    /// One looping sound inside <see cref="LoopMixer"/>: a shared sample buffer, a
    /// read position, and the gain / pan / muffle the game thread asked for.
    ///
    /// The game thread sets targets with <see cref="Set"/>; the audio thread moves the
    /// live values toward them a little every sample (about 50 ms from silent to full),
    /// so a beacon that jumps from far to near never clicks. Pan is constant-power, so a
    /// sound sweeping from left to right stays equally loud in the middle. Muffle is a
    /// simple low-pass filter, used to make beacons behind the player sound "through a
    /// wall".
    ///
    /// A voice that is not refreshed with <see cref="Set"/> for half a second is treated
    /// as abandoned and fades to silence (it resumes on the next Set). That covers the
    /// mod menu blocking handler updates, scene loads, and any handler that forgets to
    /// stop it — nothing can get stuck playing. Preview voices with an auto-stop time
    /// are exempt: nobody refreshes them on purpose.
    /// </summary>
    public sealed class MixerVoice
    {
        #region Tuning

        /// <summary>Seconds for gain to travel the full 0..1 range.</summary>
        private const float RampSeconds = 0.05f;

        /// <summary>Milliseconds without a Set() before a non-preview voice is faded out.</summary>
        private const long StaleMs = 500;

        /// <summary>Low-pass cutoff with no muffle (Hz) — effectively open.</summary>
        private const float OpenCutoffHz = 8000f;

        /// <summary>Low-pass cutoff at full muffle (Hz).</summary>
        private const float MuffledCutoffHz = 700f;

        #endregion

        #region State

        private readonly short[] _samples;
        private int _pos;

        // Live values (audio thread only).
        private float _gain;
        private float _pan;
        private float _muffle;
        private float _lp; // one-pole low-pass state

        // Targets (written by the game thread, read by the audio thread).
        private volatile float _targetGain;
        private volatile float _targetPan;
        private volatile float _targetMuffle;
        private volatile bool _stopping;
        private volatile bool _active = true;
        private long _lastSetTicks;

        // Preview support: stop by itself after this many frames (-1 = never).
        private long _autoStopFrames;

        #endregion

        /// <summary>Cue file name this voice plays (for logs and previews).</summary>
        public string Cue { get; }

        /// <summary>False once the voice has faded out and been released by the mixer.</summary>
        public bool IsActive => _active;

        /// <summary>True for preview voices that stop on their own timer.</summary>
        public bool HasAutoStop => _autoStopFrames >= 0;

        internal MixerVoice(string cue, short[] samples, int startPos, float gain, float pan,
            float autoStopSeconds)
        {
            Cue = cue;
            _samples = samples;
            _pos = Math.Clamp(startPos, 0, Math.Max(0, samples.Length - 1));
            _targetGain = Math.Clamp(gain, 0f, 1f);
            _targetPan = Math.Clamp(pan, -1f, 1f);
            _pan = _targetPan; // no need to sweep pan in from centre
            _autoStopFrames = autoStopSeconds > 0f
                ? (long)(autoStopSeconds * SoundBank.SampleRate)
                : -1;
            _lastSetTicks = Environment.TickCount64;
        }

        #region Game-thread API

        /// <summary>
        /// Sets the targets the live values glide toward. Call every update while the
        /// sound should keep playing — a voice left alone for 0.5 s fades out.
        /// </summary>
        /// <param name="gain">0 (silent) to 1 (full).</param>
        /// <param name="pan">-1 (left) to +1 (right).</param>
        /// <param name="muffle">0 (clear) to 1 (fully muffled, low-pass).</param>
        public void Set(float gain, float pan, float muffle = 0f)
        {
            if (_stopping) return;
            _targetGain = Math.Clamp(gain, 0f, 1f);
            _targetPan = Math.Clamp(pan, -1f, 1f);
            _targetMuffle = Math.Clamp(muffle, 0f, 1f);
            _lastSetTicks = Environment.TickCount64;
        }

        /// <summary>Fades the voice out and releases it. Safe to call more than once.</summary>
        public void Stop()
        {
            _stopping = true;
            _targetGain = 0f;
        }

        #endregion

        #region Audio-thread mixing

        /// <summary>
        /// Adds this voice's next <paramref name="frames"/> samples into the stereo
        /// mix buffers. Returns false when the voice has finished and can be dropped.
        /// Audio thread only — must not touch Unity.
        /// </summary>
        internal bool Mix(float[] mixL, float[] mixR, int frames)
        {
            if (!_active) return false;
            int len = _samples.Length;
            if (len == 0) { _active = false; return false; }

            bool stale = !HasAutoStop && !_stopping &&
                         Environment.TickCount64 - _lastSetTicks > StaleMs;
            float goalGain = stale ? 0f : _targetGain;
            float goalPan = _targetPan;
            float goalMuffle = _targetMuffle;

            float step = 1f / (RampSeconds * SoundBank.SampleRate);
            // Low-pass coefficient from the muffle at block start (block is 25 ms).
            float cutoff = OpenCutoffHz + (MuffledCutoffHz - OpenCutoffHz) * _muffle;
            float alpha = 1f - (float)Math.Exp(-2.0 * Math.PI * cutoff / SoundBank.SampleRate);

            for (int i = 0; i < frames; i++)
            {
                _gain = MoveToward(_gain, goalGain, step);
                _pan = MoveToward(_pan, goalPan, step);
                _muffle = MoveToward(_muffle, goalMuffle, step);

                float x = _samples[_pos] * (1f / 32768f);
                _pos++;
                if (_pos >= len) _pos = 0;

                // One-pole low-pass; with muffle 0 the cutoff is high enough to be neutral.
                _lp += alpha * (x - _lp);
                float s = _muffle > 0.001f ? _lp : x;

                // Constant-power pan: -1 -> (1,0), 0 -> (0.707,0.707), +1 -> (0,1).
                float angle = (_pan + 1f) * 0.25f * (float)Math.PI;
                float l = (float)Math.Cos(angle);
                float r = (float)Math.Sin(angle);

                float v = s * _gain;
                mixL[i] += v * l;
                mixR[i] += v * r;
            }

            if (_autoStopFrames >= 0)
            {
                _autoStopFrames -= frames;
                if (_autoStopFrames < 0) Stop();
            }

            if (_stopping && _gain <= 0.0005f)
            {
                _active = false;
                return false;
            }

            return true;
        }

        private static float MoveToward(float current, float target, float step)
        {
            if (current < target) return Math.Min(target, current + step);
            if (current > target) return Math.Max(target, current - step);
            return current;
        }

        #endregion
    }
}
