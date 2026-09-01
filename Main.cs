using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using System;
using System.Collections;
using System.IO;
using Il2CppGame;

// IMPORTANT: Do NOT access game singletons (GameManager.Instance, etc.)
// in OnInitializeMelon() or before CheckGameReady() returns true.
// Accessing game code before the game is fully loaded will crash.
// Safe access begins in OnSceneWasLoaded() or when CheckGameReady() passes.

[assembly: MelonInfo(typeof(SO2RAccess.Main), "SO2RAccess", "0.3.1", "Accessibility Mod")]
// Universal: no game-name check, so the mod loads on both the full game and the
// demo (their internal product names may differ, but the game code is identical).
[assembly: MelonGame]

namespace SO2RAccess
{
    /// <summary>
    /// Main mod entry point. Initializes all systems and dispatches global hotkeys.
    ///
    /// Keep this class small — only lifecycle methods, hotkey dispatch, and handler
    /// instantiation go here. All feature logic belongs in separate Handler classes.
    /// </summary>
    public class Main : MelonMod
    {
        #region Fields

        private bool _gameReady = false;
        private HarmonyLib.Harmony _harmony;

        /// <summary>
        /// Debug mode — logs all screen reader output and game state detail.
        /// Toggle with F12.
        /// </summary>
        public static bool DebugMode = false;

        // Handlers — one per feature area.
        private TitleMenuHandler _titleHandler;
        private ConfigMenuHandler _configHandler;
        private KeyboardMenuHandler _keyboardHandler;
        private GamepadMenuHandler _gamepadHandler;
        private HeroSelectHandler _heroSelectHandler;
        private NewGameSettingsHandler _newGameSettingsHandler;
        private LoadGameHandler _loadGameHandler;
        private DialogueHandler _dialogueHandler;
        private SubtitleHandler _subtitleHandler;
        private NotificationHandler _notificationHandler;
        private NavigationHandler _navigationHandler;
        private CampMenuHandler _campMenuHandler;
        private BattleResultHandler _battleResultHandler;
        private BattleCounterHandler _battleCounterHandler;
        private ShopHandler _shopHandler;
        private GuildHandler _guildHandler;
        private EnemyProximityHandler _enemyProximityHandler;
        private GameOverHandler _gameOverHandler;
        private SaveNotificationHandler _saveNotificationHandler;
        private BattleTargetHandler _battleTargetHandler;
        private BattlePauseHandler _battlePauseHandler;
        private BattleMenuHandler _battleMenuHandler;
        private BattleStatusHandler _battleStatusHandler;
        private WorldMapHandler _worldMapHandler;
        private ModMenuHandler _modMenuHandler;
        private EquipWizardHandler _equipWizardHandler;
        private PrivateActionHandler _privateActionHandler;
        private BonusGaugeHandler _bonusGaugeHandler;
        private DialogueChoiceHandler _dialogueChoiceHandler;
        private PickpocketHandler _pickpocketHandler;
        private QuickRecoveryHandler _quickRecoveryHandler;
        private FieldPromptHandler _fieldPromptHandler;
        private FishCollectorHandler _fishCollectorHandler;
        private ListSelectionHandler _listSelectionHandler;
        private LanguageHandler _languageHandler;
        private DebugHotkeys _debugHotkeys;

        // Gamepad nav overlay — mod modifier (L2) hold-to-open state.
        private bool _gamepadModHeld;
        private readonly DpadRepeater _dpadRepeater = new DpadRepeater();
        private bool _stickUpWasActive;
        private float _gamepadDiagTimer;

        private const float StickUpThreshold = 0.5f;
        /// <summary>Left stick magnitude above which auto-walk is cancelled (player takes over).</summary>
        private const float StickCancelThreshold = 0.3f;

        #endregion

        #region Lifecycle

