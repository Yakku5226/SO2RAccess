using System;
using System.Collections.Generic;
using Il2CppGame;
using MelonLoader;
using UnityEngine.InputSystem;
using InputKey = Il2CppCommon.InputManager.InputKey;

namespace SO2RAccess
{
    /// <summary>
    /// Debug diagnostic: dumps the game's LIVE input bindings (keyboard and
    /// gamepad) to the MelonLoader log and checks the mod's own keys against
    /// them for clashes. Runs each time debug mode (F12) is switched on, so it
    /// always reflects the player's current in-game key config.
    ///
    /// This is also the foundation of the future rebinding clash checker:
    /// <see cref="FindKeyboardClashes"/> is a pure function over
    /// <see cref="ModKeys"/> plus the live game bindings, and will be reused
    /// verbatim by the rebinding UI to warn before a key is accepted.
    /// </summary>
    internal static class InputBindingDump
    {
        /// <summary>
        /// Logs the full game keyboard and gamepad binding maps, reverse
        /// summaries for the shoulder/stick buttons the mod cares about, and a
        /// per-mod-key FREE/CLASHES verdict. Speaks a one-sentence summary so
        /// the result is audible without opening the log.
        /// </summary>
        public static void DumpAll()
        {
            try
            {
                var keyboardMap = BuildGameKeyboardMap(logEachBinding: true);
                var padMap = BuildGamePadMap(logEachBinding: true);
                if (keyboardMap == null || padMap == null)
                {
                    MelonLogger.Msg("[BINDDUMP] Game binding data not ready (ParameterManager or GameInputManager missing). Load a save and press F12 again.");
                    ScreenReader.Say(Loc.Get("binddump_not_ready"));
                    return;
                }

                MelonLogger.Msg("[BINDDUMP] ===== Reverse pad summaries =====");
                LogPadSummary(padMap, InputKey.L1);
                LogPadSummary(padMap, InputKey.L2);
                LogPadSummary(padMap, InputKey.R1);
                LogPadSummary(padMap, InputKey.R2);
                LogPadSummary(padMap, InputKey.L3);
                LogPadSummary(padMap, InputKey.R3);

                MelonLogger.Msg("[BINDDUMP] ===== Mod key clash check =====");
                var clashedKeys = new HashSet<Key>();
                foreach (var binding in ModKeys.AllKeyboard)
                {
                    string keyName = ModKeys.DisplayName(binding.Value);
                    var context = ModKeys.ContextOf(binding.Key);
                    if (keyboardMap.TryGetValue(binding.Value, out var gameActions))
                    {
                        clashedKeys.Add(binding.Value);
                        MelonLogger.Msg($"[BINDDUMP] MOD KEY '{keyName}' ({binding.Key}, context {context}): CLASHES with game {string.Join(", ", gameActions)}");
                    }
                    else
                    {
                        MelonLogger.Msg($"[BINDDUMP] MOD KEY '{keyName}' ({binding.Key}, context {context}): FREE");
                    }
                }

                ScreenReader.Say(clashedKeys.Count == 0
                    ? Loc.Get("binddump_all_free")
                    : Loc.Get("binddump_clashes", clashedKeys.Count));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[BINDDUMP] Dump failed: {ex}");
            }
        }

        /// <summary>
        /// Finds every mod keyboard binding whose key is also bound to a game
        /// action right now. Empty list = all free. Null-safe: returns an empty
        /// list when the game's binding data is not loaded yet.
        /// </summary>
        public static List<(ModAction Mod, GameInputManager.InputAction Game, Key Key)> FindKeyboardClashes()
        {
            var clashes = new List<(ModAction, GameInputManager.InputAction, Key)>();
            var keyboardMap = BuildGameKeyboardMap(logEachBinding: false);
            if (keyboardMap == null) return clashes;

            foreach (var binding in ModKeys.AllKeyboard)
            {
                if (keyboardMap.TryGetValue(binding.Value, out var gameActions))
                    foreach (var gameAction in gameActions)
                        clashes.Add((binding.Key, gameAction, binding.Value));
            }
            return clashes;
        }

