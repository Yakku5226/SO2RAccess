using HarmonyLib;
using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SO2RAccess
{
    // Partial class fragment of NavigationHandler: the audio navigation LIST UI
    // (open/close, category + item cursor movement, announcements, and the
    // scan that populates the list). See NavigationHandler.cs for the auto-walk
    // engine and NavigationHandler.MapState.cs for map/floor/traversal tracking.
    public partial class NavigationHandler
    {
        #region Navigation List UI

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
                // Say WHICH walk this key press just killed — silently losing
                // the walk left the player thinking it was still running
                // (proven by the 2026-08-29 world-map fishing test).
                string label = _autoWalkLabel;
                CancelAutoWalk();
                ScreenReader.Say(Loc.Get("nav_autowalk_cancelled_menu", label));
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
                // Opening the menu cancels the walk as a SIDE EFFECT — say so,
                // or the player believes the walk is still running (proven by
                // the 2026-08-29 world-map fishing test: the walk was 23m from
                // its goal when a menu open silently killed it).
                DebugLogger.LogState("GamepadOpenNav: cancelling auto-walk first.");
                string label = _autoWalkLabel;
                CancelAutoWalk();
                ScreenReader.Say(Loc.Get("nav_autowalk_cancelled_menu", label));
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

                DebugLogger.LogState(
                    $"NAV scan start. map={mapID} worldmap={_isWorldmap} " +
                    $"playerPos=({playerPos.x:F1},{playerPos.y:F1},{playerPos.z:F1})");

                if (_isWorldmap)
                {
                    // World map: locations (from game data), fishing spots,
                    // nearby chests/enemies. Skip NPCs, exits, markers, events,
                    // save points, stairs, doors, warps — these are either
                    // absent or redundant with Locations.
                    BuildWorldmapLocations(playerPos, fm.WorldmapID);
                    BuildChests(playerPos);
                    BuildEnemies(playerPos);
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
                    BuildSavePoints(fm.FieldSavePointList, playerPos);
                    BuildFishingSpots(playerPos);
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
        #endregion
    }
}
