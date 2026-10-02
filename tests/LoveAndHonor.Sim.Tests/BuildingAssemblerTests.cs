using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.View;
using LoveAndHonor.Tools;

namespace LoveAndHonor.Sim.Tests;

/// <summary>Phase 1 (1f): the procedural kit assembler.</summary>
public class BuildingAssemblerTests
{
    private static readonly FileSystemDataSource Source = new(RepoPaths.Data);
    private static readonly BuildingRecipesConfig Recipes = BuildingRecipesConfig.Load(Source);
    private static readonly ArtPipelineConfig Art = ArtPipelineConfig.Load(Source);
    private static float Bay => Art.KitGrid.ModuleWidthM;
    private static float Storey => Art.KitGrid.StoreyHeightM;

    private static AssembledBuilding Build(string recipe, float w, float d, float progress = 1f) =>
        BuildingAssembler.Assemble(Recipes, Recipes.Recipes[recipe], w, d, Bay, Storey, 0.5f, progress);

    /// <summary>Triangles of each level of detail of a kit piece, read from its .glb.</summary>
    private static readonly Dictionary<string, int[]> PieceLods = Directory
        .EnumerateFiles(Path.Combine(RepoPaths.Root, "assets", "models", "kit", "georgian"), "*.glb")
        .ToDictionary(f => Path.GetFileNameWithoutExtension(f), f =>
        {
            var nodes = ModelValidation.ReadGlb(File.ReadAllBytes(f)).MeshNodes;
            return Enumerable.Range(0, 4).Select(l => nodes.Where(n => n.Name.EndsWith($"_LOD{l}")).Sum(n => n.Triangles)).TakeWhile(x => x > 0).ToArray();
        });
    private static readonly Dictionary<string, int> PieceTris = PieceLods.ToDictionary(kv => kv.Key, kv => kv.Value[0]);

    private static int Triangles(AssembledBuilding b) =>
        b.Pieces.Sum(p => PieceTris[p.Piece]) + b.Generated.Values.Sum(m => m.TriangleCount);

    /// <summary>Middle distance: pieces with an _LOD1 use it, kept pieces stay, small details are dropped.</summary>
    private static int TrianglesLod1(AssembledBuilding b) =>
        b.Pieces.Sum(p => PieceLods[p.Piece].Length > 1 ? PieceLods[p.Piece][1] : BuildingAssembler.KeptAtDistance(p.Piece) ? PieceTris[p.Piece] : 0)
        + b.Generated.Values.Sum(m => m.TriangleCount);

    [Fact]
    public void GenericHall_HasBaysDoorPorticoCupolaAndChimneys()
    {
        var b = Build("georgian_hall", 33, 15);
        int storeys = Recipes.Recipes["georgian_hall"].Storeys;
        int perimeterBays = 2 * (11 + 5);
        Assert.Single(b.Pieces, p => p.Piece == BuildingAssembler.WallDoor);
        Assert.Equal(perimeterBays * storeys - 1, b.Pieces.Count(p => p.Piece == BuildingAssembler.WallWindow));
        Assert.Equal(4 * storeys, b.Pieces.Count(p => p.Piece == BuildingAssembler.Quoin));
        Assert.Equal(4, b.Pieces.Count(p => p.Piece == BuildingAssembler.Column));
        Assert.Single(b.Pieces, p => p.Piece == BuildingAssembler.Pediment);
        Assert.Single(b.Pieces, p => p.Piece == BuildingAssembler.CupolaPiece);
        Assert.Equal(2, b.Pieces.Count(p => p.Piece == BuildingAssembler.Chimney));
        Assert.Equal(4, b.Pieces.Count(p => p.Piece == BuildingAssembler.CorniceCorner));
        Assert.Equal(storeys * Storey, b.WallTopY);
        Assert.True(b.RidgeY > b.WallTopY);
        Assert.False(b.Generated.ContainsKey(BuildingAssembler.Wood), "no scaffolding on a finished building");

        // The door is on the front wall, in the middle bay, facing the front (+Z).
        var door = b.Pieces.Single(p => p.Piece == BuildingAssembler.WallDoor);
        Assert.Equal(0, door.YawDeg, 3);
        Assert.Equal(15 / 2f, door.Z, 3);
        Assert.InRange(door.X, -3.1f, 0.1f);
    }

    [Theory]
    [InlineData("georgian_hall", 20, 12)]
    [InlineData("georgian_hall", 37.5f, 13.4f)]
    [InlineData("georgian_hall_l", 48, 38)]
    [InlineData("frame_one_storey", 18, 8)]
    public void WallBays_FillEveryWall_StretchedOnlyALittle(string recipe, float w, float d)
    {
        var b = Build(recipe, w, d);
        var walls = b.Pieces.Where(p => p.Piece is BuildingAssembler.WallWindow or BuildingAssembler.WallDoor or BuildingAssembler.WallPlain && p.Y == 0).ToList();
        Assert.All(walls, p => Assert.InRange(p.ScaleX, 0.8f, 1.25f));
        // Ground-floor bays cover the whole perimeter.
        float perimeter = 0;
        for (int i = 0; i < b.Outline.Length; i++)
        {
            var (ax, az) = b.Outline[i]; var (bx, bz) = b.Outline[(i + 1) % b.Outline.Length];
            perimeter += MathF.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
        }
        Assert.Equal(perimeter, walls.Sum(p => p.ScaleX * Bay), 2);
    }