        /// <summary>
        /// Called by MelonLoader when the mod is first loaded.
        /// Only safe to initialize mod systems here — do NOT touch game code.
        /// </summary>
        public override void OnInitializeMelon()
        {
            ScreenReader.Initialize();
            ModSettings.Load();
            AudioCuePlayer.Initialize();

            // Load audio files from the mod sounds folder.
            string soundsDir = Path.Combine(Directory.GetCurrentDirectory(),
                "UserData", "SO2RAccess", "Sounds");
            string proximityWavPath = Path.Combine(soundsDir, "Enemynearby.wav");
            SpatialAudioPlayer.Initialize(proximityWavPath);

            string dodgeWavPath = Path.Combine(soundsDir, "Dodge.wav");
            AudioCuePlayer.LoadDodgeSound(dodgeWavPath);

            string saveWavPath = Path.Combine(soundsDir, "Save_sound.wav");
            AudioCuePlayer.LoadSaveSound(saveWavPath);

            string paWavPath = Path.Combine(soundsDir, "PrivateAction.wav");
            AudioCuePlayer.LoadPrivateActionSound(paWavPath);

            string gaugeFillWavPath = Path.Combine(soundsDir, "GaugeFill.wav");
            AudioCuePlayer.LoadGaugeFillSound(gaugeFillWavPath);

            string jumpWavPath = Path.Combine(soundsDir, "Jump.wav");
            AudioCuePlayer.LoadJumpSound(jumpWavPath);

            string fishPromptWavPath = Path.Combine(soundsDir, "bubble_big.wav");
            AudioCuePlayer.LoadFishPromptSound(fishPromptWavPath);

            Loc.Initialize();
            InitializeHandlers();
            MelonCoroutines.Start(AnnounceStartupDelayed());
        }

        private void InitializeHandlers()
        {
            _harmony = new HarmonyLib.Harmony("SO2RAccess");
            _titleHandler = new TitleMenuHandler();
            _configHandler = new ConfigMenuHandler();
            _keyboardHandler = new KeyboardMenuHandler();
            _gamepadHandler = new GamepadMenuHandler();
            _heroSelectHandler = new HeroSelectHandler();
            _newGameSettingsHandler = new NewGameSettingsHandler();
            _loadGameHandler = new LoadGameHandler();
            _dialogueHandler = new DialogueHandler();
            _subtitleHandler = new SubtitleHandler();
            _notificationHandler = new NotificationHandler();
            _navigationHandler = new NavigationHandler();
            _campMenuHandler = new CampMenuHandler();
            _battleResultHandler = new BattleResultHandler();
            _battleCounterHandler = new BattleCounterHandler();
            _shopHandler = new ShopHandler();
            _guildHandler = new GuildHandler();
            _enemyProximityHandler = new EnemyProximityHandler();
            _gameOverHandler = new GameOverHandler();
            _saveNotificationHandler = new SaveNotificationHandler();
            _battleTargetHandler = new BattleTargetHandler();
            _battlePauseHandler = new BattlePauseHandler();
            _battleMenuHandler = new BattleMenuHandler();
            _worldMapHandler = new WorldMapHandler();
            _battleStatusHandler = new BattleStatusHandler();
            _modMenuHandler = new ModMenuHandler();
            _equipWizardHandler = new EquipWizardHandler();
            _privateActionHandler = new PrivateActionHandler();
            _bonusGaugeHandler = new BonusGaugeHandler();
            _dialogueChoiceHandler = new DialogueChoiceHandler();
            _pickpocketHandler = new PickpocketHandler();
            _quickRecoveryHandler = new QuickRecoveryHandler();
            _fieldPromptHandler = new FieldPromptHandler();
            _fishCollectorHandler = new FishCollectorHandler();
            _listSelectionHandler = new ListSelectionHandler();
            _languageHandler = new LanguageHandler();
            _debugHotkeys = new DebugHotkeys(_navigationHandler);
        }

        private IEnumerator AnnounceStartupDelayed()
        {
            // Short delay so screen reader is ready before first announcement
            yield return new WaitForSeconds(1f);
            ScreenReader.Say(Loc.Get("mod_loaded"));
        }

        /// <summary>
        /// Called every frame. Waits for the game to be ready, then processes
        /// hotkeys and updates handlers.
        /// </summary>
        public override void OnUpdate()
        {
            if (!CheckGameReady()) return;
            ProcessGamepad();
            if (ProcessHotkeys()) return;
            UpdateHandlers();
        }

