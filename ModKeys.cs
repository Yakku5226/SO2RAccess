using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace SO2RAccess
{
    /// <summary>
    /// Every rebindable mod action. One entry per input the mod listens for.
    /// Iterable, so the binding dump and the future clash checker can cover
    /// all of them without a hand-maintained list.
    /// </summary>
    internal enum ModAction
    {
        // Global utility keys (F-row)
        Help,
        DialogueVoiceToggle,
        ReadFol,
        ModMenu,
        DebugToggle,

        // Modeless field navigation
        NavCategoryPrev,
        NavCategoryNext,
        NavItemPrev,
        NavItemNext,
        NavAutoWalkToggle,

        // Battle pause menu (same physical keys as nav — different context)
        PauseTierDown,
        PauseTierUp,
        PauseCharLeft,
        PauseCharRight,

        // Context-local readouts
        CampStoryHint,
        QuickRecoveryStatus,

        // Debug-only investigation hotkeys (active only in debug mode)
        DebugObstacleScan,     // F5
        DebugCollisionTrace,   // F6
        DebugRouteAuditor,     // F7
        DebugCharaWallScan,    // F8
        DebugGridBake,         // F9
        DebugTravelMask,       // F10
        DebugPathDiagnostics,  // F11
        DebugTextDump,         // Semicolon — dump every visible on-screen text
    }

    /// <summary>
    /// Where a binding is active. The same physical key bound in two different
    /// contexts is NOT a clash (nav and battle pause deliberately share keys);
    /// a mod key colliding with a game key IS a clash when their contexts can
    /// be live at the same time.
    /// </summary>
    internal enum ModKeyContext
    {
        Global,        // always active
        Field,         // field / world map, outside menus
        BattlePause,   // only while the battle pause menu is open
        CampMenu,      // only while the camp menu is open
        QuickRecovery, // only while the Quick Recovery overlay is open
        DebugOnly,     // only while debug mode (F12) is on
    }

    /// <summary>
    /// Single source of truth for the mod's input bindings.
    ///
    /// Keyboard bindings live in a dictionary keyed by <see cref="ModAction"/>;
    /// call sites read them through the terse static properties below. A future
    /// rebinding feature only has to replace dictionary contents (via
    /// <see cref="Apply"/>) — no call site changes.
    ///
    /// Gamepad bindings are expressed as accessors that resolve a control on the
    /// current pad, so the L2 modifier lives here too.
    /// </summary>
    internal static class ModKeys
    {
        #region Keyboard bindings

        private static readonly Dictionary<ModAction, Key> _keys = new Dictionary<ModAction, Key>
        {
            // Global F-row
            { ModAction.Help,                Key.F1 },
            { ModAction.DialogueVoiceToggle, Key.F2 },
            { ModAction.ReadFol,             Key.F3 },
            { ModAction.ModMenu,             Key.F4 },
            { ModAction.DebugToggle,         Key.F12 },

            // Modeless field navigation — a right-hand cluster on QWERTY:
            // minus/equals on the number row, brackets below them, backslash below those.
            { ModAction.NavCategoryPrev,   Key.Minus },
            { ModAction.NavCategoryNext,   Key.Equals },
            { ModAction.NavItemPrev,       Key.LeftBracket },
            { ModAction.NavItemNext,       Key.RightBracket },
            { ModAction.NavAutoWalkToggle, Key.Backslash },

            // Battle pause — same family, different context
            { ModAction.PauseTierDown,  Key.Minus },
            { ModAction.PauseTierUp,    Key.Equals },
            { ModAction.PauseCharLeft,  Key.LeftBracket },
            { ModAction.PauseCharRight, Key.RightBracket },

            // Context-local readouts. Story hint was H, but the binding dump
            // (2026-08-30) showed the game maps keyboard H to its R3 action
            // (backlog / battle target lock) — moved to the planned fallback,
            // apostrophe. P verified FREE by the same dump.
            { ModAction.CampStoryHint,       Key.Quote },
            { ModAction.QuickRecoveryStatus, Key.P },

            // Debug hotkeys
            { ModAction.DebugObstacleScan,    Key.F5 },
            { ModAction.DebugCollisionTrace,  Key.F6 },
            { ModAction.DebugRouteAuditor,    Key.F7 },
            { ModAction.DebugCharaWallScan,   Key.F8 },
            { ModAction.DebugGridBake,        Key.F9 },
            { ModAction.DebugTravelMask,      Key.F10 },
            { ModAction.DebugPathDiagnostics, Key.F11 },
            { ModAction.DebugTextDump,        Key.Semicolon },
        };

        private static readonly Dictionary<ModAction, ModKeyContext> _contexts = new Dictionary<ModAction, ModKeyContext>
        {
            { ModAction.Help,                ModKeyContext.Global },
            { ModAction.DialogueVoiceToggle, ModKeyContext.Global },
            { ModAction.ReadFol,             ModKeyContext.Global },
            { ModAction.ModMenu,             ModKeyContext.Global },
            { ModAction.DebugToggle,         ModKeyContext.Global },

            { ModAction.NavCategoryPrev,   ModKeyContext.Field },
            { ModAction.NavCategoryNext,   ModKeyContext.Field },
            { ModAction.NavItemPrev,       ModKeyContext.Field },
            { ModAction.NavItemNext,       ModKeyContext.Field },
            { ModAction.NavAutoWalkToggle, ModKeyContext.Field },

            { ModAction.PauseTierDown,  ModKeyContext.BattlePause },
            { ModAction.PauseTierUp,    ModKeyContext.BattlePause },
            { ModAction.PauseCharLeft,  ModKeyContext.BattlePause },
            { ModAction.PauseCharRight, ModKeyContext.BattlePause },

            { ModAction.CampStoryHint,       ModKeyContext.CampMenu },
            { ModAction.QuickRecoveryStatus, ModKeyContext.QuickRecovery },

            { ModAction.DebugObstacleScan,    ModKeyContext.DebugOnly },
            { ModAction.DebugCollisionTrace,  ModKeyContext.DebugOnly },
            { ModAction.DebugRouteAuditor,    ModKeyContext.DebugOnly },
            { ModAction.DebugCharaWallScan,   ModKeyContext.DebugOnly },
            { ModAction.DebugGridBake,        ModKeyContext.DebugOnly },
            { ModAction.DebugTravelMask,      ModKeyContext.DebugOnly },
            { ModAction.DebugPathDiagnostics, ModKeyContext.DebugOnly },
            { ModAction.DebugTextDump,        ModKeyContext.DebugOnly },
        };

        /// <summary>
        /// Snapshot of the shipped default bindings, taken before any user
        /// overrides are applied. Used by the rebinding menu's reset command
        /// and by settings persistence (only non-default keys are saved).
        /// </summary>
        private static readonly Dictionary<ModAction, Key> _defaultKeys =
            new Dictionary<ModAction, Key>(_keys);

        /// <summary>The keyboard key currently bound to a mod action.</summary>
        public static Key Get(ModAction action) => _keys[action];

        /// <summary>The shipped default key for a mod action.</summary>
        public static Key GetDefault(ModAction action) => _defaultKeys[action];

        /// <summary>The context in which a mod action's binding is active.</summary>
        public static ModKeyContext ContextOf(ModAction action) => _contexts[action];

        /// <summary>All keyboard bindings, for the dump and clash checker.</summary>
        public static IReadOnlyDictionary<ModAction, Key> AllKeyboard => _keys;

        /// <summary>
        /// Replaces bindings with user-configured values. Reserved for the
        /// future rebinding feature; call sites need no changes when it lands.
        /// </summary>
        public static void Apply(IReadOnlyDictionary<ModAction, Key> overrides)
        {
            foreach (var pair in overrides)
                _keys[pair.Key] = pair.Value;
        }

        // Terse call-site accessors.
        public static Key Help                => _keys[ModAction.Help];
        public static Key DialogueVoiceToggle => _keys[ModAction.DialogueVoiceToggle];
        public static Key ReadFol             => _keys[ModAction.ReadFol];
        public static Key ModMenu             => _keys[ModAction.ModMenu];
        public static Key DebugToggle         => _keys[ModAction.DebugToggle];
        public static Key NavCategoryPrev     => _keys[ModAction.NavCategoryPrev];
        public static Key NavCategoryNext     => _keys[ModAction.NavCategoryNext];
        public static Key NavItemPrev         => _keys[ModAction.NavItemPrev];
        public static Key NavItemNext         => _keys[ModAction.NavItemNext];
        public static Key NavAutoWalkToggle   => _keys[ModAction.NavAutoWalkToggle];
        public static Key PauseTierDown       => _keys[ModAction.PauseTierDown];
        public static Key PauseTierUp         => _keys[ModAction.PauseTierUp];
        public static Key PauseCharLeft       => _keys[ModAction.PauseCharLeft];
        public static Key PauseCharRight      => _keys[ModAction.PauseCharRight];
        public static Key CampStoryHint       => _keys[ModAction.CampStoryHint];
        public static Key QuickRecoveryStatus => _keys[ModAction.QuickRecoveryStatus];

        #endregion

        #region Display names

        /// <summary>Spoken/logged name of the key bound to a mod action.</summary>
        public static string DisplayName(ModAction action) => DisplayName(_keys[action]);

        /// <summary>
        /// Spoken/logged name of a keyboard key ("left bracket", "F4").
        /// Screen-reader friendly: words, not symbols.
        /// </summary>
        public static string DisplayName(Key key)
        {
            switch (key)
            {
                case Key.Minus:        return "minus";
                case Key.Equals:       return "equals";
                case Key.LeftBracket:  return "left bracket";
                case Key.RightBracket: return "right bracket";
                case Key.Backslash:    return "backslash";
                case Key.Quote:        return "apostrophe";
                case Key.Semicolon:    return "semicolon";
                case Key.Comma:        return "comma";
                case Key.Period:       return "period";
                case Key.Slash:        return "slash";
                default:               return key.ToString();
            }
        }

        #endregion

        #region Gamepad bindings

        /// <summary>
        /// The mod's held modifier button — L2 (left trigger). L1 is the game's
        /// pickpocket button, so the mod must stay off it.
        /// </summary>
        public static ButtonControl NavModifier(Gamepad gp) => gp.leftTrigger;

        /// <summary>Spoken/logged name of the nav modifier button.</summary>
        public const string NavModifierName = "L2";

        /// <summary>Chord partner for the mod settings menu (modifier + this).</summary>
        public static ButtonControl ModMenuChord(Gamepad gp) => gp.leftStickButton;

        /// <summary>Chord partner for the Fol readout (modifier + this).</summary>
        public static ButtonControl ReadFolChord(Gamepad gp) => gp.rightStickButton;

        /// <summary>Battle pause tier up — L1, which is free while pause is open.</summary>
        public static ButtonControl PauseTierUpPad(Gamepad gp) => gp.leftShoulder;

        /// <summary>Battle pause tier down — R1, free while pause is open.</summary>
        public static ButtonControl PauseTierDownPad(Gamepad gp) => gp.rightShoulder;

        #endregion
    }
}
