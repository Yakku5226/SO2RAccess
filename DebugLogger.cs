using MelonLoader;

namespace SO2RAccess
{
    /// <summary>
    /// Centralized debug logging with categories.
    /// All output is suppressed unless Main.DebugMode is true (toggle with F12).
    ///
    /// Categories:
    ///   [SR]      What the screen reader announces
    ///   [INPUT]   Key presses and input events
    ///   [STATE]   Screen and menu state changes
    ///   [HANDLER] Handler decisions and actions
    ///   [GAME]    Values read from the game
    /// </summary>
    public static class DebugLogger
    {
        /// <summary>
        /// Logs a categorized debug message. Only active when Main.DebugMode is true.
        /// </summary>
        public static void Log(LogCategory category, string message)
        {
            if (!Main.DebugMode) return;
            MelonLogger.Msg($"{GetPrefix(category)} {message}");
        }

        /// <summary>
        /// Logs a categorized debug message with a source label.
        /// </summary>
        public static void Log(LogCategory category, string source, string message)
        {
            if (!Main.DebugMode) return;
            MelonLogger.Msg($"{GetPrefix(category)} [{source}] {message}");
        }

        /// <summary>
        /// Logs screen reader output. Called automatically by ScreenReader.Say().
        /// </summary>
        public static void LogScreenReader(string text)
        {
            if (!Main.DebugMode) return;
            MelonLogger.Msg($"[SR] {text}");
        }

        /// <summary>
        /// Logs a key press event.
        /// </summary>
        public static void LogInput(string keyName, string action = null)
        {
            if (!Main.DebugMode) return;
            string msg = action != null ? $"{keyName} -> {action}" : keyName;
            MelonLogger.Msg($"[INPUT] {msg}");
        }

        /// <summary>
        /// Logs a state change such as a screen opening or mode switch.
        /// </summary>
        public static void LogState(string description)
        {
            if (!Main.DebugMode) return;
            MelonLogger.Msg($"[STATE] {description}");
        }

        /// <summary>
        /// Logs a game value that was read, useful for verifying data extraction.
        /// </summary>
        public static void LogGameValue(string name, object value)
        {
            if (!Main.DebugMode) return;
            MelonLogger.Msg($"[GAME] {name} = {value}");
        }

        private static string GetPrefix(LogCategory category)
        {
            return category switch
            {
                LogCategory.ScreenReader => "[SR]",
                LogCategory.Input       => "[INPUT]",
                LogCategory.State       => "[STATE]",
                LogCategory.Handler     => "[HANDLER]",
                LogCategory.Game        => "[GAME]",
                _                       => "[DEBUG]"
            };
        }
    }

    /// <summary>
    /// Categories for debug logging.
    /// </summary>
    public enum LogCategory
    {
        /// <summary>What the screen reader announces.</summary>
        ScreenReader,

        /// <summary>Key presses and input events.</summary>
        Input,

        /// <summary>Screen and menu state changes.</summary>
        State,

        /// <summary>Handler decisions and processing.</summary>
        Handler,

        /// <summary>Values read from the game.</summary>
        Game
    }
}
