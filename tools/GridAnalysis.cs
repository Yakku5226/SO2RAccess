using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Offline audit tool for the mod's world-map grid files. Runs WITHOUT the
// game: parses UserData\SO2RAccess\worldmap_*.grid directly.
// Supports BOTH formats:
//   WMGH (legacy): height lane doubles as obstacle lane (1 = foot obstacle).
//   WMGI (v2): pure height lane (0 = no ground) + per-cell flags byte
//              (bit0 footBlocked, bit1 bunnyBlocked, bit2 sealedInterior),
//              header extended with footMask/bunnyMask/footFloor/bunnyFloor.
// Region rule mirrors the mod: 8-dir, both cells passable for the mode,
// |height diff| <= 500cm.
public static class GridAnalysis
{
    public class Grid
    {
        public string Magic;
        public float MinX, MinZ, Cell;
        public int W, H;
        public int FootMask, BunnyMask;
        public float FootFloor, BunnyFloor;
        public ushort[] Hgt;   // file order: az * W + ax
        public byte[] Flags;   // same order; all-zero for legacy
        public bool IsV2 => Magic == "WMGI";

        public ushort HV(int ax, int az) => Hgt[(long)az * W + ax];
        public byte FV(int ax, int az) => Flags[(long)az * W + ax];

        // Passability per mode bit (1 = foot, 2 = bunny). Legacy grids:
        // flags are zero and foot obstacles are height==1 (< 2), so the
        // same rule reproduces legacy foot behavior; "bunny" on legacy
        // equals foot (the mod's fallback there too).
        public bool Passable(int ax, int az, byte modeBit)
            => HV(ax, az) >= 2 && (FV(ax, az) & modeBit) == 0;
    }

    public static Grid Load(string path)
    {
        var g = new Grid();
        using var r = new BinaryReader(File.OpenRead(path));
        g.Magic = new string(r.ReadChars(4));
        g.MinX = r.ReadSingle(); g.MinZ = r.ReadSingle(); g.Cell = r.ReadSingle();
        g.W = r.ReadInt32(); g.H = r.ReadInt32();
        if (g.IsV2)
        {
            g.FootMask = r.ReadInt32(); g.BunnyMask = r.ReadInt32();
            g.FootFloor = r.ReadSingle(); g.BunnyFloor = r.ReadSingle();
        }
        long n = (long)g.W * g.H;
        g.Hgt = new ushort[n];
        byte[] buf = r.ReadBytes(g.W * g.H * 2);
        Buffer.BlockCopy(buf, 0, g.Hgt, 0, buf.Length);
        g.Flags = g.IsV2 ? r.ReadBytes(g.W * g.H) : new byte[n];
        return g;
    }

    public static string Run(string path)
    {
        var sb = new StringBuilder();
        var g = Load(path);
        sb.AppendLine("magic=" + g.Magic + " min=(" + g.MinX + "," + g.MinZ
            + ") cell=" + g.Cell + " " + g.W + "x" + g.H
            + (g.IsV2
                ? " footMask=0x" + g.FootMask.ToString("X8")
                  + " bunnyMask=0x" + g.BunnyMask.ToString("X8")
                  + " floors=" + g.FootFloor + "/" + g.BunnyFloor
                : " (legacy)"));

        // Global counts.
        long noGround = 0, footBlk = 0, bunnyBlk = 0, sealedFoot = 0, walkFoot = 0, walkBunny = 0;
        for (long i = 0; i < g.Hgt.Length; i++)
        {
            ushort h = g.Hgt[i];
            byte f = g.Flags[i];
            if (h == 0) { noGround++; continue; }
            if (h == 1) { footBlk++; bunnyBlk++; continue; } // legacy obstacle
            if ((f & 1) != 0) { footBlk++; if ((f & 4) != 0) sealedFoot++; } else walkFoot++;
            if ((f & 2) != 0) bunnyBlk++; else walkBunny++;
        }
        sb.AppendLine("counts: noGround=" + noGround + " footBlocked=" + footBlk
            + " (sealed " + sealedFoot + ") bunnyBlocked=" + bunnyBlk
            + " footWalkable=" + walkFoot + " bunnyWalkable=" + walkBunny);

        AnalyzeMode(sb, g, 1, "FOOT");
        if (g.IsV2) AnalyzeMode(sb, g, 2, "BUNNY");
        return sb.ToString();
    }

