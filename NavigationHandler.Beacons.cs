using Il2CppGame;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    public partial class NavigationHandler
    {
        #region Beacon targets (read by ManualNavHandler)

        /// <summary>Learned jump ledges closer than this (m) are one beacon.</summary>
        private const float JumpDedupeRadius = 2f;

        /// <summary>Pause (s) before beacons retry after the list could not be built.</summary>
        private const float BeaconRetrySeconds = 2f;
        private float _beaconRetryAfter;

        /// <summary>Seconds between chest rescans on the world map (the only beacon kind that changes there).</summary>
        private const float WorldmapChestRefreshSeconds = 10f;
        private float _wmChestRefreshTime = -1000f;

        /// <summary>One object the beacon system may sound for.</summary>
        internal struct BeaconTarget
        {
            public NavCueKind Kind;
            /// <summary>World position; for moving objects, the live one at build time.</summary>
            public Vector3 Position;
            /// <summary>Live transform for objects that move (NPCs), else null.</summary>
            public Transform Live;
            public string Label;
            /// <summary>Stable identity across list rebuilds, so a voice can follow its object.</summary>
            public int Id;
        }

        /// <summary>
        /// Fills <paramref name="into"/> with everything the navigation list knows
        /// about, mapped to beacon kinds, so "what you hear" is always "what the list
        /// says": the same discovered / opened / reachable filters apply. Uses the
        /// list's own silent rebuild (map change, or 10 s old — but never while the
        /// user is cycling it). Learned jump-down ledges come from the breadcrumb
        /// graph. Returns false when the field is not free.
        /// </summary>
        internal bool TryGetBeaconTargets(List<BeaconTarget> into)
        {
            into.Clear();
            if (Time.time < _beaconRetryAfter) return false;

            bool userIdle = Time.time - _lastNavKeyTime > ListRefreshSeconds;
            if (!TryRefreshWorldmapBeaconList(userIdle)
                && !EnsureListReady(allowRefresh: userIdle, fromUser: false))
            {
                // A failed build is expensive; do not hammer it ten times a second.
                _beaconRetryAfter = Time.time + BeaconRetrySeconds;
                return false;
            }
            if (_isWorldmap)
            {
                // World map (2026-09-06): towns and dungeons each have their own
                // cue; landmarks share the location shimmer; fishing spots sound
                // from the water. Enemies stay with the enemy proximity loop.
                AddCategory(into, CAT_LOCATION, item => item.IsDungeon ? NavCueKind.Dungeon : NavCueKind.City);
                AddCategory(into, CAT_CHEST, NavCueKind.Chest);
                AddCategory(into, CAT_MARKER, NavCueKind.Location);
                AddFishingSpots(into);
                return true;
            }

            AddCategory(into, CAT_NPC, NavCueKind.Npc);
            AddCategory(into, CAT_CHEST, NavCueKind.Chest);
            AddCategory(into, CAT_EXIT, NavCueKind.Door);
            AddCategory(into, CAT_DOOR, NavCueKind.Door);
            AddCategory(into, CAT_MARKER, NavCueKind.Location);
            AddCategory(into, CAT_SAVE, NavCueKind.Save);
            AddCategory(into, CAT_WARP, NavCueKind.Location);
            AddCategory(into, CAT_STAIRS, NavCueKind.Stairs);
            AddFishingSpots(into);
            AddJumpLedges(into);
            return true;
        }

        /// <summary>
        /// World map: a full list build costs ~370 ms (40 fishing spots snapped to
        /// the grid, 20 towns tested for reachability — log 2026-09-06) and none of
        /// that changes while the player walks. So once the list exists for this
        /// map it is KEPT (closing the menu no longer clears it) and only the
        /// chests are rescanned every <see cref="WorldmapChestRefreshSeconds"/>:
        /// opened ones fall silent, ones coming into the 200 m cap appear. Returns
        /// false when a full build is still needed (first visit, map change).
        /// </summary>
        private bool TryRefreshWorldmapBeaconList(bool userIdle)
        {
            FieldmapID current;
            try
            {
                var fm = FieldManager.Instance;
                if (fm == null || !fm.IsWorldmap()) return false;
                current = fm.currentFieldmapID;
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV WM beacon refresh: map read failed ({ex.Message})");
                return false;
            }
            if (current != _listBuiltMapID || _categories[CAT_LOCATION].Count == 0) return false;

            // The flag is cleared by CancelAutoWalk; the chest scan needs it.
            _isWorldmap = true;

            if (!userIdle || Time.time - _wmChestRefreshTime < WorldmapChestRefreshSeconds) return true;
            _wmChestRefreshTime = Time.time;

            if (!TryGetPlayerPosition(out Vector3 playerPos)) return true;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                BuildChests(playerPos);
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV WM beacon refresh: chest scan failed ({ex.Message})");
            }
            DebugLogger.LogState(
                $"NAV WM beacon refresh: chests={_categories[CAT_CHEST].Count} in {sw.ElapsedMilliseconds} ms " +
                "(rest of the list kept)");
            return true;
        }

        private void AddCategory(List<BeaconTarget> into, int category, NavCueKind kind) =>
            AddCategory(into, category, _ => kind);

        /// <summary>
        /// Adds every unconsumed item of a category, letting <paramref name="kindOf"/>
        /// pick the cue per item (a category can mix kinds, e.g. cities and dungeons);
        /// returning null skips the item.
        /// </summary>
        private void AddCategory(List<BeaconTarget> into, int category, Func<NavItem, NavCueKind?> kindOf)
        {
            var items = _categories[category];
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Consumed) continue;
                NavCueKind? kind = kindOf(item);
                if (kind == null) continue;

                into.Add(new BeaconTarget
                {
                    Kind = kind.Value,
                    Position = item.Position,
                    Live = item.LiveTransform,
                    Label = item.Label,
                    Id = StableId(kind.Value, item.Position, item.LiveTransform)
                });
            }
        }

        /// <summary>
        /// Fishing spots sound from the shore point the list would walk to. (The
        /// water centre was tried first, but a world map lake is hundreds of
        /// metres across and its centre is nowhere a player can fish from.)
        /// Only fishing items of the Interactables category qualify. Unreachable
        /// spots (all stands in disconnected regions) are excluded.
        /// </summary>
        private void AddFishingSpots(List<BeaconTarget> into)
        {
            var items = _categories[CAT_INTERACTABLE];
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (!item.IsFishing) continue;
                if (item.Unreachable) continue;

                Vector3 pos = item.Position;
                into.Add(new BeaconTarget
                {
                    Kind = NavCueKind.Fishing,
                    Position = pos,
                    Label = item.Label,
                    Id = StableId(NavCueKind.Fishing, pos, null)
                });
            }
        }

        /// <summary>
        /// The high end of every one-way drop the player has ever jumped on this map.
        /// The game has no authored ledge list, so these exist only once walked.
        /// </summary>
        private void AddJumpLedges(List<BeaconTarget> into)
        {
            if (_traversal == null || !_traversal.HasData) return;

            var nodes = _traversal.Nodes;
            var placed = new List<Vector3>();
            foreach (var (high, _) in _traversal.OneWayDrops)
            {
                Vector3 p = nodes[high];
                bool dup = false;
                for (int i = 0; i < placed.Count; i++)
                    if (Vector3.Distance(placed[i], p) < JumpDedupeRadius) { dup = true; break; }
                if (dup) continue;

                placed.Add(p);
                into.Add(new BeaconTarget
                {
                    Kind = NavCueKind.Jump,
                    Position = p,
                    Label = Loc.Get("nav_jump_ledge"),
                    Id = StableId(NavCueKind.Jump, p, null)
                });
            }
        }

        /// <summary>
        /// Identity that survives a list rebuild: the live object's instance id when
        /// there is one, otherwise the kind plus the position rounded to half a metre.
        /// </summary>
        private static int StableId(NavCueKind kind, Vector3 pos, Transform live)
        {
            if (live != null)
            {
                try { return HashCode.Combine((int)kind, live.GetInstanceID()); }
                catch { /* destroyed — fall through to the position */ }
            }
            return HashCode.Combine((int)kind,
                Mathf.RoundToInt(pos.x * 2f), Mathf.RoundToInt(pos.y * 2f), Mathf.RoundToInt(pos.z * 2f));
        }

        #endregion
    }
}
