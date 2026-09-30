namespace LoveAndHonor.Sim.World;

/// <summary>
/// Terrain heights on a regular grid of samples (tile corners every <see cref="ResolutionM"/> metres).
/// Heights are stored relative to <see cref="BaseElevationM"/> (the lowest point), which is also the render-space
/// convention: world Y = elevation − base. World X = east, world Z = south, origin at the NW corner.
/// </summary>
public sealed class Heightmap
{
    public int Width { get; }
    public int Height { get; }
    public float ResolutionM { get; }
    public float BaseElevationM { get; }
    /// <summary>Row-major (north row first) heights in metres above <see cref="BaseElevationM"/>.</summary>
    public float[] Heights { get; }

    public Heightmap(int width, int height, float resolutionM, float baseElevationM, float[] heights)
    {
        if (heights.Length != width * height) throw new ArgumentException("heights length must be width × height");
        Width = width;
        Height = height;
        ResolutionM = resolutionM;
        BaseElevationM = baseElevationM;
        Heights = heights;
    }

    /// <summary>Decodes the pipeline's uint16 little-endian heightmap.</summary>
    public static Heightmap FromR16(byte[] data, int width, int height, float resolutionM, float minElevationM, float maxElevationM)
    {
        if (data.Length != width * height * 2)
            throw new InvalidDataException($"heightmap is {data.Length} bytes, expected {width * height * 2} (Git LFS pulled?)");
        var heights = new float[width * height];
        float scale = (maxElevationM - minElevationM) / 65535f;
        for (int i = 0; i < heights.Length; i++)
            heights[i] = (ushort)(data[2 * i] | data[2 * i + 1] << 8) * scale;
        return new Heightmap(width, height, resolutionM, minElevationM, heights);
    }

    public float WidthM => (Width - 1) * ResolutionM;
    public float DepthM => (Height - 1) * ResolutionM;

    public float Sample(int i, int j) =>
        Heights[Math.Clamp(j, 0, Height - 1) * Width + Math.Clamp(i, 0, Width - 1)];

    /// <summary>Bilinear height at world (x, z); clamps outside the map.</summary>
    public float HeightAt(float x, float z)
    {
        float fx = Math.Clamp(x / ResolutionM, 0, Width - 1.001f);
        float fz = Math.Clamp(z / ResolutionM, 0, Height - 1.001f);
        int i = (int)fx, j = (int)fz;
        float tx = fx - i, tz = fz - j;
        float h00 = Sample(i, j), h10 = Sample(i + 1, j), h01 = Sample(i, j + 1), h11 = Sample(i + 1, j + 1);
        return (h00 + (h10 - h00) * tx) * (1 - tz) + (h01 + (h11 - h01) * tx) * tz;
    }

    /// <summary>
    /// Ray march against the height field (for picking the tile under the mouse). Direction need not be normalised.
    /// Returns false if the ray doesn't hit the terrain within <paramref name="maxDistance"/> metres.
    /// </summary>
    public bool Raycast(float ox, float oy, float oz, float dx, float dy, float dz, float maxDistance,
        out float hitX, out float hitY, out float hitZ)
    {
        float len = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        dx /= len; dy /= len; dz /= len;
        float step = ResolutionM * 0.5f;
        float prevT = 0;
        bool prevAbove = oy >= HeightAt(ox, oz);
        for (float t = step; t <= maxDistance; t += step)
        {
            float x = ox + dx * t, y = oy + dy * t, z = oz + dz * t;
            bool inside = x >= 0 && z >= 0 && x <= WidthM && z <= DepthM;
            bool above = !inside || y >= HeightAt(x, z);
            if (prevAbove && !above)
            {
                // Refine between prevT and t.
                float lo = prevT, hi = t;
                for (int k = 0; k < 16; k++)
                {
                    float mid = (lo + hi) * 0.5f;
                    float mx = ox + dx * mid, my = oy + dy * mid, mz = oz + dz * mid;
                    if (my >= HeightAt(mx, mz)) lo = mid; else hi = mid;
                }
                hitX = ox + dx * hi; hitY = oy + dy * hi; hitZ = oz + dz * hi;
                return true;
            }
            prevAbove = above;
            prevT = t;
        }
        hitX = hitY = hitZ = 0;
        return false;
    }
}
