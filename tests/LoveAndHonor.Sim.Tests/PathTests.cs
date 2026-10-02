using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Engine;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Tests;

/// <summary>Phase 1 (1g): laying paths, removing them, paving desire paths, the Slant Walk.</summary>
public class PathTests
{
    private static readonly FileSystemDataSource Source = new(RepoPaths.Data);
    private static readonly SimData Data = SimData.Load(Source);
    private static readonly PathConfig Cfg = PathConfig.Load(Source);
    private static readonly EraTable Eras = EraTable.Load(Source);

    /// <summary>An all-university lawn map, w × h, with optional obstacles.</summary>
    private static PathMap Lawn(int w, int h, Action<TileType[], LandState[]>? edit = null)
    {
        int n = w * h;
        var types = Enumerable.Repeat(TileType.Grass, n).ToArray();
        var states = Enumerable.Repeat(LandState.Pasture, n).ToArray();
        edit?.Invoke(types, states);
        return new PathMap(w, h, 10, types, states, Enumerable.Repeat(Ownership.University, n).ToArray(), new bool[n], new byte[n],
            new float[n], new PathType[n]);
    }

    [Fact]
    public void Route_IsStraightOnOpenLawn_IncludingDiagonals()
    {
        var m = Lawn(30, 30);
        var straight = PathPlanner.Route(Cfg, m, 5 * 30 + 2, 5 * 30 + 20, out _)!;
        Assert.Equal(19, straight.Count);
        Assert.All(straight, t => Assert.Equal(5, t / 30));
        var diagonal = PathPlanner.Route(Cfg, m, 2 * 30 + 2, 14 * 30 + 14, out _)!;
        Assert.Equal(13, diagonal.Count);
        Assert.All(diagonal, t => Assert.Equal(t / 30, t % 30));
    }

    [Fact]
    public void Route_GoesAroundBuildingsAndForest_AndJoinsExistingPaths()
    {
        var m = Lawn(30, 30, (types, states) =>
        {
            for (int y = 0; y < 18; y++) types[y * 30 + 10] = TileType.Building;   // a wall with a gap below it
            for (int x = 0; x < 30; x++) types[27 * 30 + x] = TileType.Path;      // an existing path along row 27
            states[26 * 30 + 15] = LandState.Forest;
        });
        var route = PathPlanner.Route(Cfg, m, 5 * 30 + 5, 5 * 30 + 15, out var problem)!;
        Assert.Equal("", problem);
        Assert.DoesNotContain(route, t => m.Types[t] == TileType.Building || m.States[t] == LandState.Forest);
        Assert.Contains(route, t => t / 30 >= 18); // around the end of the wall
        // Paths are cheap to walk along, so a long detour joins the existing one rather than cutting new lawn.
        var along = PathPlanner.Route(Cfg, m, 26 * 30 + 1, 26 * 30 + 28, out _)!;
        Assert.True(along.Count(t => m.Types[t] == TileType.Path) > 20);

        var blocked = Lawn(10, 10, (types, _) => { for (int y = 0; y < 10; y++) types[y * 10 + 5] = TileType.Water; });
        Assert.Null(PathPlanner.Route(Cfg, blocked, 2, 8, out problem));
        Assert.StartsWith("No route", problem);
    }

    [Fact]
    public void Surface_FollowsTheEra()
    {
        string At(int year) => Cfg.Surfaces[Cfg.SurfaceFor(Eras, year)].Id;
        Assert.Equal("dirt", At(1824));
        Assert.Equal("gravel", At(1890));
        Assert.Equal("brick", At(1920));
        Assert.Equal("brick", At(2026)); // preferred over concrete once both exist
    }

    [Fact]
    public void LayingAndRemovingPaths_ChangeTheWalkingSurface()
    {
        var w = SimWorld.CreateScenario(Data, Source, "chapter1_the_hill", threads: 2, chunkSize: 256);
        var g = w.Campus.Grid;
        var land = w.Land!;
        // Two lawn tiles 6 apart on one row of university land.
        int start = Enumerable.Range(0, g.Width * g.Height).First(t =>
            Enumerable.Range(0, 7).All(k => t % g.Width + k < g.Width && g.Types[t + k] == TileType.Grass && g.Ownership[t + k] == Ownership.University));
        int x = start % g.Width, y = start / g.Width;
        long cash = land.Treasury.Cents;
        land.Enqueue(new LandCommand(LandAction.Path, x, y, x + 6, y));
        w.Simulation.ApplyPendingCommands();
        var laid = land.TakeMessages();
        Assert.True(laid.Any(m => m.StartsWith("Laid 7 tiles of dirt path")), string.Join(" / ", laid));
        for (int k = 0; k <= 6; k++)
        {
            Assert.Equal(TileType.Path, g.Types[start + k]);
            Assert.Equal(1, land.PathSurface[start + k]); // dirt
        }
        Assert.Equal(cash - (long)Math.Round(7 * 20 * 0.02 * 100), land.Treasury.Cents);
        Assert.True(w.Simulation.RebuildApplyTick >= 0, "walking routes are recomputed");

        land.Enqueue(new LandCommand(LandAction.RemovePath, x, y, x + 6, y));
        w.Simulation.ApplyPendingCommands();
        Assert.Contains(land.TakeMessages(), m => m == "Removed 7 tiles of path.");
        Assert.All(Enumerable.Range(0, 7), k => Assert.Equal(TileType.Grass, g.Types[start + k]));
    }

