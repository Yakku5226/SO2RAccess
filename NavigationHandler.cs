using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SO2RAccess
{
    /// <summary>
    /// Field navigation system — Phase 2: audio navigation list + auto-walk.
    ///
    /// KEYBOARD (modeless — the list lives in the background, no open/close;
    /// defaults in ModKeys.cs)
    /// Left/right bracket — previous / next item within the current category.
    /// Minus / equals     — previous / next non-empty category (also refreshes a stale list).
    /// Backslash          — start auto-walking to the highlighted item, or cancel the walk.
    /// Each key silently (re)builds the list when it is absent or from another map.
    ///
    /// GAMEPAD (modifier = L2, ModKeys.NavModifier)
    /// Hold L2          — scan and open the list while held (field only, not in menus/battle).
    /// D-pad Up/Down    — switch category.
    /// D-pad Left/Right — move to previous / next item within category.
    /// L2 + LStick Up   — start auto-walking to the highlighted item.
    /// Release L2       — close the list silently.
    ///
    /// AUTO-WALK
    /// On activation: closes the list, announces "Walking to [label].", then injects
    /// synthetic left stick input via GetLeftStick() postfix so the game's own movement
    /// pipeline handles physics, colliders, animations, triggers, and party AI.
    /// Announces "Arrived at [label]." on arrival (within 1.8 units).
    /// Backslash cancels while walking. L2 press also cancels and reopens the list.
    ///
    /// Items are sorted by distance (closest first) within each category.
    /// Party members (distance less than 2 units) are filtered from the NPC list.
    /// NPC names are parsed from the ConstNpcParameter code name
    ///   (e.g. NPC_0003_01a_18_GIRL1 → "Girl 1").
    /// Chests are numbered by distance: Unopened chest 1, Unopened chest 2, etc.
    /// Exits show the game's map name: "Building entrance to Arlia Village".
    ///   Names are resolved from ConstFieldParameter via TextManager at runtime.
    /// </summary>
    public partial class NavigationHandler
    {
        #region Constants

        private const int CAT_NPC          = 0;
        private const int CAT_CHEST        = 1;
        private const int CAT_EXIT         = 2;
        private const int CAT_MARKER       = 3;
        private const int CAT_EVENT        = 4;
        private const int CAT_SAVE         = 5;
        private const int CAT_ENEMY        = 6;
        private const int CAT_STAIRS       = 7;
        private const int CAT_DOOR         = 8;
        private const int CAT_WARP         = 9;
        private const int CAT_INTERACTABLE = 10;
        private const int CAT_LOCATION    = 11;
        private const int CAT_COUNT       = 12;

        private static readonly string[] _categoryNames =
            { "NPCs", "Chests", "Exits", "Markers", "Events", "Save Points", "Enemies",
              "Stairs", "Doors", "Warp Points", "Interactables", "Locations" };

        /// <summary>
        /// Manual overrides for FieldmapID destination names.
        /// Checked before the game's own map name data.
        /// </summary>
        private static readonly Dictionary<string, string> _mapNameOverrides =
            new Dictionary<string, string>
        {
            { "EXPEL", "Overworld" },
            { "NEDE",  "Nede"      },
        };

        /// <summary>
        /// Cache of resolved map names from the game's ConstFieldParameter data.
        /// Populated on first lookup per FieldmapID, persists for the session.
        /// </summary>
        private static readonly Dictionary<string, string> _mapNameCache =
            new Dictionary<string, string>();

        /// <summary>
        /// Horizontal distance in world units at which auto-run considers the target reached.
        /// Set to 1.8 units — close enough to be within typical NPC conversation range.
        /// </summary>
        private const float AutoWalkArrivalRadius = 1.8f;

        /// <summary>
        /// Tighter arrival radius for interactable objects (chests, save points).
        /// These require the player to be closer than NPCs to trigger interaction.
        /// </summary>
        private const float InteractableArrivalRadius = 1.3f;

        /// <summary>
        /// Approach radius for NPCs — how close the walk TRIES to get before
        /// stopping. 1.2m: the one logged successful talk fired at 1.0m while
        /// stops at 1.74–1.80m left the action button dead for 10s of following
        /// (Cunning Fighter, 2026-08-30); NPC + player colliders make ~1.0m the
        /// physical minimum. Acceptance stays at AutoWalkArrivalRadius (1.8m) in
        /// IsAtRealTarget: when geometry (carve holes, colliders) exhausts the
        /// path between 1.2 and 1.8m, arrival is still honestly reported.
        /// </summary>
        private const float NpcApproachRadius = 1.2f;

        /// <summary>
        /// Maximum vertical gap (world units) for the player to count as being on
        /// the target's level. Larger gaps mean the target is on a floor above or
        /// below, so arrival must NOT be announced.
        /// </summary>
        private const float ArrivalVerticalTolerance = 2.0f;

        /// <summary>Rotation speed in degrees per second while auto-walking.</summary>
        private const float AutoWalkTurnSpeed = 720f;

        /// <summary>Max distance to snap a world position onto the NavMesh surface.</summary>
        private const float NavMeshSampleRadius = 5f;

        /// <summary>Seconds between path recalculations when following a moving NPC.</summary>
        private const float PathRecalcInterval = 1.5f;

        /// <summary>Distance threshold for advancing to the next path waypoint.
        /// Set to 0.8 to account for physics-based movement via input injection
        /// (the player won't hit waypoints as precisely as transform.position did).</summary>
        private const float WaypointArrivalThreshold = 0.8f;

        /// <summary>
        /// How far an NPC must move from the last path endpoint before triggering
        /// a path recalculation (avoids recalculating for minor movement).
        /// </summary>
        private const float PathRecalcDistanceThreshold = 3f;

        /// <summary>
        /// How far back (in seconds) to check for a recently spoken message that
        /// the arrival announcement would interrupt.
        /// </summary>
        private const float ArrivalRecentWindow = 0.5f;

        /// <summary>Seconds between stuck checks during field map auto-walk.</summary>
        private const float FieldStuckCheckInterval = 2f;

        /// <summary>
        /// Minimum distance the player must move during a field stuck check interval
        /// to be considered making progress. Below this, a path recalculation is attempted.
        /// </summary>
        private const float FieldStuckMinMove = 0.5f;

        /// <summary>
        /// Maximum seconds to spend walking toward a detour point before giving up
        /// and resuming normal pathfinding.
        /// </summary>
        private const float ObstacleAvoidanceTimeout = 8f;

        /// <summary>
        /// Maximum number of obstacle avoidance attempts before giving up entirely.
        /// Prevents infinite stuck loops when the path is truly blocked (e.g. guards).
        /// </summary>
        private const int MaxAvoidanceAttempts = 3;

        /// <summary>
        /// Distance at which the detour point is considered reached during obstacle avoidance.
        /// </summary>
        private const float DetourArrivalRadius = 1.5f;

        /// <summary>
        /// Dead zone for camera follow stick injection. Below this cross-product magnitude,
        /// no camera rotation is applied (prevents jitter when already aligned).
        /// </summary>
        private const float CameraFollowDeadZone = 0.08f;

        /// <summary>
        /// Scale factor for camera follow stick. Higher = faster camera catch-up.
        /// Clamped to [-1, 1] after scaling.
        /// </summary>
        private const float CameraFollowScale = 1.5f;

        /// <summary>
        /// Minimum Y-position change (in world units) to count as a floor transition.
        /// Typical floor height in the game is ~4 units.
        /// </summary>
        private const float FloorChangeThreshold = 2.0f;

        /// <summary>
        /// Minimum seconds between floor change announcements to avoid rapid-fire
        /// triggers while on long staircases or ramps.
        /// </summary>
        private const float FloorChangeCooldown = 1.5f;

        #endregion

        #region State

        private readonly List<NavItem>[] _categories;
        private bool _isOpen;
        private int  _currentCategoryIndex;
        private int  _currentItemIndex;

        private bool      _isAutoWalking;
        private float     _autoWalkSpeed;   // queried for world map movement via GetMoveSpeed(true)
        private Vector3   _autoWalkTarget;
        private string    _autoWalkLabel;

        /// <summary>Last auto-walk target position (for diagnostics).</summary>
        public static Vector3? LastAutoWalkTarget { get; private set; }

        /// <summary>Last auto-walk target label (for diagnostics).</summary>
        public static string LastAutoWalkLabel { get; private set; }
        /// <summary>
        /// Live transform of the current auto-walk target.
        /// Null for exits and when the target has no live reference.
        /// Updated each frame so the player follows a wandering NPC.
        /// </summary>
        private Transform _autoWalkTransform;
        /// <summary>
        /// True once the player has reached the target and "Arrived" has been announced.
        /// In proximity-lock mode the player stays glued to the NPC until the walk is cancelled.
        /// </summary>
        private bool _autoWalkArrived;
        /// <summary>
        /// True once the proximity-lock arrival message has been spoken for this walk.
        /// A wandering NPC drifts in and out of the arrival radius while being
        /// followed — the full "Arrived ..." message must not replay on every
        /// re-catch (spoke 6 times in 4s at the Cunning Fighter, 2026-08-30).
        /// </summary>
        private bool _autoWalkArrivalAnnounced;
        /// <summary>
        /// Path-exhaustion recalcs used on the current walk. A live-transform target
        /// (NPC) that moved since the path was computed gets the path recomputed to
        /// its current position instead of a false "Could not reach"; capped so a
        /// genuinely unreachable NPC still gets an honest refusal.
        /// </summary>
        private int _pathExhaustRecalcs;
        /// <summary>Maximum path-exhaustion recalcs per walk for moving targets.</summary>
        private const int MaxPathExhaustRecalcs = 2;
        /// <summary>
        /// True when auto-walking to a counter NPC (shop, inn, guild) via a partial path.
        /// Arrival is detected at the last waypoint rather than proximity to the NPC.
        /// </summary>
        private bool _autoWalkIsCounter;

        /// <summary>
        /// Reference to the FieldEventCollision when auto-walking to an event.
        /// Used to call StartEvent() directly when the NavMesh path ends short
        /// of the trigger zone (transform.position bypasses Unity physics).
        /// </summary>
        private FieldEventCollision _autoWalkEventRef;

        /// <summary>
        /// Collider bounds of the event trigger zone. Used to verify the player
        /// is near the trigger edge before calling StartEvent().
        /// </summary>
        private Bounds? _autoWalkTriggerBounds;
        /// <summary>
        /// Optional position to face on arrival (e.g. water center for fishing spots).
        /// </summary>
        private Vector3? _autoWalkFacePosition;

        /// <summary>
        /// True when auto-walking to a target on a different floor (significant Y difference).
        /// When the partial path ends, the player is told the target is above or below them
        /// instead of falsely announcing arrival.
        /// </summary>
        private bool _autoWalkDifferentFloor;

        /// <summary>
        /// Category index of the current auto-walk target.
        /// Used to add compass direction hints for exit-type targets on arrival.
        /// </summary>
        private int _autoWalkCategoryIndex;

        /// <summary>Reusable NavMeshPath object — allocated once, reused for every path calculation.</summary>
        private NavMeshPath _navPath;

        /// <summary>Waypoint positions from the last NavMesh path calculation.</summary>
        private Vector3[] _pathCorners;

        /// <summary>Index of the current waypoint being walked toward in _pathCorners.</summary>
        private int _pathCornerIndex;

        /// <summary>Timer for periodic path recalculation when following moving NPCs.</summary>
        private float _pathRecalcTimer;

        /// <summary>Timer for field map stuck detection during auto-walk.</summary>
        private float _fieldStuckTimer;

        /// <summary>Player position at the last field stuck check, for distance comparison.</summary>
        private Vector3 _fieldLastStuckCheckPos;

        /// <summary>True if a recalculation was already attempted after getting stuck. Prevents infinite recalc loops.</summary>
        private bool _fieldStuckRecalcAttempted;

        /// <summary>True while the player is walking toward a detour point to avoid an obstacle.</summary>
        private bool _isAvoidingObstacle;

        /// <summary>World-space position of the detour point the player walks to during obstacle avoidance.</summary>
        private Vector3 _avoidanceDetourTarget;

        /// <summary>Time.time when obstacle avoidance started, for timeout.</summary>
        private float _avoidanceStartTime;

        /// <summary>Increments each avoidance attempt, alternating left/right detour direction.</summary>
        private int _avoidanceAttempt;

        /// <summary>
        /// True when the current auto-walk target is itself a map exit, so the
        /// hard exit barrier is allowed (the player WANTS to walk through it).
        /// </summary>
        private bool _autoWalkAllowExit;
        /// <summary>
        /// Set true when the last path was rejected because it would carry the
        /// player through a map exit. Lets callers announce a clear message.
        /// </summary>
        private bool _lastPathBlockedByExit;
        /// <summary>Margin (m) added to map-exit collider bounds for the hard barrier.</summary>
        private const float MapExitBarrierMargin = 0.5f;

        /// <summary>
        /// True while the gamepad nav overlay (held L2 modifier) is active. Static so
        /// Harmony prefixes can read it to suppress game input (D-pad, L2 actions).
        /// </summary>
        private static bool _gamepadNavActive;

        // Map name announcement: track current fieldmap to detect area changes.
        private FieldmapID _lastFieldmapID = FieldmapID.INVALID;
        private bool _fieldmapInitialized;

        // Floor change detection: track player Y to announce stair transitions.
        private float _lastPlayerY = float.NaN;
        private float _floorChangeCooldownTimer;

        // Soft spatial-awareness assist: nudges the auto-walk heading around
        // NPCs/clutter the NavMesh doesn't know about, without leaving the route.
        // See SpatialSensor.cs. Foundation for a future exploration mode.
        private readonly SpatialSensor _spatialSensor = new SpatialSensor();

        // NPC-aware field pathfinding: parks invisible carving NavMeshObstacles on
        // nearby standing NPCs so NavMesh.CalculatePath routes AROUND them (the game's
        // own A* does the work — no custom steering). See NavMeshCarverPool.cs.
        // CarveOnlyStationary=false → immediate carve, matching the proven F7 POC.
        private readonly NavMeshCarverPool _carverPool =
            new NavMeshCarverPool(CarverCap) { CarveOnlyStationary = false };
        private float _carveRefreshTimer;
        // One-shot timestamp: recompute the path once after carvers have had time to
        // cut the mesh (the initial path is computed before they exist). 0 = disarmed.
        private float _carveForceRecalcAt;
        // Periodic carve-aware recalc so the path keeps bending around the crowd the
        // player is approaching (the forced recalc only saw the start-area crowd).
        private float _carvePeriodicTimer;
        // Bounded carve recalcs on hard-wedge before falling back to the physical detour.
        private int _carveWedgeRecalcs;

        // Carve-oscillation (livelock) state — see HandleCarveLivelock / TrackPathStability.
        private Vector3 _lastPathFirstLegDir;     // first-leg heading of the currently stored path
        private bool    _pathFirstLegValid;       // false until a usable first leg exists / after a detour
        private int     _pathReversalCount;       // alternating first-leg reversals in the current window
        private bool    _livelockAnchored;        // a reversal window is open
        private float   _walkBestApproach;        // smallest XZ dist-to-target this walk; improvement = real progress
        private bool    _carveSuppressedForBlock; // committed to the un-carved (pre-carving) route for this walk
        private float   _blockCommitDeadline;     // Time.time by which the block is confirmed or we give up

        private const int CarverCap = 12;          // max NPCs carved at once
        private const float CarverRefreshInterval = 0.25f; // reposition cadence (s)
        private const float CarveSettleDelay = 0.4f;       // wait before initial carved recalc (s)
        private const float CarvePeriodicInterval = 1.0f;  // re-route cadence while carving (s)
        private const int MaxCarveWedgeRecalcs = 3;        // carve recalcs per walk before detour
        private const float CarveBand = 7f;        // only carve NPCs within this radius of the player (m)

        // Carve-oscillation (livelock) detection. When an auto-walk target sits behind a
        // line of blocking NPCs (e.g. an event behind a row of soldiers), the near-only
        // carvers toggle as the player moves, so the planner flip-flops the route between
        // "straight at the goal" and a big "loop around the room" — the player paces back
        // and forth forever. A baked wall never toggles, so a legitimate long detour keeps
        // ONE stable first-leg heading and the player nets genuinely closer over time. The
        // oscillation is the only case that ALTERNATES the stored path's first-leg direction
        // while best-ever approach to the target never improves. See HandleCarveLivelock.
        private const float PathReversalDot = -0.25f;   // first-leg dot below this = a reversal (~>104 deg)
        private const int   LivelockMinReversals = 3;   // alternating reversals to confirm oscillation
        private const float LivelockApproachEps = 1.5f; // best-approach must improve by this to count as progress (m)
        private const float BlockCommitTimeout = 3f;    // s to confirm the block after committing to the direct route
        private const float BlockProbeRange = 1.8f;     // m: blocking-NPC search radius for the give-up message
        private const float BlockAheadCos = 0.5f;       // forward cone (cos ~ 60 deg) for the give-up message

        // Observed-traversal map: records where the player actually walks and
        // routes over those breadcrumbs (100% reliable). See TraversalGraph.cs.
        private TraversalGraph _traversal = new TraversalGraph();
        /// <summary>Timer for periodic traversal autosave.</summary>
        private float _traversalSaveTimer;

        /// <summary>
        /// True when recorded traversals can drive reachability/pathfinding for
        /// the current target (field/dungeon map with breadcrumb data).
        /// </summary>
        private bool UseTraversal() =>
            !_isWorldmap && _traversal != null && _traversal.HasData;

        /// <summary>Whether the navigation list is currently open.</summary>
        public bool IsListOpen => _isOpen;

        /// <summary>Whether the player is currently being auto-walked to a target.</summary>
        public bool IsAutoWalking => _isAutoWalking;

        #endregion

        #region Constructor

        public NavigationHandler()
        {
            _categories = new List<NavItem>[CAT_COUNT];
            for (int i = 0; i < CAT_COUNT; i++)
                _categories[i] = new List<NavItem>();

            _navPath = new NavMeshPath();
        }

        #endregion

        #region Patch Application

        /// <summary>
        /// Applies Harmony patches:
        /// - GetLeftStick postfix (injects synthetic stick input during auto-walk)
        /// - GetFieldCameraRightStick postfix (rotates camera to follow walk direction)
        /// - GameInputManager.IsDown prefix (suppresses D-pad/L2 actions when gamepad nav active)
        /// - GameInputManager.IsRepeat prefix (suppresses D-pad repeat)
        /// - GameInputManager.GetDPad prefix (suppresses D-pad analog)
        /// </summary>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                harmony.Patch(
                    AccessTools.Method(typeof(GameInputManager), "GetLeftStick"),
                    postfix: new HarmonyMethod(typeof(NavigationHandler),
                        nameof(GetLeftStick_Postfix)));
                DebugLogger.LogState("NavigationHandler: GetLeftStick postfix applied.");
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error(
                    $"NavigationHandler.ApplyPatches failed (GetLeftStick): {ex.Message}");
            }

            try
            {
                harmony.Patch(
                    AccessTools.Method(typeof(GameInputManager), "GetFieldCameraRightStick"),
                    postfix: new HarmonyMethod(typeof(NavigationHandler),
                        nameof(GetFieldCameraRightStick_Postfix)));
                DebugLogger.LogState("NavigationHandler: GetFieldCameraRightStick postfix applied.");
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error(
                    $"NavigationHandler.ApplyPatches failed (GetFieldCameraRightStick): {ex.Message}");
            }

            // GetPlayerControlStick — world map movement reads this instead of GetLeftStick.
            // CallerCount(0) but Harmony patches still intercept native calls.
            try
            {
                harmony.Patch(
                    AccessTools.Method(typeof(GameInputManager), "GetPlayerControlStick"),
                    postfix: new HarmonyMethod(typeof(NavigationHandler),
                        nameof(GetPlayerControlStick_Postfix)));
                DebugLogger.LogState("NavigationHandler: GetPlayerControlStick postfix applied.");
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error(
                    $"NavigationHandler.ApplyPatches failed (GetPlayerControlStick): {ex.Message}");
            }

            // Gamepad input suppression patches
            try
            {
                harmony.Patch(
                    AccessTools.Method(typeof(GameInputManager), "IsDown",
                        new Type[] { typeof(GameInputManager.InputAction) }),
                    prefix: new HarmonyMethod(typeof(NavigationHandler),
                        nameof(IsDown_Prefix)));
                DebugLogger.LogState("NavigationHandler: IsDown prefix applied.");
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error(
                    $"NavigationHandler.ApplyPatches failed (IsDown): {ex.Message}");
            }

            try
            {
                harmony.Patch(
                    AccessTools.Method(typeof(GameInputManager), "IsRepeat",
                        new Type[] { typeof(GameInputManager.InputAction) }),
                    prefix: new HarmonyMethod(typeof(NavigationHandler),
                        nameof(IsRepeat_Prefix)));
                DebugLogger.LogState("NavigationHandler: IsRepeat prefix applied.");
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error(
                    $"NavigationHandler.ApplyPatches failed (IsRepeat): {ex.Message}");
            }

            try
            {
                harmony.Patch(
                    AccessTools.Method(typeof(GameInputManager), "GetDPad"),
                    prefix: new HarmonyMethod(typeof(NavigationHandler),
                        nameof(GetDPad_Prefix)));
                DebugLogger.LogState("NavigationHandler: GameInputManager.GetDPad prefix applied.");
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error(
                    $"NavigationHandler.ApplyPatches failed (GetDPad): {ex.Message}");
            }

            // NOTE: InputManager.IsDown(InputKey) hooks were removed — CallerCount(0)
            // means native IL2CPP code bypasses the managed wrapper, so Harmony prefixes
            // never fire. Field shortcuts are now blocked via GameInputManager ShortCut actions.

            // Fishing result announcement — CallerCount(1), fires when result screen populates.
            try
            {
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(
                    typeof(UIFieldFishingResultPresenter).TypeHandle);
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(
                    typeof(UIFieldFishingResultListItemData).TypeHandle);

                harmony.Patch(
                    AccessTools.Method(typeof(UIFieldFishingResultPresenter),
                        nameof(UIFieldFishingResultPresenter.Set)),
                    postfix: new HarmonyMethod(typeof(NavigationHandler),
                        nameof(FishingResultSet_Postfix)));
                DebugLogger.LogState("NavigationHandler: UIFieldFishingResultPresenter.Set postfix applied.");
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error(
                    $"NavigationHandler.ApplyPatches failed (FishingResult): {ex.Message}");
            }
        }

        #endregion

        #region Update

        /// <summary>
        /// Per-frame update — moves the player along NavMesh waypoints toward the target.
        /// Follows the path calculated by AutoWalkTo(), advancing to the next waypoint
        /// when within <see cref="WaypointArrivalThreshold"/>. For moving NPC targets,
        /// periodically recalculates the path when the NPC moves significantly.
        /// Announces arrival when within AutoWalkArrivalRadius of the final target.
        /// Must be called from Main.OnUpdate() every frame.
        /// </summary>
        public void Update()
        {
            CheckFieldmapChange();
            CheckFloorChange();
            CheckTraversalRecording();

            // Resume auto-walk after battle on world map.
            if (_wmResumeActive && !_isAutoWalking && IsFieldFree())
            {
                try
                {
                    var fm = FieldManager.Instance;
                    if (fm != null && fm.IsWorldmap())
                    {
                        _wmResumeActive = false;
                        DebugLogger.LogState(
                            $"NAV worldmap: resuming auto-walk to '{_wmResumeLabel}'.");

                        // Restore auto-walk state and recompute path.
                        _autoWalkTarget = _wmResumeTarget;
                        _autoWalkLabel = _wmResumeLabel;
                        _autoWalkCategoryIndex = _wmResumeCategoryIndex;
                        _autoWalkTransform = _wmResumeTransform;
                        _autoWalkTriggerBounds = _wmResumeTriggerBounds;
                        _autoWalkFacePosition = _wmResumeFacePosition;
                        _isWorldmap = true;

                        // Update target from live transform if available.
                        if (_autoWalkTransform != null)
                            _autoWalkTarget = _autoWalkTransform.position;

                        // Resume goal: locations re-plan to the stored ring
                        // point (the entrance), NOT the town-centre symbol —
                        // a centre-aimed resume always collapses to a
                        // wall-hugging floor route (2026-07-10 diagnosis).
                        Vector3 resumeGoal =
                            _autoWalkCategoryIndex == CAT_LOCATION
                                ? _wmPathGoal : _autoWalkTarget;

                        var player = fm.GetControlPlayer();
                        if (player != null)
                        {
                            Vector3 playerPos = player.transform.position;
                            bool resumePathFound = WorldmapCalculateAndStorePath(
                                playerPos, resumeGoal,
                                keepBlockedPositions: true);

                            if (resumePathFound)
                            {
                                _isAutoWalking = true;
                                _staticIsAutoWalking = true;
                                _wmStuckTimer = 0f;
                                _wmLastStuckCheckPos = playerPos;
                                _wmDiagTimer = 0f;
                                // Don't reset _wmRecalcCount or _wmBlockedPositions
                                // so we keep memory of previously stuck areas.
                                ScreenReader.Say(
                                    Loc.Get("nav_autowalk_resuming", _autoWalkLabel));
                                DebugLogger.LogState(
                                    $"NAV auto-walk resumed. target={_autoWalkLabel} " +
                                    $"waypoints={_wmPathWaypoints?.Length ?? 0}");
                            }
                            else
                            {
                                // Post-battle position has no route to the target
                                // (e.g. pushed into a sealed pocket). Announce
                                // instead of walking blind.
                                ScreenReader.Say(Loc.Get(
                                    "nav_autowalk_unreachable", _autoWalkLabel));
                                DebugLogger.LogState(
                                    $"NAV resume: no path to '{_autoWalkLabel}' " +
                                    "after battle — resume abandoned.");
                            }
                        }
                        else
                        {
                            _wmResumeActive = false;
                        }
                    }
                    else
                    {
                        // No longer on world map — clear resume.
                        _wmResumeActive = false;
                    }
                }
                catch (Exception ex)
                {
                    _wmResumeActive = false;
                    DebugLogger.LogState($"NAV resume error: {ex.Message}");
                }
            }

            // Resume auto-walk after a battle on a field map. Mirrors the world
            // map resume above, but only fires for battle interruptions — dialogue,
            // cutscenes, and menus are discarded (see UpdateFieldResume).
            if (_fieldResumePending && !_isAutoWalking)
                UpdateFieldResume();

            if (!_isAutoWalking) return;

            // Cancel auto-walk if a dialogue, event, notification, or menu appeared.
            // On the world map, EventManager.IsRunning flickers true briefly at
            // terrain zone transitions. Tolerate up to 10 consecutive frames (~0.17s)
            // of IsFieldFree failure before actually cancelling.
            if (!IsFieldFree())
            {
                if (_isWorldmap)
                {
                    _wmFieldFreeFailCount++;
                    if (_wmFieldFreeFailCount > 10)
                    {
                        // Save resume info before cancelling — battle will
                        // return to the same world map, so we can auto-resume.
                        _wmResumeActive = true;
                        _wmResumeTarget = _autoWalkTarget;
                        _wmResumeLabel = _autoWalkLabel;
                        _wmResumeCategoryIndex = _autoWalkCategoryIndex;
                        _wmResumeTransform = _autoWalkTransform;
                        // Fishing identity too — CancelAutoWalk clears both,
                        // and without them the resumed walk skips the
                        // bubble-confirmed arrival (false "Arrived").
                        _wmResumeTriggerBounds = _autoWalkTriggerBounds;
                        _wmResumeFacePosition = _autoWalkFacePosition;
                        // Keep blocked positions across battles.
                        DebugLogger.LogState(
                            $"NAV worldmap: battle interrupt, saving resume for '{_autoWalkLabel}'.");
                        CancelAutoWalk();
                        return;
                    }
                    // Brief interruption — skip this frame but don't cancel.
                    _staticAutoWalkStickDir = Vector2.zero;
                    return;
                }
                // Field map: tolerate brief field-free flicker before treating it
                // as a real interruption. Event transitions blip non-free, and the
                // post-battle return to the field settles over several frames — a
                // 1-frame blip right after a resume must not re-cancel the walk.
                _fieldFreeFailCount++;
                if (_fieldFreeFailCount > 10)
                {
                    // Save a potential resume (only fires if a battle caused the
                    // interruption — see UpdateFieldResume), then cancel.
                    SaveFieldResume();
                    CancelAutoWalk();
                    return;
                }
                // Brief interruption — skip this frame but don't cancel.
                _staticAutoWalkStickDir = Vector2.zero;
                return;
            }
            _wmFieldFreeFailCount = 0;
            _fieldFreeFailCount = 0;

            try
            {
                var fm = FieldManager.Instance;
                if (fm == null) { CancelAutoWalk(); return; }

                var player = fm.GetControlPlayer();
                if (player == null) { CancelAutoWalk(); return; }

                Vector3 playerPos = player.transform.position;

                // World map: entirely separate auto-walk logic in Worldmap.cs.
                if (_isWorldmap)
                {
                    UpdateWorldmapAutoWalk(player, playerPos);
                    return;
                }

                // If the target has a live transform (NPC, chest, marker), update
                // the target position every frame so wandering NPCs are tracked.
                if (_autoWalkTransform != null)
                    _autoWalkTarget = _autoWalkTransform.position;

                // Keep carving markers parked on the nearby crowd so the NavMesh
                // (and therefore every CalculateAndStorePath below) routes around them.
                UpdateFieldCarvers(playerPos);

                // Initial carved recalc: the path was computed before the carvers cut
                // the mesh, so recompute once they've settled. Disarmed after firing.
                if (_carveForceRecalcAt > 0f && Time.time >= _carveForceRecalcAt)
                {
                    _carveForceRecalcAt = 0f;
                    if (_carverPool.ActiveCount > 0 && !_isAvoidingObstacle
                        && !_carveSuppressedForBlock
                        && CalculateAndStorePath(playerPos, _autoWalkTarget,
                                allowPartial: true, isCounter: _autoWalkIsCounter))
                    {
                        DebugLogger.LogState(
                            $"NAV carve: re-routed around {_carverPool.ActiveCount} NPCs "
                            + $"({_pathCorners.Length} waypoints).");
                    }
                }

                // Periodic carve-aware recalc: re-bend the path around the crowd the
                // player is NOW near (the one-shot recalc above only saw the start crowd,
                // and the moving-NPC recalc below never fires for a stationary target).
                // Keeps the route around oncoming spectators BEFORE the player wedges.
                if (_carverPool.ActiveCount > 0 && ModSettings.NpcAwarePathfindingEnabled
                    && !_isAvoidingObstacle && _carveForceRecalcAt == 0f
                    && !_carveSuppressedForBlock)
                {
                    _carvePeriodicTimer += Time.deltaTime;
                    if (_carvePeriodicTimer >= CarvePeriodicInterval)
                    {
                        _carvePeriodicTimer = 0f;
                        if (CalculateAndStorePath(playerPos, _autoWalkTarget,
                                allowPartial: true, isCounter: _autoWalkIsCounter))
                        {
                            DebugLogger.LogState(
                                $"NAV carve: periodic re-route around {_carverPool.ActiveCount} NPCs "
                                + $"({_pathCorners.Length} wp).");
                        }
                    }
                }

                // --- Check arrival at the final target (not waypoint) ---
                // Use XZ distance for same-floor targets. For different-floor targets,
                // skip the proximity arrival check entirely — arrival is handled at
                // path end (when the partial path runs out) to avoid false positives
                // when the player is directly above/below the target.
                // Re-evaluate floor difference each frame: if the player has since
                // moved to the same floor (e.g. walked upstairs), re-enable proximity
                // arrival so NPCs on the now-same floor can be reached normally.
                // Distance guard: only re-enable when the player is far enough from
                // the target in XZ that the proximity arrival check won't fire
                // immediately. Without this, climbing stairs near a target causes
                // premature arrival (Y crosses threshold while XZ is already close).
                if (_autoWalkDifferentFloor &&
                    Mathf.Abs(_autoWalkTarget.y - playerPos.y) <= FloorChangeThreshold)
                {
                    float guardDx = _autoWalkTarget.x - playerPos.x;
                    float guardDz = _autoWalkTarget.z - playerPos.z;
                    float guardDist = Mathf.Sqrt(guardDx * guardDx + guardDz * guardDz);
                    if (guardDist > AutoWalkArrivalRadius * 2f)
                    {
                        _autoWalkDifferentFloor = false;
                        DebugLogger.LogState(
                            $"NAV auto-walk: player now on same floor as target " +
                            $"(playerY={playerPos.y:F1}, targetY={_autoWalkTarget.y:F1}). " +
                            "Re-enabling proximity arrival.");
                    }
                }

                float targetDx   = _autoWalkTarget.x - playerPos.x;
                float targetDz   = _autoWalkTarget.z - playerPos.z;
                float targetDist = _autoWalkDifferentFloor
                    ? float.MaxValue   // never trigger proximity arrival for different floors
                    : Mathf.Sqrt(targetDx * targetDx + targetDz * targetDz);

                // Direction toward the actual target (for facing and proximity-lock).
                Vector3 targetDir = targetDist > 0.01f
                    ? new Vector3(targetDx / targetDist, 0f, targetDz / targetDist)
                    : Vector3.forward;

                // Use a tighter arrival radius for interactable objects (chests, save points)
                // that require the player to be very close to trigger interaction.
                bool isInteractable = _autoWalkCategoryIndex == CAT_CHEST
                    || _autoWalkCategoryIndex == CAT_SAVE
                    || _autoWalkCategoryIndex == CAT_INTERACTABLE;
                float arrivalRadius = isInteractable
                    ? InteractableArrivalRadius
                    : (_autoWalkCategoryIndex == CAT_NPC
                        ? NpcApproachRadius : AutoWalkArrivalRadius);
                // Announce arrival ONLY when genuinely at the real target — never
                // while standing on a different floor than the target.
                bool atTarget = targetDist <= arrivalRadius
                    && Mathf.Abs(_autoWalkTarget.y - playerPos.y) <= ArrivalVerticalTolerance;
                if (atTarget)
                {
                    // Face toward FacePosition if set (e.g. water center), else target.
                    Vector3 faceDir = targetDir;
                    if (_autoWalkFacePosition.HasValue)
                    {
                        Vector3 toFace = _autoWalkFacePosition.Value - playerPos;
                        toFace.y = 0f;
                        if (toFace.sqrMagnitude > 0.01f)
                            faceDir = toFace.normalized;
                    }
                    player.transform.rotation = Quaternion.LookRotation(faceDir, Vector3.up);

                    // Only NPCs use proximity-lock (they wander, so the player
                    // follows until manually cancelled). All other targets with
                    // a LiveTransform (chests, save points, markers) fully stop
                    // on arrival — same as static exits.
                    bool useProximityLock = _autoWalkTransform != null
                        && _autoWalkCategoryIndex == CAT_NPC;

                    if (!useProximityLock)
                    {
                        // Non-NPC target — fully stop.
                        StopAutoWalk();

                        // --- Diagnostic dump on non-NPC arrival ---
                        LogNpcArrivalDiagnostics(player, playerPos, targetDist);

                        // For exit-type targets, add compass direction so the player
                        // knows which way to walk to pass through the exit.
                        // Field exits now trigger naturally via Unity colliders
                        // (input injection uses the game's own movement pipeline).
                        string arrivalMsg;
                        if (IsExitCategory(_autoWalkCategoryIndex))
                        {
                            string compass = GetCompassDirection(playerPos, _autoWalkTarget);
                            arrivalMsg = Loc.Get("nav_autowalk_arrived_exit",
                                _autoWalkLabel, compass);
                        }
                        else
                        {
                            arrivalMsg = Loc.Get("nav_autowalk_arrived", _autoWalkLabel);
                        }

                        AnnounceArrival(arrivalMsg);
                        DebugLogger.LogState($"NAV auto-walk arrived at '{_autoWalkLabel}'.");
                        return;
                    }

                    // NPC — proximity-lock mode: stop injecting input and let
                    // the player stand where the game's movement left them.
                    // Face the NPC so interaction button works immediately.
                    if (!_autoWalkArrived)
                    {
                        _autoWalkArrived     = true;
                        _staticIsAutoWalking = false;
                        _staticAutoWalkStickDir = Vector2.zero;

                        // Announce (and dump diagnostics) only on the FIRST catch of
                        // this walk. A wandering NPC re-enters the radius every few
                        // steps while being followed — replaying the full message
                        // each time is pure noise (6× in 4s, 2026-08-30).
                        if (!_autoWalkArrivalAnnounced)
                        {
                            _autoWalkArrivalAnnounced = true;
                            AnnounceArrival(Loc.Get("nav_autowalk_arrived_npc", _autoWalkLabel));
                            DebugLogger.LogState($"NAV auto-walk proximity lock '{_autoWalkLabel}'.");

                            // --- Diagnostic dump on NPC arrival ---
                            LogNpcArrivalDiagnostics(player, playerPos, targetDist);
                        }
                        else
                        {
                            DebugLogger.LogState(
                                $"NAV auto-walk proximity re-lock '{_autoWalkLabel}' (silent).");
                        }

                        // Face the NPC.
                        player.transform.rotation = Quaternion.LookRotation(targetDir, Vector3.up);
                    }

                    // Stay stopped — don't inject any input while in proximity-lock.
                    // If the NPC wanders away, the approach phase re-activates below.
                    return;
                }

                // --- Approach phase ---
                _autoWalkArrived     = false;
                _staticIsAutoWalking = true;

                // --- Field map: waypoint-based approach ---

                // Safety check: if path data is missing, cancel.
                if (_pathCorners == null || _pathCorners.Length == 0)
                {
                    DebugLogger.LogState("NAV auto-walk: no path corners, cancelling.");
                    CancelAutoWalk();
                    return;
                }

                // --- Obstacle avoidance: walking toward detour point ---
                if (_isAvoidingObstacle)
                {
                    float detourDx = _avoidanceDetourTarget.x - playerPos.x;
                    float detourDz = _avoidanceDetourTarget.z - playerPos.z;
                    float detourDist = Mathf.Sqrt(detourDx * detourDx + detourDz * detourDz);
                    bool timedOut = Time.time - _avoidanceStartTime > ObstacleAvoidanceTimeout;

                    if (detourDist < DetourArrivalRadius || timedOut)
                    {
                        // Reached detour or timed out — recalculate path from here.
                        _isAvoidingObstacle = false;
                        _fieldLastStuckCheckPos = playerPos;
                        _fieldStuckTimer = 0f;
                        _fieldStuckRecalcAttempted = false;

                        bool allowPartial = _autoWalkIsCounter || _autoWalkDifferentFloor;
                        if (CalculateAndStorePath(playerPos, _autoWalkTarget, allowPartial,
                                isCounter: _autoWalkIsCounter))
                        {
                            DebugLogger.LogState(
                                $"NAV obstacle avoidance {(timedOut ? "timed out" : "complete")}, " +
                                $"resumed with {_pathCorners.Length} waypoints.");
                        }
                        else
                        {
                            DebugLogger.LogState("NAV obstacle avoidance: recalc failed after detour.");
                            AnnounceBlockedGiveUp(playerPos);
                            CancelAutoWalk();
                            return;
                        }
                    }
                    else
                    {
                        // Walk toward the detour point.
                        Vector3 detourDir = new Vector3(
                            detourDx / detourDist, 0f, detourDz / detourDist);
                        _staticAutoWalkStickDir = WorldDirToCameraStick(detourDir);
                        UpdateCameraFollow(detourDir);
                        return;
                    }
                }

                // --- Carve-oscillation (livelock) detection ---
                // When the goal is walled in by near-only-carved NPCs the route flip-flops
                // and the player paces forever. Confirm that, then suppress carving and
                // commit to the direct route so the player is told the way is blocked.
                // (While committed, HandleCarveLivelock above owns the wedge/timeout give-up
                // and the carve recalc paths below are frozen via _carveSuppressedForBlock.)
                if (HandleCarveLivelock(playerPos)) return;

                // --- Sensor-driven fast escalation ---
                // If the adaptive walk-assist sidestep has provably failed (a body is
                // wedged ahead and the widened cap still made no progress), skip the
                // NavMesh recalc — it is blind to NPCs and would just return the same
                // route through the blocker — and go straight to a physical detour.
                if (ModSettings.WalkAssistEnabled && _spatialSensor.IsHardWedged
                    && !_isAvoidingObstacle && !_carveSuppressedForBlock)
                {
                    // With NPC-aware carving the recalc is NO LONGER blind — the blocker
                    // is a hole in the NavMesh, so a recalc CAN route around it. Try that
                    // first (smooth) and only fall back to the physical detour if carving
                    // is off/unavailable or we've already retried it a few times.
                    bool carveActive = ModSettings.NpcAwarePathfindingEnabled
                        && _carverPool.ActiveCount > 0;
                    if (carveActive && _carveWedgeRecalcs < MaxCarveWedgeRecalcs
                        && CalculateAndStorePath(playerPos, _autoWalkTarget,
                               allowPartial: true, isCounter: _autoWalkIsCounter))
                    {
                        _carveWedgeRecalcs++;
                        _spatialSensor.Reset();          // give the new route a chance
                        _fieldStuckTimer = 0f;
                        _fieldLastStuckCheckPos = playerPos;
                        _fieldStuckRecalcAttempted = false;
                        DebugLogger.LogState(
                            $"NAV carve: hard wedge — re-routed around {_carverPool.ActiveCount} NPCs "
                            + $"({_pathCorners.Length} wp), carve-recalc {_carveWedgeRecalcs}.");
                    }
                    else if (_avoidanceAttempt >= MaxAvoidanceAttempts)
                    {
                        DebugLogger.LogState(
                            "NAV walk-assist: hard wedge, max avoidance attempts reached. Cancelling.");
                        AnnounceBlockedGiveUp(playerPos);
                        CancelAutoWalk();
                        return;
                    }
                    else if (TryStartObstacleAvoidance(playerPos))
                    {
                        DebugLogger.LogState(
                            "NAV walk-assist: hard wedge — escalated to physical detour " +
                            "(carve recalc exhausted/off).");
                        _spatialSensor.Reset(); // clear wedge so it doesn't re-fire
                    }
                }

                // --- Field map stuck detection ---
                _fieldStuckTimer += Time.deltaTime;
                if (_fieldStuckTimer >= FieldStuckCheckInterval)
                {
                    float movedDx = playerPos.x - _fieldLastStuckCheckPos.x;
                    float movedDz = playerPos.z - _fieldLastStuckCheckPos.z;
                    float movedSq = movedDx * movedDx + movedDz * movedDz;

                    if (movedSq < FieldStuckMinMove * FieldStuckMinMove)
                    {
                        if (!_fieldStuckRecalcAttempted)
                        {
                            // First stuck detection: try recalculating from current position.
                            _fieldStuckRecalcAttempted = true;
                            DebugLogger.LogState(
                                $"NAV field stuck: moved {Mathf.Sqrt(movedSq):F2} in " +
                                $"{FieldStuckCheckInterval}s. Attempting recalc.");

                            bool allowPartial = _autoWalkIsCounter || _autoWalkDifferentFloor;
                            if (CalculateAndStorePath(playerPos, _autoWalkTarget, allowPartial,
                                    isCounter: _autoWalkIsCounter))
                            {
                                DebugLogger.LogState(
                                    $"NAV field stuck recalc OK: {_pathCorners.Length} waypoints.");
                            }
                            else
                            {
                                DebugLogger.LogState("NAV field stuck recalc failed. Cancelling.");
                                AnnounceBlockedGiveUp(playerPos);
                                CancelAutoWalk();
                                return;
                            }
                        }
                        else
                        {
                            // Committed to the un-carved route after a confirmed carve
                            // oscillation — the blockers are people, so give up here
                            // instead of detouring around them.
                            if (_carveSuppressedForBlock)
                            {
                                AnnounceBlockedGiveUp(playerPos);
                                CancelAutoWalk();
                                return;
                            }

                            // Recalc didn't help — try obstacle avoidance detour.
                            // Give up after MaxAvoidanceAttempts to prevent infinite
                            // stuck loops (e.g. guards blocking the path).
                            if (_avoidanceAttempt >= MaxAvoidanceAttempts)
                            {
                                DebugLogger.LogState(
                                    $"NAV field stuck: max avoidance attempts " +
                                    $"({MaxAvoidanceAttempts}) reached. Cancelling.");
                                AnnounceBlockedGiveUp(playerPos);
                                CancelAutoWalk();
                                return;
                            }

                            if (TryStartObstacleAvoidance(playerPos))
                            {
                                DebugLogger.LogState(
                                    $"NAV field stuck after recalc — starting obstacle avoidance " +
                                    $"(attempt {_avoidanceAttempt}).");
                            }
                            else
                            {
                                // No walkable detour found — give up.
                                DebugLogger.LogState(
                                    $"NAV field stuck after recalc, no detour available. Cancelling.");
                                AnnounceBlockedGiveUp(playerPos);
                                CancelAutoWalk();
                                return;
                            }
                        }
                    }
                    else
                    {
                        // Making progress — reset the recalc flag.
                        // Do NOT reset _avoidanceAttempt here: a detour that moves
                        // the player slightly counts as "progress" but doesn't mean
                        // the path is unblocked. The counter resets on new auto-walks.
                        _fieldStuckRecalcAttempted = false;
                    }

                    _fieldLastStuckCheckPos = playerPos;
                    _fieldStuckTimer = 0f;
                }

                // --- Recalculate path for moving NPCs ---
                if (_autoWalkTransform != null)
                {
                    _pathRecalcTimer += Time.deltaTime;
                    if (_pathRecalcTimer >= PathRecalcInterval)
                    {
                        _pathRecalcTimer = 0f;
                        // Check if the NPC has moved significantly from the path's last corner.
                        Vector3 pathEnd = _pathCorners[_pathCorners.Length - 1];
                        float endDx = _autoWalkTarget.x - pathEnd.x;
                        float endDz = _autoWalkTarget.z - pathEnd.z;
                        float endDist = Mathf.Sqrt(endDx * endDx + endDz * endDz);

                        if (endDist > PathRecalcDistanceThreshold)
                        {
                            if (CalculateAndStorePath(playerPos, _autoWalkTarget,
                                    isCounter: _autoWalkIsCounter))
                            {
                                DebugLogger.LogState(
                                    $"NAV path recalculated: {_pathCorners.Length} waypoints " +
                                    $"(NPC moved {endDist:F1} units)");
                            }
                            else
                            {
                                ScreenReader.Say(Loc.Get("nav_autowalk_lost_path", _autoWalkLabel));
                                CancelAutoWalk();
                                return;
                            }
                        }
                    }
                }

                // --- Follow the current waypoint ---
                // If the NavMesh path was fully traversed but the proximity
                // arrival check above didn't trigger, the target is beyond
                // the NavMesh edge.
                if (_pathCornerIndex >= _pathCorners.Length)
                {
                    // Live-transform rescue: the target may simply have MOVED since
                    // this path was computed (wandering NPC) — recompute to its
                    // current position instead of a false "Could not reach" (proven
                    // at the Lacuer Youth, 2026-08-30: NPC 4m from the stale goal).
                    // The initial approach is capped so a genuinely unreachable NPC
                    // still gets an honest refusal; the FOLLOW phase (after the
                    // first catch) recalcs without a cap — short 2-corner paths to
                    // a walker exhaust every few steps, and the stuck detector
                    // already guards against chasing an unreachable target.
                    if (_autoWalkTransform != null
                        && (_autoWalkArrivalAnnounced
                            || _pathExhaustRecalcs < MaxPathExhaustRecalcs))
                    {
                        if (!_autoWalkArrivalAnnounced) _pathExhaustRecalcs++;
                        bool rescued = false;
                        try
                        {
                            rescued = CalculateAndStorePath(playerPos, _autoWalkTarget,
                                allowPartial: true, isCounter: _autoWalkIsCounter);
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.LogState(
                                $"NAV auto-walk: exhaust-recalc error: {ex.Message}");
                        }
                        if (rescued)
                        {
                            DebugLogger.LogState(
                                $"NAV auto-walk: path exhausted for '{_autoWalkLabel}' " +
                                $"but target moved — recomputed to live position " +
                                $"({_autoWalkTarget.x:F1},{_autoWalkTarget.z:F1}), " +
                                (_autoWalkArrivalAnnounced
                                    ? "follow phase (uncapped)."
                                    : $"attempt {_pathExhaustRecalcs} of {MaxPathExhaustRecalcs}."));
                            return;
                        }
                        DebugLogger.LogState(
                            $"NAV auto-walk: exhaust-recalc to live position failed " +
                            $"for '{_autoWalkLabel}' — falling through to honest stop.");
                    }

                    // Path exhausted but proximity check didn't fire.
                    // With input injection, event triggers and map exits fire
                    // naturally via Unity colliders as the player walks through.
                    // Just stop and announce — the game handles transitions.
                    StopAutoWalk();

                    // Face toward FacePosition if set (e.g. water center), else target.
                    Vector3 exhaustFaceDir = targetDir;
                    if (_autoWalkFacePosition.HasValue)
                    {
                        Vector3 toFace = _autoWalkFacePosition.Value - playerPos;
                        toFace.y = 0f;
                        if (toFace.sqrMagnitude > 0.01f)
                            exhaustFaceDir = toFace.normalized;
                    }
                    player.transform.rotation = Quaternion.LookRotation(exhaustFaceDir, Vector3.up);

                    // Only claim arrival if the player is genuinely at the real
                    // target. If the NavMesh path simply ran out short of it
                    // (disconnected surface / different floor), say so honestly
                    // instead of falsely reporting arrival.
                    bool reallyArrived = IsAtRealTarget(playerPos,
                        out float exhHoriz, out float exhVert);
                    int meters = Mathf.RoundToInt(exhHoriz);
                    string compass = GetCompassDirection(playerPos, _autoWalkTarget);
                    if (reallyArrived)
                    {
                        AnnounceArrival(Loc.Get("nav_autowalk_arrived", _autoWalkLabel));
                    }
                    else
                    {
                        string key = Mathf.Abs(exhVert) <= ArrivalVerticalTolerance
                            ? "nav_autowalk_cannot_reach"
                            : (exhVert > 0f ? "nav_autowalk_cannot_reach_above"
                                            : "nav_autowalk_cannot_reach_below");
                        AnnounceArrival(Loc.Get(key, _autoWalkLabel,
                            meters.ToString(), compass));
                    }
                    Vector3 fwd = player.transform.forward;
                    DebugLogger.LogState(
                        $"NAV auto-walk: path exhausted for '{_autoWalkLabel}'. " +
                        $"reallyArrived={reallyArrived} vertGap={exhVert:F1}. " +
                        $"{meters}m {compass}. " +
                        $"player=({playerPos.x:F2},{playerPos.y:F2},{playerPos.z:F2}), " +
                        $"target=({_autoWalkTarget.x:F2},{_autoWalkTarget.y:F2},{_autoWalkTarget.z:F2}), " +
                        $"facing=({fwd.x:F2},{fwd.y:F2},{fwd.z:F2}), " +
                        $"targetDir=({targetDir.x:F2},{targetDir.y:F2},{targetDir.z:F2})");
                    return;
                }

                Vector3 waypoint = _pathCorners[_pathCornerIndex];
                float wpDx   = waypoint.x - playerPos.x;
                float wpDz   = waypoint.z - playerPos.z;
                float wpDist = Mathf.Sqrt(wpDx * wpDx + wpDz * wpDz);

                // Advance to the next waypoint if close enough to the current one.
                if (wpDist <= WaypointArrivalThreshold)
                {
                    _pathCornerIndex++;
                    if (_pathCornerIndex >= _pathCorners.Length)
                    {
                        // Counter NPCs: the partial path ends at the counter, not
                        // the NPC. Stop and announce arrival.
                        if (_autoWalkIsCounter)
                        {
                            StopAutoWalk();
                            // Face the NPC behind the counter.
                            Vector3 toNpc = _autoWalkTarget - playerPos;
                            toNpc.y = 0f;
                            if (toNpc.sqrMagnitude > 0.01f)
                                player.transform.rotation =
                                    Quaternion.LookRotation(toNpc.normalized, Vector3.up);

                            AnnounceArrival(
                                Loc.Get("nav_autowalk_arrived_npc", _autoWalkLabel));
                            DebugLogger.LogState(
                                $"NAV auto-walk arrived at counter NPC '{_autoWalkLabel}'.");
                            return;
                        }

                        // Different floor: the partial path has ended short of a
                        // target on another level. This is NOT an arrival — be
                        // honest. (Only the rare case where the player genuinely
                        // ended up next to the target counts as arrival.)
                        if (_autoWalkDifferentFloor)
                        {
                            StopAutoWalk();

                            if (IsAtRealTarget(playerPos, out float dfHoriz,
                                out float dfVert))
                            {
                                AnnounceArrival(
                                    Loc.Get("nav_autowalk_arrived", _autoWalkLabel));
                            }
                            else
                            {
                                int meters = Mathf.RoundToInt(dfHoriz);
                                string compass =
                                    GetCompassDirection(playerPos, _autoWalkTarget);
                                string key = dfVert > 0f
                                    ? "nav_autowalk_cannot_reach_above"
                                    : "nav_autowalk_cannot_reach_below";
                                AnnounceArrival(Loc.Get(key, _autoWalkLabel,
                                    meters.ToString(), compass));
                            }
                            DebugLogger.LogState(
                                $"NAV auto-walk partial path ended for '{_autoWalkLabel}' " +
                                $"(playerY={playerPos.y:F1}, targetY={_autoWalkTarget.y:F1}).");
                            return;
                        }

                        // Normal targets: will be caught by arrival check next frame.
                        // Stop injecting to let the game settle the player naturally.
                        _staticAutoWalkStickDir = Vector2.zero;
                        return;
                    }
                    waypoint = _pathCorners[_pathCornerIndex];
                    wpDx   = waypoint.x - playerPos.x;
                    wpDz   = waypoint.z - playerPos.z;
                    wpDist = Mathf.Sqrt(wpDx * wpDx + wpDz * wpDz);
                }

                // --- Input injection: set synthetic stick direction ---
                // Calculate world-space direction toward the current waypoint,
                // convert to camera-relative stick, and inject via GetLeftStick postfix.
                // The game's own movement pipeline handles physics, colliders,
                // animations, triggers, and party AI — no manual position setting.
                {
                    Vector3 moveDir = wpDist > 0.01f
                        ? new Vector3(wpDx / wpDist, 0f, wpDz / wpDist)
                        : Vector3.forward;

                    // Soft spatial-awareness assist: nudge the heading around nearby
                    // NPCs/clutter that aren't in the NavMesh. The cap is gentle in
                    // the open and widens when wedged, so the player keeps heading to
                    // the same waypoint/destination. The target transform is excluded
                    // so we never steer away from the NPC/chest we're walking toward.
                    // The sensor logs its own throttled WALK-ASSIST diagnostic.
                    if (ModSettings.WalkAssistEnabled)
                    {
                        moveDir = _spatialSensor.Steer(playerPos, moveDir,
                            _autoWalkTransform, out bool _);
                    }

                    Vector2 walkStick = WorldDirToCameraStick(moveDir);
                    if (ModSettings.WalkAssistEnabled)
                        walkStick *= _spatialSensor.LastSpeedScale; // slow while threading a wedge
                    _staticAutoWalkStickDir = walkStick;

                    // Camera follow: gently rotate camera to face the walking direction.
                    UpdateCameraFollow(moveDir);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV auto-walk Update error: {ex}");
                DebugLogger.LogState(
                    $"NAV auto-walk state: _pathCornerIndex={_pathCornerIndex}, " +
                    $"_pathCorners={(_pathCorners == null ? "null" : _pathCorners.Length.ToString())}, " +
                    $"_isWorldmap={_isWorldmap}, _autoWalkTransform={(_autoWalkTransform == null ? "null" : "set")}");
                CancelAutoWalk();
            }
        }

        /// <summary>
        /// Per-frame late update — currently unused; kept as a hook point for future needs.
        /// Animation is handled by the game's own movement pipeline via GetLeftStick injection.
        /// </summary>
        public void LateUpdate() { }

        /// <summary>
        /// The SINGLE authority for "is the player actually AT the navigation
        /// target". Measures distance to the REAL final target — the chest/NPC
        /// itself, never a multi-segment bridge waypoint — in full 3D: within the
        /// arrival radius horizontally AND on the same vertical level. This is
        /// what prevents false "arrived" reports when a path stops short of, or
        /// above/below, the real target.
        /// </summary>
        /// <param name="playerPos">Current player position.</param>
        /// <param name="horizDist">Out: horizontal (XZ) distance to the real target.</param>
        /// <param name="vertGap">Out: signed vertical gap (target.y - player.y).</param>
        private bool IsAtRealTarget(Vector3 playerPos, out float horizDist, out float vertGap)
        {
            Vector3 real = _autoWalkTarget;
            float dx = real.x - playerPos.x;
            float dz = real.z - playerPos.z;
            horizDist = Mathf.Sqrt(dx * dx + dz * dz);
            vertGap = real.y - playerPos.y;

            bool isInteractable = _autoWalkCategoryIndex == CAT_CHEST
                || _autoWalkCategoryIndex == CAT_SAVE
                || _autoWalkCategoryIndex == CAT_INTERACTABLE;
            float radius = isInteractable
                ? InteractableArrivalRadius : AutoWalkArrivalRadius;

            return horizDist <= radius
                && Mathf.Abs(vertGap) <= ArrivalVerticalTolerance;
        }

        /// <summary>
        /// Logs detailed diagnostic info when arriving at a target.
        /// Dumps game contactDistance/conversationDistance for nearby NPCs,
        /// party member positions, and all FieldObject distances.
        /// </summary>
        private void LogNpcArrivalDiagnostics(FieldPlayer player, Vector3 playerPos, float targetDist)
        {
            try
            {
                DebugLogger.LogState(
                    $"NAV ARRIVAL DIAG: label='{_autoWalkLabel}' " +
                    $"player=({playerPos.x:F2},{playerPos.y:F2},{playerPos.z:F2}) " +
                    $"target=({_autoWalkTarget.x:F2},{_autoWalkTarget.y:F2},{_autoWalkTarget.z:F2}) " +
                    $"distXZ={targetDist:F2} " +
                    $"cat={_autoWalkCategoryIndex} counter={_autoWalkIsCounter} " +
                    $"liveTransform={(_autoWalkTransform != null ? "yes" : "no")}");

                // Dump contactDistance for all nearby FieldObjects
                var fieldObjects = UnityEngine.Object.FindObjectsOfType<FieldObject>();
                if (fieldObjects != null)
                {
                    foreach (var fo in fieldObjects)
                    {
                        if (fo == null) continue;
                        float foDist = Vector3.Distance(playerPos, fo.transform.position);
                        if (foDist > 10f) continue; // only nearby
                        try
                        {
                            float contactDist = fo.ContactDistance;
                            DebugLogger.LogState(
                                $"NAV ARRIVAL DIAG FieldObj: '{fo.name}' " +
                                $"pos=({fo.transform.position.x:F2},{fo.transform.position.y:F2},{fo.transform.position.z:F2}) " +
                                $"distToPlayer={foDist:F2} contactDistance={contactDist:F2}");
                        }
                        catch
                        {
                            DebugLogger.LogState(
                                $"NAV ARRIVAL DIAG FieldObj: '{fo.name}' distToPlayer={foDist:F2} (contactDist read failed)");
                        }
                    }
                }

                // Dump NPC-specific data: conversationDistance from ConstNpcParameter
                var npcs = UnityEngine.Object.FindObjectsOfType<FieldNpcCharacter>();
                if (npcs != null)
                {
                    foreach (var npc in npcs)
                    {
                        if (npc == null) continue;
                        float npcDist = Vector3.Distance(playerPos, npc.transform.position);
                        if (npcDist > 10f) continue;
                        try
                        {
                            // Try to read the NPC's ConstNpcParameter for conversationDistance
                            string npcInfo = $"NAV ARRIVAL DIAG NPC: '{npc.name}' " +
                                $"type={npc.npcType} " +
                                $"pos=({npc.transform.position.x:F2},{npc.transform.position.y:F2},{npc.transform.position.z:F2}) " +
                                $"distToPlayer={npcDist:F2}";
                            try
                            {
                                float contactDist = npc.ContactDistance;
                                npcInfo += $" contactDist={contactDist:F2}";
                            }
                            catch { npcInfo += " contactDist=ERR"; }
                            DebugLogger.LogState(npcInfo);
                        }
                        catch (Exception npcEx)
                        {
                            DebugLogger.LogState(
                                $"NAV ARRIVAL DIAG NPC: '{npc.name}' distToPlayer={npcDist:F2} error={npcEx.Message}");
                        }
                    }
                }

                // Dump party member (FieldFollowCharacter) positions
                var followers = UnityEngine.Object.FindObjectsOfType<FieldFollowCharacter>();
                if (followers != null)
                {
                    foreach (var fc in followers)
                    {
                        if (fc == null) continue;
                        Vector3 fcPos = fc.transform.position;
                        float fcDist = Vector3.Distance(playerPos, fcPos);
                        DebugLogger.LogState(
                            $"NAV ARRIVAL DIAG PARTY: '{fc.name}' " +
                            $"pos=({fcPos.x:F2},{fcPos.y:F2},{fcPos.z:F2}) " +
                            $"distToPlayer={fcDist:F2} " +
                            $"distToTarget={Vector3.Distance(fcPos, _autoWalkTarget):F2}");
                    }
                }

                // Log all colliders near the target (interaction might use trigger colliders)
                var colliders = UnityEngine.Physics.OverlapSphere(_autoWalkTarget, 3f);
                if (colliders != null)
                {
                    foreach (var col in colliders)
                    {
                        if (col == null) continue;
                        DebugLogger.LogState(
                            $"NAV ARRIVAL DIAG COLLIDER: '{col.name}' " +
                            $"type={col.GetType().Name} isTrigger={col.isTrigger} " +
                            $"pos=({col.transform.position.x:F2},{col.transform.position.y:F2},{col.transform.position.z:F2}) " +
                            $"bounds=({col.bounds.size.x:F2},{col.bounds.size.y:F2},{col.bounds.size.z:F2})");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV ARRIVAL DIAG error: {ex.Message}");
            }
        }

        #endregion


    }
}
