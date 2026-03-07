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
    /// KEYBOARD
    /// NumPad 5 (first press) — scan the field, build a sorted list, announce first item.
    /// NumPad 5 (second press) — close the list.
    /// NumPad 8 / 2    — move up / down within the current category.
    /// NumPad 4 / 6    — switch to the previous / next non-empty category.
    /// NumPad 1        — start auto-walking to the currently highlighted item.
    ///
    /// GAMEPAD
    /// Hold L1         — scan and open the list while held (field only, not in menus/battle).
    /// D-pad Up/Down   — switch category.
    /// D-pad Left/Right— move to previous / next item within category.
    /// L1 + LStick Up  — start auto-walking to the highlighted item.
    /// Release L1      — close the list silently.
    ///
    /// AUTO-WALK
    /// On activation: closes the list, announces "Walking to [label].", then moves
    /// the player toward the target via direct transform manipulation.
    /// Announces "Arrived at [label]." on arrival (within 1.8 units).
    /// NumPad 1 cancels while walking. L1 press also cancels and reopens the list.
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

        /// <summary>Rotation speed in degrees per second while auto-walking.</summary>
        private const float AutoWalkTurnSpeed = 720f;

        /// <summary>Max distance to snap a world position onto the NavMesh surface.</summary>
        private const float NavMeshSampleRadius = 5f;

        /// <summary>Seconds between path recalculations when following a moving NPC.</summary>
        private const float PathRecalcInterval = 1.5f;

        /// <summary>Distance threshold for advancing to the next path waypoint.</summary>
        private const float WaypointArrivalThreshold = 0.3f;

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

        /// <summary>Seconds between stuck checks during world map auto-walk.</summary>
        private const float WorldmapStuckCheckInterval = 3f;

        /// <summary>
        /// Minimum distance the player must move during a stuck check interval
        /// to be considered making progress. Below this, auto-walk is cancelled.
        /// </summary>
        private const float WorldmapStuckMinMove = 2f;

        /// <summary>Max distance to show chests on the world map.</summary>
        private const float WorldmapChestMaxDistance = 200f;

        /// <summary>Max distance to show enemies on the world map.</summary>
        private const float WorldmapEnemyMaxDistance = 150f;

        /// <summary>
        /// Arrival radius for world map targets (larger than field because
        /// world map symbols and objects are bigger).
        /// </summary>
        private const float WorldmapArrivalRadius = 15f;

        /// <summary>
        /// Number of CalcHeight samples along the line from player to target
        /// for ocean barrier detection on the world map.
        /// </summary>
        private const int WorldmapCalcHeightSamples = 10;

        #endregion

        #region State

        private readonly List<NavItem>[] _categories;
        private bool _isOpen;
        private int  _currentCategoryIndex;
        private int  _currentItemIndex;

        private bool      _isAutoWalking;
        private float     _autoWalkSpeed;   // queried from player at walk start via GetMoveSpeed(true)
        private Vector3   _autoWalkTarget;
        private string    _autoWalkLabel;
        /// <summary>
        /// Live transform of the current auto-walk target.
        /// Null for exits and when the target has no live reference.
        /// Updated each frame so the player follows a wandering NPC.
        /// </summary>
        private Transform _autoWalkTransform;
        /// <summary>
        /// True once the player has reached the target and "Arrived" has been announced.
        /// In proximity-lock mode the player stays glued to the NPC until NumPad 5 is pressed.
        /// </summary>
        private bool _autoWalkArrived;
        /// <summary>
        /// True when auto-walking to a counter NPC (shop, inn, guild) via a partial path.
        /// Arrival is detected at the last waypoint rather than proximity to the NPC.
        /// </summary>
        private bool _autoWalkIsCounter;

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

        /// <summary>
        /// Static mirror of _isAutoWalking — readable by the Harmony prefix which must be static.
        /// True only during the approach phase (not in proximity-lock and not stopped).
        /// </summary>
        private static bool _staticIsApproaching;

        /// <summary>
        /// True while the gamepad L1 nav overlay is active. Static so Harmony prefixes
        /// can read it to suppress game input (D-pad, FieldCameraLeft).
        /// </summary>
        private static bool _gamepadNavActive;

        // Map name announcement: track current fieldmap to detect area changes.
        private FieldmapID _lastFieldmapID = FieldmapID.INVALID;
        private bool _fieldmapInitialized;

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
        /// - PlayMoveAnimation prefix (blocks animation resets during auto-walk)
        /// - GameInputManager.IsDown prefix (suppresses D-pad/L1 camera when gamepad nav active)
        /// - GameInputManager.IsRepeat prefix (suppresses D-pad repeat)
        /// - GameInputManager.GetDPad prefix (suppresses D-pad analog)
        /// </summary>
        public void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                harmony.Patch(
                    AccessTools.Method(typeof(FieldBillboardObject), "PlayMoveAnimation",
                        new Type[] { typeof(FieldAnimationKind) }),
                    prefix: new HarmonyMethod(typeof(NavigationHandler),
                        nameof(PlayMoveAnimation_Prefix)));
                DebugLogger.LogState("NavigationHandler: PlayMoveAnimation prefix applied.");
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error(
                    $"NavigationHandler.ApplyPatches failed (PlayMoveAnimation): {ex.Message}");
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
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Toggles the navigation list open or closed (keyboard: NumPad 5).
        /// On open: scans the field, builds the list, announces the first item.
        /// On close: clears the list and announces closure.
        /// Cancels any active auto-walk before closing.
        /// </summary>
        public void ToggleNavList()
        {
            if (_isAutoWalking)
            {
                CancelAutoWalk();
                return;
            }

            if (_isOpen)
            {
                CloseList();
                return;
            }

            if (!IsFieldFree())
                return;

            ScanAndOpenList();
        }

        /// <summary>
        /// Opens the navigation list for gamepad use (L1 pressed).
        /// Cancels auto-walk if active, checks field is free, scans, opens.
        /// Sets <see cref="_gamepadNavActive"/> to enable input suppression.
        /// </summary>
        public void GamepadOpenNav()
        {
            DebugLogger.LogState("GamepadOpenNav called.");

            if (_isAutoWalking)
            {
                DebugLogger.LogState("GamepadOpenNav: cancelling auto-walk first.");
                CancelAutoWalk();
            }

            if (!IsFieldFree())
            {
                DebugLogger.LogState("GamepadOpenNav: IsFieldFree=false, aborting.");
                return;
            }

            DebugLogger.LogState("GamepadOpenNav: IsFieldFree=true, scanning...");
            ScanAndOpenList();

            if (_isOpen)
            {
                _gamepadNavActive = true;
                DebugLogger.LogState("GamepadOpenNav: list opened, _gamepadNavActive=true.");
            }
            else
            {
                DebugLogger.LogState("GamepadOpenNav: ScanAndOpenList did not open the list.");
            }
        }

        /// <summary>
        /// Closes the navigation list for gamepad use (L1 released).
        /// Closes silently (no "closed" announcement) and disables input suppression.
        /// Category and item indices persist so the user can quickly reopen.
        /// </summary>
        public void GamepadCloseNav()
        {
            DebugLogger.LogState($"GamepadCloseNav called. _isOpen={_isOpen} _gamepadNavActive={_gamepadNavActive}");
            _gamepadNavActive = false;

            if (_isOpen)
            {
                _isOpen = false;
                for (int i = 0; i < CAT_COUNT; i++) _categories[i].Clear();
                // No announcement — user knows they released L1.
            }
        }

        /// <summary>Moves to the next item in the current category. Wraps around.</summary>
        public void NavDown()
        {
            if (!_isOpen) return;
            var cat = _categories[_currentCategoryIndex];
            if (cat.Count == 0) return;
            _currentItemIndex = (_currentItemIndex + 1) % cat.Count;
            AnnounceCurrentItem();
        }

        /// <summary>Moves to the previous item in the current category. Wraps around.</summary>
        public void NavUp()
        {
            if (!_isOpen) return;
            var cat = _categories[_currentCategoryIndex];
            if (cat.Count == 0) return;
            _currentItemIndex = (_currentItemIndex - 1 + cat.Count) % cat.Count;
            AnnounceCurrentItem();
        }

        /// <summary>Moves to the next non-empty category and announces it.</summary>
        public void NavCategoryNext()
        {
            if (!_isOpen) return;
            int next = FirstNonEmptyCategoryFrom(_currentCategoryIndex + 1);
            if (next == _currentCategoryIndex) return;
            _currentCategoryIndex = next;
            _currentItemIndex     = 0;
            AnnounceCategory();
        }

        /// <summary>Moves to the previous non-empty category and announces it.</summary>
        public void NavCategoryPrev()
        {
            if (!_isOpen) return;
            int prev = LastNonEmptyCategoryBefore(_currentCategoryIndex);
            if (prev == _currentCategoryIndex) return;
            _currentCategoryIndex = prev;
            _currentItemIndex     = 0;
            AnnounceCategory();
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

            if (!_isAutoWalking) return;

            // Cancel auto-walk if a dialogue, event, notification, or menu appeared.
            if (!IsFieldFree())
            {
                CancelAutoWalk();
                return;
            }

            try
            {
                var fm = FieldManager.Instance;
                if (fm == null) { CancelAutoWalk(); return; }

                var player = fm.GetControlPlayer();
                if (player == null) { CancelAutoWalk(); return; }

                Vector3 playerPos = player.transform.position;

                // If the target has a live transform (NPC, chest, marker), update
                // the target position every frame so wandering NPCs are tracked.
                if (_autoWalkTransform != null)
                    _autoWalkTarget = _autoWalkTransform.position;

                // --- Check arrival at the final target (not waypoint) ---
                float targetDx   = _autoWalkTarget.x - playerPos.x;
                float targetDz   = _autoWalkTarget.z - playerPos.z;
                float targetDist = Mathf.Sqrt(targetDx * targetDx + targetDz * targetDz);

                // Direction toward the actual target (for facing and proximity-lock).
                Vector3 targetDir = targetDist > 0.01f
                    ? new Vector3(targetDx / targetDist, 0f, targetDz / targetDist)
                    : Vector3.forward;

                float arrivalRadius = _isWorldmap ? WorldmapArrivalRadius : AutoWalkArrivalRadius;
                if (targetDist <= arrivalRadius)
                {
                    // Face the target.
                    player.transform.rotation = Quaternion.LookRotation(targetDir, Vector3.up);

                    // Only NPCs use proximity-lock (they wander, so the player
                    // follows until manually cancelled). All other targets with
                    // a LiveTransform (chests, save points, markers) fully stop
                    // on arrival — same as static exits.
                    bool useProximityLock = _autoWalkTransform != null
                        && _autoWalkCategoryIndex == CAT_NPC;

                    if (!useProximityLock)
                    {
                        // Non-NPC target — fully stop.
                        _isAutoWalking       = false;
                        _staticIsApproaching = false;
                        _pathCorners         = null;

                        // Snap the player close to the target so the game's
                        // interaction check succeeds immediately on button press.
                        const float InteractDist = 1.0f;
                        if (targetDist > InteractDist && _autoWalkTransform != null)
                        {
                            player.transform.position = new Vector3(
                                _autoWalkTarget.x - targetDir.x * InteractDist,
                                playerPos.y,
                                _autoWalkTarget.z - targetDir.z * InteractDist);
                        }

                        // For exit-type targets, add compass direction so the player
                        // knows which way to walk to pass through the exit.
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

                    // NPC — proximity-lock mode (follow until manually cancelled).
                    if (!_autoWalkArrived)
                    {
                        _autoWalkArrived     = true;
                        _staticIsApproaching = false;
                        AnnounceArrival(Loc.Get("nav_autowalk_arrived_npc", _autoWalkLabel));
                        DebugLogger.LogState($"NAV auto-walk proximity lock '{_autoWalkLabel}'.");
                    }

                    // Lock the player 1 unit away from the NPC.
                    const float LockDist = 1.0f;
                    player.transform.position = new Vector3(
                        _autoWalkTarget.x - targetDir.x * LockDist,
                        playerPos.y,
                        _autoWalkTarget.z - targetDir.z * LockDist);
                    return;
                }

                // --- Approach phase ---
                _autoWalkArrived     = false;
                _staticIsApproaching = true;

                // World map: use per-frame WorldmapFindPath from real player
                // position. The A* pathfinder gives a single next-step waypoint
                // that avoids terrain obstacles. Stored waypoints are not used
                // because coordinate wrapping shifts positions each frame.
                if (_isWorldmap)
                {
                    // Stuck detection: cancel if no progress over interval.
                    _wmStuckTimer += Time.deltaTime;
                    if (_wmStuckTimer >= WorldmapStuckCheckInterval)
                    {
                        float movedDx = playerPos.x - _wmLastStuckCheckPos.x;
                        float movedDz = playerPos.z - _wmLastStuckCheckPos.z;
                        float movedSq = movedDx * movedDx + movedDz * movedDz;
                        if (movedSq < WorldmapStuckMinMove * WorldmapStuckMinMove)
                        {
                            DebugLogger.LogState(
                                $"NAV worldmap: stuck (moved {Mathf.Sqrt(movedSq):F1} in " +
                                $"{WorldmapStuckCheckInterval}s). Cancelling.");
                            ScreenReader.Say(Loc.Get("nav_autowalk_unreachable", _autoWalkLabel));
                            CancelAutoWalk();
                            return;
                        }
                        _wmLastStuckCheckPos = playerPos;
                        _wmStuckTimer = 0f;
                    }

                    // Ask the A* pathfinder for the next step toward the target.
                    Vector3 moveDir = targetDir; // fallback: straight toward target
                    var pf = GetWorldmapPathFinder();
                    if (pf != null)
                    {
                        try
                        {
                            Vector3 from = playerPos;
                            Vector3 to = _autoWalkTarget;
                            if (pf.WorldmapFindPath(ref from, ref to) &&
                                pf.routeCount > 0 && pf.routes != null)
                            {
                                Vector3 nextStep = pf.routes[0];
                                float nsDx = nextStep.x - playerPos.x;
                                float nsDz = nextStep.z - playerPos.z;
                                float nsDist = Mathf.Sqrt(nsDx * nsDx + nsDz * nsDz);
                                if (nsDist > 0.1f)
                                    moveDir = new Vector3(nsDx / nsDist, 0f, nsDz / nsDist);
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.LogState($"NAV worldmap FindPath: {ex.Message}");
                        }
                    }

                    float step = _autoWalkSpeed * Time.deltaTime;
                    float nx = playerPos.x + moveDir.x * step;
                    float nz = playerPos.z + moveDir.z * step;
                    float ny = Mathf.Lerp(playerPos.y, _autoWalkTarget.y, 0.15f);
                    player.transform.position = new Vector3(nx, ny, nz);

                    Quaternion moveRot = Quaternion.LookRotation(moveDir, Vector3.up);
                    player.transform.rotation = Quaternion.RotateTowards(
                        player.transform.rotation, moveRot, AutoWalkTurnSpeed * Time.deltaTime);
                    return;
                }

                // --- Field map: waypoint-based approach ---

                // Safety check: if path data is missing, cancel.
                if (_pathCorners == null || _pathCorners.Length == 0)
                {
                    DebugLogger.LogState("NAV auto-walk: no path corners, cancelling.");
                    CancelAutoWalk();
                    return;
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
                            if (CalculateAndStorePath(playerPos, _autoWalkTarget))
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
                        // Reached the final waypoint.
                        player.transform.position = new Vector3(
                            waypoint.x,
                            Mathf.Lerp(playerPos.y, waypoint.y, 0.3f),
                            waypoint.z);

                        // Counter NPCs: the partial path ends at the counter, not
                        // the NPC. Announce arrival here and stop walking.
                        if (_autoWalkIsCounter)
                        {
                            // Face the NPC behind the counter.
                            Vector3 toNpc = _autoWalkTarget - player.transform.position;
                            toNpc.y = 0f;
                            if (toNpc.sqrMagnitude > 0.01f)
                                player.transform.rotation =
                                    Quaternion.LookRotation(toNpc.normalized, Vector3.up);

                            _isAutoWalking       = false;
                            _staticIsApproaching = false;
                            _pathCorners         = null;
                            AnnounceArrival(
                                Loc.Get("nav_autowalk_arrived_npc", _autoWalkLabel));
                            DebugLogger.LogState(
                                $"NAV auto-walk arrived at counter NPC '{_autoWalkLabel}'.");
                            return;
                        }

                        // Normal targets: will be caught by arrival check next frame.
                        return;
                    }
                    waypoint = _pathCorners[_pathCornerIndex];
                    wpDx   = waypoint.x - playerPos.x;
                    wpDz   = waypoint.z - playerPos.z;
                    wpDist = Mathf.Sqrt(wpDx * wpDx + wpDz * wpDz);
                }

                {
                    // Direction toward the current waypoint.
                    Vector3 moveDir = wpDist > 0.01f
                        ? new Vector3(wpDx / wpDist, 0f, wpDz / wpDist)
                        : Vector3.forward;

                    // Move toward the waypoint.
                    float step = _autoWalkSpeed * Time.deltaTime;
                    float nx   = playerPos.x + moveDir.x * step;
                    float nz   = playerPos.z + moveDir.z * step;
                    // Interpolate Y toward the waypoint's Y for smooth terrain following.
                    float ny   = Mathf.Lerp(playerPos.y, waypoint.y, 0.15f);
                    player.transform.position = new Vector3(nx, ny, nz);

                    // Rotate to face the direction of travel.
                    Quaternion moveRot = Quaternion.LookRotation(moveDir, Vector3.up);
                    player.transform.rotation = Quaternion.RotateTowards(
                        player.transform.rotation, moveRot, AutoWalkTurnSpeed * Time.deltaTime);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV auto-walk Update error: {ex.Message}");
                CancelAutoWalk();
            }
        }

        /// <summary>
        /// Per-frame animation update — called from Main.OnLateUpdate().
        /// LateUpdate runs after ALL game MonoBehaviour Update() calls, so setting
        /// the walk animation here overrides whatever the game's character controller
        /// reset it to during its own Update(). Movement is handled in Update();
        /// only the animation state is managed here.
        /// Does nothing outside of the approach phase (idle at arrival, stopped, etc.).
        /// </summary>
        /// <summary>
        /// Per-frame late update — currently unused; kept as a hook point for future needs.
        /// Animation is now handled via the PlayMoveAnimation Harmony prefix + a single
        /// PlayMoveAnimation(Walk) call at walk start, rather than per-frame overrides.
        /// </summary>
        public void LateUpdate() { }

        #endregion

        #region Private — Announce

        private void CloseList()
        {
            _isOpen = false;
            for (int i = 0; i < CAT_COUNT; i++) _categories[i].Clear();
            ScreenReader.Say(Loc.Get("nav_close"));
        }

        /// <summary>Announces the current item as "[label], [distance] units."</summary>
        private void AnnounceCurrentItem()
        {
            var cat = _categories[_currentCategoryIndex];
            if (cat.Count == 0 || _currentItemIndex >= cat.Count) return;
            var item = cat[_currentItemIndex];
            ScreenReader.Say(Loc.Get("nav_item", item.Label, DistanceUnits(item.Distance)));
        }

        /// <summary>
        /// Announces the current category and its first item as
        /// "[category]. [label], [distance] units."
        /// </summary>
        private void AnnounceCategory()
        {
            var    cat     = _categories[_currentCategoryIndex];
            string catName = _categoryNames[_currentCategoryIndex];
            if (cat.Count == 0)
            {
                ScreenReader.Say(Loc.Get("nav_category_empty", catName));
                return;
            }
            var item = cat[_currentItemIndex];
            ScreenReader.Say(Loc.Get("nav_category",
                catName, item.Label, DistanceUnits(item.Distance)));
        }

        #endregion

        #region Private — Helpers

        /// <summary>
        /// Scans the field and opens the navigation list. Shared by keyboard toggle
        /// and gamepad L1 open. Announces the first item on success.
        /// </summary>
        private void ScanAndOpenList()
        {
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null)
                {
                    ScreenReader.Say(Loc.Get("nav_not_in_field"));
                    return;
                }

                var player = fm.GetControlPlayer();
                if (player == null)
                {
                    ScreenReader.Say(Loc.Get("nav_not_in_field"));
                    return;
                }

                Vector3    playerPos = player.transform.position;
                FieldmapID mapID     = fm.currentFieldmapID;
                _isWorldmap = fm.IsWorldmap();
                if (_isWorldmap)
                    ClearWorldmapCache();

                DebugLogger.LogState(
                    $"NAV scan start. map={mapID} worldmap={_isWorldmap} " +
                    $"playerPos=({playerPos.x:F1},{playerPos.y:F1},{playerPos.z:F1})");

                if (_isWorldmap)
                {
                    // World map: locations (from game data), nearby chests/enemies only.
                    // Skip NPCs, exits, markers, events, save points, stairs, doors,
                    // warps — these are either absent or redundant with Locations.
                    BuildWorldmapLocations(playerPos, fm.WorldmapID);
                    BuildChests(playerPos);
                    BuildEnemies(playerPos);
                }
                else
                {
                    // Field map: full scan as before.
                    BuildNpcs(playerPos, mapID);
                    BuildChests(playerPos);
                    BuildExits(playerPos);
                    BuildMarkers(fm.FieldLocationPointList, playerPos);
                    BuildEvents(playerPos);
                    BuildSavePoints(fm.FieldSavePointList, playerPos);
                    BuildEnemies(playerPos);
                    BuildStairs(fm.FieldStairsList, playerPos);
                    BuildDoors(fm.FieldDoorList, playerPos);
                    BuildWarpPoints(fm, playerPos);
                }

                int totalItems = 0;
                for (int i = 0; i < CAT_COUNT; i++) totalItems += _categories[i].Count;

                DebugLogger.LogState(
                    $"NAV list built. npcs={_categories[CAT_NPC].Count} " +
                    $"chests={_categories[CAT_CHEST].Count} " +
                    $"exits={_categories[CAT_EXIT].Count} " +
                    $"markers={_categories[CAT_MARKER].Count} " +
                    $"events={_categories[CAT_EVENT].Count} " +
                    $"saves={_categories[CAT_SAVE].Count} " +
                    $"enemies={_categories[CAT_ENEMY].Count} " +
                    $"stairs={_categories[CAT_STAIRS].Count} " +
                    $"doors={_categories[CAT_DOOR].Count} " +
                    $"warps={_categories[CAT_WARP].Count} " +
                    $"interactables={_categories[CAT_INTERACTABLE].Count} " +
                    $"locations={_categories[CAT_LOCATION].Count}");

                if (totalItems == 0)
                {
                    ScreenReader.Say(Loc.Get("nav_no_items"));
                    return;
                }

                _isOpen = true;
                _currentCategoryIndex = FirstNonEmptyCategoryFrom(0);
                _currentItemIndex     = 0;

                var  firstItem = _categories[_currentCategoryIndex][0];
                int  dist      = DistanceUnits(firstItem.Distance);
                ScreenReader.Say(Loc.Get("nav_open",
                    _categoryNames[_currentCategoryIndex],
                    firstItem.Label,
                    dist));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"NavigationHandler.ScanAndOpenList: {ex.Message}");
            }
        }

        private bool IsFieldFree() => FieldState.IsFieldFree();

        private int FirstNonEmptyCategoryFrom(int startIndex)
        {
            for (int i = 0; i < CAT_COUNT; i++)
            {
                int idx = (startIndex + i) % CAT_COUNT;
                if (_categories[idx].Count > 0) return idx;
            }
            return startIndex % CAT_COUNT;
        }

        private int LastNonEmptyCategoryBefore(int startIndex)
        {
            for (int i = 1; i <= CAT_COUNT; i++)
            {
                int idx = (startIndex - i + CAT_COUNT) % CAT_COUNT;
                if (_categories[idx].Count > 0) return idx;
            }
            return startIndex;
        }

        private static int DistanceUnits(float dist) => (int)Math.Round(dist);

        /// <summary>
        /// Checks if the current fieldmap has changed and announces the new map name.
        /// Called every frame from Update(). Skips the first detection to avoid
        /// announcing on game load.
        /// </summary>
        private void CheckFieldmapChange()
        {
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null)
                {
                    // Not on a field — reset so next field entry announces.
                    if (_fieldmapInitialized)
                    {
                        _fieldmapInitialized = false;
                        _lastFieldmapID = FieldmapID.INVALID;
                    }
                    return;
                }

                FieldmapID current = fm.currentFieldmapID;
                if (current == _lastFieldmapID) return;

                FieldmapID previous = _lastFieldmapID;
                _lastFieldmapID = current;

                // Skip the very first detection (game load / initial scene).
                if (!_fieldmapInitialized)
                {
                    _fieldmapInitialized = true;
                    return;
                }

                // Skip INVALID transitions.
                if (current == FieldmapID.INVALID) return;

                string name = ResolveMapName(current);
                if (!string.IsNullOrEmpty(name))
                {
                    ScreenReader.Say(name);
                    DebugLogger.LogState($"MapChange: {previous} → {current} = '{name}'");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"CheckFieldmapChange error: {ex.Message}");
            }
        }

        /// <summary>
        /// Resolves a FieldmapID to a human-readable destination name.
        /// Priority: manual overrides → game data (ConstFieldParameter + TextManager) → code suffix.
        /// Results are cached for the session.
        /// </summary>
        private static string ResolveMapName(FieldmapID destId)
        {
            string destCode = destId.ToString();

            // 1. Manual overrides (EXPEL → "Overworld", etc.)
            if (_mapNameOverrides.TryGetValue(destCode, out string overrideName))
                return overrideName;

            // 2. Check cache
            if (_mapNameCache.TryGetValue(destCode, out string cached))
                return cached;

            // 3. Try game data: ConstFieldParameter.FieldmapNameID → TextManager
            string resolved = null;
            try
            {
                var paramMgr = ParameterManager.Instance;
                if (paramMgr != null)
                {
                    var fieldParam = paramMgr.GetFieldParameter(destId);
                    if (fieldParam != null)
                    {
                        string nameKey = fieldParam.FieldmapNameID;
                        DebugLogger.LogGameValue("NAV:MAP_KEY",
                            $"{destCode} → FieldmapNameID='{nameKey}'");

                        if (!string.IsNullOrEmpty(nameKey))
                        {
                            // Try resolving through the game's text system
                            var textMgr = TextManager.Instance;
                            if (textMgr != null)
                            {
                                string text = textMgr.GetMessage(
                                    nameKey, TextManager.MessageType.System);
                                if (!string.IsNullOrEmpty(text))
                                {
                                    resolved = text;
                                    DebugLogger.LogGameValue("NAV:MAP_NAME",
                                        $"{destCode} → '{resolved}' (via TextManager)");
                                }
                            }

                            // If TextManager didn't resolve, use the raw key
                            // (it might already be a readable name)
                            if (resolved == null)
                            {
                                resolved = nameKey;
                                DebugLogger.LogGameValue("NAV:MAP_NAME",
                                    $"{destCode} → '{resolved}' (raw key)");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV:MAP_NAME error for {destCode}: {ex.Message}");
            }

            // 4. Fallback: last underscore segment (e.g. "MF_0003_22A" → "22A")
            if (resolved == null)
            {
                int last = destCode.LastIndexOf('_');
                resolved = (last >= 0 && last < destCode.Length - 1)
                    ? destCode.Substring(last + 1)
                    : destCode;
                DebugLogger.LogGameValue("NAV:MAP_NAME",
                    $"{destCode} → '{resolved}' (fallback suffix)");
            }

            _mapNameCache[destCode] = resolved;
            return resolved;
        }

        /// <summary>
        /// Returns true for NPC types that are commonly placed behind counters
        /// or barriers (shops, inns, guilds). These NPCs should not be filtered
        /// by NavMesh reachability because the game allows interaction over
        /// the counter even though no walkable path exists.
        /// </summary>
        /// <summary>
        /// Returns true for NPC types that represent interactable objects
        /// (switches, beds, inspection points) rather than characters.
        /// These go into the Interactables nav category instead of NPCs.
        /// </summary>
        private static bool IsInteractableNpcType(NpcType type)
        {
            return type switch
            {
                NpcType.CHECK => true,
                NpcType.BED   => true,
                _             => false
            };
        }

        private static bool IsFunctionalNpcType(NpcType type)
        {
            return type switch
            {
                NpcType.INN            => true,
                NpcType.SHOP_EQUIPMENT => true,
                NpcType.SHOP_ITEM      => true,
                NpcType.SHOP_FOOD      => true,
                NpcType.GUILD          => true,
                NpcType.FISH_COLLECTOR => true,
                NpcType.FACILITY       => true,
                _                      => false
            };
        }

        private static string GetNpcCategory(NpcType type)
        {
            return type switch
            {
                NpcType.INN            => "Innkeeper",
                NpcType.SHOP_EQUIPMENT => "Equipment shop",
                NpcType.SHOP_ITEM      => "Item shop",
                NpcType.SHOP_FOOD      => "Food shop",
                NpcType.GUILD          => "Guild",
                NpcType.FISH_COLLECTOR => "Collector",
                NpcType.FACILITY       => "Facility",
                NpcType.CHECK          => "Switch",
                NpcType.BED            => "Bed",
                NpcType.PSYNARD        => "Psynard",
                _                      => "NPC"
            };
        }

        private static Il2CppSystem.Collections.Generic.List<ConstNpcParameter> TryGetNpcParams(
            FieldmapID mapID)
        {
            try
            {
                return ParameterManager.Instance?.GetNpcParameter(mapID);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState(
                    $"NAV: TryGetNpcParams failed for map {mapID}: {ex.Message}");
                return null;
            }
        }

        #endregion
    }
}