    [Fact]
    public void PavingADiagonalDesirePath_MakesTheSlantWalk_Once()
    {
        var w = SimWorld.CreateScenario(Data, Source, "chapter1_the_hill", threads: 2, chunkSize: 256);
        var g = w.Campus.Grid;
        var land = w.Land!;
        // Fake a long diagonal desire path (in play: students wear it in over weeks, 1b) on cleared university land.
        int x0 = Enumerable.Range(0, g.Width).First(x => Enumerable.Range(0, g.Height).Any(y => g.Ownership[g.Index(x, y)] == Ownership.University));
        int y0 = Enumerable.Range(0, g.Height).First(y => g.Ownership[g.Index(x0, y)] == Ownership.University);
        var diagonal = Enumerable.Range(2, 12).Select(k => g.Index(x0 + k, y0 + k)).ToList();
        var straight = Enumerable.Range(2, 12).Select(k => g.Index(x0 + k, y0 + 20)).ToList();
        foreach (int t in diagonal.Concat(straight))
        {
            g.LandState[t] = LandState.Pasture;
            g.Types[t] = TileType.Grass;
            g.Wear[t] = 1f;
        }

        // A straight east-west shortcut is paved but isn't the Slant Walk.
        land.Enqueue(new LandCommand(LandAction.PaveDesire, straight[3] % g.Width, straight[3] / g.Width, 0, 0));
        w.Simulation.ApplyPendingCommands();
        Assert.All(straight, t => Assert.Equal(TileType.Path, g.Types[t]));
        Assert.Empty(land.SlantWalk);

        land.Enqueue(new LandCommand(LandAction.PaveDesire, diagonal[5] % g.Width, diagonal[5] / g.Width, 0, 0));
        w.Simulation.ApplyPendingCommands();
        // The player is asked (answers to Q16): declining keeps the honour free...
        Assert.Contains(land.TakeMessages(), m => m.Contains("could become the Slant Walk"));
        Assert.Equal(diagonal.Order(), land.SlantWalkCandidate.Order());
        Assert.Empty(land.SlantWalk);
        land.Enqueue(new LandCommand(LandAction.DeclineSlantWalk, 0, 0, 0, 0));
        w.Simulation.ApplyPendingCommands();
        Assert.Empty(land.SlantWalkCandidate);
        Assert.Empty(land.SlantWalk);
        Assert.Equal(0, land.HeritageBonus);
        // ...and naming one makes it the Slant Walk. (Re-ask by marking the same tiles as a candidate again.)
        foreach (int t in diagonal) { g.Types[t] = TileType.Grass; g.Wear[t] = 1f; }
        land.Enqueue(new LandCommand(LandAction.PaveDesire, diagonal[5] % g.Width, diagonal[5] / g.Width, 0, 0));
        land.Enqueue(new LandCommand(LandAction.DesignateSlantWalk, 0, 0, 0, 0));
        w.Simulation.ApplyPendingCommands();
        var messages = land.TakeMessages();
        Assert.Contains(messages, m => m.Contains("is now the Slant Walk"));
        Assert.Equal(diagonal.Order(), land.SlantWalk.Order());
        Assert.Equal(Cfg.SlantWalk.HeritageBonus, land.HeritageBonus);
        Assert.All(diagonal, t => Assert.Equal(0f, g.Wear[t] < TileGrid.DesirePathWear ? 0f : 0f));

        // Nothing worn under the cursor: nothing to pave.
        var q = LandSystem.QuotePaths(Cfg, Eras, new LandCommand(LandAction.PaveDesire, x0 + 30, y0 + 5, 0, 0), land.LivePathMap(), w.Simulation.Time.Date, out _);
        Assert.False(q.Ok);
    }

    [Fact]
    public void SlantWalkShape_NeedsALongDiagonal()
    {
        var diag = Enumerable.Range(0, 12).Select(k => (50 + k) * 100 + 50 + k).ToList();
        var shortDiag = diag.Take(5).ToList();
        var east = Enumerable.Range(0, 12).Select(k => 50 * 100 + 50 + k).ToList();
        Assert.True(PathPlanner.SlantWalk(Cfg, diag, 100, 10).Qualifies);
        Assert.False(PathPlanner.SlantWalk(Cfg, shortDiag, 100, 10).Qualifies);
        Assert.False(PathPlanner.SlantWalk(Cfg, east, 100, 10).Qualifies);
    }
}
