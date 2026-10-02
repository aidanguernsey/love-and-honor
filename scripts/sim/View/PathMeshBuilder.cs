using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.View;

/// <summary>
/// Paths and roads as their own meshes (Phase 1 1g; retires risk R11: a 2.5 m path no longer paints a whole 10 m
/// tile). The walking logic stays on tiles; this draws, for one terrain chunk, a strip of the path's real width from
/// each path tile's centre to each neighbouring path tile's centre, and a rounded cap on every path tile, draped on the
/// terrain surface (<see cref="TerrainMesher.SurfaceHeight"/>) and lifted slightly. Diagonal strips are drawn only where
/// there's no corner to go round, so an L-bend stays an L and a diagonal path (the Slant Walk) stays a clean diagonal.
/// Triangles are in Godot order (front face up), with flat normals and one colour per vertex (sRGB, as given by the
/// caller: the bridge material treats them as sRGB). Engine-agnostic.
/// </summary>
public static class PathMeshBuilder
{
    /// <summary>Neighbours drawn from each tile (the other four are drawn from the other end): E, SE, S, SW.</summary>
    private static readonly (int Dx, int Dy)[] Forward = [(1, 0), (1, 1), (0, 1), (-1, 1)];

    /// <param name="isPath">Whether a tile has a path.</param>
    /// <param name="width">Path width in metres for a path tile.</param>
    /// <param name="color">Colour for a path tile.</param>
    /// <param name="originX">Subtracted from x (and <paramref name="originZ"/> from z) so a chunk's mesh can sit at its own
    /// position (Godot visibility ranges are measured to the node's origin).</param>
    public static TerrainChunkMesh BuildChunk(Heightmap heights, float tileSizeM, int gridW, int gridH, int tx0, int ty0, int sizeTiles,
        Func<int, bool> isPath, Func<int, float> width, Func<int, Rgb> color, float liftM, int segments = 2, float originX = 0, float originZ = 0)
    {
        var pos = new List<float>();
        var nrm = new List<float>();
        var col = new List<float>();

        void Tri((float X, float Y, float Z) a, (float X, float Y, float Z) b, (float X, float Y, float Z) c, Rgb rgb)
        {
            // Normal = (c - a) x (b - a) points up for Godot's front face; swap if it doesn't.
            float ux = c.X - a.X, uy = c.Y - a.Y, uz = c.Z - a.Z, vx = b.X - a.X, vy = b.Y - a.Y, vz = b.Z - a.Z;
            float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            if (ny < 0) { (b, c) = (c, b); nx = -nx; ny = -ny; nz = -nz; }
            float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len < 1e-9f) return;
            foreach (var p in new[] { a, b, c })
            {
                pos.Add(p.X - originX); pos.Add(p.Y); pos.Add(p.Z - originZ);
                nrm.Add(nx / len); nrm.Add(ny / len); nrm.Add(nz / len);
                col.Add(rgb.R); col.Add(rgb.G); col.Add(rgb.B); col.Add(1f);
            }
        }

        (float, float, float) Ground(float x, float z) => (x, TerrainMesher.SurfaceHeight(heights, tileSizeM, x, z) + liftM, z);

