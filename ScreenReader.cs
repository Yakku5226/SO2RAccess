using System;
using System.Runtime.InteropServices;
using MelonLoader;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Wrapper for the Tolk screen reader library.
    /// Announces text via NVDA, JAWS, or other screen readers.
    ///
    /// Requires Tolk.dll and nvdaControllerClient64.dll in the game folder.
    /// </summary>
    public static class ScreenReader
    {
        #region Native Imports

        [DllImport("Tolk.dll")]
        private static extern void Tolk_Load();

        [DllImport("Tolk.dll")]
        private static extern void Tolk_Unload();

        [DllImport("Tolk.dll")]
        private static extern bool Tolk_IsLoaded();

        [DllImport("Tolk.dll")]
        private static extern bool Tolk_HasSpeech();

        [DllImport("Tolk.dll", CharSet = CharSet.Unicode)]
        private static extern bool Tolk_Output(string text, bool interrupt);

        [DllImport("Tolk.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr Tolk_DetectScreenReader();

        #endregion

        #region Fields

        private static bool _available = false;
        private static bool _initialized = false;

        /// <summary>The most recently spoken message text.</summary>
        private static string _lastMessage = null;

        /// <summary>Time.time when the last message was spoken.</summary>
        private static float _lastMessageTime = -1f;

        /// <summary>
        /// Time.time until which a high-priority announcement is protected from being
        /// interrupted. While active, routine (normal-priority) announcements and
        /// subsequent high-priority announcements queue behind it instead of cutting
        /// it off — so reward/unlock popups aren't choked by the skill readout that
        /// fires a few frames later. See <see cref="Priority"/>.
        /// </summary>
        private static float _protectUntil = -1f;

        /// <summary>Seconds a high-priority announcement is protected from interruption.</summary>
        private const float ProtectWindowSeconds = 1.5f;

        #endregion

        #region Priority

        /// <summary>
        /// Announcement priority. <see cref="Normal"/> is routine output (menu cursor,
        /// skill readouts). <see cref="High"/> is for reward/unlock popups that must not
        /// be choked by routine output that races them by a few frames.
        /// </summary>
        public enum Priority
        {
            Normal,
            High
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Initializes Tolk. Call once at mod startup.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;

            try
            {
                Tolk_Load();
                _available = Tolk_IsLoaded() && Tolk_HasSpeech();

                if (_available)
                {
                    IntPtr srNamePtr = Tolk_DetectScreenReader();
                    string srName = srNamePtr != IntPtr.Zero
                        ? Marshal.PtrToStringUni(srNamePtr)
                        : "Unknown";
                    MelonLogger.Msg($"Screen reader detected: {srName}");
                }
                else
                {
                    MelonLogger.Warning("No screen reader detected or Tolk not available.");
                }
            }
            catch (DllNotFoundException)
            {
                MelonLogger.Error("Tolk.dll not found. Place Tolk.dll in the game folder.");
                _available = false;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to initialize Tolk: {ex.Message}");
                _available = false;
            }

            _initialized = true;
        }

        /// <summary>
        /// Announces text via the screen reader.
        /// When debug mode is on, also logs via DebugLogger.
        /// </summary>
        /// <param name="text">Text to speak.</param>
        /// <param name="interrupt">If true, stops current speech before speaking.</param>
        public static void Say(string text, bool interrupt = true,
            Priority priority = Priority.Normal)
        {
            if (string.IsNullOrEmpty(text)) return;

            DebugLogger.LogScreenReader(text);

            float now = UnityEngine.Time.time;
            bool protectedActive = now < _protectUntil;

            if (priority == Priority.High)
            {
                // Don't cut off an already-protected high-priority message; queue
                // behind it so a sequence of rewards/unlocks all play. Extend the
                // protection window to cover this one too.
                if (protectedActive) interrupt = false;
                _protectUntil = now + ProtectWindowSeconds;
            }
            else if (protectedActive && interrupt)
            {
                // A high-priority message is still playing — queue this routine
                // output after it instead of choking it.
                interrupt = false;
            }

            _lastMessage = text;
            _lastMessageTime = now;

            if (!_available) return;

            try
            {
                Tolk_Output(text, interrupt);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"ScreenReader.Say failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Queued announcement — waits for current speech to finish.
        /// Use for additional info after a main announcement.
        /// </summary>
        /// <param name="text">Text to speak after current speech finishes.</param>
        public static void SayQueued(string text)
        {
            Say(text, false);
        }

        /// <summary>
        /// Returns the last spoken message if it was said within the given time window,
        /// or null if no recent message exists. Used to detect interrupted speech
        /// so the caller can replay it after a higher-priority announcement.
        /// </summary>
        /// <param name="withinSeconds">Maximum age in seconds for the message to be considered recent.</param>
        public static string GetRecentMessage(float withinSeconds)
        {
            if (_lastMessage == null) return null;
            if (Time.time - _lastMessageTime > withinSeconds) return null;
            return _lastMessage;
        }

        /// <summary>
        /// Shuts down Tolk. Call when the game closes.
        /// </summary>
        public static void Shutdown()
        {
            if (!_initialized) return;

            try
            {
                Tolk_Unload();
            }
            catch { }

            _initialized = false;
            _available = false;
        }

        /// <summary>
        /// Returns true if a screen reader is available.
        /// </summary>
        public static bool IsAvailable => _available;

        #endregion
    }
}
