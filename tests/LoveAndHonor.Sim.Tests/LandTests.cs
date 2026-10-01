using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Engine;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Tests;

/// <summary>Phase 1 (1d): the 1824 start, clearing and buying land, town growth.</summary>
public class LandTests
{
    private static readonly FileSystemDataSource Source = new(RepoPaths.Data);
    private static readonly SimData Data = SimData.Load(Source);

    private static SimWorld Chapter1(int threads = 2) => SimWorld.CreateScenario(Data, Source, "chapter1_the_hill", threads: threads, chunkSize: 256);

    /// <summary>A rectangle of university-owned forest inside the starting land, or null.</summary>
    private static LandCommand? ForestRect(TileGrid g, int size)
    {
        for (int y = 0; y + size < g.Height; y++)
            for (int x = 0; x + size < g.Width; x++)
            {
                bool all = true;
                for (int dy = 0; dy < size && all; dy++)
                    for (int dx = 0; dx < size && all; dx++)
                    {
                        int t = g.Index(x + dx, y + dy);
                        all = g.Ownership[t] == Ownership.University && g.LandState[t] == LandState.Forest;
                    }
                if (all) return new LandCommand(LandAction.Clear, x, y, x + size - 1, y + size - 1);
            }
        return null;
    }

    [Fact]
    public void Chapter1_StartsIn1824_WithOldMainAndATinyCollege()
    {
        var w = Chapter1();
        var g = w.Campus.Grid;
        Assert.Equal(new DateOnly(1824, 11, 1), w.Simulation.Time.Date);
        Assert.Equal(1, w.CampusReport!.CampusBuildings);
        Assert.Equal("old_main", w.Campus.Buildings[0].DefId);
        Assert.True(w.CampusReport.HousingZones >= 1, "students board in town");
        Assert.Equal(23, w.Population.Count);
        Assert.Equal(3000_00, w.Land!.Treasury.Cents);

        int university = g.Ownership.Count(o => o == Ownership.University);
        Assert.InRange(university, 700, 1000); // ~300 m square of 10 m tiles
        Assert.Contains(g.LandState, s => s == LandState.Town);
        Assert.True(g.LandState.Count(s => s == LandState.Forest) > g.LandState.Length / 3, "1824 Oxford is mostly forest");
        Assert.True(g.Types.Count(t => t == TileType.Building) < 200, "today's buildings aren't standing in 1824");
        Assert.NotNull(ForestRect(g, 3));
    }

    [Fact]
    public void Clearing_CostsMoney_TakesDays_AndTurnsForestIntoCampusLawn()
    {
        var w = Chapter1();
        var g = w.Campus.Grid;
        var land = w.Land!;
        var order = ForestRect(g, 4)!.Value; // 16 tiles
        var quote = LandSystem.Quote(LandConfig.Load(Source), EraTable.Load(Source), order, g.Width, g.Height,
            g.LandState, g.Ownership, g.Types, land.Clearing, w.Simulation.Time.Date);
        Assert.True(quote.Ok);
        Assert.Equal(16, quote.Tiles);
        Assert.Equal(16 * 100 * 0.02 * 100, quote.Cents, precision: 0); // $100/tile modern x 0.02 in 1824 = $2
        Assert.Equal(4.0, quote.Days);

        land.Enqueue(order);
        w.Simulation.ApplyLandCommands();
        Assert.Equal(3000_00 - quote.Cents, land.Treasury.Cents);
        Assert.Equal(16, land.ActiveClearingTiles);
        Assert.Contains(land.TakeMessages(), m => m.StartsWith("Clearing 16 tiles"));

        int t0 = g.Index(order.X0, order.Y0);
        Assert.Equal(TileType.Rough, g.Types[t0]);
        while (w.Simulation.Time.Day < 6) w.Simulation.Tick(); // 4 days of work (November: full speed)
        Assert.Equal(0, land.ActiveClearingTiles);
        Assert.Equal(LandState.Pasture, g.LandState[t0]);
        Assert.Equal(TileType.Grass, g.Types[t0]); // university land, cleared → lawn
    }

    [Fact]
    public void Buying_NeedsLandTouchingTheCampus_AndEnoughMoney()
    {
        var w = Chapter1();
        var g = w.Campus.Grid;
        var land = w.Land!;
        // A strip just east of the starting square.
        int maxX = 0, minY = g.Height, maxY = 0;
        for (int t = 0; t < g.Ownership.Length; t++)
            if (g.Ownership[t] == Ownership.University)
            {
                maxX = Math.Max(maxX, t % g.Width); minY = Math.Min(minY, t / g.Width); maxY = Math.Max(maxY, t / g.Width);
            }
        var next = new LandCommand(LandAction.Buy, maxX + 1, minY + 5, maxX + 3, minY + 9);
        var farAway = new LandCommand(LandAction.Buy, maxX + 20, minY, maxX + 25, minY + 5);

        land.Enqueue(farAway);
        land.Enqueue(next);
        w.Simulation.ApplyLandCommands();
        var messages = land.TakeMessages();
        Assert.Contains(messages, m => m.Contains("next to university land"));
        Assert.Contains(messages, m => m.StartsWith("Bought 15 tiles"));
        Assert.Equal(Ownership.University, g.Ownership[g.Index(maxX + 2, minY + 7)]);
        Assert.NotEqual(Ownership.University, g.Ownership[g.Index(maxX + 22, minY + 2)]);
        Assert.True(land.Treasury.Cents < 3000_00);

        // A purchase bigger than the treasury is refused and costs nothing.
        long before = land.Treasury.Cents;
        land.Enqueue(new LandCommand(LandAction.Buy, maxX + 1, 0, maxX + 60, g.Height - 1));
        w.Simulation.ApplyLandCommands();
        Assert.Contains(land.TakeMessages(), m => m.StartsWith("Not enough money"));
        Assert.Equal(before, land.Treasury.Cents);
    }

    [Fact]
    public void Town_GrowsOverTheYears()
    {
        var w = Chapter1();
        var g = w.Campus.Grid;
        int town = g.LandState.Count(s => s == LandState.Town);
        while (w.Simulation.Time.Date < new DateOnly(1826, 11, 2)) w.Simulation.Tick();
        Assert.True(g.LandState.Count(s => s == LandState.Town) > town, "the town spreads by the land-history rules");
        Assert.All(Enumerable.Range(0, g.Ownership.Length).Where(t => g.Ownership[t] == Ownership.University),
            t => Assert.NotEqual(LandState.Town, g.LandState[t]));
    }

    [Fact]
    public void LandOrders_AreDeterministicAcrossThreadCounts()
    {
        ulong Run(int threads)
        {
            var w = Chapter1(threads);
            var order = ForestRect(w.Campus.Grid, 3)!.Value;
            while (w.Simulation.Time.Tick < 72)
            {
                if (w.Simulation.Time.Tick == 5) w.Land!.Enqueue(order);
                w.Simulation.Tick();
            }
            return StateHash.Compute(w.Simulation, w.Campus.Grid) ^ (ulong)w.Land!.Treasury.Cents;
        }
        Assert.Equal(Run(1), Run(4));
    }
}
