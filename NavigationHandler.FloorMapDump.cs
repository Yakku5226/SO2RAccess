using Il2CppGame;
using MelonLoader;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    public partial class NavigationHandler
    {
        #region Floor map dump (F11, debug mode, field maps)

        /// <summary>Raster step of the text floor map (m per character).</summary>
        private const float FloorMapCell = 0.75f;
        /// <summary>A wall face this close to a cell centre paints the cell as wall.</summary>
        private const float FloorMapWallRadius = 0.45f;
        /// <summary>Largest map drawn, in characters per side. Towns are skipped.</summary>
        private const int FloorMapMaxCells = 80;
        /// <summary>Floors this far above or below the player's level are ignored.</summary>
        private const float FloorMapLevelWindow = 3f;
        /// <summary>Most solid colliders listed one per line after the map.</summary>
        private const int FloorMapMaxColliders = 300;

        /// <summary>
        /// Draws the room around the player as text, one row per 0.75 m, north
        /// (+z) at the top and −x on the left, from the same rays the wall tones
        /// use: a cell is '#' when a wall face stands within
        /// <see cref="FloorMapWallRadius"/> of its centre at knee height, otherwise
        /// it shows the floor height relative to the player ('.' same level, ','
        /// lower, ':' a step, 'o' waist high) or ' ' for no floor. Navigation items
        /// are marked with digits and letters, the player with 'P'. Then every
        /// solid wall collider is listed with its bounds. The floor grid cannot
        /// draw this: thin obstacle boxes fall between its 1.5 m downward rays.
        /// Log-only; nothing walks on it.
        /// </summary>
        private void LogFloorMap(Vector3 playerPos)
        {
            var (marks, legend) = CollectFloorMapMarks();

            // Extent: the breadcrumbs, the nav items and the player, padded.
            var b = new Bounds(playerPos, Vector3.zero);
            foreach (var (_, pos) in marks) b.Encapsulate(pos);
            if (_traversal.HasData)
                foreach (var n in _traversal.Nodes) b.Encapsulate(n);
            b.Expand(new Vector3(6f, 0f, 6f));

            int w = Mathf.CeilToInt(b.size.x / FloorMapCell) + 1;
            int h = Mathf.CeilToInt(b.size.z / FloorMapCell) + 1;
            if (w > FloorMapMaxCells || h > FloorMapMaxCells)
            {
                MelonLogger.Msg($"[SO2RAccess] [FLOORMAP] area {b.size.x:F0} x {b.size.z:F0} m is too large to draw, skipped.");
                return;
            }

            var rows = new char[h][];
            for (int r = 0; r < h; r++) rows[r] = new string(' ', w).ToCharArray();

            var wallNames = new Dictionary<string, int>();
            for (int ix = 0; ix < w; ix++)
            for (int iz = 0; iz < h; iz++)
            {
                var at = new Vector3(b.min.x + ix * FloorMapCell, playerPos.y, b.min.z + iz * FloorMapCell);
                if (!TryFloorNearLevel(at, out float floorY)) continue;
                at.y = floorY;
                char ch;
                if (WallProbe.AnyFaceAround(at, FloorMapWallRadius, out Collider col))
                {
                    ch = '#';
                    string name = col != null ? $"{col.name}/L{col.gameObject.layer}" : "?";
                    wallNames[name] = wallNames.TryGetValue(name, out int n) ? n + 1 : 1;
                }
                else
                {
                    float dy = floorY - playerPos.y;
                    ch = dy < -0.3f ? ',' : dy < 0.3f ? '.' : dy < 1.0f ? ':' : 'o';
                }
                rows[h - 1 - iz][ix] = ch;
            }

            void Mark(Vector3 p, char symbol)
            {
                int ix = Mathf.RoundToInt((p.x - b.min.x) / FloorMapCell);
                int iz = Mathf.RoundToInt((p.z - b.min.z) / FloorMapCell);
                if (ix < 0 || ix >= w || iz < 0 || iz >= h) return;
                rows[h - 1 - iz][ix] = symbol;
            }
            foreach (var (symbol, pos) in marks) Mark(pos, symbol);
            Mark(playerPos, 'P');

            MelonLogger.Msg("[SO2RAccess] [FLOORMAP] legend: '#' wall face within 0.45 m, '.' player's level, ',' lower, " +
                            "':' step, 'o' waist high, ' ' no floor near this level, P player" +
                            (legend.Count > 0 ? "; " + string.Join(", ", legend) : ""));
            MelonLogger.Msg($"[SO2RAccess] [FLOORMAP] x from {b.min.x:F1} (left) to {b.min.x + (w - 1) * FloorMapCell:F1} (right), " +
                            $"z from {b.min.z:F1} (bottom) to {b.min.z + (h - 1) * FloorMapCell:F1} (top), {FloorMapCell:F2} m per character");
            for (int r = 0; r < h; r++)
            {
                float z = b.min.z + (h - 1 - r) * FloorMapCell;
                MelonLogger.Msg($"[SO2RAccess] [FLOORMAP] z {z,6:F1} |{new string(rows[r])}|");
            }
            var walls = new List<string>();
            foreach (var kv in wallNames) walls.Add($"{kv.Key} x{kv.Value}");
            MelonLogger.Msg("[SO2RAccess] [FLOORMAP] wall cells by collider: " + (walls.Count > 0 ? string.Join(", ", walls) : "none"));

            LogSolidColliders(b);
            LogDoorsTriggersStairs();
        }

        /// <summary>
        /// Lists the game's own door, event-trigger and stairs objects with the
        /// settings that decide whether the player can pass: a FieldDoor's state,
        /// open distance, one-way flag and facing; an event trigger's box and
        /// whether it is armed; a stairs marker's facing. Answers "why does this
        /// door not open" from the log. Every game read is guarded, since these
        /// are IL2CPP objects that may be half-initialised.
        /// </summary>
        private static void LogDoorsTriggersStairs()
        {
            var fm = FieldManager.Instance;
            try
            {
                var doors = fm?.FieldDoorList;
                int inList = doors?.Count ?? 0;
                var all = UnityEngine.Object.FindObjectsOfType<FieldDoor>();
                MelonLogger.Msg($"[SO2RAccess] [FLOORMAP] doors: {inList} in FieldDoorList, {all?.Length ?? 0} in scene.");
                if (all != null)
                    foreach (var d in all)
                    {
                        if (d == null) continue;
                        var t = d.transform;
                        string info;
                        try
                        {
                            int cols = 0, enabled = 0;
                            var arr = d.colliders;
                            if (arr != null)
                                foreach (var c in arr) { cols++; if (c != null && c.enabled) enabled++; }
                            info = $"state={d.DoorState} se={d.SeType} openDistance={d.OpenDistance:F1} " +
                                   $"oneWay={d.IsOneWay} openDir={d.isOpenDir} doubleSide={d.isOpenDoubleSide} " +
                                   $"keepCollision={d.IsDontDisableCollision} colliders={enabled}/{cols} enabled";
                        }
                        catch (Exception ex) { info = $"(read error: {ex.Message})"; }
                        MelonLogger.Msg($"[SO2RAccess] [FLOORMAP] door '{d.name}' parent='{(t.parent != null ? t.parent.name : "-")}' " +
                                        $"pos=({t.position.x:F1},{t.position.y:F1},{t.position.z:F1}) " +
                                        $"forward=({t.forward.x:F2},{t.forward.z:F2}) active={d.gameObject.activeInHierarchy} {info}");
                    }
            }
            catch (Exception ex) { MelonLogger.Msg($"[SO2RAccess] [FLOORMAP] door listing error: {ex.Message}"); }

            try
            {
                var events = UnityEngine.Object.FindObjectsOfType<FieldEventCollision>();
                if (events != null)
                    foreach (var e in events)
                    {
                        if (e == null) continue;
                        string armed;
                        try { armed = e.IsEventActivate().ToString(); } catch (Exception ex) { armed = "? " + ex.Message; }
                        string box = "no collider";
                        try
                        {
                            var c = e.GetComponent<Collider>();
                            if (c != null)
                            {
                                var cb = c.bounds;
                                box = $"box x=[{cb.min.x:F1},{cb.max.x:F1}] y=[{cb.min.y:F1},{cb.max.y:F1}] z=[{cb.min.z:F1},{cb.max.z:F1}] trigger={c.isTrigger} enabled={c.enabled}";
                            }
                        }
                        catch (Exception ex) { box = "collider error: " + ex.Message; }
                        MelonLogger.Msg($"[SO2RAccess] [FLOORMAP] event '{e.name}' armed={armed} active={e.gameObject.activeInHierarchy} {box}");
                    }
            }
            catch (Exception ex) { MelonLogger.Msg($"[SO2RAccess] [FLOORMAP] event listing error: {ex.Message}"); }

            try
            {
                var stairs = fm?.FieldStairsList;
                if (stairs != null)
                    for (int i = 0; i < stairs.Count; i++)
                    {
                        var st = stairs[i];
                        if (st == null) continue;
                        var t = st.transform;
                        string up;
                        try { up = st.isUpperStage.ToString(); } catch (Exception ex) { up = "? " + ex.Message; }
                        MelonLogger.Msg($"[SO2RAccess] [FLOORMAP] stairs '{st.name}' pos=({t.position.x:F1},{t.position.y:F1},{t.position.z:F1}) " +
                                        $"forward=({t.forward.x:F2},{t.forward.z:F2}) isUpperStage={up}");
                    }
            }
            catch (Exception ex) { MelonLogger.Msg($"[SO2RAccess] [FLOORMAP] stairs listing error: {ex.Message}"); }
        }

        /// <summary>Nav items as (symbol, position) plus a legend, up to 36 of them.</summary>
        private (List<(char symbol, Vector3 pos)> marks, List<string> legend) CollectFloorMapMarks()
        {
            const string symbols = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            var marks = new List<(char symbol, Vector3 pos)>();
            var legend = new List<string>();
            for (int cat = 0; cat < _categories.Length && marks.Count < symbols.Length; cat++)
            {
                var items = _categories[cat];
                if (items == null) continue;
                for (int i = 0; i < items.Count && marks.Count < symbols.Length; i++)
                {
                    char symbol = symbols[marks.Count];
                    marks.Add((symbol, items[i].Position));
                    legend.Add($"{symbol}={items[i].Label}");
                }
            }
            return (marks, legend);
        }

        /// <summary>
        /// Floor under a map cell within <see cref="FloorMapLevelWindow"/> of the
        /// player's level: the highest solid floor-like hit in that window.
        /// </summary>
        private static bool TryFloorNearLevel(Vector3 at, out float floorY)
        {
            floorY = 0f;
            var origin = new Vector3(at.x, at.y + FloorMapLevelWindow, at.z);
            var hits = Physics.RaycastAll(origin, Vector3.down, FloorMapLevelWindow * 2f,
                ~0, QueryTriggerInteraction.Ignore);
            if (hits == null) return false;
            bool found = false;
            foreach (var hit in hits)
            {
                if (hit.normal.y < FloorProbeGrid.MinFloorNormalY) continue;
                if (hit.collider == null || !FloorProbeGrid.IsSolidFloorCollider(hit.collider)) continue;
                if (!found || hit.point.y > floorY) { floorY = hit.point.y; found = true; }
            }
            return found;
        }

        /// <summary>
        /// Lists every solid box or mesh collider overlapping the drawn area with
        /// its name, layer and bounds, so a wall on the map can be matched to the
        /// object that makes it. Capped at <see cref="FloorMapMaxColliders"/>.
        /// </summary>
        private static void LogSolidColliders(Bounds area)
        {
            var cols = UnityEngine.Object.FindObjectsOfType<Collider>();
            if (cols == null) return;
            int listed = 0, total = 0;
            foreach (var col in cols)
            {
                if (col == null || !FloorProbeGrid.IsSolidFloorCollider(col)) continue;
                var cb = col.bounds;
                if (cb.max.x < area.min.x || cb.min.x > area.max.x || cb.max.z < area.min.z || cb.min.z > area.max.z) continue;
                total++;
                if (listed >= FloorMapMaxColliders) continue;
                listed++;
                string parent = col.transform.parent != null ? col.transform.parent.name : "-";
                MelonLogger.Msg($"[SO2RAccess] [FLOORMAP] collider '{col.name}' parent='{parent}' L{col.gameObject.layer} " +
                                $"{col.GetIl2CppType().Name} x=[{cb.min.x:F1},{cb.max.x:F1}] y=[{cb.min.y:F1},{cb.max.y:F1}] z=[{cb.min.z:F1},{cb.max.z:F1}]");
            }
            MelonLogger.Msg($"[SO2RAccess] [FLOORMAP] {listed} of {total} solid colliders in the drawn area listed.");
        }

        #endregion
    }
}
