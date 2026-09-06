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
            if (!EnsureListReady(allowRefresh: userIdle, fromUser: false))
            {
                // A failed build is expensive; do not hammer it ten times a second.
                _beaconRetryAfter = Time.time + BeaconRetrySeconds;
                return false;
            }
            if (_isWorldmap) return true; // beacons are a field/dungeon/town feature

            AddCategory(into, CAT_NPC, NavCueKind.Npc);
            AddCategory(into, CAT_CHEST, NavCueKind.Chest);
            AddCategory(into, CAT_EXIT, NavCueKind.Door);
            AddCategory(into, CAT_DOOR, NavCueKind.Door);
            AddCategory(into, CAT_MARKER, NavCueKind.Location);
            AddCategory(into, CAT_SAVE, NavCueKind.Save);
            AddCategory(into, CAT_WARP, NavCueKind.Location);
            AddCategory(into, CAT_STAIRS, NavCueKind.Stairs);
            AddJumpLedges(into);
            return true;
        }

        private void AddCategory(List<BeaconTarget> into, int category, NavCueKind kind)
        {
            var items = _categories[category];
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Consumed) continue;

                into.Add(new BeaconTarget
                {
                    Kind = kind,
                    Position = item.Position,
                    Live = item.LiveTransform,
                    Label = item.Label,
                    Id = StableId(kind, item.Position, item.LiveTransform)
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
