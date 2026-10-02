using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.View;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Tests;

/// <summary>Phase 1 (1g): paths drawn as meshes on the terrain surface.</summary>
public class PathMeshTests
{
    private static Heightmap Hills(int tiles)
    {
        int n = tiles * 2 + 1; // 5 m samples, 10 m tiles
        var h = new float[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
                h[j * n + i] = 3f * MathF.Sin(i * 0.37f) + 2f * MathF.Cos(j * 0.23f) + 5f;
        return new Heightmap(n, n, 5f, 0f, h);
    }

    [Fact]
    public void SurfaceHeight_MatchesTheTerrainMesh()
    {
        var hm = Hills(8);
        var mesh = TerrainMesher.BuildChunk(hm, 10f, 0, 0, 8, 1, (_, _) => new Rgb(1, 1, 1), 0f);
        // Every vertex of the full-detail surface (not the skirts) lies on SurfaceHeight.
        int surfaceVerts = 8 * 8 * 2 * 3;
        for (int v = 0; v < surfaceVerts; v++)
        {
            float x = mesh.Positions[v * 3], y = mesh.Positions[v * 3 + 1], z = mesh.Positions[v * 3 + 2];
            Assert.Equal(y, TerrainMesher.SurfaceHeight(hm, 10f, x, z), 3);
        }
        // And points inside a tile lie on its triangles: the centroid of each surface triangle.
        for (int tri = 0; tri < 8 * 8 * 2; tri++)
        {
            float cx = 0, cy = 0, cz = 0;
            for (int k = 0; k < 3; k++) { cx += mesh.Positions[(tri * 3 + k) * 3]; cy += mesh.Positions[(tri * 3 + k) * 3 + 1]; cz += mesh.Positions[(tri * 3 + k) * 3 + 2]; }
            Assert.Equal(cy / 3, TerrainMesher.SurfaceHeight(hm, 10f, cx / 3, cz / 3), 3);
        }
    }

    [Fact]
    public void StraightRuns_HaveNoJointCaps_BendsDo()
    {
        var hm = Hills(12);
        int w = 12;
        var straight = new HashSet<int>(Enumerable.Range(2, 6).Select(x => 5 * w + x));       // 6 tiles in a row
        var bend = new HashSet<int>(straight) { 4 * w + 7, 3 * w + 7 };                        // turns north at the end
        TerrainChunkMesh Build(HashSet<int> tiles) => PathMeshBuilder.BuildChunk(hm, 10f, w, w, 0, 0, w, tiles.Contains, _ => 2.5f,
            _ => new Rgb(0.5f, 0.4f, 0.3f), 0.08f);
        var s = Build(straight);
        var b = Build(bend);
        Assert.True(s.TriangleCount > 0);
        // 5 strips between 6 tiles; caps only at the 2 ends (squares, 2 triangles each).
        int strips = 5, ends = 2;
        Assert.InRange(s.TriangleCount, strips * 2 + ends * 2, strips * 2 * 2 + ends * 2);
        Assert.True(b.TriangleCount > s.TriangleCount);
        // Everything sits just above the ground.
        for (int v = 0; v < s.VertexCount; v++)
            Assert.Equal(TerrainMesher.SurfaceHeight(hm, 10f, s.Positions[v * 3], s.Positions[v * 3 + 2]) + 0.08f, s.Positions[v * 3 + 1], 2);
    }

    [Fact]
    public void Diagonals_AreDrawnOnlyWhereThereIsNoCorner()
    {
        var hm = Hills(10);
        int w = 10;
        var diagonal = new HashSet<int>(Enumerable.Range(1, 6).Select(k => (1 + k) * w + 1 + k));
        var stairs = new HashSet<int> { 2 * w + 2, 2 * w + 3, 3 * w + 3, 3 * w + 4, 4 * w + 4 };
        var d = PathMeshBuilder.BuildChunk(hm, 10f, w, w, 0, 0, w, diagonal.Contains, _ => 2.5f, _ => new Rgb(1, 1, 1), 0.08f);
        var st = PathMeshBuilder.BuildChunk(hm, 10f, w, w, 0, 0, w, stairs.Contains, _ => 2.5f, _ => new Rgb(1, 1, 1), 0.08f);
        Assert.True(d.TriangleCount > 0);
        // The staircase connects through its corners (4 orthogonal strips), never diagonally across them.
        Assert.True(st.TriangleCount < d.TriangleCount * 3);
    }
}
