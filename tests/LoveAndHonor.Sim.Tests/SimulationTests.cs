using LoveAndHonor.Sim.Core;
using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Engine;
using LoveAndHonor.Sim.Population;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Tests;

/// <summary>Spike A behaviour on the real /data config with a reduced population (fast to run).</summary>
public class SimulationTests
{
    private const int Students = 2000, Faculty = 200, Chunk = 256;
    private static readonly SimData Data = SimData.Load(new FileSystemDataSource(RepoPaths.Data));

    private static SimWorld NewWorld(int threads = 4) => SimWorld.CreateSynthetic(Data, threads, Students, Faculty, Chunk);

    private static void RunUntil(SimWorld w, int tick)
    {
        while (w.Simulation.Time.Tick <= tick) w.Simulation.Tick();
    }

    [Fact]
    public void Data_LoadsTypedConfig()
    {
        Assert.Equal(1.3f, Data.Balance.Needs.HappinessWeights["belonging"]);
        Assert.Equal(8.0, Data.Balance.Performance.TickBudgetMs);
        Assert.Contains("off_campus_housing", Data.Buildings.Keys);
        Assert.Equal("2026-08-24", Data.Spike.StartDate);
    }

    [Fact]
    public void SimTime_MapsTicksToCalendar()
    {
        var t = new SimTime(new DateOnly(2026, 8, 24)); // a Monday
        Assert.Equal(0, t.WeekdayIndex);
        for (int i = 0; i < 24 * 5 + 13; i++) t.Advance();
        Assert.Equal(13, t.HourOfDay);
        Assert.Equal(5, t.WeekdayIndex); // Saturday
        Assert.True(t.IsWeekend);
        Assert.Equal(new DateOnly(2026, 8, 29), t.Date);
    }

    [Fact]
    public void Campus_EveryBuildingPairIsReachable()
    {
        var w = NewWorld();
        int b = w.Fields.BuildingCount;
        Assert.Equal(Data.Spike.Campus.Buildings.Sum(x => x.Count), b);
        for (int from = 0; from < b; from++)
            for (int to = 0; to < b; to++)
            {
                // Nobody walks between two off-campus housing blocks, so those pairs have no route (by design).
                if (from != to && !w.Fields.HasField(from) && !w.Fields.HasField(to)) continue;
                Assert.True(float.IsFinite(w.Fields.Distance(from, to)), $"{from}->{to} unreachable");
                Assert.NotEmpty(w.Fields.Route(from, to));
            }
    }

    [Fact]
    public void SameSeed_OneThreadAndFourThreads_ProduceIdenticalState()
    {
        var single = NewWorld(threads: 1);
        var multi = NewWorld(threads: 4);
        for (int i = 0; i < 72; i++)
        {
            single.Simulation.Tick();
            multi.Simulation.Tick();
        }
        Assert.Equal(StateHash.Compute(single.Population, single.Campus.Grid), StateHash.Compute(multi.Population, multi.Campus.Grid));
        Assert.Equal(single.Simulation.LastTick.AverageHappiness, multi.Simulation.LastTick.AverageHappiness);
    }

    [Fact]
    public void AtThreeAm_EveryoneIsAsleepAtHome()
    {
        var w = NewWorld();
        RunUntil(w, 3); // Monday 03:00
        var p = w.Population;
        for (int a = 0; a < p.Count; a++)
        {
            Assert.Equal(Activity.Sleep, p.CurrentActivity[a]);
            Assert.Equal(p.Home[a], p.CurrentBuilding[a]);
        }
    }

    [Fact]
    public void DuringAClassHour_StudentsWithThatSectionAreInTheClassBuilding()
    {
        var w = NewWorld();
        const int hour = 10;
        RunUntil(w, hour); // Monday 10:00
        var p = w.Population;
        int checkedCount = 0;
        for (int a = 0; a < p.StudentCount; a++)
            for (int k = 0; k < p.SectionCount[a]; k++)
            {
                int i = a * PopulationStore.MaxSections + k;
                if (p.SectionHour[i] != hour || (p.SectionDays[i] & 1) == 0) continue;
                Assert.Equal(Activity.Class, p.CurrentActivity[a]);
                Assert.Equal(p.SectionBuilding[i], p.CurrentBuilding[a]);
                checkedCount++;
            }
        Assert.True(checkedCount > 50, "expected plenty of Monday 10:00 sections");
    }

    [Fact]
    public void FacultyWorkOrTeachMidMorning_AndNobodyHasClassOnSaturday()
    {
        var w = NewWorld();
        RunUntil(w, 10); // Monday 10:00
        var p = w.Population;
        for (int a = p.StudentCount; a < p.Count; a++)
            Assert.Contains(p.CurrentActivity[a], new[] { Activity.Work, Activity.Teach });

        RunUntil(w, 24 * 5 + 11); // Saturday 11:00
        for (int a = 0; a < p.Count; a++)
            Assert.DoesNotContain(p.CurrentActivity[a], new[] { Activity.Class, Activity.Teach });
    }

    [Fact]
    public void AfterAWeek_NeedsAndHappinessStayInRange_AndFootTrafficWearsGrass()
    {
        var w = NewWorld();
        RunUntil(w, 24 * 7);
        var p = w.Population;
        Assert.All(p.Needs, v => Assert.InRange(v, 0f, 100f));
        Assert.All(p.Happiness, v => Assert.InRange(v, 0f, 100f));

        var grid = w.Campus.Grid;
        long grassTraffic = 0, pathTraffic = 0;
        for (int i = 0; i < grid.FootTraffic.Length; i++)
        {
            if (grid.Types[i] == TileType.Grass) grassTraffic += grid.FootTraffic[i];
            else if (grid.Types[i] == TileType.Path) pathTraffic += grid.FootTraffic[i];
        }
        Assert.True(pathTraffic > 0);
        Assert.True(grassTraffic > 0, "some walkers should cut across grass (desire paths)");
    }

    [Fact]
    public void NeedsModel_AppliesBaselinePlusActivityEffect_AndWeightedHappiness()
    {
        var model = new NeedsModel(Data.Balance.Needs);
        var needs = Enumerable.Repeat(50f, PopulationStore.NeedCount).ToArray();
        Assert.Equal(50f, model.Happiness(needs, 0), precision: 4);

        model.Update(needs, 0, Activity.Sleep);
        int sleep = Array.IndexOf(Data.Balance.Needs.Ids, "sleep");
        float expected = 50f + Data.Balance.Needs.BaselinePerHour["sleep"] + Data.Balance.Needs.ActivityEffects["sleep"]["sleep"];
        Assert.Equal(expected, needs[sleep], precision: 4);
    }
}