        /// <summary>
        /// Called every frame after all Unity Update() calls complete.
        /// Used to set walk animation during auto-walk — LateUpdate guarantees
        /// we run after the game has finished its own animation state updates.
        /// </summary>
        public override void OnLateUpdate()
        {
            if (!_gameReady) return;
            DialogueHandler.ProcessPendingDialogue();
            _navigationHandler.LateUpdate();
        }

        private bool CheckGameReady()
        {
            if (_gameReady) return true;

            // Wait for the main game singleton to exist before doing anything
            if (GameManager.Instance != null)
            {
                _gameReady = true;
                MelonLogger.Msg("Game ready.");
            }

            return _gameReady;
        }

        /// <summary>
        /// Called when a Unity scene is loaded. Resets game-ready state so
        /// CheckGameReady() will re-verify singletons for the new scene.
        /// </summary>
        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            MelonLogger.Msg($"Scene loaded: {sceneName}");
            DebugLogger.LogState($"Scene changed to: {sceneName}");
            _gameReady = false;
            _navigationHandler?.CancelAutoWalk();
            _campMenuHandler?.OnSceneChanged();
            _shopHandler?.OnSceneChanged();
            _guildHandler?.OnSceneChanged();
            _enemyProximityHandler?.OnSceneChanged();
            _gameOverHandler?.OnSceneChanged();
            _saveNotificationHandler?.OnSceneChanged();
            _battleTargetHandler?.OnSceneChanged();
            _battlePauseHandler?.OnSceneChanged();
            _battleMenuHandler?.OnSceneChanged();
            _battleStatusHandler?.OnSceneChanged();
            _equipWizardHandler?.OnSceneChanged();
            _worldMapHandler?.OnSceneChanged();
            _privateActionHandler?.OnSceneChanged();
            _bonusGaugeHandler?.OnSceneChanged();
            _pickpocketHandler?.OnSceneChanged();
            _quickRecoveryHandler?.OnSceneChanged();
            _fishCollectorHandler?.OnSceneChanged();
            _listSelectionHandler?.OnSceneChanged();
            _subtitleHandler?.OnSceneChanged();
            ConfigMenuHandler.OnSceneChanged();

            // Apply patches once — safe to call on every scene load, handlers guard against duplicates.
            _titleHandler.ApplyPatches(_harmony);
            _configHandler.ApplyPatches(_harmony);
            _keyboardHandler.ApplyPatches(_harmony);
            _gamepadHandler.ApplyPatches(_harmony);
            _heroSelectHandler.ApplyPatches(_harmony);
            _newGameSettingsHandler.ApplyPatches(_harmony);
            _loadGameHandler.ApplyPatches(_harmony);
            _dialogueHandler.ApplyPatches(_harmony);
            _subtitleHandler.ApplyPatches(_harmony);
            _notificationHandler.ApplyPatches(_harmony);
            _navigationHandler.ApplyPatches(_harmony);
            _campMenuHandler.ApplyPatches(_harmony);
            _battleResultHandler.ApplyPatches(_harmony);
            _battleCounterHandler.ApplyPatches(_harmony);
            _shopHandler.ApplyPatches(_harmony);
            _guildHandler.ApplyPatches(_harmony);
            _enemyProximityHandler.ApplyPatches(_harmony);
            _gameOverHandler.ApplyPatches(_harmony);
            _saveNotificationHandler.ApplyPatches(_harmony);
            _battleTargetHandler.ApplyPatches(_harmony);
            _battlePauseHandler.ApplyPatches(_harmony);
            _battleMenuHandler.ApplyPatches(_harmony);
            _battleStatusHandler.ApplyPatches(_harmony);
            _equipWizardHandler.ApplyPatches(_harmony);
            _bonusGaugeHandler.ApplyPatches(_harmony);
            _dialogueChoiceHandler.ApplyPatches(_harmony);
            _fieldPromptHandler.ApplyPatches(_harmony);
            _quickRecoveryHandler.ApplyPatches(_harmony);
            _listSelectionHandler.ApplyPatches(_harmony);
            _languageHandler.ApplyPatches(_harmony);
        }

        /// <summary>
        /// Called when the game closes. Shuts down the screen reader cleanly.
        /// </summary>
        public override void OnApplicationQuit()
        {
            _navigationHandler?.SaveTraversal();
            SpatialAudioPlayer.Shutdown();
            ScreenReader.Shutdown();
            AudioCuePlayer.Shutdown();
        }