    private static void AnalyzeMode(StringBuilder sb, Grid g, byte modeBit, string name)
    {
        sb.AppendLine("=== " + name + " ===");
        int[] dx8 = { 0, 1, 0, -1, 1, 1, -1, -1 };
        int[] dz8 = { 1, 0, -1, 0, 1, -1, -1, 1 };
        int[] regions = new int[(long)g.W * g.H]; // file order az*W+ax
        var counts = new Dictionary<int, long>();
        int next = 0;
        var q = new Queue<int>();
        for (int az0 = 0; az0 < g.H; az0++)
        {
            for (int ax0 = 0; ax0 < g.W; ax0++)
            {
                long i0 = (long)az0 * g.W + ax0;
                if (!g.Passable(ax0, az0, modeBit) || regions[i0] != 0) continue;
                next++;
                regions[i0] = next; counts[next] = 1;
                q.Enqueue((int)i0);
                while (q.Count > 0)
                {
                    int i = q.Dequeue();
                    int cax = i % g.W, caz = i / g.W;
                    ushort ch = g.Hgt[i];
                    for (int d = 0; d < 8; d++)
                    {
                        int nx = cax + dx8[d], nz = caz + dz8[d];
                        if (nx < 0 || nx >= g.W || nz < 0 || nz >= g.H) continue;
                        int ni = nz * g.W + nx;
                        if (regions[ni] != 0) continue;
                        if (!g.Passable(nx, nz, modeBit)) continue;
                        if (Math.Abs(ch - g.Hgt[ni]) > 500) continue;
                        regions[ni] = next; counts[next]++;
                        q.Enqueue(ni);
                    }
                }
            }
        }
        sb.AppendLine("total regions: " + next);

        Func<double, double, string> probe = (wx, wz) =>
        {
            int ax = (int)((wx - g.MinX) / g.Cell), az = (int)((wz - g.MinZ) / g.Cell);
            long i = (long)az * g.W + ax;
            int rg = regions[i];
            return "(" + wx + "," + wz + ") cell=(" + ax + "," + az + ") h=" + g.HV(ax, az)
                + " flags=" + g.FV(ax, az)
                + " region=" + rg + (rg > 0 ? " size=" + counts[rg] : "");
        };
        // Known probe points (Krosse/Salva/Arlia set + Lasgus/Mountain Palace).
        sb.AppendLine("Krosse ring    : " + probe(-94.0, -54.7));
        sb.AppendLine("Krosse plains  : " + probe(-93.8, -81.1));
        sb.AppendLine("Corridor mid   : " + probe(-140.0, -175.0));
        sb.AppendLine("Salva mapjump  : " + probe(-162.9, -307.1));
        sb.AppendLine("Salva ring     : " + probe(-155.6, -315.5));
        sb.AppendLine("SalvaArlia junc: " + probe(-174.7, -305.4));
        sb.AppendLine("Arlia ring     : " + probe(-42.4, -400.5));
        sb.AppendLine("Lasgus entrance: " + probe(-272.0, -88.0));

        var top = new List<KeyValuePair<int, long>>(counts);
        top.Sort((a, b) => b.Value.CompareTo(a.Value));
        for (int k = 0; k < Math.Min(6, top.Count); k++)
            sb.AppendLine("top region #" + top[k].Key + " size=" + top[k].Value);
    }

    // ASCII dump around a world point for one mode:
    // '~'=no ground, '#'=blocked for the mode, 'S'=sealed-interior blocked,
    // '.'=passable. North is up.
    public static string Dump(string path, double wx, double wz, int radius,
        byte modeBit = 1)
    {
        var g = Load(path);
        int cx = (int)((wx - g.MinX) / g.Cell), cz = (int)((wz - g.MinZ) / g.Cell);
        var sb = new StringBuilder();
        sb.AppendLine("dump around world=(" + wx + "," + wz + ") cell=(" + cx + "," + cz
            + ") modeBit=" + modeBit + "  ~=noGround #=blocked S=sealed .=passable");
        for (int az = cz + radius; az >= cz - radius; az--)
        {
            var line = new StringBuilder();
            for (int ax = cx - radius; ax <= cx + radius; ax++)
            {
                if (ax < 0 || ax >= g.W || az < 0 || az >= g.H) { line.Append(' '); continue; }
                ushort v = g.HV(ax, az);
                byte f = g.FV(ax, az);
                if (v == 0) line.Append('~');
                else if (v == 1) line.Append('#'); // legacy obstacle
                else if ((f & modeBit) != 0)
                    line.Append((f & 4) != 0 ? 'S' : '#');
                else line.Append('.');
            }
            sb.AppendLine(line.ToString());
        }
        return sb.ToString();
    }
}
