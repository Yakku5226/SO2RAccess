using System.Collections.Generic;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// A debug-only record of where the player has walked on the world map this
    /// session, so the F11 wall audit has ground truth there: the field maps have
    /// their breadcrumb graph, the world map records nothing persistent (it is
    /// hundreds of metres across and routes come from the baked grid). Nodes are
    /// dropped every <see cref="SpacingMeters"/>; consecutive nodes are linked only
    /// when the player really walked between them — a gap wider than
    /// <see cref="MaxLinkMeters"/> (fast travel, a battle return elsewhere) or a
    /// change of travel mode breaks the trail, and psynard flight is never
    /// recorded. In memory only; cleared on every map change.
    /// </summary>
    internal sealed class WorldmapTrail
    {
        /// <summary>Distance (m, flat) between recorded nodes.</summary>
        public const float SpacingMeters = 1.5f;

        /// <summary>Two consecutive nodes farther apart than this (m) are not linked.</summary>
        public const float MaxLinkMeters = 4f;

        private readonly List<Vector3> _nodes = new List<Vector3>();
        private readonly List<WorldmapTravelMode> _modes = new List<WorldmapTravelMode>();
        private readonly List<(int a, int b)> _edges = new List<(int a, int b)>();
        private bool _broken = true;

        public IReadOnlyList<Vector3> Nodes => _nodes;
        public IEnumerable<(int a, int b)> Edges => _edges;
        public int EdgeCount => _edges.Count;

        /// <summary>Travel mode the player was in at a node.</summary>
        public WorldmapTravelMode ModeOf(int node) => _modes[node];

        /// <summary>Records the player's position if far enough from the last node.</summary>
        public void Record(Vector3 pos, WorldmapTravelMode mode)
        {
            if (mode == WorldmapTravelMode.Psynard)
            {
                _broken = true;
                return;
            }

            if (_nodes.Count > 0)
            {
                Vector3 last = _nodes[_nodes.Count - 1];
                float dx = pos.x - last.x, dz = pos.z - last.z;
                float flat = Mathf.Sqrt(dx * dx + dz * dz);
                if (flat < SpacingMeters) { _broken = false; return; }

                bool link = !_broken && flat <= MaxLinkMeters && _modes[_nodes.Count - 1] == mode;
                _nodes.Add(pos);
                _modes.Add(mode);
                if (link) _edges.Add((_nodes.Count - 2, _nodes.Count - 1));
            }
            else
            {
                _nodes.Add(pos);
                _modes.Add(mode);
            }
            _broken = false;
        }

        /// <summary>The next node will not be linked to the previous one (menu, battle, teleport).</summary>
        public void Break() => _broken = true;

        /// <summary>Forgets everything (map change).</summary>
        public void Clear()
        {
            _nodes.Clear();
            _modes.Clear();
            _edges.Clear();
            _broken = true;
        }
    }
}