        #endregion

        #region Hotkeys

        /// <summary>
        /// Processes global mod hotkeys. Returns true if a key was consumed.
        /// Dispatch to handlers here — do not put feature logic directly in Main.
        /// </summary>
        private bool ProcessHotkeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return false;

            // F4 — toggle mod settings menu
            if (kb[ModKeys.ModMenu].wasPressedThisFrame && !_modMenuHandler.IsOpen)
            {
                DebugLogger.LogInput(ModKeys.DisplayName(ModAction.ModMenu), "ModMenuOpen");
                _modMenuHandler.Open();
                return true;
            }

            // Mod menu consumes all keyboard input while open
            if (_modMenuHandler.IsOpen)
                return _modMenuHandler.ProcessKeyboard(kb);

            // Debug-only investigation hotkeys (F5–F11) — see DebugHotkeys.cs.
            if (DebugMode && _debugHotkeys.Process(kb))
                return true;

            // F12 — toggle debug mode
            if (kb[ModKeys.DebugToggle].wasPressedThisFrame)
            {
                DebugMode = !DebugMode;
                ScreenReader.Say(Loc.Get(DebugMode ? "debug_on" : "debug_off"));
                MelonLogger.Msg($"Debug mode {(DebugMode ? "enabled" : "disabled")}.");
                if (DebugMode)
                {
                    // Dump the game's live bindings + mod key clash check each
                    // time debug turns on, so the log always shows the current
                    // key config (the player can rebind game keys in-game).
                    InputBindingDump.DumpAll();
                }
                return true;
            }

            // F1 — help
            if (kb[ModKeys.Help].wasPressedThisFrame)
            {
                DebugLogger.LogInput(ModKeys.DisplayName(ModAction.Help), "Help");
                // Key names come from the live binding table, so the help text
                // stays correct after rebinding.
                ScreenReader.Say(Loc.Get("help",
                    ModKeys.DisplayName(ModAction.Help),
                    ModKeys.DisplayName(ModAction.DialogueVoiceToggle),
                    ModKeys.DisplayName(ModAction.ReadFol),
                    ModKeys.DisplayName(ModAction.ModMenu),
                    ModKeys.DisplayName(ModAction.NavCategoryPrev),
                    ModKeys.DisplayName(ModAction.NavCategoryNext),
                    ModKeys.DisplayName(ModAction.NavItemPrev),
                    ModKeys.DisplayName(ModAction.NavItemNext),
                    ModKeys.DisplayName(ModAction.NavAutoWalkToggle),
                    ModKeys.DisplayName(ModAction.PauseTierDown),
                    ModKeys.DisplayName(ModAction.PauseTierUp),
                    ModKeys.DisplayName(ModAction.PauseCharLeft),
                    ModKeys.DisplayName(ModAction.PauseCharRight),
                    ModKeys.DisplayName(ModAction.CampStoryHint),
                    ModKeys.DisplayName(ModAction.QuickRecoveryStatus),
                    ModKeys.DisplayName(ModAction.DebugToggle)));
                return true;
            }

            // F2 — toggle dialogue voice mode
            if (kb[ModKeys.DialogueVoiceToggle].wasPressedThisFrame)
            {
                ModSettings.DialogueVoiceMode =
                    ModSettings.DialogueVoiceMode == DialogueVoiceMode.Full
                        ? DialogueVoiceMode.NameOnlyWhenVoiced
                        : DialogueVoiceMode.Full;
                string locKey = ModSettings.DialogueVoiceMode == DialogueVoiceMode.Full
                    ? "dialogue_mode_full"
                    : "dialogue_mode_name_only";
                ScreenReader.Say(Loc.Get(locKey));
                MelonLogger.Msg($"Dialogue voice mode: {ModSettings.DialogueVoiceMode}.");
                ModSettings.Save();
                return true;
            }

            // F3 — read current Fol
            if (kb[ModKeys.ReadFol].wasPressedThisFrame)
            {
                DebugLogger.LogInput(ModKeys.DisplayName(ModAction.ReadFol), "ReadFol");
                AnnounceFol();
                return true;
            }

