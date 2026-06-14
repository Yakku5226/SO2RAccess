using System;

namespace SO2RAccess
{
    /// <summary>
    /// Drives D-pad auto-repeat for audio menus: fires once on a fresh direction
    /// press, then at a steady interval while the direction is held. Shared by the
    /// gamepad navigation overlay (<see cref="Main"/>) and the mod settings menu
    /// (<see cref="ModMenuHandler"/>) so the repeat behaviour stays consistent.
    /// </summary>
    internal sealed class DpadRepeater
    {
        private readonly float _initialDelay;
        private readonly float _repeatInterval;

        // 0 = none, 1 = up, 2 = down, 3 = left, 4 = right.
        private int _dir;
        private float _timer;

        public DpadRepeater(float initialDelay = 0.4f, float repeatInterval = 0.15f)
        {
            _initialDelay = initialDelay;
            _repeatInterval = repeatInterval;
        }

        /// <summary>Clears the repeat state (call when the owning overlay opens or closes).</summary>
        public void Reset()
        {
            _dir = 0;
            _timer = 0f;
        }

        /// <summary>
        /// Call once per frame with the currently-pressed direction (0 = none).
        /// Invokes <paramref name="fire"/> immediately on a new direction and again
        /// on each repeat tick while it stays held.
        /// </summary>
        public void Update(int currentDir, float deltaTime, Action<int> fire)
        {
            if (currentDir == 0)
            {
                Reset();
            }
            else if (currentDir != _dir)
            {
                _dir = currentDir;
                _timer = _initialDelay;
                fire(currentDir);
            }
            else
            {
                _timer -= deltaTime;
                if (_timer <= 0f)
                {
                    _timer = _repeatInterval;
                    fire(currentDir);
                }
            }
        }
    }
}
