using LoveAndHonor.Sim.Core;
using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Engine;
using LoveAndHonor.Sim.Pathing;
using LoveAndHonor.Sim.Population;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Tests;

/// <summary>Phase 1 (1b): the walking model v2 (§12.4 v0.4) with the real balance.json values.</summary>
public class WalkingModelTests
{
    private const float TileM = 10f;
    private static readonly SimData Data = SimData.Load(new FileSystemDataSource(RepoPaths.Data));

    private static CampusBuilding At(short index, TileGrid grid, int x, int y, BuildingKind kind = BuildingKind.Academic) => new()
    {
        Index = index, DefId = "test", Kind = kind, X = x, Y = y, W = 1, H = 1, EntranceTile = grid.Index(x, y),
    };

    /// <summary>A square lawn of side <paramref name="side"/> tiles inside a path ring, water outside; buildings at
    /// opposite corners. Returns how many lawn tiles the route between them crosses.</summary>
    private static int LawnTilesOnRoute(int side)
    {
        int size = side + 3;
        var grid = new TileGrid(size, size, TileM);
        Array.Fill(grid.Types, TileType.Water);
        for (int y = 1; y <= side + 1; y++)
            for (int x = 1; x <= side + 1; x++)
                grid.Types[grid.Index(x, y)] = x == 1 || y == 1 || x == side + 1 || y == side + 1 ? TileType.Path : TileType.Grass;
        var campus = new Campus(grid, [At(0, grid, 1, 1), At(1, grid, side + 1, side + 1)]);
        var fields = new FlowFieldSet(campus, Data.Balance.Walking.Costs, threads: 1);
        fields.Build();
        return fields.Route(0, 1).Count(t => grid.Types[t] == TileType.Grass);
    }

    [Fact]
    public void BigQuad_IsCutDiagonally_SmallLawnIsWalkedAround()
    {
        Assert.True(LawnTilesOnRoute(12) > 0, "a 120 m quad's diagonal should be worth cutting");
        Assert.Equal(0, LawnTilesOnRoute(4)); // a 40 m lawn isn't: the path round it is nearly as short
    }

    [Fact]
    public void RoughGround_IsAvoidedWhenARoadGoesRound()
    {
        // A 9-tile-wide field between two points; a road goes round it (6 tiles longer each way of the detour).
        var grid = new TileGrid(13, 8, TileM);
        Array.Fill(grid.Types, TileType.Rough);
        for (int x = 0; x < 13; x++) grid.Types[grid.Index(x, 7)] = TileType.Path;
        for (int y = 0; y < 8; y++) { grid.Types[grid.Index(0, y)] = TileType.Path; grid.Types[grid.Index(12, y)] = TileType.Path; }
        var campus = new Campus(grid, [At(0, grid, 0, 2), At(1, grid, 12, 2)]);
        var fields = new FlowFieldSet(campus, Data.Balance.Walking.Costs, 1);
        fields.Build();
        Assert.DoesNotContain(fields.Route(0, 1), t => grid.Types[t] == TileType.Rough);

        // Same field as mown lawn: crossing it is worth it.
        for (int i = 0; i < grid.Types.Length; i++) if (grid.Types[i] == TileType.Rough) grid.Types[i] = TileType.Grass;
        grid.MarkChanged();
        fields.Build();
        Assert.Contains(fields.Route(0, 1), t => grid.Types[t] == TileType.Grass);
    }

    [Fact]
    public void PathExitPenalty_KeepsRoutesSymmetric()
    {
        var grid = new TileGrid(20, 20, TileM);
        for (int i = 0; i < 20; i++) { grid.Types[grid.Index(i, 10)] = TileType.Path; grid.Types[grid.Index(5, i)] = TileType.Path; }
        var campus = new Campus(grid, [At(0, grid, 1, 1), At(1, grid, 18, 17), At(2, grid, 9, 3)]);
        var fields = new FlowFieldSet(campus, Data.Balance.Walking.Costs, 1);
        fields.Build();
        for (int a = 0; a < 3; a++)
            for (int b = 0; b < 3; b++)
                Assert.Equal(fields.Distance(a, b), fields.Distance(b, a), precision: 3);
    }

    [Fact]
    public void Wear_BuildsFromRegularTraffic_AndGrowsBack()
    {
        var grid = new TileGrid(3, 1, TileM);
        grid.Types[2] = TileType.Path;
        var w = Data.Balance.Walking.DesirePaths;
        float wear = 1f / w.DaysToWear, regrow = 1f / w.DaysToRegrow;
        for (int day = 0; day < (int)Math.Ceiling(w.DaysToWear); day++)
        {
            grid.FootTraffic[0] += w.MinWalkersPerDay;      // regular shortcut
            grid.FootTraffic[1] += w.MinWalkersPerDay - 1;  // occasional crossings
            grid.FootTraffic[2] += 10_000;                  // a paved path
            grid.UpdateWear(w.MinWalkersPerDay, wear, regrow);
        }
        Assert.True(grid.Wear[0] >= TileGrid.DesirePathWear, $"wear {grid.Wear[0]}");
        Assert.Equal(0f, grid.Wear[1]);
        Assert.Equal(0f, grid.Wear[2]);
        Assert.Equal(1, grid.CountDesirePaths());
        Assert.Equal(grid.FootTraffic, grid.TrafficAtMidnight);

        for (int day = 0; day < (int)Math.Ceiling(w.DaysToRegrow); day++) grid.UpdateWear(w.MinWalkersPerDay, wear, regrow);
        Assert.Equal(0f, grid.Wear[0]);
    }