            // Battle pause menu — tier/character cycling (takes priority over
            // nav, which shares the same physical keys in a different context).
            if (_battlePauseHandler.IsPauseOpen)
            {
                if (kb[ModKeys.PauseTierUp].wasPressedThisFrame)
                {
                    DebugLogger.LogInput(ModKeys.DisplayName(ModAction.PauseTierUp), "PauseTierUp");
                    _battlePauseHandler.TierUp();
                    return true;
                }
                if (kb[ModKeys.PauseTierDown].wasPressedThisFrame)
                {
                    DebugLogger.LogInput(ModKeys.DisplayName(ModAction.PauseTierDown), "PauseTierDown");
                    _battlePauseHandler.TierDown();
                    return true;
                }
                if (kb[ModKeys.PauseCharLeft].wasPressedThisFrame)
                {
                    DebugLogger.LogInput(ModKeys.DisplayName(ModAction.PauseCharLeft), "PauseCharLeft");
                    _battlePauseHandler.CycleCharacterLeft();
                    return true;
                }
                if (kb[ModKeys.PauseCharRight].wasPressedThisFrame)
                {
                    DebugLogger.LogInput(ModKeys.DisplayName(ModAction.PauseCharRight), "PauseCharRight");
                    _battlePauseHandler.CycleCharacterRight();
                    return true;
                }
                return false;
            }

            // Modeless navigation — always active on a free field. Each key
            // ensures the background list is built (and fresh, for category
            // keys) before acting; when the field is busy the handler returns
            // false and the key passes through to the game untouched.
            if (kb[ModKeys.NavCategoryPrev].wasPressedThisFrame)
            {
                DebugLogger.LogInput(ModKeys.DisplayName(ModAction.NavCategoryPrev), "NavCategoryPrev");
                return _navigationHandler.ModelessCategoryPrev();
            }
            if (kb[ModKeys.NavCategoryNext].wasPressedThisFrame)
            {
                DebugLogger.LogInput(ModKeys.DisplayName(ModAction.NavCategoryNext), "NavCategoryNext");
                return _navigationHandler.ModelessCategoryNext();
            }
            if (kb[ModKeys.NavItemPrev].wasPressedThisFrame)
            {
                DebugLogger.LogInput(ModKeys.DisplayName(ModAction.NavItemPrev), "NavItemPrev");
                return _navigationHandler.ModelessItemPrev();
            }
            if (kb[ModKeys.NavItemNext].wasPressedThisFrame)
            {
                DebugLogger.LogInput(ModKeys.DisplayName(ModAction.NavItemNext), "NavItemNext");
                return _navigationHandler.ModelessItemNext();
            }
            if (kb[ModKeys.NavAutoWalkToggle].wasPressedThisFrame)
            {
                DebugLogger.LogInput(ModKeys.DisplayName(ModAction.NavAutoWalkToggle), "NavAutoWalkToggle");
                return _navigationHandler.ModelessAutoWalkToggle();
            }

            // Movement keys cancel auto-walk silently — player takes manual control.
            if (_navigationHandler.IsAutoWalking)
            {
                if (kb[Key.W].wasPressedThisFrame || kb[Key.A].wasPressedThisFrame ||
                    kb[Key.S].wasPressedThisFrame || kb[Key.D].wasPressedThisFrame ||
                    kb[Key.UpArrow].wasPressedThisFrame || kb[Key.DownArrow].wasPressedThisFrame ||
                    kb[Key.LeftArrow].wasPressedThisFrame || kb[Key.RightArrow].wasPressedThisFrame)
                {
                    DebugLogger.LogInput("MovementKey", "CancelAutoWalk");
                    _navigationHandler.CancelAutoWalk();
                    // Don't return true — let the game process the movement input normally.
                }
            }

