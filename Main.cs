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

[assembly: MelonInfo(typeof(SO2RAccess.Main), "SO2RAccess", "0.1.0", "Accessibility Mod")]
[assembly: MelonGame("SquareEnix", "SO2R")]

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

        // Gamepad nav overlay — L1 hold-to-open state.
        private bool _gamepadL1Held;
        private float _dpadRepeatTimer;
        private int _dpadRepeatDir; // 0=none, 1=up, 2=down, 3=left, 4=right
        private bool _stickUpWasActive;
        private float _gamepadDiagTimer;

        private const float DpadRepeatInitial = 0.4f;
        private const float DpadRepeatInterval = 0.15f;
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

            // Apply patches once — safe to call on every scene load, handlers guard against duplicates.
            _titleHandler.ApplyPatches(_harmony);
            _configHandler.ApplyPatches(_harmony);
            _keyboardHandler.ApplyPatches(_harmony);
            _gamepadHandler.ApplyPatches(_harmony);
            _heroSelectHandler.ApplyPatches(_harmony);
            _newGameSettingsHandler.ApplyPatches(_harmony);
            _loadGameHandler.ApplyPatches(_harmony);
            _dialogueHandler.ApplyPatches(_harmony);
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
            if (kb[Key.F4].wasPressedThisFrame && !_modMenuHandler.IsOpen)
            {
                DebugLogger.LogInput("F4", "ModMenuOpen");
                _modMenuHandler.Open();
                return true;
            }

            // Mod menu consumes all keyboard input while open
            if (_modMenuHandler.IsOpen)
                return _modMenuHandler.ProcessKeyboard(kb);

            // F5 — scan L22/L23 obstacle parents near player (debug only, world map)
            if (DebugMode && kb[Key.F5].wasPressedThisFrame)
            {
                try
                {
                    var player = Il2CppGame.FieldManager.Instance?.GetControlPlayer();
                    if (player != null)
                    {
                        Vector3 pos = player.transform.position;
                        MelonLogger.Msg($"[F5] Scanning L22+L23 colliders within 50m of ({pos.x:F1},{pos.y:F1},{pos.z:F1})...");

                        int layerMask = (1 << 22) | (1 << 23);
                        var cols = UnityEngine.Physics.OverlapSphere(pos, 50f, layerMask);
                        if (cols == null || cols.Length == 0)
                        {
                            MelonLogger.Msg("[F5] No L22/L23 colliders found within 50m.");
                        }
                        else
                        {
                            // Group by parent name
                            var groups = new System.Collections.Generic.Dictionary<string,
                                (int count, float minX, float maxX, float minZ, float maxZ, int layer)>();

                            foreach (var col in cols)
                            {
                                if (col == null || col.isTrigger) continue;
                                string parentName = "?";
                                var parentT = col.transform.parent;
                                if (parentT != null) parentName = parentT.gameObject.name;
                                int layer = col.gameObject.layer;
                                string key = $"{parentName}(L{layer})";

                                var b = col.bounds;
                                if (groups.ContainsKey(key))
                                {
                                    var g = groups[key];
                                    g.count++;
                                    if (b.min.x < g.minX) g.minX = b.min.x;
                                    if (b.max.x > g.maxX) g.maxX = b.max.x;
                                    if (b.min.z < g.minZ) g.minZ = b.min.z;
                                    if (b.max.z > g.maxZ) g.maxZ = b.max.z;
                                    groups[key] = g;
                                }
                                else
                                {
                                    groups[key] = (1, b.min.x, b.max.x, b.min.z, b.max.z, layer);
                                }
                            }

                            MelonLogger.Msg($"[F5] Found {cols.Length} colliders in {groups.Count} groups:");
                            foreach (var kv in groups)
                            {
                                var g = kv.Value;
                                float sizeX = g.maxX - g.minX;
                                float sizeZ = g.maxZ - g.minZ;
                                MelonLogger.Msg(
                                    $"[F5]   {kv.Key}: {g.count} colliders, " +
                                    $"X=[{g.minX:F1},{g.maxX:F1}] Z=[{g.minZ:F1},{g.maxZ:F1}] " +
                                    $"size={sizeX:F1}x{sizeZ:F1}m");
                            }
                        }

                        ScreenReader.Say("Obstacle scan complete. Check log.");
                    }
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Msg($"[F5] Error: {ex.Message}");
                }
                return true;
            }

            // F6 — test AIMoveController.Destination (debug only, world map)
            if (DebugMode && kb[Key.F6].wasPressedThisFrame)
            {
                try
                {
                    var fm = FieldManager.Instance;
                    if (fm != null && fm.IsWorldmap())
                    {
                        var player = fm.GetControlPlayer();
                        if (player != null)
                        {
                            var aiCtrl = player.FieldAIController;
                            if (aiCtrl == null)
                            {
                                ScreenReader.Say("No AI controller on player.");
                                MelonLogger.Msg("[F6] FieldAIController is null.");
                            }
                            else
                            {
                                var moveCtrl = aiCtrl.AIMoveController;
                                if (moveCtrl == null)
                                {
                                    ScreenReader.Say("No move controller available.");
                                    MelonLogger.Msg("[F6] AIMoveController is null.");
                                }
                                else
                                {
                                    var playerPos = player.transform.position;
                                    var target = playerPos + new Vector3(0, 0, -10);

                                    MelonLogger.Msg($"[F6] AIMoveController type: {moveCtrl.GetType().Name}");
                                    MelonLogger.Msg($"[F6] Current Destination: ({moveCtrl.Destination.x:F1},{moveCtrl.Destination.y:F1},{moveCtrl.Destination.z:F1})");
                                    MelonLogger.Msg($"[F6] Current moveState: {moveCtrl.MoveState}");
                                    MelonLogger.Msg($"[F6] Setting Destination to ({target.x:F1},{target.y:F1},{target.z:F1}) (10m south)");

                                    moveCtrl.Destination = target;
                                    moveCtrl.FinalDestination = target;

                                    MelonLogger.Msg($"[F6] After set — Destination: ({moveCtrl.Destination.x:F1},{moveCtrl.Destination.y:F1},{moveCtrl.Destination.z:F1})");
                                    MelonLogger.Msg($"[F6] After set — moveState: {moveCtrl.MoveState}");

                                    ScreenReader.Say("AIMoveController destination set 10m south. Watch player movement. Check log.");
                                }
                            }
                        }
                    }
                    else
                    {
                        ScreenReader.Say("F6 test only available on world map.");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg($"[F6] AIMoveController test error: {ex}");
                    ScreenReader.Say($"F6 test failed: {ex.Message}");
                }
                return true;
            }

            // F7 — test game's WorldmapFindPath (debug only, world map)
            if (DebugMode && kb[Key.F7].wasPressedThisFrame)
            {
                try
                {
                    var fm = FieldManager.Instance;
                    if (fm != null && fm.IsWorldmap())
                    {
                        var player = fm.GetControlPlayer();
                        if (player != null)
                        {
                            var aiCtrl = player.FieldAIController;
                            var aiParam = aiCtrl?.aiParameter;
                            var pf = aiParam?.aiPathFinder;

                            if (pf == null)
                            {
                                ScreenReader.Say("No pathfinder available.");
                                MelonLogger.Msg("[F7] AIPathFinder is null.");
                            }
                            else
                            {
                                var playerPos = player.transform.position;
                                var target = NavigationHandler.LastAutoWalkTarget;
                                if (!target.HasValue)
                                {
                                    ScreenReader.Say("No target. Auto-walk first, then press F7.");
                                }
                                else
                                {
                                    var targetPos = target.Value;
                                    MelonLogger.Msg($"[F7] Testing WorldmapFindPath...");
                                    MelonLogger.Msg($"[F7] Player: ({playerPos.x:F1},{playerPos.y:F1},{playerPos.z:F1})");
                                    MelonLogger.Msg($"[F7] Target: ({targetPos.x:F1},{targetPos.y:F1},{targetPos.z:F1})");

                                    bool result = pf.WorldmapFindPath(ref playerPos, ref targetPos);
                                    int count = pf.routeCount;
                                    MelonLogger.Msg($"[F7] Result: {result}, routeCount: {count}");

                                    if (count > 0 && pf.routes != null)
                                    {
                                        int logCount = Math.Min(count, 10);
                                        for (int i = 0; i < logCount; i++)
                                        {
                                            var wp = pf.routes[i];
                                            float dist = UnityEngine.Vector3.Distance(playerPos, wp);
                                            MelonLogger.Msg($"[F7] route[{i}]: ({wp.x:F1},{wp.y:F1},{wp.z:F1}) dist={dist:F1}m");
                                        }
                                    }

                                    ScreenReader.Say($"WorldmapFindPath returned {result}, {count} routes. Check log.");
                                }
                            }
                        }
                    }
                    else
                    {
                        ScreenReader.Say("Only available on world map.");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg($"[F7] WorldmapFindPath error: {ex}");
                    ScreenReader.Say($"WorldmapFindPath failed: {ex.Message}");
                }
                return true;
            }

            // F9 — generate world map grid (debug only)
            if (DebugMode && kb[Key.F9].wasPressedThisFrame)
            {
                try
                {
                    WorldmapGridGenerator.GenerateAndSave();
                    WorldmapPathfinder.ClearCache();
                }
                catch (Exception ex)
                {
                    MelonLoader.MelonLogger.Msg($"F9 grid generation error: {ex.Message}");
                }
                return true;
            }

            // F8 — CharaWall boundary scan (debug only, world map)
            if (DebugMode && kb[Key.F8].wasPressedThisFrame)
            {
                try
                {
                    if (FieldManager.Instance != null &&
                        FieldManager.Instance.IsWorldmap())
                    {
                        var player = FieldManager.Instance.GetControlPlayer();
                        if (player != null)
                        {
                            WorldmapDiagnostics.ScanCharaWalls(
                                player.transform.position);
                        }
                    }
                    else
                    {
                        ScreenReader.Say("Wall scan only available on world map.");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg($"F8 wall scan error: {ex.Message}");
                }
                return true;
            }

            // F10 — player collider diagnostics (debug only)
            if (DebugMode && kb[Key.F10].wasPressedThisFrame)
            {
                try
                {
                    WorldmapGridGenerator.LogPlayerCollider();
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg($"F10 collider diagnostics error: {ex.Message}");
                }
                return true;
            }

            // F11 — diagnostics (world map or field map NavMesh islands)
            if (DebugMode && kb[Key.F11].wasPressedThisFrame)
            {
                try
                {
                    var fm = FieldManager.Instance;
                    if (fm != null)
                    {
                        var player = fm.GetControlPlayer();
                        if (player != null)
                        {
                            if (fm.IsWorldmap())
                            {
                                WorldmapDiagnostics.RunAll(
                                    player.transform.position);
                            }
                            else
                            {
                                // Recorded-traversal reachability report.
                                _navigationHandler.LogTraversalDiagnostic(
                                    player.transform.position);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg($"F11 diagnostics error: {ex.Message}");
                }
                return true;
            }

            // F12 — toggle debug mode
            if (kb[Key.F12].wasPressedThisFrame)
            {
                DebugMode = !DebugMode;
                ScreenReader.Say(Loc.Get(DebugMode ? "debug_on" : "debug_off"));
                MelonLogger.Msg($"Debug mode {(DebugMode ? "enabled" : "disabled")}.");
                return true;
            }

            // F1 — help
            if (kb[Key.F1].wasPressedThisFrame)
            {
                DebugLogger.LogInput("F1", "Help");
                ScreenReader.Say(Loc.Get("help"));
                return true;
            }

            // F2 — toggle dialogue voice mode
            if (kb[Key.F2].wasPressedThisFrame)
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
            if (kb[Key.F3].wasPressedThisFrame)
            {
                DebugLogger.LogInput("F3", "ReadFol");
                AnnounceFol();
                return true;
            }

            // Battle pause menu — tier/character cycling (takes priority over nav)
            if (_battlePauseHandler.IsPauseOpen)
            {
                if (kb[Key.Numpad8].wasPressedThisFrame)
                {
                    DebugLogger.LogInput("Numpad8", "PauseTierUp");
                    _battlePauseHandler.TierUp();
                    return true;
                }
                if (kb[Key.Numpad2].wasPressedThisFrame)
                {
                    DebugLogger.LogInput("Numpad2", "PauseTierDown");
                    _battlePauseHandler.TierDown();
                    return true;
                }
                if (kb[Key.Numpad4].wasPressedThisFrame)
                {
                    DebugLogger.LogInput("Numpad4", "PauseCharLeft");
                    _battlePauseHandler.CycleCharacterLeft();
                    return true;
                }
                if (kb[Key.Numpad6].wasPressedThisFrame)
                {
                    DebugLogger.LogInput("Numpad6", "PauseCharRight");
                    _battlePauseHandler.CycleCharacterRight();
                    return true;
                }
                return false;
            }

            // NumPad 5 — toggle navigation list (also cancels auto-walk)
            if (kb[Key.Numpad5].wasPressedThisFrame)
            {
                DebugLogger.LogInput("Numpad5", "NavToggle");
                _navigationHandler.ToggleNavList();
                return true;
            }

            // NumPad navigation — only active while the nav list is open
            if (_navigationHandler.IsListOpen)
            {
                if (kb[Key.Numpad8].wasPressedThisFrame)
                {
                    DebugLogger.LogInput("Numpad8", "NavUp");
                    _navigationHandler.NavUp();
                    return true;
                }
                if (kb[Key.Numpad2].wasPressedThisFrame)
                {
                    DebugLogger.LogInput("Numpad2", "NavDown");
                    _navigationHandler.NavDown();
                    return true;
                }
                if (kb[Key.Numpad4].wasPressedThisFrame)
                {
                    DebugLogger.LogInput("Numpad4", "NavCategoryPrev");
                    _navigationHandler.NavCategoryPrev();
                    return true;
                }
                if (kb[Key.Numpad6].wasPressedThisFrame)
                {
                    DebugLogger.LogInput("Numpad6", "NavCategoryNext");
                    _navigationHandler.NavCategoryNext();
                    return true;
                }
                if (kb[Key.Numpad1].wasPressedThisFrame)
                {
                    DebugLogger.LogInput("Numpad1", "AutoWalkTo");
                    _navigationHandler.AutoWalkTo();
                    return true;
                }
            }

            // NumPad 1 also cancels an active auto-walk
            if (_navigationHandler.IsAutoWalking && kb[Key.Numpad1].wasPressedThisFrame)
            {
                DebugLogger.LogInput("Numpad1", "CancelAutoWalk");
                _navigationHandler.CancelAutoWalk();
                return true;
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
        /// Processes gamepad L1 hold-to-open navigation overlay each frame.
        /// L1 pressed: opens nav list (field only, not in menus/battle).
        /// L1 held: D-pad Up/Down switches category, D-pad Left/Right switches item.
        /// L1 held + Left stick up: starts auto-walk to highlighted item.
        /// L1 released: closes nav list silently.
        /// </summary>
        private void ProcessGamepad()
        {
            var gp = Gamepad.current;
            if (gp == null)
            {
                // No gamepad connected — ensure state is clean.
                if (_gamepadL1Held)
                {
                    _gamepadL1Held = false;
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
                    bool ls = gp.leftShoulder.isPressed;
                    bool rs = gp.rightShoulder.isPressed;
                    bool du = gp.dpad.up.isPressed;
                    bool dd = gp.dpad.down.isPressed;
                    bool dl = gp.dpad.left.isPressed;
                    bool dr = gp.dpad.right.isPressed;
                    float ly = gp.leftStick.y.ReadValue();
                    MelonLogger.Msg($"[GAMEPAD DIAG] L1={ls} R1={rs} DUp={du} DDown={dd} DLeft={dl} DRight={dr} LStickY={ly:F2} | _gamepadL1Held={_gamepadL1Held} navOpen={_navigationHandler.IsListOpen} autoWalk={_navigationHandler.IsAutoWalking}");
                }
            }

            // Mod menu — L1+L3 to toggle, then consume all gamepad input while open.
            if (gp.leftShoulder.isPressed && gp.leftStickButton.wasPressedThisFrame)
            {
                DebugLogger.LogInput("L1+L3", "ModMenuToggle");
                _modMenuHandler.Toggle();
                return;
            }
            // L1+R3 — read current Fol
            if (gp.leftShoulder.isPressed && gp.rightStickButton.wasPressedThisFrame)
            {
                DebugLogger.LogInput("L1+R3", "ReadFol");
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
                if (gp.leftShoulder.wasPressedThisFrame)
                {
                    DebugLogger.LogInput("L1", "PauseTierUp");
                    _battlePauseHandler.TierUp();
                }
                else if (gp.rightShoulder.wasPressedThisFrame)
                {
                    DebugLogger.LogInput("R1", "PauseTierDown");
                    _battlePauseHandler.TierDown();
                }
                return; // Don't process L1 nav overlay while pause is open
            }

            // Left stick cancels auto-walk silently — player takes manual control.
            if (_navigationHandler.IsAutoWalking && !gp.leftShoulder.isPressed)
            {
                float stickMag = gp.leftStick.ReadValue().magnitude;
                if (stickMag > StickCancelThreshold)
                {
                    DebugLogger.LogInput("LStick", "CancelAutoWalk");
                    _navigationHandler.CancelAutoWalk();
                    // Don't return — let the game process the stick input normally.
                }
            }

            bool l1Pressed = gp.leftShoulder.wasPressedThisFrame;
            bool l1Held    = gp.leftShoulder.isPressed;
            bool l1Released = gp.leftShoulder.wasReleasedThisFrame;

            // L1 just pressed — open nav overlay (only when field is free).
            if (l1Pressed)
            {
                // If camp menu (or other overlay) is open, let L1 pass through
                // to the game without activating the nav overlay.
                if (CampMenuHandler.IsCampOpen)
                    return;

                _gamepadL1Held = true;
                _dpadRepeatDir = 0;
                _dpadRepeatTimer = 0f;
                _stickUpWasActive = false;
                DebugLogger.LogInput("L1", "GamepadNavOpen");
                _navigationHandler.GamepadOpenNav();
                return;
            }

            // L1 just released — close nav overlay.
            if (l1Released && _gamepadL1Held)
            {
                _gamepadL1Held = false;
                _dpadRepeatDir = 0;
                DebugLogger.LogInput("L1 release", "GamepadNavClose");
                _navigationHandler.GamepadCloseNav();
                return;
            }

            // L1 held — process D-pad navigation and left stick auto-walk.
            if (!_gamepadL1Held || !l1Held) return;
            if (!_navigationHandler.IsListOpen) return;

            // --- D-pad navigation with auto-repeat ---
            bool dUp    = gp.dpad.up.isPressed;
            bool dDown  = gp.dpad.down.isPressed;
            bool dLeft  = gp.dpad.left.isPressed;
            bool dRight = gp.dpad.right.isPressed;

            int currentDir = dUp ? 1 : dDown ? 2 : dLeft ? 3 : dRight ? 4 : 0;

            if (currentDir == 0)
            {
                // No D-pad held — reset repeat.
                _dpadRepeatDir = 0;
                _dpadRepeatTimer = 0f;
            }
            else if (currentDir != _dpadRepeatDir)
            {
                // New direction pressed — act immediately, start initial delay.
                _dpadRepeatDir = currentDir;
                _dpadRepeatTimer = DpadRepeatInitial;
                FireDpadAction(currentDir);
            }
            else
            {
                // Same direction held — count down repeat timer.
                _dpadRepeatTimer -= Time.deltaTime;
                if (_dpadRepeatTimer <= 0f)
                {
                    _dpadRepeatTimer = DpadRepeatInterval;
                    FireDpadAction(currentDir);
                }
            }

            // --- Left stick up — auto-walk trigger ---
            bool stickUp = gp.leftStick.y.ReadValue() > StickUpThreshold;
            if (stickUp && !_stickUpWasActive)
            {
                DebugLogger.LogInput("LStickUp", "GamepadAutoWalk");
                _navigationHandler.AutoWalkTo();
                // AutoWalkTo closes the list and starts walking.
                // _gamepadL1Held stays true so input suppression continues
                // until L1 is released (prevents accidental camera/movement).
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
        }

        #endregion
    }
}
