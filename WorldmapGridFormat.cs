using Il2CppGame;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SO2RAccess
{
    /// <summary>
    /// Binary save/load for the world-map walkability grid, plus the
    /// <see cref="CachedGrid"/> runtime container. Two formats are supported:
    ///
    /// WMGI (v2, current): the height lane is PURE terrain data
    /// (0 = no ground, else height in offset centimeters) and a separate
    /// per-cell flags byte carries per-travel-mode blocked bits
    /// (foot / bunny) baked with the game's own wall layer masks, which the
    /// header records for auditability.
    ///
    /// WMGH (legacy, the grid embedded in shipped mod builds): the height
    /// lane doubles as the obstacle lane (1 = foot obstacle). Loading it
    /// yields byte-identical foot behavior to the pre-v2 mod; bunny data is
    /// marked unavailable so all bunny reachability queries fail open.
    /// </summary>
    public static class WorldmapGridFormat
    {
        /// <summary>Current file format magic (v2: per-mode flags lane).</summary>
        public const string MagicV2 = "WMGI";

        /// <summary>Legacy format magic (height lane doubles as obstacle lane).</summary>
        public const string MagicLegacy = "WMGH";

        /// <summary>
        /// Saves a v2 (WMGI) grid: header with the baked per-mode masks and
        /// clearance floors, pure height lane, per-cell flags lane, then the
        /// sparse clearance offset/value tables (foot-only data).
        /// </summary>
        public static void SaveGrid(string path, float worldMinX,
            float worldMinZ, float cellSize, int gridW, int gridH,
            ushort[,] height, byte[] flags,
            Dictionary<long, (float, float)> clearanceOffsets,
            Dictionary<long, float> clearanceValues,
            int footMask, int bunnyMask, float footFloor, float bunnyFloor)
        {
            using var stream = new FileStream(path, FileMode.Create);
            using var writer = new BinaryWriter(stream);

            // Header.
            writer.Write(MagicV2.ToCharArray()); // 4 bytes "WMGI"
            writer.Write(worldMinX);             // float
            writer.Write(worldMinZ);             // float
            writer.Write(cellSize);              // float
            writer.Write(gridW);                 // int32
            writer.Write(gridH);                 // int32
            writer.Write(footMask);              // int32 — bake input, audit
            writer.Write(bunnyMask);             // int32 — bake input, audit
            writer.Write(footFloor);             // float — bake input, audit
            writer.Write(bunnyFloor);            // float — bake input, audit

            // Height lane: 2 bytes per cell (ushort), row-major (az outer).
            // 0 = no ground; >= 2 = height in offset cm: (y + 100) * 100.
            for (int az = 0; az < gridH; az++)
                for (int ax = 0; ax < gridW; ax++)
                    writer.Write(height[ax, az]);

            // Flags lane: 1 byte per cell, same order. See CachedGrid.Flag*.
            for (int az = 0; az < gridH; az++)
                for (int ax = 0; ax < gridW; ax++)
                    writer.Write(flags[(long)ax * gridH + az]);

            WriteClearanceTables(writer, gridH, clearanceOffsets, clearanceValues);
        }

        /// <summary>Writes the sparse clearance offset + value tables (shared
        /// layout between WMGH and WMGI).</summary>
        private static void WriteClearanceTables(BinaryWriter writer, int gridH,
            Dictionary<long, (float, float)> clearanceOffsets,
            Dictionary<long, float> clearanceValues)
        {
            // Clearance offset table (sparse).
            // Each entry: ushort gridX, ushort gridZ, byte offX, byte offZ.
            writer.Write(clearanceOffsets.Count);
            foreach (var kvp in clearanceOffsets)
            {
                int az = (int)(kvp.Key % gridH);
                int ax = (int)(kvp.Key / gridH);
                writer.Write((ushort)ax);
                writer.Write((ushort)az);
                writer.Write(EncodeClearanceOffset(kvp.Value.Item1));
                writer.Write(EncodeClearanceOffset(kvp.Value.Item2));
            }

            // Clearance value table (sparse).
            // Each entry: ushort gridX, ushort gridZ, ushort clearanceCm.
            writer.Write(clearanceValues.Count);
            foreach (var kvp in clearanceValues)
            {
                int az = (int)(kvp.Key % gridH);
                int ax = (int)(kvp.Key / gridH);
                writer.Write((ushort)ax);
                writer.Write((ushort)az);
                int cm = (int)(kvp.Value * 100f);
                writer.Write((ushort)Math.Max(0, Math.Min(65535, cm)));
            }
        }

        /// <summary>Encodes a clearance offset (-0.25 to +0.25m) as a byte.</summary>
        private static byte EncodeClearanceOffset(float meters)
        {
            // Map -0.25..+0.25 to 0..250. Precision: ~0.002m per step.
            float normalized = (meters / 0.25f + 1.0f) * 125f;
            return (byte)Math.Max(0, Math.Min(250, (int)normalized));
        }

        /// <summary>Decodes a byte back to a clearance offset in meters.</summary>
        private static float DecodeClearanceOffset(byte encoded)
        {
            return (encoded / 125f - 1.0f) * 0.25f;
        }

        /// <summary>
        /// Loads a cached grid from disk (extracting the embedded copy first
        /// if no local file exists). Supports WMGI (v2) and legacy WMGH.
        /// Anything older is rejected with a regenerate hint. Returns null
        /// when nothing loadable exists — callers treat that as "no grid".
        /// </summary>
        public static CachedGrid LoadGrid(WorldmapID wmID)
        {
            string mapName = wmID == WorldmapID.EXPEL ? "expel" : "nede";
            string dir = Path.Combine(
                Directory.GetCurrentDirectory(), "UserData", "SO2RAccess");
            string filePath = Path.Combine(dir, $"worldmap_{mapName}.grid");

            if (!File.Exists(filePath) &&
                !TryExtractEmbeddedGrid(mapName, dir, filePath))
            {
                DebugLogger.LogState($"[GridGen] No cached grid at: {filePath}");
                return null;
            }

            try
            {
                using var stream = new FileStream(filePath, FileMode.Open);
                using var reader = new BinaryReader(stream);

                string magicStr = new string(reader.ReadChars(4));
                bool isV2 = magicStr == MagicV2;
                bool isLegacy = magicStr == MagicLegacy;
                if (!isV2 && !isLegacy)
                {
                    DebugLogger.LogState(
                        $"[GridGen] Unsupported grid file (expected {MagicV2} " +
                        $"or {MagicLegacy}, got {magicStr}). Regenerate with F9.");
                    return null;
                }

                float wMinX = reader.ReadSingle();
                float wMinZ = reader.ReadSingle();
                float cSize = reader.ReadSingle();
                int gridW = reader.ReadInt32();
                int gridH = reader.ReadInt32();

                int footMask = 0, bunnyMask = 0;
                float footFloor = 0f, bunnyFloor = 0f;
                if (isV2)
                {
                    footMask = reader.ReadInt32();
                    bunnyMask = reader.ReadInt32();
                    footFloor = reader.ReadSingle();
                    bunnyFloor = reader.ReadSingle();
                }

                // Both lanes are read with ONE bulk ReadBytes each and
                // unpacked from memory — per-element BinaryReader calls
                // would be ~75 million virtual calls (a multi-second freeze
                // at first world-map load). The file is little-endian,
                // az-outer/ax-inner; the arrays are indexed by (ax, az).
                var height = new ushort[gridW, gridH];
                byte[] laneBuf = reader.ReadBytes(gridW * gridH * 2);
                int p = 0;
                for (int az = 0; az < gridH; az++)
                {
                    for (int ax = 0; ax < gridW; ax++)
                    {
                        height[ax, az] = (ushort)(laneBuf[p] |
                            (laneBuf[p + 1] << 8));
                        p += 2;
                    }
                }

                // Flags lane. v2 reads it from the file; legacy grids get an
                // all-zero lane so runtime code can treat both formats
                // identically (legacy foot obstacles are already impassable
                // through their height value of 1, i.e. below the ground
                // threshold of 2 — no flag synthesis needed).
                long cellCount = (long)gridW * gridH;
                var flags = new byte[cellCount];
                if (isV2)
                {
                    byte[] flagBuf = reader.ReadBytes(gridW * gridH);
                    p = 0;
                    for (int az = 0; az < gridH; az++)
                        for (int ax = 0; ax < gridW; ax++)
                            flags[(long)ax * gridH + az] = flagBuf[p++];
                }

                // Clearance offset table (both formats).
                Dictionary<long, (float, float)> offsets = null;
                if (stream.Position < stream.Length)
                {
                    int count = reader.ReadInt32();
                    offsets = new Dictionary<long, (float, float)>(count);
                    for (int i = 0; i < count; i++)
                    {
                        ushort oax = reader.ReadUInt16();
                        ushort oaz = reader.ReadUInt16();
                        float offX = DecodeClearanceOffset(reader.ReadByte());
                        float offZ = DecodeClearanceOffset(reader.ReadByte());
                        offsets[(long)oax * gridH + oaz] = (offX, offZ);
                    }
                    DebugLogger.LogState(
                        $"[GridGen] Loaded {count} clearance offsets.");
                }

                // Clearance value table (both formats).
                Dictionary<long, float> clrValues = null;
                if (stream.Position < stream.Length)
                {
                    int count = reader.ReadInt32();
                    clrValues = new Dictionary<long, float>(count);
                    for (int i = 0; i < count; i++)
                    {
                        ushort oax = reader.ReadUInt16();
                        ushort oaz = reader.ReadUInt16();
                        float val = reader.ReadUInt16() / 100f;
                        clrValues[(long)oax * gridH + oaz] = val;
                    }
                    DebugLogger.LogState(
                        $"[GridGen] Loaded {count} clearance values.");
                }

                DebugLogger.LogState(
                    $"[GridGen] Loaded {mapName} grid ({magicStr}): " +
                    $"{gridW}x{gridH} at {cSize}m from ({wMinX:F1},{wMinZ:F1})" +
                    (isV2
                        ? $" footMask=0x{footMask:X8} bunnyMask=0x{bunnyMask:X8}"
                        : " (legacy — bunny data unavailable, foot-only)"));
                if (isLegacy)
                {
                    // One hint, not repeated speech: regenerating upgrades to
                    // per-mode (bunny-aware) reachability data.
                    MelonLoader.MelonLogger.Msg(
                        $"[GridGen] {mapName} grid is legacy {MagicLegacy}. " +
                        "Bunny reachability will fail open (no annotations). " +
                        "Press F9 on the world map in debug mode to bake the " +
                        $"per-mode {MagicV2} grid.");
                }

                return new CachedGrid
                {
                    Height = height,
                    Flags = flags,
                    IsV2 = isV2,
                    FootMask = footMask,
                    BunnyMask = bunnyMask,
                    FootFloor = footFloor,
                    BunnyFloor = bunnyFloor,
                    WorldMinX = wMinX,
                    WorldMinZ = wMinZ,
                    CellSize = cSize,
                    GridW = gridW,
                    GridH = gridH,
                    ClearanceOffsets = offsets,
                    ClearanceValues = clrValues
                };
            }
            catch (Exception ex)
            {
                DebugLogger.LogState($"[GridGen] Load error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extracts a gzip-compressed grid file embedded in the mod DLL to
        /// the UserData folder, so end users never need to generate the grid
        /// themselves (F9 remains available for developers to rebake). A grid
        /// already present in UserData is never overwritten — a locally
        /// regenerated grid always wins over the shipped one.
        /// </summary>
        private static bool TryExtractEmbeddedGrid(string mapName, string dir,
            string filePath)
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                string suffix = $"grids.worldmap_{mapName}.grid.gz";
                foreach (var name in asm.GetManifestResourceNames())
                {
                    if (!name.EndsWith(suffix,
                        StringComparison.OrdinalIgnoreCase)) continue;
                    using var res = asm.GetManifestResourceStream(name);
                    if (res == null) return false;
                    Directory.CreateDirectory(dir);
                    using var gz = new System.IO.Compression.GZipStream(
                        res, System.IO.Compression.CompressionMode.Decompress);
                    using var outFs = new FileStream(filePath, FileMode.Create);
                    gz.CopyTo(outFs);
                    MelonLoader.MelonLogger.Msg(
                        $"[GridGen] Extracted embedded {mapName} grid " +
                        $"to {filePath}");
                    return true;
                }
                DebugLogger.LogState(
                    $"[GridGen] No embedded grid resource for {mapName}.");
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Warning(
                    $"[GridGen] Embedded grid extraction failed: {ex.Message}");
            }
            return false;
        }

        /// <summary>Holds a loaded cached grid with its metadata and the
        /// per-travel-mode passability API used by the pathfinder.</summary>
        public class CachedGrid
        {
            /// <summary>Per-cell flag: blocked for the on-foot player.</summary>
            public const byte FlagFootBlocked = 1;

            /// <summary>Per-cell flag: blocked for the bunny mount.</summary>
            public const byte FlagBunnyBlocked = 2;

            /// <summary>Per-cell flag (diagnostic): cell was sealed by the
            /// town-interior flood fill rather than a direct collider hit.</summary>
            public const byte FlagSealedInterior = 4;

            /// <summary>Both travel-mode blocked bits — used by runtime
            /// stamps/clears that apply regardless of mode.</summary>
            public const byte FlagAnyModeBlocked =
                FlagFootBlocked | FlagBunnyBlocked;

            /// <summary>
            /// Height lane. v2 (WMGI): 0 = no ground, else height in offset
            /// centimeters ((y + 100) * 100, clamped to a minimum of 2).
            /// Legacy (WMGH): additionally 1 = baked foot obstacle. In both
            /// formats a value below 2 means "cannot stand here", so the
            /// shared ground test is Height >= 2.
            /// </summary>
            public ushort[,] Height;

            /// <summary>
            /// Per-cell mode flags, indexed ax * GridH + az. Never null:
            /// legacy grids get an all-zero lane (their foot obstacles are
            /// already impassable via the height lane). Runtime searches
            /// journal their mutations here — the height lane is read-only
            /// after load.
            /// </summary>
            public byte[] Flags;

            /// <summary>True when loaded from the v2 (WMGI) format.</summary>
            public bool IsV2;

            /// <summary>True when the grid carries real bunny bake data.
            /// False (legacy grid) means every bunny reachability question
            /// must fail open.</summary>
            public bool BunnyDataAvailable => IsV2;

            /// <summary>Foot wall layer mask the grid was baked with (v2).</summary>
            public int FootMask;

            /// <summary>Bunny wall layer mask the grid was baked with (v2).</summary>
            public int BunnyMask;

            /// <summary>Foot clearance floor (m) the grid was baked with (v2).</summary>
            public float FootFloor;

            /// <summary>Bunny clearance floor (m) the grid was baked with (v2).</summary>
            public float BunnyFloor;

            /// <summary>World X of grid cell (0,0)'s center.</summary>
            public float WorldMinX;

            /// <summary>World Z of grid cell (0,0)'s center.</summary>
            public float WorldMinZ;

            /// <summary>Cell spacing in meters (0.5).</summary>
            public float CellSize;

            /// <summary>Grid width in cells (X axis).</summary>
            public int GridW;

            /// <summary>Grid height in cells (Z axis).</summary>
            public int GridH;

            /// <summary>
            /// Sparse table of sub-cell clearance offsets for cells near
            /// CharaWalls. Key = ax * GridH + az. Value = (offsetX, offsetZ)
            /// in meters from cell center. Foot-only data (the bunny ignores
            /// CharaWalls). Null if absent from the file.
            /// </summary>
            public Dictionary<long, (float, float)> ClearanceOffsets;

            /// <summary>
            /// Sparse table of actual clearance distances (meters) for cells
            /// near walls. The foot pathfinder uses these for a continuous
            /// penalty — tighter cells cost more. Null if absent.
            /// </summary>
            public Dictionary<long, float> ClearanceValues;

            /// <summary>
            /// Connected-region label per cell for FOOT travel
            /// (index = ax * GridH + az), built by
            /// WorldmapPathfinder.BuildRegions at grid load. 0 = unlabeled
            /// (no ground / blocked / overflow — treat as unknown). Null if
            /// the region build failed (fail open — no fast reject).
            /// </summary>
            public ushort[] RegionsFoot;

            /// <summary>
            /// Connected-region label per cell for BUNNY travel. Built
            /// LAZILY on the first bunny reachability query (costs ~75MB and
            /// ~1-2s once) and only when <see cref="BunnyDataAvailable"/>.
            /// Null until then, or on failure (fail open).
            /// </summary>
            public ushort[] RegionsBunny;

            /// <summary>
            /// Set when a bunny region build failed (e.g. out of memory), so
            /// the lazy builder never retries the multi-second allocation on
            /// every subsequent query — one failure, then permanent fail-open
            /// until the grid cache is cleared.
            /// </summary>
            public bool RegionsBunnyBuildFailed;

            /// <summary>
            /// True if the cell is inside the grid, has ground, and is not
            /// blocked for the given travel mode bit
            /// (<see cref="FlagFootBlocked"/> or <see cref="FlagBunnyBlocked"/>).
            /// This is THE passability rule — the A*, the region builder and
            /// all reachability checks share it.
            /// </summary>
            public bool IsPassable(int ax, int az, byte modeBit)
            {
                if (ax < 0 || ax >= GridW || az < 0 || az >= GridH)
                    return false;
                if (Height[ax, az] < 2) return false;
                return (Flags[(long)ax * GridH + az] & modeBit) == 0;
            }

            /// <summary>Convert world position to grid indices.</summary>
            public void WorldToGrid(float worldX, float worldZ,
                out int ax, out int az)
            {
                ax = (int)((worldX - WorldMinX) / CellSize);
                az = (int)((worldZ - WorldMinZ) / CellSize);
            }

            /// <summary>Convert grid indices to world position (cell center).</summary>
            public Vector3 GridToWorld(int ax, int az)
            {
                return new Vector3(
                    WorldMinX + ax * CellSize, 0f,
                    WorldMinZ + az * CellSize);
            }

            /// <summary>
            /// Convert grid indices to world position, applying clearance
            /// offset if available. For cells near CharaWalls, this returns
            /// the sub-cell position with maximum clearance from all walls
            /// instead of the cell center. Foot-only refinement.
            /// </summary>
            public Vector3 GridToWorldWithClearance(int ax, int az)
            {
                float x = WorldMinX + ax * CellSize;
                float z = WorldMinZ + az * CellSize;

                if (ClearanceOffsets != null)
                {
                    long key = (long)ax * GridH + az;
                    if (ClearanceOffsets.TryGetValue(key, out var offset))
                    {
                        x += offset.Item1;
                        z += offset.Item2;
                    }
                }

                return new Vector3(x, 0f, z);
            }

            /// <summary>
            /// Gets the actual clearance distance (meters) for a cell.
            /// Returns float.MaxValue for cells with no clearance data
            /// (wide open, far from any wall).
            /// </summary>
            public float GetClearance(int ax, int az)
            {
                if (ClearanceValues == null) return float.MaxValue;
                long key = (long)ax * GridH + az;
                return ClearanceValues.TryGetValue(key, out float val)
                    ? val : float.MaxValue;
            }

            /// <summary>
            /// Get the real height in meters for a cell.
            /// Returns float.MinValue for cells without usable ground data.
            /// </summary>
            public float GetHeightM(int ax, int az)
            {
                if (ax < 0 || ax >= GridW || az < 0 || az >= GridH)
                    return float.MinValue;
                ushort v = Height[ax, az];
                if (v < 2) return float.MinValue;
                return (v / 100f) - 100f;
            }
        }
    }
}
