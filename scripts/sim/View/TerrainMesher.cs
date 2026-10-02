using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.View;

/// <summary>Flat-shaded triangle soup for one terrain chunk (non-indexed: 3 vertices per triangle, one normal per face).</summary>
public sealed class TerrainChunkMesh
{
    public required float[] Positions { get; init; }  // xyz per vertex
    public required float[] Normals { get; init; }    // xyz per vertex
    public required float[] Colors { get; init; }     // rgba per vertex (linear)
    public int VertexCount => Positions.Length / 3;
    public int TriangleCount => VertexCount / 3;
}

/// <summary>
/// Low-poly terrain (§28.1, §30.3): one quad per tile (or per 2×2 / 4×4 tiles at coarser LODs), heights from the
/// heightmap at tile corners, each quad split along its flatter diagonal and flat-shaded, coloured per tile.
/// Chunk edges get vertical "skirts" so LOD seams between neighbouring chunks don't show cracks.
/// Winding: clockwise seen from above = Godot front face; normals point up.
/// </summary>
public static class TerrainMesher
{
    /// <param name="tileColor">Linear RGB colour of tile (tx, ty).</param>
    /// <param name="step">Tiles per quad: 1 = full detail, 2, 4 … for distant LODs. Must divide sizeTiles.</param>
    /// <summary>
    /// Height of the full-detail terrain surface (LOD 0) at world (x, z): the same tile corners and the same diagonal
    /// split as <see cref="BuildChunk"/>, so things laid on the ground (paths) sit exactly on it.
    /// </summary>
    public static float SurfaceHeight(Heightmap heights, float tileSizeM, float x, float z)
    {
        int spt = (int)MathF.Round(tileSizeM / heights.ResolutionM);
        float fx = x / tileSizeM, fz = z / tileSizeM;
        int cx = Math.Clamp((int)MathF.Floor(fx), 0, (heights.Width - 2) / spt), cy = Math.Clamp((int)MathF.Floor(fz), 0, (heights.Height - 2) / spt);
        float u = Math.Clamp(fx - cx, 0, 1), v = Math.Clamp(fz - cy, 0, 1);
        float ha = heights.Sample(cx * spt, cy * spt), hb = heights.Sample((cx + 1) * spt, cy * spt);
        float hc = heights.Sample(cx * spt, (cy + 1) * spt), hd = heights.Sample((cx + 1) * spt, (cy + 1) * spt);
        if (MathF.Abs(ha - hd) <= MathF.Abs(hb - hc))
            return u >= v ? ha + (hb - ha) * u + (hd - hb) * v : ha + (hd - hc) * u + (hc - ha) * v; // split a–d
        return u + v <= 1 ? ha + (hb - ha) * u + (hc - ha) * v : hd + (hc - hd) * (1 - u) + (hb - hd) * (1 - v); // split b–c
    }

