using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Engine;
using LoveAndHonor.Sim.Population;
using LoveAndHonor.Sim.View;

namespace LoveAndHonor.Sim.Tests;

public class RunnerAndCrowdTests
{
    private static readonly SimData Data = SimData.Load(new FileSystemDataSource(RepoPaths.Data));

    private static (SpikeWorld world, SimRunner runner) NewRunner()
    {
        var world = SpikeWorld.Create(Data, threads: 2, students: 2000, faculty: 200, chunkSize: 256);
        var t = Data.Balance.Time;
        return (world, new SimRunner(world.Simulation, world.Campus.Grid, t.Speeds, t.RealSecondsPerGameDay, t.TicksPerGameDay));
    }

    private static SimSnapshot WaitFor(SimRunner runner, Func<SimSnapshot, bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var s = runner.AcquireLatest();
            if (condition(s)) return s;
            Thread.Sleep(5);
        }
        throw new TimeoutException("Sim thread did not publish the expected snapshot.");
    }

    [Fact]
    public void Snapshot_ListsThisTicksWalkers_InAgentOrder_WithCategories()
    {
        var (world, runner) = NewRunner();
        runner.RunTicksNow(9); // ticks 0..8 → the 08:00 tick is the last one
        var s = runner.AcquireLatest();
        var p = world.Population;

        Assert.Equal(9, s.Tick);
        Assert.True(s.WalkCount > 100);
        Assert.Equal(world.Simulation.LastTick.WalksStarted, s.WalkCount);
        for (int i = 0; i < s.WalkCount; i++)
        {
            int a = s.WalkAgent[i];
            if (i > 0) Assert.True(a > s.WalkAgent[i - 1]);
            Assert.Equal(8 * 60, p.WalkDepartMinute[a]);
            Assert.Equal(p.WalkTo[a], s.WalkTo[i]);
            byte expected = p.Kind[a] == AgentKind.Faculty ? SimRunner.FacultyCategory : (byte)(p.Year[a] - 1);
            Assert.Equal(expected, s.WalkCategory[i]);
        }
    }

    [Fact]
    public void AdvanceRealTime_RunsTicksAtGameSpeed_AndPausesAtZero()
    {
        var (_, runner) = NewRunner();
        using (runner)
        {
            runner.SpeedIndex = 1; // 1× = 24 ticks per 2 real seconds
            runner.Start();
            runner.AdvanceRealTime(0.25); // 3 ticks, within the quarter-second backlog allowance
            var s = WaitFor(runner, x => x.Tick >= 3);
            Assert.Equal(3, s.Tick);
            Assert.Equal(0, s.DroppedTicks);

            runner.SpeedIndex = 0; // paused
            runner.AdvanceRealTime(5.0);
            Thread.Sleep(150);
            Assert.Equal(3, runner.AcquireLatest().Tick);
        }
    }

    [Fact]
    public void AdvanceRealTime_DropsBacklogWhenTooFarBehind()
    {
        var (_, runner) = NewRunner();
        using (runner)
        {
            runner.SpeedIndex = 1;
            runner.Start();
            runner.AdvanceRealTime(10.0); // 120 ticks due; backlog cap at 1× is max(2, 12 × 0.25) = 3
            var s = WaitFor(runner, x => x.Tick >= 3);
            Assert.Equal(3, s.Tick);
            Assert.Equal(117, s.DroppedTicks);
        }
    }

    [Fact]
    public void TrafficCopy_OnlyWhenRequested()
    {
        var (world, runner) = NewRunner();
        runner.RunTicksNow(10);
        Assert.Equal(0, runner.AcquireLatest().TrafficVersion);
        runner.RequestTraffic();
        runner.RunTicksNow(1);
        var s = runner.AcquireLatest();
        Assert.Equal(1, s.TrafficVersion);
        Assert.Equal(world.Campus.Grid.FootTraffic, s.Traffic);
    }

    [Fact]
    public void Crowd_FillsFromWalksInView_FreezesWhenPaused_AndCullsOutsideView()
    {
        var (world, runner) = NewRunner();
        runner.RunTicksNow(9);
        var s = runner.AcquireLatest();
        var grid = world.Campus.Grid;
        var whole = new GroundRect(0, 0, grid.Width * grid.TileSizeM, grid.Height * grid.TileSizeM);
        var crowd = new VisualCrowd(world.Fields, grid, capacity: 300, speedMps: 12, lateralSpreadM: 0, attemptsPerSlot: 8, seed: 1);

        crowd.Update(0.016f, 1f, whole, 0, s);
        Assert.Equal(300, crowd.ActiveCount);

        var before = new float[300 * VisualCrowd.FloatsPerInstance];
        var after = new float[before.Length];
        float[] colors = Enumerable.Repeat(1f, 5 * 4).ToArray();
        crowd.WriteInstances(before, 1f, 0.9f, colors);
        crowd.Update(1f, 0f, whole, 0, s); // paused: nobody moves
        crowd.WriteInstances(after, 1f, 0.9f, colors);
        Assert.Equal(before, after);

        // Every walker is on the map, at walker height.
        for (int i = 0; i < crowd.ActiveCount; i++)
        {
            Assert.InRange(before[i * 16 + 3], whole.MinX, whole.MaxX);
            Assert.InRange(before[i * 16 + 11], whole.MinZ, whole.MaxZ);
            Assert.Equal(0.9f, before[i * 16 + 7]);
        }

        // Point the "camera" at an empty corner: existing walkers are culled and none of today's routes spawn there.
        var corner = new GroundRect(0, 0, 5, 5);
        crowd.Update(0.016f, 1f, corner, 0, s);
        Assert.Equal(0, crowd.ActiveCount);
    }

    [Fact]
    public void Palette_ResolvesHexAndBrandingReferences()
    {
        var palette = new Palette(File.ReadAllText(Path.Combine(RepoPaths.Data, "branding.json")));
        Assert.Equal(Palette.ParseHex("#8E3B2E"), palette.Resolve("brick"));
        Assert.Equal(Palette.ParseHex("#B85C47"), palette.Resolve("brick:2"));
        Assert.Equal(new Rgb(1, 1, 1), palette.Resolve("#FFFFFF"));
        Assert.Throws<InvalidDataException>(() => palette.Resolve("chartreuse"));
    }
}
