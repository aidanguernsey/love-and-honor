using LoveAndHonor.Sim.Pathing;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Tests;

public class FlowFieldTests
{
    private const float TileM = 10f;

    /// <summary>Two 1×1 "buildings" whose entrances are at the given tiles; everything else as painted by the caller.</summary>
    private static Campus TwoBuildings(TileGrid grid, (int x, int y) entranceA, (int x, int y) entranceB)
    {
        CampusBuilding Make(short index, (int x, int y) e)
        {
            // Building tile directly above the entrance.
            grid.SetType(e.x, e.y - 1, TileType.Building);
            grid.BuildingAt[grid.Index(e.x, e.y - 1)] = index;
            return new CampusBuilding
            {
                Index = index, DefId = "test", Kind = BuildingKind.Other,
                X = e.x, Y = e.y - 1, W = 1, H = 1, EntranceTile = grid.Index(e.x, e.y),
            };
        }
        return new Campus(grid, [Make(0, entranceA), Make(1, entranceB)]);
    }

    [Fact]
    public void OpenGrass_DistanceIsStraightLine()
    {
        var grid = new TileGrid(30, 10, TileM);
        var campus = TwoBuildings(grid, (3, 5), (26, 5));
        var fields = new FlowFieldSet(campus, grassCostMultiplier: 1f, threads: 1);
        fields.Build();

        Assert.Equal(23 * TileM, fields.Distance(0, 1), precision: 3);
        Assert.Equal(fields.Distance(0, 1), fields.Distance(1, 0), precision: 3);
        Assert.Equal(0f, fields.Distance(0, 0));
    }

    [Fact]
    public void Wall_ForcesDetour_AndRouteIsContinuousAndAvoidsBuildings()
    {
        var grid = new TileGrid(30, 12, TileM);
        var campus = TwoBuildings(grid, (3, 5), (26, 5));
        grid.FillRect(14, 0, 2, 10, TileType.Building); // gap at y = 10, 11
        var fields = new FlowFieldSet(campus, 1f, 1);
        fields.Build();

        Assert.True(fields.Distance(0, 1) > 23 * TileM);
        var route = fields.Route(0, 1);
        Assert.Equal(campus.Buildings[0].EntranceTile, route[0]);
        Assert.Equal(campus.Buildings[1].EntranceTile, route[^1]);
        for (int i = 0; i < route.Length; i++)
        {
            Assert.NotEqual(TileType.Building, grid.Types[route[i]]);
            if (i == 0) continue;
            int dx = Math.Abs(route[i] % grid.Width - route[i - 1] % grid.Width);
            int dy = Math.Abs(route[i] / grid.Width - route[i - 1] / grid.Width);
            Assert.True(dx <= 1 && dy <= 1 && dx + dy > 0, "route must move one tile at a time");
        }
    }

    /// <summary>L-shaped path between two entrances. Expensive grass → walkers keep to the path; cheap grass → they cut across (desire path).</summary>
    [Theory]
    [InlineData(3.0f, false)]
    [InlineData(1.0f, true)]
    public void GrassCost_DecidesBetweenPathAndShortcut(float grassMultiplier, bool expectShortcut)
    {
        var grid = new TileGrid(26, 16, TileM);
        var campus = TwoBuildings(grid, (2, 3), (22, 13));
        grid.FillRect(2, 3, 21, 1, TileType.Path);   // row y=3 from x=2..22
        grid.FillRect(22, 3, 1, 11, TileType.Path);  // column x=22 from y=3..13
        var fields = new FlowFieldSet(campus, grassMultiplier, 1);
        fields.Build();

        bool usesGrass = fields.Route(0, 1).Any(t => grid.Types[t] == TileType.Grass);
        Assert.Equal(expectShortcut, usesGrass);
    }

    [Fact]
    public void MapChange_MarksFieldsStale_AndRebuildReflectsIt()
    {
        var grid = new TileGrid(30, 12, TileM);
        var campus = TwoBuildings(grid, (3, 5), (26, 5));
        var fields = new FlowFieldSet(campus, 1f, 1);
        fields.Build();
        float before = fields.Distance(0, 1);
        Assert.False(fields.IsStale);

        grid.FillRect(14, 0, 2, 10, TileType.Building);
        Assert.True(fields.IsStale);
        Assert.True(fields.EnsureCurrent());
        Assert.False(fields.EnsureCurrent());
        Assert.True(fields.Distance(0, 1) > before);
    }

    [Fact]
    public void Unreachable_GivesInfiniteDistanceAndEmptyRoute()
    {
        var grid = new TileGrid(30, 10, TileM);
        var campus = TwoBuildings(grid, (3, 5), (26, 5));
        grid.FillRect(14, 0, 1, 10, TileType.Building); // full wall
        var fields = new FlowFieldSet(campus, 1f, 1);
        fields.Build();

        Assert.True(float.IsPositiveInfinity(fields.Distance(0, 1)));
        Assert.Empty(fields.Route(0, 1));
    }
}