    [Fact]
    public void Students_LeaveBeforeTheHour_AndAreLateOnlyAfterABackToBackClass()
    {
        var world = SimWorld.CreateSynthetic(Data, threads: 2, students: 2000, faculty: 200, chunkSize: 256);
        var sim = world.Simulation;
        var p = world.Population;
        int window = (int)Data.Balance.Time.ClassChangeWindowMinutes;
        var walkers = new int[p.Count];
        int classWalks = 0, lateChecked = 0;
        while (sim.Time.Tick < 24 * 2)
        {
            var before = (Activity[])p.CurrentActivity.Clone();
            var previousArrival = (int[])p.WalkArriveMinute.Clone();
            int tickStart = sim.Time.TickStartMinute;
            sim.Tick();
            int n = sim.CopyLastTickWalkers(walkers);
            int late = 0;
            for (int i = 0; i < n; i++)
            {
                int a = walkers[i];
                if (p.CurrentActivity[a] is not (Activity.Class or Activity.Teach)) continue;
                classWalks++;
                int arrive = p.WalkArriveMinute[a];
                bool backToBack = before[a] is Activity.Class or Activity.Teach;
                // Late without a class just before only if the previous walk ended too late to leave in time.
                if (!backToBack)
                    Assert.True(arrive <= tickStart || p.WalkDepartMinute[a] == previousArrival[a],
                        $"agent {a} left too late for a class at {tickStart}: depart {p.WalkDepartMinute[a]}, arrive {arrive}");
                else Assert.True(p.WalkDepartMinute[a] >= tickStart - window, "left before the previous class ended");
                if (arrive > tickStart) late++;
            }
            Assert.Equal(late, sim.LastTick.LateArrivals);
            lateChecked += late;
        }
        Assert.True(classWalks > 1000, $"only {classWalks} class walks");
    }

    [Fact]
    public void FreeTime_StaysTheSameWithinABlock()
    {
        var p = SimWorld.CreateSynthetic(Data, threads: 1, students: 1000, faculty: 10, chunkSize: 256).Population;
        var schedule = new ScheduleModel(Data.Schedules, new RngStreams(Data.Spike.Seed));
        int block = Data.Schedules.Student.FreeBlockHours;
        Assert.True(block >= 2);
        int pairs = 0;
        const int saturday = 5, day = 5;
        for (int a = 0; a < 1000; a++)
            for (int h = 13; h < 17; h++)
            {
                if ((h + a % block) / block != (h + 1 + a % block) / block) continue; // block boundary
                short b1 = schedule.Resolve(p, a, day, saturday, h, out var act1);
                short b2 = schedule.Resolve(p, a, day, saturday, h + 1, out var act2);
                if (act1 is Activity.Eat or Activity.Sleep || act2 is Activity.Eat or Activity.Sleep) continue;
                Assert.Equal(act1, act2);
                Assert.Equal(b1, b2);
                pairs++;
            }
        Assert.True(pairs > 500, $"only {pairs} pairs checked");
    }

    [Fact]
    public void RealMap_WorldIsDeterministicAcrossThreadCounts_WithAPathEdit()
    {
        var source = new FileSystemDataSource(RepoPaths.Data);
        ulong Run(int threads)
        {
            var w = SimWorld.CreateReal(Data, source, threads: threads, students: 2000, faculty: 200, chunkSize: 256);
            Assert.True(w.CampusReport!.CampusBuildings > 50);
            Assert.True(w.CampusReport.HousingZones > 20);
            Assert.Contains(w.Campus.Grid.Types, t => t == TileType.Rough);
            int edit = -1;
            while (w.Simulation.Time.Tick < 60)
            {
                if (w.Simulation.Time.Tick == 10)
                {
                    var grid = w.Campus.Grid;
                    int e = w.Campus.Buildings[0].EntranceTile;
                    int ex = e % grid.Width, ey = e / grid.Width;
                    edit = Enumerable.Range(0, 81).Select(k => grid.Index(ex + k % 9 - 4, ey + k / 9 - 4)).First(grid.IsUnpaved);
                    w.Simulation.ApplyTileEdits([new TileEdit(edit, TileType.Path)]);
                }
                w.Simulation.Tick();
            }
            Assert.Equal(-1, w.Simulation.RebuildApplyTick); // swapped in at tick 10 + latency
            return StateHash.Compute(w.Simulation, w.Campus.Grid);
        }
        Assert.Equal(Run(1), Run(4));
    }
}