        for (int ty = ty0; ty < Math.Min(gridH, ty0 + sizeTiles); ty++)
            for (int tx = tx0; tx < Math.Min(gridW, tx0 + sizeTiles); tx++)
            {
                int t = ty * gridW + tx;
                if (!isPath(t)) continue;
                float cx = (tx + 0.5f) * tileSizeM, cz = (ty + 0.5f) * tileSizeM;
                float wt = width(t);
                var ct = color(t);

                // Cap: an octagon, so joints and bends look rounded. Not needed in the middle of a straight run.
                bool P(int dx, int dy) => (uint)(tx + dx) < (uint)gridW && (uint)(ty + dy) < (uint)gridH && isPath((ty + dy) * gridW + tx + dx);
                bool e = P(1, 0), wv = P(-1, 0), nn = P(0, -1), s = P(0, 1);
                int orth = (e ? 1 : 0) + (wv ? 1 : 0) + (nn ? 1 : 0) + (s ? 1 : 0);
                bool straight = orth == 2 && ((e && wv) || (nn && s))
                    || orth == 0 && ((P(1, 1) && P(-1, -1) && !P(1, -1) && !P(-1, 1)) || (P(1, -1) && P(-1, 1) && !P(1, 1) && !P(-1, -1)));
                bool diagonalJoint = P(1, 1) && !e && !s || P(-1, 1) && !wv && !s || P(1, -1) && !e && !nn || P(-1, -1) && !wv && !nn;
                if (!straight && !diagonalJoint)
                {
                    // Only straight connections: a square the path's width fills the joint (2 triangles).
                    float h = wt * 0.5f;
                    var q00 = Ground(cx - h, cz - h); var q10 = Ground(cx + h, cz - h); var q01 = Ground(cx - h, cz + h); var q11 = Ground(cx + h, cz + h);
                    Tri(q00, q10, q11, ct);
                    Tri(q00, q11, q01, ct);
                }
                const int sides = 8;
                var centre = Ground(cx, cz);
                for (int i = 0; i < sides && !straight && diagonalJoint; i++)
                {
                    float a0 = MathF.PI * 2 * (i + 0.5f) / sides, a1 = MathF.PI * 2 * (i + 1.5f) / sides, r = wt * 0.5f / MathF.Cos(MathF.PI / sides);
                    Tri(centre, Ground(cx + MathF.Cos(a0) * r, cz + MathF.Sin(a0) * r), Ground(cx + MathF.Cos(a1) * r, cz + MathF.Sin(a1) * r), ct);
                }

                // A 2 × 2 block of road tiles (roads two tiles wide): fill the square between the four centres. Footpaths
                // stay as lines: the campus's dense footpaths would otherwise turn into plazas.
                if (e && s && P(1, 1) && wt >= 5 && width(t + 1) >= 5 && width(t + gridW) >= 5 && width(t + gridW + 1) >= 5)
                {
                    float x1 = cx + tileSizeM, z1 = cz + tileSizeM;
                    var f00 = Ground(cx, cz); var f10 = Ground(x1, cz); var f01 = Ground(cx, z1); var f11 = Ground(x1, z1);
                    Tri(f00, f10, f11, ct);
                    Tri(f00, f11, f01, ct);
                }

                foreach (var (dx, dy) in Forward)
                {
                    int nx = tx + dx, ny = ty + dy;
                    if ((uint)nx >= (uint)gridW || (uint)ny >= (uint)gridH) continue;
                    int n = ny * gridW + nx;
                    if (!isPath(n)) continue;
                    if (dx != 0 && dy != 0 && (isPath(ty * gridW + nx) || isPath(ny * gridW + tx))) continue; // goes round the corner instead
                    float w = MathF.Min(wt, width(n)) * 0.5f;
                    var c = wt <= width(n) ? ct : color(n);
                    float ex = (nx + 0.5f) * tileSizeM, ez = (ny + 0.5f) * tileSizeM;
                    float len = MathF.Sqrt((ex - cx) * (ex - cx) + (ez - cz) * (ez - cz));
                    float px = -(ez - cz) / len * w, pz = (ex - cx) / len * w;
                    // One segment where the ground under the strip is flat enough, else `segments`.
                    var g0 = Ground(cx, cz); var gm = Ground((cx + ex) / 2, (cz + ez) / 2); var g1 = Ground(ex, ez);
                    int segs = MathF.Abs(gm.Item2 - (g0.Item2 + g1.Item2) / 2) < 0.04f ? 1 : segments;
                    for (int sg = 0; sg < segs; sg++)
                    {
                        float f0 = (float)sg / segs, f1 = (float)(sg + 1) / segs;
                        float x0 = cx + (ex - cx) * f0, z0 = cz + (ez - cz) * f0, x1 = cx + (ex - cx) * f1, z1 = cz + (ez - cz) * f1;
                        var l0 = Ground(x0 + px, z0 + pz); var r0 = Ground(x0 - px, z0 - pz);
                        var l1 = Ground(x1 + px, z1 + pz); var r1 = Ground(x1 - px, z1 - pz);
                        Tri(l0, r0, r1, c);
                        Tri(l0, r1, l1, c);
                    }
                }
            }
        return new TerrainChunkMesh { Positions = pos.ToArray(), Normals = nrm.ToArray(), Colors = col.ToArray() };
    }
}
