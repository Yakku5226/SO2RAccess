using Il2CppGame;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    public partial class NavigationHandler
    {
        #region Climb Points

        /// <summary>
        /// Two climb-point targets closer than this are the same ladder seen twice
        /// (live gimmick object vs. placement-table entry).
        /// </summary>
        private const float ClimbDedupeRadius = 2.0f;

        /// <summary>
        /// One climbable spot. <see cref="Contact"/> is where the player must walk
        /// (the gimmick's collision); <see cref="Low"/>/<see cref="High"/> are the two
        /// ends of the climb when known, otherwise both equal <see cref="Contact"/>.
        /// </summary>
        private struct ClimbCandidate
        {
            public Vector3 Contact;
            public Vector3 Low;
            public Vector3 High;
            public bool    EndsKnown;
            public string  Source;
        }

        /// <summary>
        /// Scans for climb points (ladders, ivy walls, cliff scrambles) on the current
        /// field map and appends them to the Stairs category.
        ///
        /// The game's climb is FieldGimmick03: a contact gimmick with a start and an end
        /// position. Walking into its collision puts the player into the Ladder
        /// character state (FieldCharacterLadderBaseTask holds a fieldGimmick03
        /// reference), which carries them to the other end. Two sources are read:
        ///  - FieldGimmickManager.FieldGimmickList — the live objects (authoritative
        ///    start/end positions).
        ///  - ParameterManager.GetGimmick03ParameterList(map) — the static placement
        ///    table, so a climb point is listed even before its object is spawned.
        /// Entries within <see cref="ClimbDedupeRadius"/> of each other count once.
        /// Labels: "Climb up" / "Climb down" by where the far end lies relative to
        /// the player's floor, "Climb point" when the ends are unknown.
        /// Must run AFTER BuildStairs, which clears the category.
        /// </summary>
        private void BuildClimbPoints(FieldManager fm, FieldmapID mapID, Vector3 playerPos)
        {
            var candidates = new List<ClimbCandidate>();

            CollectClimbPointsFromScene(fm, candidates);
            CollectClimbPointsFromTable(mapID, candidates);

            if (candidates.Count == 0)
            {
                DebugLogger.LogState($"NAV:CLIMB no climb points on map {mapID}.");
                return;
            }

            var items = new List<NavItem>();
            foreach (var c in candidates)
            {
                string key = ClimbLabelKey(c, playerPos);
                float  dist = Vector3.Distance(playerPos, c.Contact);

                items.Add(new NavItem
                {
                    Label         = Loc.Get(key),
                    Distance      = dist,
                    Position      = c.Contact,
                    LiveTransform = null,
                });

                DebugLogger.LogGameValue("NAV:CLIMB",
                    $"{key} source={c.Source} dist={dist:F1} " +
                    $"contact=({c.Contact.x:F1},{c.Contact.y:F1},{c.Contact.z:F1}) " +
                    (c.EndsKnown
                        ? $"low=({c.Low.x:F1},{c.Low.y:F1},{c.Low.z:F1}) high=({c.High.x:F1},{c.High.y:F1},{c.High.z:F1})"
                        : "ends=unknown"));
            }

            // Number duplicates by distance BEFORE the floor suffix is added, so the
            // label comparison still matches the plain key text.
            items.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            NumberDuplicateLabels(items, "nav_climb_up",   "nav_climb_up_n");
            NumberDuplicateLabels(items, "nav_climb_down", "nav_climb_down_n");
            NumberDuplicateLabels(items, "nav_climb",      "nav_climb_n");

            SortAndFilterUnreachable(items, playerPos);
            _categories[CAT_STAIRS].AddRange(items);
        }

        /// <summary>
        /// Picks the label key for a climb point: the far end is the one further from
        /// the player's height; "up" if it is higher than the near end, "down" if lower.
        /// </summary>
        private static string ClimbLabelKey(ClimbCandidate c, Vector3 playerPos)
        {
            if (!c.EndsKnown) return "nav_climb";

            float span = c.High.y - c.Low.y;
            if (span < FloorChangeThreshold) return "nav_climb";

            bool nearLow = Mathf.Abs(c.Low.y - playerPos.y) <= Mathf.Abs(c.High.y - playerPos.y);
            return nearLow ? "nav_climb_up" : "nav_climb_down";
        }

        /// <summary>
        /// Live FieldGimmick03 objects from the gimmick manager. Also logs every
        /// gimmick's type name so a map that climbs through some other gimmick
        /// shows up in the log instead of silently listing nothing.
        /// </summary>
        private void CollectClimbPointsFromScene(FieldManager fm, List<ClimbCandidate> candidates)
        {
            try
            {
                var gimmickMgr = fm.FieldGimmickManager;
                var list = gimmickMgr?.FieldGimmickList;
                if (list == null)
                {
                    DebugLogger.LogState("NAV:CLIMB gimmick list is null.");
                    return;
                }

                DebugLogger.LogState($"NAV:CLIMB gimmick list has {list.Count} entries.");
                for (int i = 0; i < list.Count; i++)
                {
                    var gimmick = list[i];
                    if (gimmick == null) continue;

                    string typeName = gimmick.GetIl2CppType()?.Name ?? "unknown";
                    Vector3 pos = gimmick.transform.position;
                    DebugLogger.LogState(
                        $"NAV:CLIMB gimmick[{i}] {typeName} at ({pos.x:F1},{pos.y:F1},{pos.z:F1})");

                    var ladder = gimmick.TryCast<FieldGimmick03>();
                    if (ladder == null) continue;

                    Vector3 start = ladder.StartPosition;
                    Vector3 end   = ladder.EndPosition;
                    string startup = "?";
                    try { startup = ladder.GetGimmickStartupType().ToString(); }
                    catch (Exception ex) { startup = "error: " + ex.Message; }
                    DebugLogger.LogState($"NAV:CLIMB ladder startup={startup}");

                    AddClimbCandidate(candidates, new ClimbCandidate
                    {
                        Contact   = pos,
                        Low       = start.y <= end.y ? start : end,
                        High      = start.y <= end.y ? end : start,
                        EndsKnown = true,
                        Source    = "scene",
                    });
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV:CLIMB scene scan failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Static placement table for the map. Each ConstGimmick03Parameter carries
        /// parallel lists: ColPosition/ColDirection/ColSize (one per contact collision)
        /// and Position (the climb's end points). Every collision becomes a target;
        /// the direction is read from the Position list when it brackets the
        /// collision's height. The raw lists are logged because their exact pairing
        /// is not documented — see the log before trusting an "up"/"down" here.
        /// </summary>
        private void CollectClimbPointsFromTable(FieldmapID mapID, List<ClimbCandidate> candidates)
        {
            try
            {
                var table = ParameterManager.Instance?.GetGimmick03ParameterList(mapID);
                if (table == null)
                {
                    DebugLogger.LogState($"NAV:CLIMB table for {mapID} is null.");
                    return;
                }

                DebugLogger.LogState($"NAV:CLIMB table for {mapID}: {table.Count} entries.");
                for (int i = 0; i < table.Count; i++)
                {
                    var entry = table[i];
                    if (entry == null) continue;

                    var cols = entry.ColPosition;
                    var ends = entry.Position;
                    DebugLogger.LogState(
                        $"NAV:CLIMB table[{i}] cols={FormatVectors(cols)} ends={FormatVectors(ends)}");
                    if (cols == null) continue;

                    for (int k = 0; k < cols.Count; k++)
                    {
                        Vector3 contact = cols[k];
                        var candidate = new ClimbCandidate
                        {
                            Contact   = contact,
                            Low       = contact,
                            High      = contact,
                            EndsKnown = false,
                            Source    = "table",
                        };

                        // Highest and lowest end points bracket the climb.
                        if (ends != null && ends.Count > 0)
                        {
                            Vector3 low = ends[0], high = ends[0];
                            for (int e = 1; e < ends.Count; e++)
                            {
                                if (ends[e].y < low.y)  low  = ends[e];
                                if (ends[e].y > high.y) high = ends[e];
                            }
                            candidate.Low       = low;
                            candidate.High      = high;
                            candidate.EndsKnown = true;
                        }

                        AddClimbCandidate(candidates, candidate);
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"NAV:CLIMB table lookup failed: {ex.Message}");
            }
        }

        /// <summary>Adds a candidate unless one already sits within the dedupe radius.</summary>
        private static void AddClimbCandidate(List<ClimbCandidate> candidates, ClimbCandidate c)
        {
            bool duplicate = candidates.Exists(
                existing => Vector3.Distance(existing.Contact, c.Contact) < ClimbDedupeRadius);
            if (duplicate)
            {
                DebugLogger.LogState(
                    $"NAV:CLIMB skipped duplicate {c.Source} entry at ({c.Contact.x:F1},{c.Contact.y:F1},{c.Contact.z:F1})");
                return;
            }
            candidates.Add(c);
        }

        /// <summary>Formats an IL2CPP vector list for the log.</summary>
        private static string FormatVectors(Il2CppSystem.Collections.Generic.List<Vector3> list)
        {
            if (list == null) return "null";
            var parts = new List<string>(list.Count);
            for (int i = 0; i < list.Count; i++)
                parts.Add($"({list[i].x:F1},{list[i].y:F1},{list[i].z:F1})");
            return "[" + string.Join(" ", parts) + "]";
        }

        /// <summary>
        /// When more than one item carries the plain label for <paramref name="key"/>,
        /// relabels them "label 1", "label 2"... in list order using <paramref name="nKey"/>.
        /// </summary>
        private static void NumberDuplicateLabels(List<NavItem> items, string key, string nKey)
        {
            string plain = Loc.Get(key);
            int count = items.FindAll(it => it.Label == plain).Count;
            if (count < 2) return;

            int n = 1;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Label != plain) continue;
                var item = items[i];
                item.Label = Loc.Get(nKey, n++);
                items[i] = item;
            }
        }

        #endregion
    }
}
