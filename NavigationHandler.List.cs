using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SO2RAccess
{
    // Partial class fragment of NavigationHandler: the audio navigation LIST
    // (modeless keyboard cycling, gamepad hold-overlay, category + item cursor
    // movement, announcements, and the scan that populates the list). See
    // NavigationHandler.cs for the auto-walk engine and
    // NavigationHandler.MapState.cs for map/floor/traversal tracking.
    public partial class NavigationHandler
    {
        #region Navigation List UI

        /// <summary>Map the background list was built for (staleness check).</summary>
        private FieldmapID _listBuiltMapID = FieldmapID.INVALID;

        /// <summary>Time.time when the background list was last built.</summary>
        private float _listBuiltTime;

        /// <summary>
        /// Age at which a category-cycle key rebuilds the background list.
        /// Item keys never refresh, so item order stays stable while cycling.
        /// </summary>
        private const float ListRefreshSeconds = 10f;

        /// <summary>When a modeless nav key last used the list (Time.time); see EnsureListReady.</summary>
        private float _lastNavKeyTime = -1000f;

        /// <summary>
        /// Modeless: previous category. Rebuilds the list first when stale.
        /// Returns true if the key press was acted on.
        /// </summary>
        public bool ModelessCategoryPrev()
        {
            if (!EnsureListReady(allowRefresh: true)) return false;
            NavCategoryPrev();
            return true;
        }

        /// <summary>Modeless: next category. See <see cref="ModelessCategoryPrev"/>.</summary>
        public bool ModelessCategoryNext()
        {
            if (!EnsureListReady(allowRefresh: true)) return false;
            NavCategoryNext();
            return true;
        }

        /// <summary>
        /// Modeless: previous item in the current category. Never refreshes a
        /// live list — item order stays stable while the user cycles.
        /// </summary>
        public bool ModelessItemPrev()
        {
            if (!EnsureListReady(allowRefresh: false)) return false;
            NavUp();
            return true;
        }

        /// <summary>Modeless: next item. See <see cref="ModelessItemPrev"/>.</summary>
        public bool ModelessItemNext()
        {
            if (!EnsureListReady(allowRefresh: false)) return false;
            NavDown();
            return true;
        }

        /// <summary>
        /// Modeless: starts an auto-walk to the selected item, or cancels the
        /// active walk (with speech — a deliberate cancel must never be silent).
        /// Spoken directions are cancelled the same way: the walk key is the
        /// player's one "stop guiding me" key, whichever aid is running.
        /// </summary>
        public bool ModelessAutoWalkToggle()
        {
            if (_guideActive && !_isAutoWalking)
            {
                CancelGuidanceSpoken();
                return true;
            }
            if (_isAutoWalking)
            {
                string label = _autoWalkLabel;
                CancelAutoWalk();
                ScreenReader.Say(Loc.Get("nav_autowalk_cancelled", label));
                return true;
            }
            if (!EnsureListReady(allowRefresh: false)) return false;
            AutoWalkTo();
            return true;
        }

        /// <summary>
        /// Keyboard twin of the gamepad "L2 + left stick down" gesture: spoken
        /// directions to the selected item. Same rules as <see cref="GuideTo"/>:
        /// a second press on the same item stops, a press on another item
        /// switches. Returns false (key passes through to the game) when the
        /// field is not free.
        /// </summary>
        public bool ModelessGuideToggle()
        {
            if (!EnsureListReady(allowRefresh: false)) return false;
            GuideTo();
            return true;
        }

        /// <summary>
        /// Guarantees the background list is built and current before a modeless
        /// key acts. Rebuilds when the list is absent, built for another map, or
        /// (category keys only) older than <see cref="ListRefreshSeconds"/>.
        /// A rebuild preserves the selected category when it still has items.
        /// Returns false — silently, so the key passes through to the game —
        /// when the field is not free (menus, dialogue, battle).
        /// </summary>
        /// <param name="allowRefresh">Rebuild when older than <see cref="ListRefreshSeconds"/>.</param>
        /// <param name="fromUser">
        /// True for the modeless keys (the default). The beacon system passes false
        /// and refreshes only when the user has not touched the list for a while, so
        /// item order never changes under a cursor that is being cycled.
        /// </param>
        private bool EnsureListReady(bool allowRefresh, bool fromUser = true)
        {
            if (!IsFieldFree()) return false;
            if (fromUser) _lastNavKeyTime = Time.time;

            FieldmapID currentMap = FieldmapID.INVALID;
            try
            {
                var fm = FieldManager.Instance;
                if (fm != null) currentMap = fm.currentFieldmapID;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV EnsureListReady: map read failed ({ex.Message})");
            }

            // The beacons read the list whether it is open or closed: a closed
            // list on the same map is fresh until it ages out (closing used to
            // force an immediate full rebuild, two 200 ms stalls within half a
            // second in the 2026-09-06 Krosse log). The user's keys keep the
            // old rule — a closed list rebuilds so its cursor starts fresh.
            float age = Time.time - _listBuiltTime;
            bool stale = currentMap != _listBuiltMapID
                || !AnyCategoryPopulated()
                || (fromUser
                    ? (!_isOpen || (allowRefresh && age > ListRefreshSeconds))
                    : age > ListRefreshSeconds);
            if (!stale) return true;

            DebugLogger.LogState(
                $"NAV modeless rebuild: open={_isOpen} builtMap={_listBuiltMapID} " +
                $"currentMap={currentMap} age={Time.time - _listBuiltTime:F1}s");

            int previousCategory = _currentCategoryIndex;
            if (!BuildList())
            {
                _isOpen = false;
                return false;
            }
            _isOpen = true;

            // Keep the user's category when it survived the rebuild; clamp the
            // item cursor if the category shrank.
            _currentCategoryIndex = _categories[previousCategory].Count > 0
                ? previousCategory
                : FirstNonEmptyCategoryFrom(0);
            if (_currentItemIndex >= _categories[_currentCategoryIndex].Count)
                _currentItemIndex = 0;
            return true;
        }

        /// <summary>
        /// Drops the background list (called on map change — its items belong to
        /// the old map). The next modeless key rebuilds. Leaves the gamepad
        /// overlay alone; it manages its own lifecycle via open/close.
        /// </summary>
        public void InvalidateNavList()
        {
            if (_gamepadNavActive) return;
            _isOpen = false;
            for (int i = 0; i < CAT_COUNT; i++) _categories[i].Clear();
        }

        /// <summary>
        /// Opens the navigation list for gamepad use (L2 modifier pressed).
        /// Cancels auto-walk if active, checks field is free, scans, opens.
        /// Sets <see cref="_gamepadNavActive"/> to enable input suppression.
        /// </summary>
        public void GamepadOpenNav()
        {
            DebugLogger.LogState("GamepadOpenNav called.");

            if (_isAutoWalking)
            {
                // Opening the menu cancels the walk as a SIDE EFFECT — say so,
                // or the player believes the walk is still running (proven by
                // the 2026-08-29 world-map fishing test: the walk was 23m from
                // its goal when a menu open silently killed it).
                DebugLogger.LogState("GamepadOpenNav: cancelling auto-walk first.");
                string label = _autoWalkLabel;
                CancelAutoWalk();
                ScreenReader.Say(Loc.Get("nav_autowalk_cancelled", label));
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
                // Refresh which game actions the held modifier (L2) must block —
                // the player can rebind pad buttons in the game config.
                RebuildModifierSuppressSet();
                _gamepadNavActive = true;
                DebugLogger.LogState("GamepadOpenNav: list opened, _gamepadNavActive=true.");
            }
            else
            {
                DebugLogger.LogState("GamepadOpenNav: ScanAndOpenList did not open the list.");
            }
        }

        /// <summary>
        /// Closes the navigation list for gamepad use (L2 modifier released).
        /// Closes silently (no "closed" announcement) and disables input suppression.
        /// Category and item indices persist so the user can quickly reopen.
        /// </summary>
        public void GamepadCloseNav()
        {
            DebugLogger.LogState($"GamepadCloseNav called. _isOpen={_isOpen} _gamepadNavActive={_gamepadNavActive}");
            _gamepadNavActive = false;

            // The items are deliberately KEPT: every user path rebuilds on the
            // next open anyway (GamepadOpenNav scans, the modeless keys treat a
            // closed list as stale), while the beacons keep reading the closed
            // list instead of forcing a full rebuild the moment L2 is released
            // (a 370 ms stutter on the world map, log 2026-09-06).
            _isOpen = false;
            // No announcement — user knows they released the modifier.
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

        /// <summary>
        /// Moves to the next non-empty category and announces it. With only one
        /// non-empty category the current one is re-announced — a press must
        /// always produce audible feedback.
        /// </summary>
        public void NavCategoryNext()
        {
            if (!_isOpen) return;
            int next = FirstNonEmptyCategoryFrom(_currentCategoryIndex + 1);
            if (next != _currentCategoryIndex)
            {
                _currentCategoryIndex = next;
                _currentItemIndex     = 0;
            }
            AnnounceCategory();
        }

        /// <summary>Moves to the previous non-empty category and announces it.</summary>
        public void NavCategoryPrev()
        {
            if (!_isOpen) return;
            int prev = LastNonEmptyCategoryBefore(_currentCategoryIndex);
            if (prev != _currentCategoryIndex)
            {
                _currentCategoryIndex = prev;
                _currentItemIndex     = 0;
            }
            AnnounceCategory();
        }

        /// <summary>Announces the current item as "[label], [distance] units."</summary>
        private void AnnounceCurrentItem()
        {
            var cat = _categories[_currentCategoryIndex];
            if (cat.Count == 0 || _currentItemIndex >= cat.Count) return;
            var item = cat[_currentItemIndex];
            ScreenReader.Say(Loc.Get("nav_item", item.Label, LiveDistanceUnits(item)));
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
                catName, item.Label, LiveDistanceUnits(item)));
        }

        /// <summary>
        /// Distance to an item measured from the player's CURRENT position.
        /// The background list can be minutes old, so the distance captured at
        /// build time misleads; falls back to it when live data is unavailable.
        /// </summary>
        private int LiveDistanceUnits(NavItem item)
        {
            try
            {
                var player = FieldManager.Instance?.GetControlPlayer();
                if (player != null)
                {
                    Vector3 target = item.LiveTransform != null
                        ? item.LiveTransform.position
                        : item.Position;
                    return DistanceUnits(
                        Vector3.Distance(player.transform.position, target));
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV live distance failed: {ex.Message}");
            }
            return DistanceUnits(item.Distance);
        }

        /// <summary>
        /// Scans the field and opens the navigation list with an announcement —
        /// the gamepad hold-overlay entry path. (Modeless keyboard keys rebuild
        /// silently via EnsureListReady instead.)
        /// </summary>
        private void ScanAndOpenList()
        {
            if (!TryReuseWorldmapList() && !BuildList()) return;

            _isOpen = true;
            _currentCategoryIndex = FirstNonEmptyCategoryFrom(0);
            _currentItemIndex     = 0;

            var firstItem = _categories[_currentCategoryIndex][0];
            ScreenReader.Say(Loc.Get("nav_open",
                _categoryNames[_currentCategoryIndex],
                firstItem.Label,
                LiveDistanceUnits(firstItem)));
        }

        /// <summary>True when any category holds an item (the list has been built for some map).</summary>
        private bool AnyCategoryPopulated()
        {
            for (int i = 0; i < CAT_COUNT; i++)
                if (_categories[i].Count > 0) return true;
            return false;
        }

        /// <summary>
        /// A world map list younger than this (s) is reopened without the full
        /// scan; only the things that change (chests, enemies, landmarks) are
        /// rescanned. Towns, dungeons and fishing spots come from game data and
        /// never move, yet they are what the full scan spends its ~400 ms on.
        /// </summary>
        private const float WorldmapListReuseSeconds = 60f;

        /// <summary>
        /// Reopens a fresh world map list without the full scan (three L2
        /// presses cost three 400 ms stalls in the 2026-09-06 log). Returns
        /// false when a full build is needed: not the world map, another map,
        /// nothing built yet, or the list is older than
        /// <see cref="WorldmapListReuseSeconds"/>.
        /// </summary>
        private bool TryReuseWorldmapList()
        {
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null || !fm.IsWorldmap()) return false;
                if (fm.currentFieldmapID != _listBuiltMapID) return false;
                if (_categories[CAT_LOCATION].Count == 0) return false;
                if (Time.time - _listBuiltTime > WorldmapListReuseSeconds) return false;

                var player = fm.GetControlPlayer();
                if (player == null) return false;
                Vector3 playerPos = player.transform.position;

                var sw = System.Diagnostics.Stopwatch.StartNew();
                _isWorldmap = true;
                BuildChests(playerPos);
                BuildEnemies(playerPos);
                BuildMarkers(fm.FieldLocationPointList, playerPos);
                DebugLogger.LogState(
                    $"NAV list reused (world map, age {Time.time - _listBuiltTime:F0}s): chests={_categories[CAT_CHEST].Count} " +
                    $"enemies={_categories[CAT_ENEMY].Count} markers={_categories[CAT_MARKER].Count} " +
                    $"rescanned in {sw.ElapsedMilliseconds} ms");
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV list reuse failed ({ex.Message}) — full scan instead.");
                return false;
            }
        }

        /// <summary>
        /// Scans the field and populates the category lists. Speaks only on
        /// failure ("not on a field" / "no items"); success is silent so both
        /// the gamepad open and the modeless rebuild can choose their own
        /// announcements. Records which map and when the list was built.
        /// Returns true when at least one item was found.
        /// </summary>
        private bool BuildList()
        {
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null)
                {
                    ScreenReader.Say(Loc.Get("nav_not_in_field"));
                    return false;
                }

                var player = fm.GetControlPlayer();
                if (player == null)
                {
                    ScreenReader.Say(Loc.Get("nav_not_in_field"));
                    return false;
                }

                Vector3    playerPos = player.transform.position;
                FieldmapID mapID     = fm.currentFieldmapID;
                _isWorldmap = fm.IsWorldmap();
                var sw = System.Diagnostics.Stopwatch.StartNew();

                // Forget the stable numbers when entering a new map — numbers
                // restart from 1 in distance order there. A battle keeps the
                // map ID, so numbers survive fights.
                if (_listBuiltMapID != mapID)
                {
                    _stableNumbers.Clear();
                    DebugLogger.LogState($"NAV numbering: reset for new map {mapID}.");
                }

                // Start from nothing. The builders each clear their own category
                // EXCEPT Interactables (fishing spots and interactable objects are
                // appended by two builders); closing the list used to clear it
                // for them, and once the list was kept across closes (2026-09-06)
                // the interactables piled up on every rebuild — on fields too.
                for (int i = 0; i < CAT_COUNT; i++) _categories[i].Clear();

                DebugLogger.LogState(
                    $"NAV scan start. map={mapID} worldmap={_isWorldmap} " +
                    $"playerPos=({playerPos.x:F1},{playerPos.y:F1},{playerPos.z:F1})");

                if (_isWorldmap)
                {
                    // World map: locations (from game data), fishing spots,
                    // nearby chests/enemies, and the undiscovered landmarks the
                    // game places there (six on Expel). Skip NPCs, exits, events,
                    // save points, stairs, doors, warps — these are either
                    // absent or redundant with Locations.
                    BuildWorldmapLocations(playerPos, fm.WorldmapID);
                    BuildChests(playerPos);
                    BuildEnemies(playerPos);
                    BuildMarkers(fm.FieldLocationPointList, playerPos);
                    BuildFishingSpots(playerPos);
                    LogWorldmapObjectSurvey(fm);
                }
                else
                {
                    // Field map: full scan as before.
                    BuildNpcs(playerPos, mapID);
                    BuildChests(playerPos);
                    BuildExits(playerPos);
                    BuildMarkers(fm.FieldLocationPointList, playerPos);
                    BuildEvents(playerPos);
                    LogSubEventTable(mapID); // debug-only: all stages + unlock conditions
                    BuildSavePoints(fm.FieldSavePointList, playerPos);
                    BuildFishingSpots(playerPos);
                    BuildEnemies(playerPos);
                    BuildStairs(fm.FieldStairsList, playerPos);
                    BuildDoors(fm.FieldDoorList, playerPos);
                    // One scan of the game's gimmick list feeds the climb points
                    // and every other interactable kind (Interactables, Stairs,
                    // Warp Points, Doors) — after the builders that clear those
                    // categories.
                    var gimmickHits = CollectGimmickHits(fm);
                    BuildClimbPoints(gimmickHits, mapID, playerPos);
                    BuildGimmicks(gimmickHits, playerPos);
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
                    $"locations={_categories[CAT_LOCATION].Count} " +
                    $"in {sw.ElapsedMilliseconds} ms");

                _listBuiltMapID = mapID;
                _listBuiltTime  = Time.time;

                if (totalItems == 0)
                {
                    ScreenReader.Say(Loc.Get("nav_no_items"));
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"NavigationHandler.BuildList: {ex.Message}");
                return false;
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
        #endregion
    }
}