    public static TerrainChunkMesh BuildChunk(Heightmap heights, float tileSizeM, int tx0, int ty0, int sizeTiles,
        int step, Func<int, int, Rgb> tileColor, float skirtDepthM)
    {
        if (sizeTiles % step != 0) throw new ArgumentException("step must divide the chunk size");
        int quads = sizeTiles / step;
        int triangles = quads * quads * 2 + 4 * quads * 2 * 2; // surface + double-sided skirts on 4 edges
        var b = new Builder(triangles * 3);
        int samplesPerTile = (int)MathF.Round(tileSizeM / heights.ResolutionM);

        float H(int cx, int cy) => heights.Sample(cx * samplesPerTile, cy * samplesPerTile);

        for (int qy = 0; qy < quads; qy++)
            for (int qx = 0; qx < quads; qx++)
            {
                int cx = tx0 + qx * step, cy = ty0 + qy * step;
                float x0 = cx * tileSizeM, z0 = cy * tileSizeM, x1 = (cx + step) * tileSizeM, z1 = (cy + step) * tileSizeM;
                float ha = H(cx, cy), hb = H(cx + step, cy), hc = H(cx, cy + step), hd = H(cx + step, cy + step);
                var col = tileColor(cx, cy);
                // a = NW, b = NE, c = SW, d = SE. Split along the diagonal with the smaller height difference.
                if (MathF.Abs(ha - hd) <= MathF.Abs(hb - hc))
                {
                    b.Triangle(x0, ha, z0, x1, hb, z0, x1, hd, z1, col);
                    b.Triangle(x0, ha, z0, x1, hd, z1, x0, hc, z1, col);
                }
                else
                {
                    b.Triangle(x0, ha, z0, x1, hb, z0, x0, hc, z1, col);
                    b.Triangle(x1, hb, z0, x1, hd, z1, x0, hc, z1, col);
                }
            }

        // Skirts: a vertical strip hanging below each chunk edge (both windings, so visible from either side).
        for (int k = 0; k < quads; k++)
        {
            int a = k * step, c = a + step;
            Skirt(b, tx0 + a, ty0, tx0 + c, ty0, tileSizeM, skirtDepthM, H, tileColor(tx0 + a, ty0));                                   // north
            Skirt(b, tx0 + a, ty0 + sizeTiles, tx0 + c, ty0 + sizeTiles, tileSizeM, skirtDepthM, H, tileColor(tx0 + a, ty0 + sizeTiles - 1)); // south
            Skirt(b, tx0, ty0 + a, tx0, ty0 + c, tileSizeM, skirtDepthM, H, tileColor(tx0, ty0 + a));                                   // west
            Skirt(b, tx0 + sizeTiles, ty0 + a, tx0 + sizeTiles, ty0 + c, tileSizeM, skirtDepthM, H, tileColor(tx0 + sizeTiles - 1, ty0 + a)); // east
        }

        return b.Build();
    }

    private static void Skirt(Builder b, int cx0, int cy0, int cx1, int cy1, float tile, float depth, Func<int, int, float> h, Rgb col)
    {
        float x0 = cx0 * tile, z0 = cy0 * tile, x1 = cx1 * tile, z1 = cy1 * tile;
        float h0 = h(cx0, cy0), h1 = h(cx1, cy1);
        b.Triangle(x0, h0, z0, x1, h1, z1, x1, h1 - depth, z1, col);
        b.Triangle(x0, h0, z0, x1, h1 - depth, z1, x0, h0 - depth, z0, col);
        b.Triangle(x1, h1 - depth, z1, x1, h1, z1, x0, h0, z0, col);
        b.Triangle(x0, h0 - depth, z0, x1, h1 - depth, z1, x0, h0, z0, col);
    }

    private sealed class Builder(int vertices)
    {
        private readonly float[] _pos = new float[vertices * 3];
        private readonly float[] _nrm = new float[vertices * 3];
        private readonly float[] _col = new float[vertices * 4];
        private int _v;

        public void Triangle(float ax, float ay, float az, float bx, float by, float bz, float cx, float cy, float cz, Rgb color)
        {
            // Face normal = (c − a) × (b − a): points up for clockwise-from-above triangles.
            float ux = cx - ax, uy = cy - ay, uz = cz - az, vx = bx - ax, vy = by - ay, vz = bz - az;
            float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len > 0) { nx /= len; ny /= len; nz /= len; }
            Put(ax, ay, az, nx, ny, nz, color);
            Put(bx, by, bz, nx, ny, nz, color);
            Put(cx, cy, cz, nx, ny, nz, color);
        }

        private void Put(float x, float y, float z, float nx, float ny, float nz, Rgb c)
        {
            int p = _v * 3, q = _v * 4;
            _pos[p] = x; _pos[p + 1] = y; _pos[p + 2] = z;
            _nrm[p] = nx; _nrm[p + 1] = ny; _nrm[p + 2] = nz;
            _col[q] = c.R; _col[q + 1] = c.G; _col[q + 2] = c.B; _col[q + 3] = 1f;
            _v++;
        }

        public TerrainChunkMesh Build() => new() { Positions = _pos, Normals = _nrm, Colors = _col };
    }
}
