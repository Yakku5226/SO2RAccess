# Main.cs (978 lines)

// Do NOT access game singletons in OnInitializeMelon() or before CheckGameReady().
// Safe access begins in OnSceneWasLoaded() or when CheckGameReady() passes.
// [assembly: MelonInfo] + [assembly: MelonGame] attributes at lines 15-16.
namespace: SO2RAccess (line 18)
usings (non-System / notable only): HarmonyLib, MelonLoader, UnityEngine, UnityEngine.InputSystem, Il2CppGame

## class Main : MelonMod (line 26)
Main mod entry point. Initializes all systems and dispatches global hotkeys. Keep small — only lifecycle, hotkey dispatch, and handler instantiation.

fields/properties (declaration order):
- _gameReady : bool (line 30)
- _harmony : HarmonyLib.Harmony (line 31)
- DebugMode : static bool (line 37)  — toggle with F12; logs all screen reader output and game state detail
- _titleHandler : TitleMenuHandler (line 40)
- _configHandler : ConfigMenuHandler (line 41)
- _keyboardHandler : KeyboardMenuHandler (line 42)
- _gamepadHandler : GamepadMenuHandler (line 43)
- _heroSelectHandler : HeroSelectHandler (line 44)
- _newGameSettingsHandler : NewGameSettingsHandler (line 45)
- _loadGameHandler : LoadGameHandler (line 46)
- _dialogueHandler : DialogueHandler (line 47)
- _notificationHandler : NotificationHandler (line 48)
- _navigationHandler : NavigationHandler (line 49)
- _campMenuHandler : CampMenuHandler (line 50)
- _battleResultHandler : BattleResultHandler (line 51)
- _battleCounterHandler : BattleCounterHandler (line 52)
- _shopHandler : ShopHandler (line 53)
- _guildHandler : GuildHandler (line 54)
- _enemyProximityHandler : EnemyProximityHandler (line 55)
- _gameOverHandler : GameOverHandler (line 56)
- _saveNotificationHandler : SaveNotificationHandler (line 57)
- _battleTargetHandler : BattleTargetHandler (line 58)
- _battlePauseHandler : BattlePauseHandler (line 59)
- _battleMenuHandler : BattleMenuHandler (line 60)
- _battleStatusHandler : BattleStatusHandler (line 61)
- _worldMapHandler : WorldMapHandler (line 62)
- _modMenuHandler : ModMenuHandler (line 63)
- _equipWizardHandler : EquipWizardHandler (line 64)
- _privateActionHandler : PrivateActionHandler (line 65)
- _bonusGaugeHandler : BonusGaugeHandler (line 66)
- _dialogueChoiceHandler : DialogueChoiceHandler (line 67)
- _pickpocketHandler : PickpocketHandler (line 68)
- _quickRecoveryHandler : QuickRecoveryHandler (line 69)
- _fieldPromptHandler : FieldPromptHandler (line 70)
- _gamepadL1Held : bool (line 73)
- _dpadRepeatTimer : float (line 74)
- _dpadRepeatDir : int (line 75)  — 0=none, 1=up, 2=down, 3=left, 4=right
- _stickUpWasActive : bool (line 76)
- _gamepadDiagTimer : float (line 77)
- DpadRepeatInitial : const float = 0.4f (line 79)
- DpadRepeatInterval : const float = 0.15f (line 80)
- StickUpThreshold : const float = 0.5f (line 81)
- StickCancelThreshold : const float = 0.3f (line 83)  — left stick magnitude above which auto-walk is cancelled

methods (declaration order):
- void OnInitializeMelon() (line 93)
  - note: MelonLoader lifecycle entry point; initializes ScreenReader, ModSettings, AudioCuePlayer, SpatialAudioPlayer, all sound files, Loc, all handlers; starts AnnounceStartupDelayed coroutine. Do NOT touch game code here.
- void InitializeHandlers() (line 125)
  - note: Creates Harmony instance and instantiates every handler in declaration order.
- IEnumerator AnnounceStartupDelayed() (line 161)
  - note: 1-second delay coroutine, then ScreenReader.Say(mod_loaded).
- void OnUpdate() (line 172)
  - note: MelonLoader per-frame; guards on CheckGameReady(), then calls ProcessGamepad(), ProcessHotkeys(), UpdateHandlers() in that order.
- void OnLateUpdate() (line 185)
  - note: MelonLoader post-Unity-Update; calls DialogueHandler.ProcessPendingDialogue() and _navigationHandler.LateUpdate().
- bool CheckGameReady() (line 192)
  - note: Returns cached _gameReady; sets it true once GameManager.Instance is non-null.
- void OnSceneWasLoaded(int buildIndex, string sceneName) (line 210)
  - note: MelonLoader scene hook; resets _gameReady, calls OnSceneChanged() on all stateful handlers, then calls ApplyPatches(_harmony) on every handler (safe to call multiple times — handlers guard duplicates).
- void OnApplicationQuit() (line 264)
  - note: MelonLoader shutdown; saves traversal, shuts down SpatialAudioPlayer, ScreenReader, AudioCuePlayer.
- bool ProcessHotkeys() (line 280)
  - note: Dispatches F1–F12 and NumPad hotkeys. F5–F11 are DebugMode-only. Returns true if a key was consumed. NumPad 8/2/4/6 are context-sensitive: BattlePauseHandler gets priority, then NavigationHandler when list is open.
- void AnnounceFol() (line 726)
  - note: Reads EventManager.Instance.GetMoney() and announces via Loc.Get("fol_amount").
- void ProcessGamepad() (line 752)
  - note: L1 hold-to-open nav overlay. L1 press opens (blocked when CampMenuHandler.IsCampOpen). D-pad with auto-repeat navigates. Left stick up triggers auto-walk. L1 release closes. L1+L3 toggles mod menu; L1+R3 reads Fol.
- void FireDpadAction(int dir) (line 930)
  - note: dir 1=up→NavCategoryPrev, 2=down→NavCategoryNext, 3=left→NavUp, 4=right→NavDown.
- void UpdateHandlers() (line 953)
  - note: Calls Update() on all polling-based handlers: navigation, camp, shop, guild, enemyProximity, gameOver, notification, saveNotification, battleTarget, battlePause, battleMenu, equipWizard, worldMap, privateAction, bonusGauge, dialogueChoice, pickpocket, quickRecovery, fieldPrompt.
