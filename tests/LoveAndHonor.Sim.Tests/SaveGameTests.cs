using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Engine;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Tests;

/// <summary>Phase 1 (1k): saves (§31) and the time-lapse recording.</summary>
public class SaveGameTests
{
    private static readonly FileSystemDataSource Source = new(RepoPaths.Data);
    private static readonly SimData Data = SimData.Load(Source);

    private static SimWorld Chapter1(int threads = 2) =>
        SimWorld.CreateScenario(Data, Source, "chapter1_the_hill", threads: threads, chunkSize: 16, startingCash: 20_000);

    private static void RunUntil(SimWorld w, DateOnly date)
    {
        while (w.Simulation.Time.Date < date) w.Simulation.Tick();
    }

    /// <summary>Everything that matters, hashed: people, traffic, the map, money, buildings, systems' counters.</summary>
    private static ulong Fingerprint(SimWorld w)
    {
        ulong h = StateHash.Compute(w.Simulation, w.Campus.Grid);
        void Mix(long v) => h = (h ^ (ulong)v) * 1099511628211UL;
        var g = w.Campus.Grid;
        for (int t = 0; t < g.Types.Length; t++) Mix((long)g.Types[t] << 16 | (long)g.LandState[t] << 8 | (long)g.Ownership[t]);
        Mix(w.Land!.Treasury.Cents); Mix(w.Land.Treasury.Ledger.Count);
        Mix(w.Population.Count); Mix(w.Population.StudentCount); Mix(w.Campus.Buildings.Count); Mix(w.Simulation.Time.Tick);
        Mix(w.Enrollment!.Alumni); Mix((long)(w.Enrollment.Scholarship * 1000)); Mix((long)(w.Budget!.Confidence * 1000));
        Mix(w.Campaign!.Version); Mix(w.Placement!.Sites.Count); Mix(w.Simulation.Timelapse!.Frames.Count);
        Mix((long)(w.Reputation!.Reputation * 1e6)); Mix(w.Reputation.Version);
        return h;
    }

    /// <summary>A game with some history: a path, a building, clearing, a year of enrollment.</summary>
    private static SimWorld PlayedWorld()
    {
        var w = Chapter1();
        var g = w.Campus.Grid;
        var pl = w.Placement!;
        var hall = pl.Catalog.Find("frame_recitation_hall")!;
        var oldMain = w.Campus.Buildings[0];
        for (int r = 0; r < 40; r++)
        {
            bool placed = false;
            for (int dy = -r; dy <= r && !placed; dy++)
                for (int dx = -r; dx <= r && !placed; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    var pose = FootprintMath.Snap(oldMain.X + dx + 0.5f, oldMain.Y + dy + 0.5f, hall.W, hall.H, 0);
                    var q = PlacementSystem.Check(pl.Catalog, hall, pose, pl.LiveMap(), w.Simulation.Time.Date);
                    if (!q.Problem.StartsWith("The entrance needs a path") && !q.Ok) continue;
                    w.Land!.Enqueue(new LandCommand(LandAction.Path, q.Entrance % g.Width, q.Entrance / g.Width, q.Entrance % g.Width, q.Entrance / g.Width));
                    w.Simulation.ApplyPendingCommands();
                    pl.Enqueue(PlacementCommand.Build(hall.Id, pose));
                    w.Simulation.ApplyPendingCommands();
                    placed = true;
                }
            if (placed) break;
        }
        RunUntil(w, new DateOnly(1825, 9, 3));
        while (!SaveGame.CanSaveExactly(w)) w.Simulation.Tick();
        return w;
    }

    [Fact]
    public void ALoadedGame_ContinuesExactlyLikeTheOriginal()
    {
        var a = PlayedWorld();
        var bytes = new MemoryStream();
        var header = SaveGame.Write(bytes, a, "test", "2026-10-02T00:00:00Z", "test");
        Assert.True(header.Exact);
        Assert.True(bytes.Length < 2_000_000, $"save is {bytes.Length:N0} bytes");

        var b = Chapter1();
        bytes.Position = 0;
        SaveGame.Restore(bytes, b);
        Assert.Equal(a.Simulation.Time.Tick, b.Simulation.Time.Tick);
        Assert.Equal(a.Population.Count, b.Population.Count);
        Assert.Equal(a.Campus.Buildings.Count, b.Campus.Buildings.Count);

        for (int i = 0; i < 24 * 40; i++) { a.Simulation.Tick(); b.Simulation.Tick(); } // 40 days, incl. Oct 1 costs
        Assert.Equal(Fingerprint(a), Fingerprint(b));
    }

    [Fact]
    public void TheHeaderDescribesTheSave_AndOtherFilesAreRefused()
    {
        var w = Chapter1();
        var bytes = new MemoryStream();
        SaveGame.Write(bytes, w, "Autosave", "2026-10-02T00:00:00Z", "0.1");
        bytes.Position = 0;
        var h = SaveGame.ReadHeader(bytes);
        Assert.Equal(("Autosave", "chapter1_the_hill", "1824-11-01", 20, SaveGame.Format), (h.Name, h.Scenario, h.Date, h.Students, h.Format));
        Assert.Throws<InvalidDataException>(() => SaveGame.ReadHeader(new MemoryStream("not a save at all"u8.ToArray())));
        var other = SimWorld.CreateScenario(Data, Source, "preview_2026", threads: 2, students: 50, faculty: 5);
        bytes.Position = 0;
        Assert.Throws<InvalidDataException>(() => SaveGame.Restore(bytes, other));
    }

    [Fact]
    public void Timelapse_ReconstructsEachRecordedMonth()
    {
        var w = Chapter1();
        var g = w.Campus.Grid;
        var copies = new List<(TileType[] Types, LandState[] States, Ownership[] Owners)>();
        // Buy and clear some land so the map changes month to month.
        int t0 = Enumerable.Range(0, g.Width * g.Height).First(t => g.Ownership[t] == Ownership.University && g.LandState[t] == LandState.Forest);
        w.Land!.Enqueue(new LandCommand(LandAction.Clear, t0 % g.Width, t0 / g.Width, t0 % g.Width + 6, t0 / g.Width + 6));
        var rec = w.Simulation.Timelapse!;
        int seen = rec.Frames.Count;
        while (w.Simulation.Time.Date < new DateOnly(1825, 4, 2))
        {
            w.Simulation.Tick();
            if (rec.Frames.Count > seen)
            {
                seen = rec.Frames.Count;
                copies.Add(((TileType[])g.Types.Clone(), (LandState[])g.LandState.Clone(), (Ownership[])g.Ownership.Clone()));
            }
        }
        Assert.True(copies.Count >= 5);
        int n = g.Width * g.Height;
        var types = new TileType[n]; var states = new LandState[n]; var owners = new Ownership[n]; var surf = new byte[n]; var classes = new PathType[n];
        for (int i = 0; i < copies.Count; i++)
        {
            rec.Reconstruct(rec.Frames.Count - copies.Count + i + 1, states, owners, types, surf, classes);
            Assert.Equal(copies[i].Types, types);
            Assert.Equal(copies[i].States, states);
            Assert.Equal(copies[i].Owners, owners);
        }
        Assert.Contains(rec.Frames, f => f.Tiles.Length > 0); // the clearing shows up
    }
}
