# Code Index: Main.cs

## Top-Level Comments

- Lines 9-12: Warning — do NOT access game singletons (GameManager.Instance, etc.) in
  OnInitializeMelon() or before CheckGameReady() returns true. Accessing game code before
  the game is fully loaded will crash. Safe access begins in OnSceneWasLoaded() or when
  CheckGameReady() passes.
- Line 14: Assembly attribute — MelonInfo declaring mod name "SO2RAccess", version "0.1.0",
  author "Accessibility Mod"
- Line 15: Assembly attribute — MelonGame targeting "SquareEnix" / "SO2R"

---

## Class: Main (line 25)

Extends MelonMod. Namespace: SO2RAccess.

Note: Intended to stay small. Only lifecycle, hotkey dispatch, and handler instantiation
belong here. All feature logic lives in separate Handler classes.

### Fields

- `public static bool DebugMode` (line 36) — toggled by F12; gates all debug logging
- `private bool _gameReady` (line 29)
- `private HarmonyLib.Harmony _harmony` (line 30)
- `private TitleMenuHandler _titleHandler` (line 39)
- `private ConfigMenuHandler _configHandler` (line 40)
- `private KeyboardMenuHandler _keyboardHandler` (line 41)
- `private GamepadMenuHandler _gamepadHandler` (line 42)
- `private HeroSelectHandler _heroSelectHandler` (line 43)
- `private NewGameSettingsHandler _newGameSettingsHandler` (line 44)
- `private LoadGameHandler _loadGameHandler` (line 45)
- `private DialogueHandler _dialogueHandler` (line 46)
- `private NotificationHandler _notificationHandler` (line 47)
- `private NavigationHandler _navigationHandler` (line 48)
- `private CampMenuHandler _campMenuHandler` (line 49)
- `private BattleResultHandler _battleResultHandler` (line 50)
- `private BattleCounterHandler _battleCounterHandler` (line 51)
- `private ShopHandler _shopHandler` (line 52)
- `private EnemyProximityHandler _enemyProximityHandler` (line 53)
- `private GameOverHandler _gameOverHandler` (line 54)
- `private bool _gamepadL1Held` (line 57)
- `private float _dpadRepeatTimer` (line 58)
- `private int _dpadRepeatDir` (line 59) — 0=none, 1=up, 2=down, 3=left, 4=right
- `private bool _stickUpWasActive` (line 60)
- `private const float DpadRepeatInitial` (line 62) — value: 0.4f
- `private const float DpadRepeatInterval` (line 63) — value: 0.15f
- `private const float StickUpThreshold` (line 64) — value: 0.5f
- `private float _gamepadDiagTimer` (line 300) — declared outside its region; used only
  inside ProcessGamepad() for periodic debug logging

### Methods

#### Lifecycle (lines 68-198)

- `public override void OnInitializeMelon()` (line 74)
  Note: Initializes ScreenReader, AudioCuePlayer, SpatialAudioPlayer (loads WAV from disk),
  Loc, all handlers, and starts the delayed startup announcement coroutine. Does NOT touch
  game code.

- `private void InitializeHandlers()` (line 90)
  Note: Instantiates all handler objects and the Harmony instance. Called only from
  OnInitializeMelon().

- `private IEnumerator AnnounceStartupDelayed()` (line 111)
  Note: Coroutine. Waits 1 second then calls ScreenReader.Say with the "mod_loaded"
  localization key. Delay ensures the screen reader is ready.

- `public override void OnUpdate()` (line 122)
  Note: Called every frame. Guards on CheckGameReady(), then runs ProcessGamepad(),
  ProcessHotkeys(), and UpdateHandlers() in that order. Returns early if a hotkey was
  consumed.

- `public override void OnLateUpdate()` (line 135)
  Note: Called every frame after all Unity Update() calls. Only forwards to
  NavigationHandler.LateUpdate() to set walk animation after the game's own animation
  updates.

- `private bool CheckGameReady()` (line 141)
  Note: Returns true immediately once _gameReady is set. First call after game loads checks
  GameManager.Instance != null and latches _gameReady = true. Returns false (blocking all
  feature logic) until then.

- `public override void OnSceneWasLoaded(int buildIndex, string sceneName)` (line 159)
  Note: Resets _gameReady so CheckGameReady() re-verifies for the new scene. Cancels
  auto-walk silently, notifies ShopHandler, EnemyProximityHandler, and GameOverHandler of
  the scene change, then applies Harmony patches for all handlers. Patch application is
  idempotent — handlers guard against duplicate patching.

- `public override void OnApplicationQuit()` (line 191)
  Note: Shuts down SpatialAudioPlayer, ScreenReader, and AudioCuePlayer in that order.

#### Hotkeys (lines 200-291)

- `private bool ProcessHotkeys()` (line 206)
  Note: Reads Keyboard.current each frame. Handles F12 (debug toggle), F1 (help announce),
  NumPad5 (nav list toggle / auto-walk cancel), and NumPad 8/2/4/6/1 (nav list navigation,
  only when the list is open). Returns true if any key was consumed, preventing further
  handler updates that frame.

- `private void ProcessGamepad()` (line 302)
  Note: Handles the L1 hold-to-open nav overlay pattern. L1 press opens nav (skipped if
  camp menu is open), L1 release closes nav, L1 held drives D-pad navigation with
  auto-repeat (initial delay 0.4s, repeat interval 0.15s) and left-stick-up auto-walk
  trigger. Includes periodic debug diagnostic logging when DebugMode is on.

- `private void FireDpadAction(int dir)` (line 429)
  Note: Translates an integer direction (1=up, 2=down, 3=left, 4=right) into the
  appropriate NavigationHandler category/item call. Despite "D-pad Up/Down" names, Up
  maps to NavCategoryPrev and Left maps to NavUp (previous item).

#### Handler Updates (lines 450-462)

- `private void UpdateHandlers()` (line 452)
  Note: Calls Update() on each handler that requires per-frame polling:
  NavigationHandler, CampMenuHandler, ShopHandler, EnemyProximityHandler,
  GameOverHandler, NotificationHandler. Handlers that are purely hook-driven are
  not listed here.