            return false;
        }

        /// <summary>
        /// Announces the player's current Fol (money) via screen reader.
        /// </summary>
        private void AnnounceFol()
        {
            try
            {
                var em = EventManager.Instance;
                if (em == null)
                {
                    DebugLogger.Log(LogCategory.Handler, "AnnounceFol", "EventManager.Instance is null");
                    return;
                }
                int fol = em.GetMoney();
                ScreenReader.Say(Loc.Get("fol_amount", fol));
            }
            catch (System.Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "AnnounceFol", ex.Message);
            }
        }

        /// <summary>
        /// Processes the gamepad hold-to-open navigation overlay each frame.
        /// The mod modifier is L2 (ModKeys.NavModifier) — L1 belongs to the
        /// game (pickpocket, battle arts).
        /// L2 pressed: opens nav list (field only, not in menus/battle).
        /// L2 held: D-pad Up/Down switches category, D-pad Left/Right switches item.
        /// L2 held + Left stick up: starts auto-walk to highlighted item.
        /// L2 released: closes nav list silently.
        /// </summary>
        private void ProcessGamepad()
        {
            var gp = Gamepad.current;
            if (gp == null)
            {
                // No gamepad connected — ensure state is clean.
                if (_gamepadModHeld)
                {
                    _gamepadModHeld = false;
                    _navigationHandler.GamepadCloseNav();
                }

                // Periodic diagnostic: report no gamepad detected.
                if (DebugMode)
                {
                    _gamepadDiagTimer -= Time.deltaTime;
                    if (_gamepadDiagTimer <= 0f)
                    {
                        _gamepadDiagTimer = 3f;
                        MelonLogger.Msg("[GAMEPAD DIAG] Gamepad.current is NULL — no gamepad detected by Unity InputSystem.");
                    }
                }
                return;
            }

            // Periodic diagnostic: dump gamepad button state every 2 seconds in debug mode.
            if (DebugMode)
            {
                _gamepadDiagTimer -= Time.deltaTime;
                if (_gamepadDiagTimer <= 0f)
                {
                    _gamepadDiagTimer = 2f;
                    bool l1 = gp.leftShoulder.isPressed;
                    bool l2 = ModKeys.NavModifier(gp).isPressed;
                    bool rs = gp.rightShoulder.isPressed;
                    bool du = gp.dpad.up.isPressed;
                    bool dd = gp.dpad.down.isPressed;
                    bool dl = gp.dpad.left.isPressed;
                    bool dr = gp.dpad.right.isPressed;
                    float ly = gp.leftStick.y.ReadValue();
                    MelonLogger.Msg($"[GAMEPAD DIAG] L1={l1} L2={l2} R1={rs} DUp={du} DDown={dd} DLeft={dl} DRight={dr} LStickY={ly:F2} | _gamepadModHeld={_gamepadModHeld} navOpen={_navigationHandler.IsListOpen} autoWalk={_navigationHandler.IsAutoWalking}");
                }
            }

            // Mod menu — L2+L3 to toggle, then consume all gamepad input while open.
            if (ModKeys.NavModifier(gp).isPressed && ModKeys.ModMenuChord(gp).wasPressedThisFrame)
            {
                DebugLogger.LogInput(ModKeys.NavModifierName + "+L3", "ModMenuToggle");
                _modMenuHandler.Toggle();
                return;
            }
            // L2+R3 — read current Fol
            if (ModKeys.NavModifier(gp).isPressed && ModKeys.ReadFolChord(gp).wasPressedThisFrame)
            {
                DebugLogger.LogInput(ModKeys.NavModifierName + "+R3", "ReadFol");
                AnnounceFol();
                return;
            }
            if (_modMenuHandler.IsOpen)
            {
                _modMenuHandler.ProcessGamepad(gp);
                return;
            }

            // Battle pause menu — L1/R1 for tier cycling.
            // Game uses ALL D-pad directions for character cycling natively;
            // our polling detects character index changes and announces.
            // L1/R1 are free during pause (nav overlay blocked by return).
            if (_battlePauseHandler.IsPauseOpen)
            {
                if (ModKeys.PauseTierUpPad(gp).wasPressedThisFrame)
                {
                    DebugLogger.LogInput("L1", "PauseTierUp");
                    _battlePauseHandler.TierUp();
                }
                else if (ModKeys.PauseTierDownPad(gp).wasPressedThisFrame)
                {
                    DebugLogger.LogInput("R1", "PauseTierDown");
                    _battlePauseHandler.TierDown();
                }
                return; // Don't process the nav overlay while pause is open
            }

            // Left stick cancels auto-walk silently — player takes manual control.
            if (_navigationHandler.IsAutoWalking && !ModKeys.NavModifier(gp).isPressed)
            {
                float stickMag = gp.leftStick.ReadValue().magnitude;
                if (stickMag > StickCancelThreshold)
                {
                    DebugLogger.LogInput("LStick", "CancelAutoWalk");
                    _navigationHandler.CancelAutoWalk();
                    // Don't return — let the game process the stick input normally.
                }
            }

            var modBtn = ModKeys.NavModifier(gp);
            bool modPressed  = modBtn.wasPressedThisFrame;
            bool modHeld     = modBtn.isPressed;
            bool modReleased = modBtn.wasReleasedThisFrame;

            // Modifier just pressed — open nav overlay (only when field is free).
            if (modPressed)
            {
                // If camp menu (or other overlay) is open, let the button pass
                // through to the game without activating the nav overlay.
                if (CampMenuHandler.IsCampOpen)
                    return;

                _gamepadModHeld = true;
                _dpadRepeater.Reset();
                _stickUpWasActive = false;
                DebugLogger.LogInput(ModKeys.NavModifierName, "GamepadNavOpen");
                _navigationHandler.GamepadOpenNav();
                return;
            }

            // Modifier just released — close nav overlay.
            if (modReleased && _gamepadModHeld)
            {
                _gamepadModHeld = false;
                _dpadRepeater.Reset();
                DebugLogger.LogInput(ModKeys.NavModifierName + " release", "GamepadNavClose");
                _navigationHandler.GamepadCloseNav();
                return;
            }

            // Modifier held — process D-pad navigation and left stick auto-walk.
            if (!_gamepadModHeld || !modHeld) return;
            if (!_navigationHandler.IsListOpen) return;

            // --- D-pad navigation with auto-repeat ---
            bool dUp    = gp.dpad.up.isPressed;
            bool dDown  = gp.dpad.down.isPressed;
            bool dLeft  = gp.dpad.left.isPressed;
            bool dRight = gp.dpad.right.isPressed;

            int currentDir = dUp ? 1 : dDown ? 2 : dLeft ? 3 : dRight ? 4 : 0;
            _dpadRepeater.Update(currentDir, Time.deltaTime, FireDpadAction);

            // --- Left stick up — auto-walk trigger ---
            bool stickUp = gp.leftStick.y.ReadValue() > StickUpThreshold;
            if (stickUp && !_stickUpWasActive)
            {
                DebugLogger.LogInput("LStickUp", "GamepadAutoWalk");
                _navigationHandler.AutoWalkTo();
                // AutoWalkTo closes the list and starts walking.
                // _gamepadModHeld stays true so input suppression continues
                // until L2 is released (prevents accidental camera/movement).
            }
            _stickUpWasActive = stickUp;
        }

        /// <summary>
        /// Dispatches a D-pad action for the gamepad nav overlay.
        /// Up/Down = category switch, Left/Right = item navigation.
        /// </summary>
        private void FireDpadAction(int dir)
        {
            switch (dir)
            {
                case 1: // D-pad Up — previous category
                    _navigationHandler.NavCategoryPrev();
                    break;
                case 2: // D-pad Down — next category
                    _navigationHandler.NavCategoryNext();
                    break;
                case 3: // D-pad Left — previous item
                    _navigationHandler.NavUp();
                    break;
                case 4: // D-pad Right — next item
                    _navigationHandler.NavDown();
                    break;
            }
        }

        #endregion

        #region Handler Updates

        private void UpdateHandlers()
        {
            _languageHandler.Update();
            _navigationHandler.Update();
            _campMenuHandler.Update();
            _shopHandler.Update();
            _guildHandler.Update();
            _enemyProximityHandler.Update();
            _gameOverHandler.Update();
            _notificationHandler.Update();
            _saveNotificationHandler.Update();
            _battleTargetHandler.Update();
            _battlePauseHandler.Update();
            _battleMenuHandler.Update();
            _equipWizardHandler.Update();
            _worldMapHandler.Update();
            _privateActionHandler.Update();
            _bonusGaugeHandler.Update();
            _dialogueChoiceHandler.Update();
            _pickpocketHandler.Update();
            _quickRecoveryHandler.Update();
            _fieldPromptHandler.Update();
            _fishCollectorHandler.Update();
            _listSelectionHandler.Update();
            _subtitleHandler.Update();
        }

        #endregion
    }
}
