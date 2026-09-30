using System.Buffers.Binary;
using System.IO.Compression;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Benchmarks;

/// <summary>
/// Writes a foot-traffic heatmap of the real map as a PNG (no image library needed): walkable tiles in muted
/// map colours, traffic on a log scale from yellow to red, Miami building entrances as white dots.
/// For eyeballing that routes follow the real paths (Phase 1, 1a).
/// </summary>
internal static class TrafficImage
{
    public static void Write(string path, RealMap map, Campus campus, int scale = 2)
    {
        var grid = map.Grid;
        int w = grid.Width * scale, h = grid.Height * scale;
        var rgb = new byte[w * h * 3];
        int maxTraffic = 1;
        foreach (int t in grid.FootTraffic) if (t > maxTraffic) maxTraffic = t;
        double logMax = Math.Log(1 + maxTraffic);

        for (int ty = 0; ty < grid.Height; ty++)
            for (int tx = 0; tx < grid.Width; tx++)
            {
                int t = grid.Index(tx, ty);
                (byte r, byte g, byte b) = grid.Types[t] switch
                {
                    TileType.Building => ((byte)70, (byte)70, (byte)76),
                    TileType.Water => ((byte)90, (byte)130, (byte)170),
                    TileType.Path => ((byte)150, (byte)150, (byte)140),
                    _ => ((byte)200, (byte)210, (byte)190),
                };
                int traffic = grid.FootTraffic[t];
                if (traffic > 0)
                {
                    double v = Math.Log(1 + traffic) / logMax;           // 0..1
                    double a = 0.35 + 0.65 * v;                           // opacity
                    byte hr = 230, hg = (byte)(220 * (1 - v)), hb = 20;   // yellow -> red
                    r = (byte)(r * (1 - a) + hr * a);
                    g = (byte)(g * (1 - a) + hg * a);
                    b = (byte)(b * (1 - a) + hb * a);
                }
                for (int sy = 0; sy < scale; sy++)
                    for (int sx = 0; sx < scale; sx++)
                    {
                        int p = ((ty * scale + sy) * w + tx * scale + sx) * 3;
                        rgb[p] = r; rgb[p + 1] = g; rgb[p + 2] = b;
                    }
            }

        foreach (var bld in campus.Buildings)
        {
            if (bld.Kind == BuildingKind.OffCampusHousing) continue;
            int cx = bld.EntranceTile % grid.Width * scale, cy = bld.EntranceTile / grid.Width * scale;
            for (int dy = 0; dy < scale; dy++)
                for (int dx = 0; dx < scale; dx++)
                {
                    int p = ((cy + dy) * w + cx + dx) * 3;
                    rgb[p] = rgb[p + 1] = rgb[p + 2] = 255;
                }
        }

        WritePng(path, w, h, rgb);
    }

    private static void WritePng(string path, int w, int h, byte[] rgb)
    {
        using var file = File.Create(path);
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, w);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), h);
        ihdr[8] = 8; ihdr[9] = 2; // 8-bit RGB
        Chunk(file, "IHDR", ihdr);

        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
            for (int y = 0; y < h; y++)
            {
                z.WriteByte(0); // no filter
                z.Write(rgb, y * w * 3, w * 3);
            }
        Chunk(file, "IDAT", raw.ToArray());
        Chunk(file, "IEND", []);
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        uint crc = Crc(typeBytes, data);
        var c = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(c, crc);
        s.Write(c);
    }

    private static uint Crc(byte[] type, byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte b in type) c = Step(c, b);
        foreach (byte b in data) c = Step(c, b);
        return c ^ 0xFFFFFFFF;

        static uint Step(uint c, byte b)
        {
            c ^= b;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            return c;
        }
    }
}
