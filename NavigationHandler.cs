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
    public class NavigationHandler
    {
        #region Data Model

        private struct NavItem
        {
            public string    Label;
            public float     Distance;
            public Vector3   Position;
            /// <summary>
            /// Live transform of the target object (NPCs, chests, markers).
            /// Updated each frame during auto-walk so moving NPCs are tracked.
            /// Null for exits — their position in the world does not change.
            /// </summary>
            public Transform LiveTransform;
            /// <summary>
            /// True for functional NPCs (shops, inns, guilds) that are commonly
            /// behind counters. These skip the NavMesh reachability filter because
            /// the game allows interaction over the counter.
            /// </summary>
            public bool      IsCounterNpc;
        }

        private const int CAT_NPC    = 0;
        private const int CAT_CHEST  = 1;
        private const int CAT_EXIT   = 2;
        private const int CAT_MARKER = 3;
        private const int CAT_EVENT  = 4;
        private const int CAT_SAVE   = 5;
        private const int CAT_ENEMY  = 6;
        private const int CAT_COUNT  = 7;

        private static readonly string[] _categoryNames =
            { "NPCs", "Chests", "Exits", "Markers", "Events", "Save Points", "Enemies" };

        /// <summary>
        /// Manual overrides for FieldmapID destination names.
        /// Checked before the game's own map name data.
        /// </summary>
        private static readonly Dictionary<string, string> MapNameOverrides =
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

        /// <summary>
        /// Starts auto-walking to the currently highlighted navigation item.
        /// Calculates a NavMesh path to the target and walks along waypoints via Update().
        /// Announces an error and aborts if no path can be found.
        /// Called by NumPad 5.
        /// </summary>
        public void AutoWalkTo()
        {
            if (!_isOpen) return;
            var cat = _categories[_currentCategoryIndex];
            if (cat.Count == 0 || _currentItemIndex >= cat.Count) return;

            var item = cat[_currentItemIndex];

            // Calculate a NavMesh path before committing to the walk.
            Vector3 playerPos;
            try
            {
                var p = FieldManager.Instance?.GetControlPlayer();
                if (p == null)
                {
                    ScreenReader.Say(Loc.Get("nav_not_in_field"));
                    return;
                }
                playerPos = p.transform.position;
            }
            catch
            {
                ScreenReader.Say(Loc.Get("nav_not_in_field"));
                return;
            }

            // Counter NPCs (shops, inns, guilds) may be behind barriers, so
            // accept a partial NavMesh path — the player walks as close as possible.
            bool pathFound;
            try
            {
                pathFound = CalculateAndStorePath(playerPos, item.Position,
                    allowPartial: item.IsCounterNpc);
            }
            catch
            {
                // NavMesh API completely unavailable — announce and abort.
                ScreenReader.Say(Loc.Get("nav_autowalk_no_navmesh"));
                return;
            }

            if (!pathFound)
            {
                ScreenReader.Say(Loc.Get("nav_autowalk_unreachable", item.Label));
                return;
            }

            _autoWalkTarget      = item.Position;
            _autoWalkLabel       = item.Label;
            _autoWalkTransform   = item.LiveTransform; // may be null for exits
            _autoWalkIsCounter   = item.IsCounterNpc;
            _isAutoWalking       = true;
            _autoWalkArrived     = false;
            _staticIsApproaching = true;

            // Close the list — the player is now running, not browsing.
            _isOpen = false;
            for (int i = 0; i < CAT_COUNT; i++) _categories[i].Clear();

            // Query the player's actual run speed and start the run animation.
            // _staticIsApproaching=true means the Harmony prefix will block the game
            // from resetting the Run animation to Idle each frame.
            try
            {
                var player = FieldManager.Instance?.GetControlPlayer();
                if (player != null)
                {
                    _autoWalkSpeed = player.GetMoveSpeed(true);
                    DebugLogger.LogState($"NAV auto-run: speed={_autoWalkSpeed:F1}");
                    player.PlayMoveAnimation(FieldAnimationKind.Run);
                }
                else
                {
                    _autoWalkSpeed = 10.0f; // fallback if player unavailable
                }
            }
            catch (Exception ex)
            {
                _autoWalkSpeed = 10.0f;
                DebugLogger.LogState($"NAV AutoWalkTo: run setup failed: {ex.Message}");
            }

            ScreenReader.Say(Loc.Get("nav_autowalk_start", item.Label));
            DebugLogger.LogState(
                $"NAV auto-walk started. target={item.Label} " +
                $"pos=({item.Position.x:F1},{item.Position.y:F1},{item.Position.z:F1}) " +
                $"waypoints={_pathCorners.Length}");
        }

        /// <summary>
        /// Cancels an active auto-walk and optionally announces the cancellation.
        /// Called by NumPad 5 during walking, or automatically on scene change.
        /// </summary>
        public void CancelAutoWalk(bool announce = true)
        {
            if (!_isAutoWalking) return;
            _isAutoWalking       = false;
            _autoWalkArrived     = false;
            _autoWalkIsCounter   = false;
            _autoWalkTransform   = null;
            _staticIsApproaching = false; // re-enable normal animation resets
            _pathCorners         = null;
            _pathCornerIndex     = 0;
            _pathRecalcTimer     = 0f;
            if (announce)
                ScreenReader.Say(Loc.Get("nav_autowalk_cancelled"));
            DebugLogger.LogState("NAV auto-walk cancelled.");
        }

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

                if (targetDist <= AutoWalkArrivalRadius)
                {
                    // Face the target.
                    player.transform.rotation = Quaternion.LookRotation(targetDir, Vector3.up);

                    if (_autoWalkTransform == null)
                    {
                        // Static target (exit, static marker) — fully stop.
                        _isAutoWalking       = false;
                        _staticIsApproaching = false;
                        _pathCorners         = null;
                        ScreenReader.Say(Loc.Get("nav_autowalk_arrived", _autoWalkLabel));
                        DebugLogger.LogState($"NAV auto-walk arrived (static) at '{_autoWalkLabel}'.");
                        return;
                    }

                    // Moving target (NPC) — proximity-lock mode.
                    if (!_autoWalkArrived)
                    {
                        _autoWalkArrived     = true;
                        _staticIsApproaching = false;
                        ScreenReader.Say(Loc.Get("nav_autowalk_arrived_npc", _autoWalkLabel));
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
                                CancelAutoWalk(false);
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
                            ScreenReader.Say(
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

        #region Private — Build

        /// <summary>
        /// Scans for NPCs in the current field.
        /// Resolves each NPC's display name via ConstNpcParameter position matching
        /// and code name parsing. Falls back to NPC type label if no match.
        /// Generic NPCs (no specific type) are numbered by distance: NPC 1, NPC 2, etc.
        /// Party members (within 2 units of the player) are filtered out.
        /// </summary>
        private void BuildNpcs(Vector3 playerPos, FieldmapID mapID)
        {
            _categories[CAT_NPC].Clear();

            var npcParams = TryGetNpcParams(mapID);
            DebugLogger.LogState(
                $"NAV: npcParams for map {mapID}: " +
                (npcParams == null ? "null" : $"{npcParams.Count} entries"));

            var found = UnityEngine.Object.FindObjectsOfType<FieldNpcCharacter>();
            if (found == null) return;

            var items = new List<NavItem>();
            foreach (var npc in found)
            {
                if (npc == null) continue;

                // Skip enemies — they have their own category
                if (npc.TryCast<FieldEnemy>() != null) continue;

                Vector3 pos  = npc.transform.position;
                float   dist = Vector3.Distance(playerPos, pos);

                if (dist < 2.0f) continue; // party members walk alongside the player

                string label = ResolveNpcName(npc, npcParams);
                bool isCounter = IsFunctionalNpcType(npc.npcType);
                items.Add(new NavItem
                {
                    Label         = label,
                    Distance      = dist,
                    Position      = pos,
                    LiveTransform = npc.transform,
                    IsCounterNpc  = isCounter,
                });
                DebugLogger.LogGameValue("NAV:NPC",
                    $"[{label}] type={npc.npcType} dist={dist:F1} pos={pos}");
            }

            items.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            // Filter out NPCs that are unreachable via NavMesh.
            // Functional NPCs (shops, inns, guilds) skip this check because
            // they are commonly behind counters — the game allows interaction
            // over the counter even though a walkable path does not exist.
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (items[i].IsCounterNpc)
                {
                    DebugLogger.LogState(
                        $"NAV: keeping counter NPC '{items[i].Label}' (skip reachability)");
                    continue;
                }
                if (!IsReachable(playerPos, items[i].Position))
                {
                    DebugLogger.LogState($"NAV: filtered unreachable NPC '{items[i].Label}'");
                    items.RemoveAt(i);
                }
            }

            // Number any NPCs that still carry the generic "NPC" label.
            int npcNum = 1;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Label == "NPC")
                {
                    item.Label = Loc.Get("nav_npc_n", npcNum++);
                    items[i]   = item;
                }
            }

            _categories[CAT_NPC].AddRange(items);
        }

        /// <summary>
        /// Resolves a human-readable name for an NPC.
        ///
        /// Steps:
        ///   1. Check DialogueHandler.NpcDisplayNames — if the player has already spoken
        ///      to this NPC the real display name is returned, qualified with the NPC's
        ///      functional role when relevant (e.g. "Equipment shop (Hahn)").
        ///   2. Try matching the NPC's initial position against ConstNpcParameter entries.
        ///   3. If a persistent dialogue name is found, return it qualified as above.
        ///   4. Parse the code name (e.g. NPC_..._GIRL1 → "Girl 1").
        ///   5. Fall back to the NPC type category label (e.g. "Item shop").
        ///   6. Final fallback: return "NPC" so the caller can number it.
        /// </summary>
        private static string ResolveNpcName(
            FieldNpcCharacter npc,
            Il2CppSystem.Collections.Generic.List<ConstNpcParameter> npcParams)
        {
            // Resolve the NPC's functional category up front — used to qualify dialogue names.
            // e.g. NpcType.SHOP_EQUIPMENT → "Equipment shop", NpcType.NPC → "NPC"
            string category = GetNpcCategory(npc.npcType);

            // Prefer the real dialogue name if we've already talked to this NPC.
            int instanceID = npc.GetInstanceID();
            if (DialogueHandler.NpcDisplayNames.TryGetValue(instanceID, out string knownName))
            {
                string qualified = QualifyNpcName(knownName, category);
                DebugLogger.LogState(
                    $"NAV: NPC id={instanceID} → '{qualified}' (from dialogue map)");
                return qualified;
            }

            if (npcParams != null && npcParams.Count > 0)
            {
                try
                {
                    Vector3 spawn = npc.InitialPosition;
                    for (int i = 0; i < npcParams.Count; i++)
                    {
                        var param = npcParams[i];
                        if (param == null) continue;
                        if (Vector3.Distance(spawn, param.Position) < 2.0f)
                        {
                            string codeName = param.Name;
                            if (!string.IsNullOrEmpty(codeName))
                            {
                                // Prefer a real name learned from dialogue (persists across sessions).
                                if (DialogueHandler.PersistentNpcNames.TryGetValue(
                                        codeName, out string persistedName))
                                {
                                    string qualified = QualifyNpcName(persistedName, category);
                                    DebugLogger.LogState(
                                        $"NAV: NPC '{codeName}' → '{qualified}' (persistent)");
                                    return qualified;
                                }

                                string readable = ParseNpcCodeName(codeName);
                                if (!string.IsNullOrEmpty(readable))
                                {
                                    DebugLogger.LogState(
                                        $"NAV: NPC '{codeName}' → '{readable}'");
                                    return readable;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"NAV: ResolveNpcName error: {ex.Message}");
                }
            }

            // Fall back to the NPC type category (e.g. "Item shop", "Innkeeper").
            return category;
        }

        /// <summary>
        /// Combines a dialogue display name with a functional NPC category.
        /// Returns "[category] ([displayName])" for functional NPCs (shop, inn, guild, etc.)
        /// and just "[displayName]" for plain NPCs with no specific role.
        /// This ensures "Hahn" becomes "Equipment shop (Hahn)" while "Elderly person"
        /// stays as "Elderly person".
        /// </summary>
        private static string QualifyNpcName(string displayName, string category)
        {
            if (category == "NPC") return displayName;
            return $"{category} ({displayName})";
        }

        /// <summary>
        /// Parses an NPC internal code name into a human-readable label.
        ///
        /// Format: NPC_{mapArea}_{mapSub}_{orderNum}_{DESCRIPTOR}{num}
        /// e.g. NPC_0003_01a_18_GIRL1 → "Girl 1"
        ///      NPC_0003_01a_17_GRANDFATHER2 → "Grandfather 2"
        ///      NPC_0003_01a_26_WEAPONSHOP1  → "Weaponshop 1"
        ///
        /// The last underscore segment is extracted, trailing digits are split from
        /// the descriptor text, and the text is title-cased.
        /// Returns null if the code name cannot be parsed.
        /// </summary>
        private static string ParseNpcCodeName(string codeName)
        {
            if (string.IsNullOrEmpty(codeName)) return null;

            // Take the last segment after the final underscore.
            int lastUnder = codeName.LastIndexOf('_');
            if (lastUnder < 0 || lastUnder >= codeName.Length - 1) return null;

            string suffix = codeName.Substring(lastUnder + 1); // e.g. "GIRL1"

            // Split trailing digits from the descriptor text.
            int numStart = suffix.Length;
            while (numStart > 0 && char.IsDigit(suffix[numStart - 1]))
                numStart--;

            string text = suffix.Substring(0, numStart);       // e.g. "GIRL"
            string num  = suffix.Substring(numStart);           // e.g. "1"

            if (string.IsNullOrEmpty(text)) return null;

            // Title-case: first letter uppercase, rest lowercase.
            string readable = char.ToUpper(text[0]) + text.Substring(1).ToLower();

            return string.IsNullOrEmpty(num) ? readable : $"{readable} {num}";
        }

        /// <summary>
        /// Scans for treasure chests and labels each by opened/unopened status,
        /// numbered separately by type in distance order.
        /// </summary>
        private void BuildChests(Vector3 playerPos)
        {
            _categories[CAT_CHEST].Clear();

            var found = UnityEngine.Object.FindObjectsOfType<FieldTreasureBox>();
            if (found == null) return;

            var items = new List<NavItem>();
            foreach (var chest in found)
            {
                if (chest == null) continue;

                Vector3 pos   = chest.transform.position;
                float   dist  = Vector3.Distance(playerPos, pos);
                string  label = chest.isAcquired
                    ? Loc.Get("nav_chest_opened")
                    : Loc.Get("nav_chest_unopened");

                items.Add(new NavItem
                {
                    Label         = label,
                    Distance      = dist,
                    Position      = pos,
                    LiveTransform = chest.transform,
                });
            }

            items.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            // Filter out chests that are unreachable via NavMesh.
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (!IsReachable(playerPos, items[i].Position))
                {
                    DebugLogger.LogState($"NAV: filtered unreachable chest at dist={items[i].Distance:F1}");
                    items.RemoveAt(i);
                }
            }

            int unopenedNum = 1;
            int openedNum   = 1;
            for (int i = 0; i < items.Count; i++)
            {
                var  item     = items[i];
                bool isOpened = item.Label == Loc.Get("nav_chest_opened");
                item.Label = isOpened
                    ? Loc.Get("nav_chest_opened_n",   openedNum++)
                    : Loc.Get("nav_chest_unopened_n", unopenedNum++);
                items[i] = item;
                DebugLogger.LogGameValue("NAV:CHEST", $"[{item.Label}] dist={item.Distance:F1}");
            }

            _categories[CAT_CHEST].AddRange(items);
        }

        /// <summary>
        /// Scans for map exits and labels each by icon type and destination.
        /// DOOR = "Building entrance to [dest]", GATE = "Town gate to [dest]".
        /// Destinations resolved via game data (ConstFieldParameter + TextManager).
        /// </summary>
        private void BuildExits(Vector3 playerPos)
        {
            _categories[CAT_EXIT].Clear();

            var found = UnityEngine.Object.FindObjectsOfType<FieldMapjumpCollision>();
            if (found == null) return;

            var items = new List<NavItem>();
            foreach (var exit in found)
            {
                if (exit == null) continue;
                try
                {
                    Vector3    pos      = exit.transform.position;
                    float      dist     = Vector3.Distance(playerPos, pos);
                    string     icon     = exit.iconType.ToString();
                    FieldmapID destId   = exit.fieldmapID;
                    string     destName = ResolveMapName(destId);
                    string     typeLabel = icon == "GATE"
                        ? Loc.Get("nav_exit_gate")
                        : Loc.Get("nav_exit_door");
                    string     label    = Loc.Get("nav_exit_with_dest", typeLabel, destName);

                    items.Add(new NavItem { Label = label, Distance = dist, Position = pos });
                    DebugLogger.LogGameValue("NAV:EXIT",
                        $"[{label}] dest={destId} dist={dist:F1}");
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"NAV:EXIT error: {ex.Message}");
                }
            }

            items.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            // Filter out exits that are unreachable via NavMesh.
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (!IsReachable(playerPos, items[i].Position))
                {
                    DebugLogger.LogState($"NAV: filtered unreachable exit '{items[i].Label}'");
                    items.RemoveAt(i);
                }
            }

            _categories[CAT_EXIT].AddRange(items);
        }

        /// <summary>
        /// Reads quest markers from FieldManager.FieldLocationPointList.
        /// Numbers markers if more than one is present.
        /// </summary>
        private void BuildMarkers(
            Il2CppSystem.Collections.Generic.List<FieldLocationPoint> list,
            Vector3 playerPos)
        {
            _categories[CAT_MARKER].Clear();
            if (list == null) return;

            var items = new List<NavItem>();
            for (int i = 0; i < list.Count; i++)
            {
                var marker = list[i];
                if (marker == null) continue;

                Vector3 pos  = marker.transform.position;
                float   dist = Vector3.Distance(playerPos, pos);
                items.Add(new NavItem
                {
                    Label         = Loc.Get("nav_marker"),
                    Distance      = dist,
                    Position      = pos,
                    LiveTransform = marker.transform,
                });
                DebugLogger.LogGameValue("NAV:MARKER",
                    $"id={marker.locationPointID} dist={dist:F1}");
            }

            items.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            // Filter out markers that are unreachable via NavMesh.
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (!IsReachable(playerPos, items[i].Position))
                {
                    DebugLogger.LogState($"NAV: filtered unreachable marker at dist={items[i].Distance:F1}");
                    items.RemoveAt(i);
                }
            }

            if (items.Count > 1)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var item   = items[i];
                    item.Label = Loc.Get("nav_marker_n", i + 1);
                    items[i]   = item;
                }
            }

            _categories[CAT_MARKER].AddRange(items);
        }

        /// <summary>
        /// Scans for active event triggers (story, private action, sub-event).
        /// Only includes triggers whose conditions are currently satisfied.
        /// </summary>
        private void BuildEvents(Vector3 playerPos)
        {
            _categories[CAT_EVENT].Clear();

            var found = UnityEngine.Object.FindObjectsOfType<FieldEventCollision>();
            if (found == null) return;

            var items = new List<NavItem>();
            foreach (var evt in found)
            {
                if (evt == null) continue;
                try
                {
                    if (!evt.IsEventActivate()) continue;

                    Vector3 pos  = evt.transform.position;
                    float   dist = Vector3.Distance(playerPos, pos);

                    string label;
                    var scenario = evt.GetEnableScenarioEvent();
                    var pa       = evt.GetEnablePrivateActionEvent();
                    var sub      = evt.GetEnableSubEvent();

                    if (scenario != null)
                        label = Loc.Get("nav_event_story");
                    else if (pa != null)
                        label = Loc.Get("nav_event_pa");
                    else if (sub != null)
                        label = Loc.Get("nav_event_side");
                    else
                        label = Loc.Get("nav_event_generic");

                    items.Add(new NavItem
                    {
                        Label         = label,
                        Distance      = dist,
                        Position      = pos,
                        LiveTransform = null,
                    });
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState($"NAV:EVENT error: {ex.Message}");
                }
            }

            items.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (!IsReachable(playerPos, items[i].Position))
                {
                    DebugLogger.LogState($"NAV: filtered unreachable event at dist={items[i].Distance:F1}");
                    items.RemoveAt(i);
                }
            }

            int storyNum = 1, paNum = 1, sideNum = 1, genericNum = 1;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Label == Loc.Get("nav_event_story"))
                    item.Label = Loc.Get("nav_event_story_n", storyNum++);
                else if (item.Label == Loc.Get("nav_event_pa"))
                    item.Label = Loc.Get("nav_event_pa_n", paNum++);
                else if (item.Label == Loc.Get("nav_event_side"))
                    item.Label = Loc.Get("nav_event_side_n", sideNum++);
                else
                    item.Label = Loc.Get("nav_event_generic_n", genericNum++);
                items[i] = item;
                DebugLogger.LogGameValue("NAV:EVENT", $"[{item.Label}] dist={item.Distance:F1}");
            }

            _categories[CAT_EVENT].AddRange(items);
        }

        /// <summary>
        /// Scans for save points on the current field map.
        /// Labels as "Save point" or "Recovery save point" based on IsRecovery.
        /// Uses FieldManager.FieldSavePointList (game-managed list).
        /// </summary>
        private void BuildSavePoints(
            Il2CppSystem.Collections.Generic.List<FieldSavePoint> list,
            Vector3 playerPos)
        {
            _categories[CAT_SAVE].Clear();
            if (list == null) return;

            var items = new List<NavItem>();
            int saveCount = 0, recoveryCount = 0;

            for (int i = 0; i < list.Count; i++)
            {
                var sp = list[i];
                if (sp == null) continue;

                Vector3 pos  = sp.transform.position;
                float   dist = Vector3.Distance(playerPos, pos);

                bool recovery = false;
                try { recovery = sp.IsRecovery; } catch { }

                string label = recovery
                    ? Loc.Get("nav_save_recovery")
                    : Loc.Get("nav_save");

                if (recovery) recoveryCount++;
                else          saveCount++;

                items.Add(new NavItem
                {
                    Label         = label,
                    Distance      = dist,
                    Position      = pos,
                    LiveTransform = sp.transform,
                });

                DebugLogger.LogGameValue("NAV:SAVE",
                    $"recovery={recovery} dist={dist:F1}");
            }

            items.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (!IsReachable(playerPos, items[i].Position))
                {
                    DebugLogger.LogState($"NAV: filtered unreachable save point at dist={items[i].Distance:F1}");
                    items.RemoveAt(i);
                }
            }

            // Number items if there are multiples of either type.
            if (saveCount > 1 || recoveryCount > 1)
            {
                int sNum = 1, rNum = 1;
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    if (item.Label == Loc.Get("nav_save_recovery"))
                    {
                        if (recoveryCount > 1)
                            item.Label = Loc.Get("nav_save_recovery_n", rNum++);
                    }
                    else
                    {
                        if (saveCount > 1)
                            item.Label = Loc.Get("nav_save_n", sNum++);
                    }
                    items[i] = item;
                }
            }

            _categories[CAT_SAVE].AddRange(items);
        }

        /// <summary>
        /// Scans for FieldEnemy objects and builds the Enemies category.
        /// Resolves enemy names from party data via ParameterManager + TextManager.
        /// </summary>
        private void BuildEnemies(Vector3 playerPos)
        {
            _categories[CAT_ENEMY].Clear();

            var found = UnityEngine.Object.FindObjectsOfType<FieldEnemy>();
            if (found == null || found.Length == 0) return;

            var pm = ParameterManager.Instance;
            var tm = TextManager.Instance;
            var items = new List<NavItem>();

            for (int i = 0; i < found.Length; i++)
            {
                var enemy = found[i];
                if (enemy == null) continue;

                Vector3 pos  = enemy.transform.position;
                float   dist = Vector3.Distance(playerPos, pos);

                // Get symbol type for difficulty label
                string typeName = "";
                try
                {
                    var symbolType = enemy.EnemySymbolType;
                    typeName = GetEnemyTypeName(symbolType);
                }
                catch { }

                // Resolve enemy name via encounter chain:
                // FieldEnemy.EncountID → encounter params → partyID → enemy params → name
                string enemyName = "";
                try
                {
                    if (pm != null && tm != null)
                    {
                        int encountID = enemy.encountID;

                        if (encountID != 0)
                        {
                            // Step 1: encounter ID → encounter params (has enemy party ID)
                            var encParams = pm.GetFieldmapEncountParameter(encountID);

                            if (encParams != null && encParams.Count > 0)
                            {
                                int partyID = encParams[0].enemyPartyID;

                                if (partyID != 0)
                                {
                                    // Step 2: party ID → enemy parameters (has name key)
                                    var partyMembers =
                                        pm.GetEnemyParameterListByPartyID(partyID);

                                    if (partyMembers != null && partyMembers.Count > 0)
                                    {
                                        string nameKey = partyMembers[0].charaNameID;

                                        if (!string.IsNullOrEmpty(nameKey))
                                        {
                                            // Try all known MessageTypes
                                            enemyName = tm.GetMessage(
                                                nameKey, TextManager.MessageType.System);
                                            if (string.IsNullOrEmpty(enemyName))
                                                enemyName = tm.GetMessage(
                                                    nameKey, TextManager.MessageType.Skill);
                                            if (string.IsNullOrEmpty(enemyName))
                                                enemyName = tm.GetMessage(
                                                    nameKey, TextManager.MessageType.Item);

                                            // Fallback: parse the key into a readable name
                                            // e.g. "CHARA_LIZARDAXE" → "Lizardaxe"
                                            if (string.IsNullOrEmpty(enemyName))
                                                enemyName = ParseCharaNameID(nameKey);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogState(
                        $"NAV:ENEMY name resolve failed: {ex.Message}");
                }

                // Build label: "Name, type" or "Type enemy" fallback
                string label;
                if (!string.IsNullOrEmpty(enemyName))
                {
                    label = string.IsNullOrEmpty(typeName)
                        ? enemyName
                        : Loc.Get("nav_enemy_named", enemyName, typeName);
                }
                else
                {
                    label = string.IsNullOrEmpty(typeName)
                        ? Loc.Get("nav_enemy_unknown")
                        : Loc.Get("nav_enemy_typed", typeName);
                }

                items.Add(new NavItem
                {
                    Label         = label,
                    Distance      = dist,
                    Position      = pos,
                    LiveTransform = enemy.transform,
                });

                DebugLogger.LogGameValue("NAV:ENEMY",
                    $"label='{label}' type={typeName} " +
                    $"partyID={enemy.EnemyPartyID} dist={dist:F1}");
            }

            // Sort by distance
            items.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            // Filter unreachable
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (!IsReachable(playerPos, items[i].Position))
                {
                    DebugLogger.LogState(
                        $"NAV: filtered unreachable enemy at dist={items[i].Distance:F1}");
                    items.RemoveAt(i);
                }
            }

            // Number duplicates of the same base label
            var labelCounts = new Dictionary<string, int>();
            foreach (var item in items)
            {
                if (!labelCounts.ContainsKey(item.Label))
                    labelCounts[item.Label] = 0;
                labelCounts[item.Label]++;
            }

            var labelNums = new Dictionary<string, int>();
            for (int i = 0; i < items.Count; i++)
            {
                string baseLabel = items[i].Label;
                if (labelCounts[baseLabel] > 1)
                {
                    if (!labelNums.ContainsKey(baseLabel))
                        labelNums[baseLabel] = 1;
                    var item = items[i];
                    item.Label = $"{baseLabel} {labelNums[baseLabel]++}";
                    items[i] = item;
                }
            }

            _categories[CAT_ENEMY].AddRange(items);
        }

        /// <summary>
        /// Parses a charaNameID key into a readable enemy name.
        /// e.g. "CHARA_LIZARDAXE" → "Lizardaxe", "CHARA_VOPALBUNNY" → "Vopalbunny"
        /// Strips the "CHARA_" prefix and converts to title case.
        /// </summary>
        private static string ParseCharaNameID(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";

            // Strip common prefixes
            string name = key;
            if (name.StartsWith("CHARA_", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(6);
            else if (name.StartsWith("MON_", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(4);

            if (string.IsNullOrEmpty(name)) return key;

            // Convert: "LIZARDAXE" → "Lizardaxe", "KILLERRABI" → "Killerrabi"
            return char.ToUpper(name[0]) + name.Substring(1).ToLower();
        }

        /// <summary>Returns a friendly name for the enemy symbol type.</summary>
        private static string GetEnemyTypeName(FieldEnemySymbolType type)
        {
            switch (type)
            {
                case FieldEnemySymbolType.Weak:
                case FieldEnemySymbolType.SubspecificWeak:
                    return Loc.Get("nav_enemy_weak");
                case FieldEnemySymbolType.Medium:
                case FieldEnemySymbolType.SubspecificMedium:
                    return Loc.Get("nav_enemy_medium");
                case FieldEnemySymbolType.Strong:
                case FieldEnemySymbolType.SubspecificStrong:
                    return Loc.Get("nav_enemy_strong");
                case FieldEnemySymbolType.Raid:
                    return Loc.Get("nav_enemy_raid");
                default:
                    return "";
            }
        }

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

                DebugLogger.LogState(
                    $"NAV scan start. map={mapID} " +
                    $"playerPos=({playerPos.x:F1},{playerPos.y:F1},{playerPos.z:F1})");

                BuildNpcs(playerPos, mapID);
                BuildChests(playerPos);
                BuildExits(playerPos);
                BuildMarkers(fm.FieldLocationPointList, playerPos);
                BuildEvents(playerPos);
                BuildSavePoints(fm.FieldSavePointList, playerPos);
                BuildEnemies(playerPos);

                int totalItems = 0;
                for (int i = 0; i < CAT_COUNT; i++) totalItems += _categories[i].Count;

                DebugLogger.LogState(
                    $"NAV list built. npcs={_categories[CAT_NPC].Count} " +
                    $"chests={_categories[CAT_CHEST].Count} " +
                    $"exits={_categories[CAT_EXIT].Count} " +
                    $"markers={_categories[CAT_MARKER].Count} " +
                    $"events={_categories[CAT_EVENT].Count} " +
                    $"saves={_categories[CAT_SAVE].Count} " +
                    $"enemies={_categories[CAT_ENEMY].Count}");

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

        /// <summary>
        /// Returns true if the player is in the field with no menus blocking.
        /// Used by gamepad nav to decide whether to activate the L1 overlay.
        /// </summary>
        private bool IsFieldFree()
        {
            try
            {
                bool hasFM = FieldManager.Instance != null;
                bool hasPlayer = hasFM && FieldManager.Instance.GetControlPlayer() != null;
                bool campOpen = CampMenuHandler.IsCampOpen;
                bool result = hasFM && hasPlayer && !campOpen;
                DebugLogger.LogState(
                    $"IsFieldFree: FM={hasFM} player={hasPlayer} campOpen={campOpen} => {result}");
                return result;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"IsFieldFree: exception: {ex.Message}");
                return false;
            }
        }

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
        /// Checks whether a complete NavMesh path exists between two world positions.
        /// Both positions are snapped to the nearest NavMesh surface within
        /// <see cref="NavMeshSampleRadius"/> before path calculation.
        /// Returns true as a fallback if NavMesh is unavailable (scene has none).
        /// </summary>
        private bool IsReachable(Vector3 playerPos, Vector3 targetPos)
        {
            try
            {
                if (!NavMesh.SamplePosition(playerPos, out NavMeshHit playerHit,
                        NavMeshSampleRadius, NavMesh.AllAreas))
                    return true; // no NavMesh near player — fallback, don't filter

                if (!NavMesh.SamplePosition(targetPos, out NavMeshHit targetHit,
                        NavMeshSampleRadius, NavMesh.AllAreas))
                {
                    DebugLogger.LogState(
                        $"NAV: IsReachable=false — target not on NavMesh " +
                        $"(target={targetPos}, sampleRadius={NavMeshSampleRadius})");
                    return false; // target not near any NavMesh surface — unreachable
                }

                NavMesh.CalculatePath(playerHit.position, targetHit.position,
                    NavMesh.AllAreas, _navPath);
                if (_navPath.status != NavMeshPathStatus.PathComplete)
                {
                    float directDist = Vector3.Distance(playerPos, targetPos);
                    DebugLogger.LogState(
                        $"NAV: IsReachable=false — path status={_navPath.status}, " +
                        $"directDist={directDist:F1}, " +
                        $"player={playerHit.position}, target={targetHit.position}");
                }
                return _navPath.status == NavMeshPathStatus.PathComplete;
            }
            catch (System.Exception ex)
            {
                DebugLogger.LogState($"NAV: IsReachable exception: {ex.Message}");
                // NavMesh API unavailable — preserve current behavior, don't filter.
                return true;
            }
        }

        /// <summary>
        /// Calculates a NavMesh path from the player to the target and populates
        /// <see cref="_pathCorners"/> and <see cref="_pathCornerIndex"/>.
        /// Returns true if a usable path was found.
        /// When <paramref name="allowPartial"/> is true, a partial path is accepted
        /// — the player walks as close as the NavMesh allows (e.g. to a counter).
        /// </summary>
        private bool CalculateAndStorePath(Vector3 playerPos, Vector3 targetPos,
            bool allowPartial = false)
        {
            if (!NavMesh.SamplePosition(playerPos, out NavMeshHit playerHit,
                    NavMeshSampleRadius, NavMesh.AllAreas))
                return false;

            if (!NavMesh.SamplePosition(targetPos, out NavMeshHit targetHit,
                    NavMeshSampleRadius, NavMesh.AllAreas))
                return false;

            NavMesh.CalculatePath(playerHit.position, targetHit.position,
                NavMesh.AllAreas, _navPath);

            if (_navPath.status == NavMeshPathStatus.PathInvalid)
                return false;

            if (_navPath.status == NavMeshPathStatus.PathPartial && !allowPartial)
                return false;

            // Copy corners from IL2CPP array into managed array.
            var il2cppCorners = _navPath.corners;
            _pathCorners = new Vector3[il2cppCorners.Length];
            for (int i = 0; i < il2cppCorners.Length; i++)
                _pathCorners[i] = il2cppCorners[i];

            // Start walking toward index 1 (index 0 is the start position).
            _pathCornerIndex = _pathCorners.Length > 1 ? 1 : 0;
            _pathRecalcTimer = 0f;

            DebugLogger.LogState(
                $"NAV path: {_pathCorners.Length} waypoints, status={_navPath.status}");
            return true;
        }

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
            if (MapNameOverrides.TryGetValue(destCode, out string overrideName))
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
        /// Harmony prefix for FieldBillboardObject.PlayMoveAnimation(FieldAnimationKind).
        /// During the auto-run approach phase, blocks any non-Run animation from being
        /// applied to the player. This prevents the game's internal state machine from
        /// resetting the player's Run animation to Idle every frame when no movement
        /// keys are held. Returns false (skip original) to block; true to allow.
        /// </summary>
        private static bool PlayMoveAnimation_Prefix(
            FieldBillboardObject __instance, FieldAnimationKind animationKind)
        {
            // Only intercept during the approach phase (not proximity-lock, not stopped).
            if (!_staticIsApproaching) return true;

            // Run is always allowed — let it set or re-set Run normally.
            if (animationKind == FieldAnimationKind.Run) return true;

            // Check if this FieldBillboardObject is the player character.
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null) return true;
                var player = fm.GetControlPlayer();
                if (player == null) return true;
                if (__instance.GetInstanceID() != player.GetInstanceID()) return true;

                // It is the player and we're in the approach phase — block the reset.
                return false;
            }
            catch
            {
                return true; // on any error, allow the call through
            }
        }

        /// <summary>
        /// Harmony prefix for GameInputManager.IsDown(InputAction).
        /// While gamepad nav overlay is active, blocks D-pad directions and
        /// FieldCameraLeft (L1 camera panning) so the game ignores them.
        /// </summary>
        private static bool IsDown_Prefix(
            GameInputManager.InputAction inputAction, ref bool __result)
        {
            if (!_gamepadNavActive) return true;

            // Block directional and shortcut actions while gamepad nav is active.
            // Up=11, Down=12, Right=13, Left=14 — basic D-pad movement
            // ShortCutUp=39, ShortCutDown=40, ShortCutLeft=41, ShortCutRight=42 — field shortcuts (Quick Heal etc.)
            // FieldCameraLeft=56 — L1 camera panning
            if (inputAction == GameInputManager.InputAction.Up ||
                inputAction == GameInputManager.InputAction.Down ||
                inputAction == GameInputManager.InputAction.Left ||
                inputAction == GameInputManager.InputAction.Right ||
                inputAction == GameInputManager.InputAction.ShortCutUp ||
                inputAction == GameInputManager.InputAction.ShortCutDown ||
                inputAction == GameInputManager.InputAction.ShortCutLeft ||
                inputAction == GameInputManager.InputAction.ShortCutRight ||
                inputAction == GameInputManager.InputAction.FieldCameraLeft)
            {
                __result = false;
                return false; // skip original
            }

            return true;
        }

        /// <summary>
        /// Harmony prefix for GameInputManager.IsRepeat(InputAction).
        /// Mirrors IsDown suppression so held D-pad doesn't auto-repeat in the game.
        /// </summary>
        private static bool IsRepeat_Prefix(
            GameInputManager.InputAction inputAction, ref bool __result)
        {
            if (!_gamepadNavActive) return true;

            if (inputAction == GameInputManager.InputAction.Up ||
                inputAction == GameInputManager.InputAction.Down ||
                inputAction == GameInputManager.InputAction.Left ||
                inputAction == GameInputManager.InputAction.Right ||
                inputAction == GameInputManager.InputAction.ShortCutUp ||
                inputAction == GameInputManager.InputAction.ShortCutDown ||
                inputAction == GameInputManager.InputAction.ShortCutLeft ||
                inputAction == GameInputManager.InputAction.ShortCutRight ||
                inputAction == GameInputManager.InputAction.FieldCameraLeft)
            {
                __result = false;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Harmony prefix for GameInputManager.GetDPad().
        /// Returns zero vector while gamepad nav is active so D-pad analog
        /// input doesn't move the player character.
        /// </summary>
        private static bool GetDPad_Prefix(ref Vector2 __result)
        {
            if (!_gamepadNavActive) return true;

            __result = Vector2.zero;
            return false;
        }


        /// <summary>
        /// Returns true for NPC types that are commonly placed behind counters
        /// or barriers (shops, inns, guilds). These NPCs should not be filtered
        /// by NavMesh reachability because the game allows interaction over
        /// the counter even though no walkable path exists.
        /// </summary>
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
                NpcType.CHECK          => "Info",
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