        /// <summary>
        /// Game actions currently bound to one keyboard key, for the rebinding
        /// menu's passive clash warning. Empty list = key is free (or the game
        /// binding data is not loaded yet — the warning is best-effort).
        /// </summary>
        public static List<GameInputManager.InputAction> GameActionsForKey(Key key)
        {
            var keyboardMap = BuildGameKeyboardMap(logEachBinding: false);
            if (keyboardMap != null && keyboardMap.TryGetValue(key, out var actions))
                return actions;
            return new List<GameInputManager.InputAction>();
        }

        /// <summary>
        /// Reads the game's live keyboard binding for every defined InputAction
        /// via SystemConfigParameter.GetKeyboardKey. Returns key → actions, or
        /// null when the game data is not available yet.
        /// </summary>
        private static Dictionary<Key, List<GameInputManager.InputAction>> BuildGameKeyboardMap(bool logEachBinding)
        {
            Il2CppGame.SystemConfigParameter scp;
            try
            {
                scp = ParameterManager.Instance?.SystemConfigParameter;
            }
            catch (Exception ex)
            {
                MelonLogger.Msg($"[BINDDUMP] SystemConfigParameter unavailable: {ex.Message}");
                return null;
            }
            if (scp == null) return null;

            if (logEachBinding)
                MelonLogger.Msg("[BINDDUMP] ===== Game keyboard bindings (live) =====");

            var map = new Dictionary<Key, List<GameInputManager.InputAction>>();
            foreach (GameInputManager.InputAction action in Enum.GetValues(typeof(GameInputManager.InputAction)))
            {
                if (action == GameInputManager.InputAction.Invalid ||
                    action == GameInputManager.InputAction.Max)
                    continue;
                try
                {
                    Key key = scp.GetKeyboardKey(action);
                    if (key == Key.None) continue;
                    if (logEachBinding)
                        MelonLogger.Msg($"[BINDDUMP KB] {action} = {key}");
                    if (!map.TryGetValue(key, out var actions))
                        map[key] = actions = new List<GameInputManager.InputAction>();
                    actions.Add(action);
                }
                catch (Exception ex)
                {
                    // Individual actions may not resolve (native lookup quirk);
                    // log and keep going so one bad entry can't kill the dump.
                    MelonLogger.Msg($"[BINDDUMP KB] {action}: read failed ({ex.Message})");
                }
            }
            return map;
        }

        /// <summary>
        /// Reads the game's live gamepad binding for every defined InputAction
        /// via GameInputManager.GetBindInputKey. Returns virtual pad button →
        /// actions, or null when GameInputManager is not available yet.
        /// </summary>
        private static Dictionary<InputKey, List<GameInputManager.InputAction>> BuildGamePadMap(bool logEachBinding)
        {
            GameInputManager gim;
            try
            {
                gim = GameInputManager.Instance;
            }
            catch (Exception ex)
            {
                MelonLogger.Msg($"[BINDDUMP] GameInputManager unavailable: {ex.Message}");
                return null;
            }
            if (gim == null) return null;

            if (logEachBinding)
                MelonLogger.Msg("[BINDDUMP] ===== Game gamepad bindings (live) =====");

            var map = new Dictionary<InputKey, List<GameInputManager.InputAction>>();
            foreach (GameInputManager.InputAction action in Enum.GetValues(typeof(GameInputManager.InputAction)))
            {
                if (action == GameInputManager.InputAction.Invalid ||
                    action == GameInputManager.InputAction.Max)
                    continue;
                try
                {
                    InputKey padKey = gim.GetBindInputKey(action);
                    if (padKey == InputKey.Invalid) continue;
                    if (logEachBinding)
                        MelonLogger.Msg($"[BINDDUMP PAD] {action} = {padKey}");
                    if (!map.TryGetValue(padKey, out var actions))
                        map[padKey] = actions = new List<GameInputManager.InputAction>();
                    actions.Add(action);
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg($"[BINDDUMP PAD] {action}: read failed ({ex.Message})");
                }
            }
            return map;
        }

        /// <summary>Logs which game actions live on one virtual pad button.</summary>
        private static void LogPadSummary(
            Dictionary<InputKey, List<GameInputManager.InputAction>> padMap, InputKey button)
        {
            string actions = padMap.TryGetValue(button, out var list)
                ? string.Join(", ", list)
                : "(none)";
            MelonLogger.Msg($"[BINDDUMP] Actions on {button}: {actions}");
        }
    }
}