    [Fact]
    public void LShape_HasSixWallsFiveOuterCornersAndMergedRoofs()
    {
        var b = Build("georgian_hall_l", 48, 38);
        int storeys = Recipes.Recipes["georgian_hall_l"].Storeys;
        Assert.Equal(6, b.Outline.Length);
        Assert.Equal(5 * storeys, b.Pieces.Count(p => p.Piece == BuildingAssembler.Quoin));
        Assert.Equal(5, b.Pieces.Count(p => p.Piece == BuildingAssembler.CorniceCorner));
        Assert.Equal(2 * (6 + 2), b.Generated[BuildingAssembler.Roof].TriangleCount); // two hip roofs + two soffits
    }

    [Fact]
    public void UnderConstruction_WallsRiseThenTheRoof_WithScaffolding()
    {
        var hall = Recipes.Recipes["georgian_hall"];
        var early = Build("georgian_hall", 33, 15, progress: 0.3f);
        var late = Build("georgian_hall", 33, 15, progress: 0.9f);
        var done = Build("georgian_hall", 33, 15);
        Assert.True(early.Pieces.Count(p => p.Piece == BuildingAssembler.WallWindow) < late.Pieces.Count(p => p.Piece == BuildingAssembler.WallWindow));
        Assert.All(early.Pieces.Where(p => p.Piece == BuildingAssembler.WallWindow), p => Assert.True(p.Y < 0.3f / 0.8f * hall.Storeys * Storey));
        Assert.False(early.Generated.ContainsKey(BuildingAssembler.Roof));
        Assert.True(early.Generated[BuildingAssembler.Wood].TriangleCount > 0);
        Assert.True(late.Generated.ContainsKey(BuildingAssembler.Roof));
        Assert.DoesNotContain(late.Pieces, p => p.Piece == BuildingAssembler.CupolaPiece);
        Assert.Contains(done.Pieces, p => p.Piece == BuildingAssembler.CupolaPiece);
    }

    [Fact]
    public void GableRoof_HasGableEndWalls()
    {
        var b = Build("elliott_hall", 13, 32);
        Assert.Equal(2, b.Generated[BuildingAssembler.WallMaterial].TriangleCount);
        Assert.Equal(4 + 2, b.Generated[BuildingAssembler.Roof].TriangleCount); // two slopes + soffit
        Assert.Equal(4, b.Pieces.Count(p => p.Piece == BuildingAssembler.Chimney));
    }

    [Theory]
    [InlineData("elliott_hall", 13, 32)]
    [InlineData("georgian_hall", 33, 15)]
    [InlineData("frame_two_storey", 18, 8)]
    public void TypicalBuildings_StayWithinTheTriangleBudget(string recipe, float w, float d)
    {
        // §28.1a: a typical building is 1-5k triangles; the middle distance a fraction; the far block a few dozen.
        var b = Build(recipe, w, d);
        int tris = Triangles(b);
        Assert.InRange(tris, 300, Art.Categories["bldg"].MaxTris);
        Assert.True(TrianglesLod1(b) <= tris / 2, $"LOD1 {TrianglesLod1(b)} vs LOD0 {tris}");
        Assert.InRange(b.Massing.Values.Sum(m => m.TriangleCount), 8, 60);
    }

    [Theory]
    [InlineData("old_main", 52, 21)]
    [InlineData("georgian_hall_l", 48, 38)]
    [InlineData("georgian_hall_l", 58, 58)]
    public void LargeBuildings_CostAtMost35TrianglesPerWindowBay(string recipe, float w, float d)
    {
        // Big halls have more facade than §28.1a's "typical" building: hold them to the per-bay cost instead, and
        // keep the middle distance (where most of the campus is seen from) within the 5k budget.
        var b = Build(recipe, w, d);
        int bays = b.Pieces.Count(p => p.Piece is BuildingAssembler.WallWindow or BuildingAssembler.WallDoor or BuildingAssembler.WallPlain);
        Assert.True(Triangles(b) <= 35 * bays, $"{Triangles(b)} triangles for {bays} bays");
        Assert.True(TrianglesLod1(b) <= Art.Categories["bldg"].MaxTris, $"LOD1 {TrianglesLod1(b)}");
    }

    [Fact]
    public void Recipes_ReferenceRealPiecesStylesAndFinishes()
    {
        foreach (var (id, r) in Recipes.Recipes)
        {
            Assert.True(Recipes.WindowStyles.ContainsKey(r.Windows), id);
            Assert.True(Recipes.Walls.ContainsKey(r.Walls), id);
            foreach (var p in Build(id, 30, 15).Pieces) Assert.True(PieceTris.ContainsKey(p.Piece), $"{id}: missing kit piece {p.Piece}");
        }
        Assert.Equal("elliott_hall", Recipes.RecipeForTimeline("elliott_hall"));
        Assert.Null(Recipes.RecipeForTimeline("_comment"));
    }
}
