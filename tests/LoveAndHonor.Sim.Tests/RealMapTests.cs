using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.View;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Tests;

public class RealMapTests
{
    private static readonly RealMap Map = RealMapLoader.Load(new FileSystemDataSource(RepoPaths.Data));

    [Fact]
    public void LoadsOxfordAtTheExpectedSize()
    {
        Assert.Equal(400, Map.Grid.Width);
        Assert.Equal(10f, Map.Grid.TileSizeM);
        Assert.Equal(801, Map.Heights.Width);
        Assert.Equal(4000f, Map.Heights.WidthM);
        Assert.Equal(4000f, Map.SizeM);
    }

    [Fact]
    public void ElevationAtCampusCentre_MatchesUsgs()
    {
        // USGS 3DEP identify at 39.5087,-84.7337 returned 278.36 m (2026-09-30). The map centre is world (2000, 2000).
        float elevation = Map.Heights.BaseElevationM + Map.Heights.HeightAt(2000, 2000);
        Assert.InRange(elevation, 277.0f, 279.7f);
        Assert.InRange(Map.Heights.BaseElevationM, 225f, 240f);
    }

    [Fact]
    public void TileLayer_HasSensibleLandStates()
    {
        var counts = Map.Grid.LandState.GroupBy(s => s).ToDictionary(g => g.Key, g => g.Count() / (double)Map.Grid.LandState.Length);
        foreach (var s in new[] { LandState.Forest, LandState.Town, LandState.University, LandState.Protected, LandState.Water, LandState.Farmland })
            Assert.True(counts.GetValueOrDefault(s) > 0.001, $"{s} missing");
        // The campus centre is university land.
        int centre = Map.TileAt(2005, 2005);
        Assert.Equal(Ownership.University, Map.Grid.Ownership[centre]);
    }

    [Fact]
    public void Walkability_BuildingsAndWaterBlock_PathsCrossWater()
    {
        var g = Map.Grid;
        for (int i = 0; i < g.Types.Length; i++)
        {
            if (Map.HasBuilding[i]) Assert.Equal(TileType.Building, g.Types[i]);
            else if (g.PathType[i] is not (PathType.None or PathType.Railway)) Assert.Equal(TileType.Path, g.Types[i]);
        }
        Assert.Contains(TileType.Water, g.Types);
        Assert.False(g.IsWalkable(Array.IndexOf(g.Types, TileType.Water)));
    }

    [Fact]
    public void LandStateRules_FirstMatchWins()
    {
        var codes = new MapMetadata.CodeTables
        {
            Landcover = new() { ["open"] = 0, ["forest"] = 1, ["residential"] = 3 },
        };
        var rules = SimJson.Parse<LandStateRules>(File.ReadAllText(Path.Combine(RepoPaths.Data, "map", "land_states.json")), "land_states.json");
        var table = RealMapLoader.BuildStateTable(codes, rules);
        LandState At(int lc, int own, bool prot) => table[lc * 6 + own * 2 + (prot ? 1 : 0)];

        Assert.Equal(LandState.Protected, At(1, 2, true));    // protected beats forest
        Assert.Equal(LandState.Forest, At(1, 2, false));      // forest beats university ownership
        Assert.Equal(LandState.University, At(0, 2, false));
        Assert.Equal(LandState.Town, At(3, 1, false));
        Assert.Equal(LandState.Developed, At(3, 0, false));   // residential outside town limits
        Assert.Equal(LandState.Pasture, At(0, 0, false));     // fallback
    }

    [Fact]
    public void Heightmap_BilinearAndRaycast()
    {
        // 3×3 samples at 5 m: a ramp rising 1 m per metre eastward.
        var h = new Heightmap(3, 3, 5f, 100f, [0, 5, 10, 0, 5, 10, 0, 5, 10]);
        Assert.Equal(2.5f, h.HeightAt(2.5f, 3f), precision: 4);
        Assert.Equal(7.5f, h.HeightAt(7.5f, 9f), precision: 4);

        // Straight down onto (4, 4) from 50 m.
        Assert.True(h.Raycast(4, 50, 4, 0, -1, 0, 100, out float x, out float y, out float z));
        Assert.Equal(4f, x, precision: 3);
        Assert.Equal(4f, y, precision: 2);
        // Pointing up never hits.
        Assert.False(h.Raycast(4, 50, 4, 0, 1, 0, 100, out _, out _, out _));
    }

    [Fact]
    public void Heightmap_DecodesR16()
    {
        byte[] data = [0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x7F, 0x00, 0x00];
        var h = Heightmap.FromR16(data, 2, 2, 5f, 200f, 265.535f);
        Assert.Equal(0f, h.Sample(0, 0));
        Assert.Equal(65.535f, h.Sample(1, 0), precision: 3);
        Assert.Equal(32.767f, h.Sample(0, 1), precision: 2);
    }

    [Fact]
    public void Mesher_FlatShadedUpFacingSurface_WithSkirts()
    {
        var c = new Rgb(0.2f, 0.5f, 0.1f);
        var mesh = TerrainMesher.BuildChunk(Map.Heights, 10f, 200, 200, 40, 1, (_, _) => c, 5f);
        int surface = 40 * 40 * 2, skirts = 4 * 40 * 4;
        Assert.Equal(surface + skirts, mesh.TriangleCount);

        // Surface triangles: normals point up, and each triangle's three vertices share one normal (flat shading).
        for (int t = 0; t < surface; t++)
        {
            int v = t * 3;
            Assert.True(mesh.Normals[v * 3 + 1] > 0.5f, "surface normal should point up");
            for (int k = 1; k < 3; k++)
                for (int a = 0; a < 3; a++)
                    Assert.Equal(mesh.Normals[v * 3 + a], mesh.Normals[(v + k) * 3 + a]);
        }

        // Vertices lie on the heightmap at tile corners and inside the chunk.
        for (int v = 0; v < surface * 3; v++)
        {
            float x = mesh.Positions[v * 3], y = mesh.Positions[v * 3 + 1], z = mesh.Positions[v * 3 + 2];
            Assert.InRange(x, 2000f, 2400f);
            Assert.InRange(z, 2000f, 2400f);
            Assert.Equal(Map.Heights.HeightAt(x, z), y, precision: 3);
        }

        var lod = TerrainMesher.BuildChunk(Map.Heights, 10f, 200, 200, 40, 4, (_, _) => c, 5f);
        Assert.Equal(10 * 10 * 2 + 4 * 10 * 4, lod.TriangleCount);
    }
}
