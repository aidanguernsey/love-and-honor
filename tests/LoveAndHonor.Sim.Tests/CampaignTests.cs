using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Engine;
using LoveAndHonor.Sim.Population;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Tests;

/// <summary>Phase 1 (1j): Chapter 1's events, History Book, advisors and goals.</summary>
public class CampaignTests
{
    private static readonly FileSystemDataSource Source = new(RepoPaths.Data);
    private static readonly SimData Data = SimData.Load(Source);

    private static SimWorld Chapter1() => SimWorld.CreateScenario(Data, Source, "chapter1_the_hill", threads: 2, chunkSize: 16);

    private static void RunUntil(SimWorld w, DateOnly date)
    {
        while (w.Simulation.Time.Date < date) w.Simulation.Tick();
    }

    private static float MeanNeed(PopulationStore p, string need)
    {
        int k = Array.IndexOf(Data.Balance.Needs.Ids, need);
        var students = Enumerable.Range(0, p.Count).Where(a => p.Kind[a] == AgentKind.Student).ToList();
        return students.Average(a => p.Needs[a * PopulationStore.NeedCount + k]);
    }

    [Fact]
    public void Content_LoadsWithSourcesForEveryHistoricalEvent()
    {
        var c = CampaignContent.Load(Source);
        Assert.InRange(c.Events.Count, 10, 15);
        Assert.All(c.Events.Where(e => e.Kind == "historical"), e => Assert.NotEmpty(e.Sources));
        Assert.All(c.Codex, e => Assert.NotEmpty(e.Sources));
        Assert.All(c.Events.Concat<object>(c.Codex), x => Assert.False(x is EventDef e ? e.Verified : ((CodexEntry)x).Verified));
    }

    [Fact]
    public void HistoricalEvents_FireOnTheirDates_AndUnlockTheHistoryBook()
    {
        var w = Chapter1();
        var camp = w.Campaign!;
        Assert.Contains("opening_1824", camp.Unlocked);
        Assert.DoesNotContain("literary_societies", camp.Unlocked);
        RunUntil(w, new DateOnly(1825, 11, 14));
        float before = MeanNeed(w.Population, "belonging");
        w.Simulation.Tick(); // hour 0 of Nov 14, 1825
        var view = camp.View();
        Assert.Contains(view.Cards, c => c.Id == "erodelphian_society_1825" && c.Historical && c.Sources.Length > 0);
        Assert.Contains("literary_societies", camp.Unlocked);
        Assert.True(MeanNeed(w.Population, "belonging") > before);
    }

    [Fact]
    public void McGuffey_JoinsTheFaculty_In1826()
    {
        var w = Chapter1();
        RunUntil(w, new DateOnly(1826, 6, 13));
        int faculty = w.Population.FacultyCount;
        RunUntil(w, new DateOnly(1826, 6, 15));
        var p = w.Population;
        Assert.Equal(faculty + 1, p.FacultyCount);
        Assert.Contains(Enumerable.Range(0, p.Count), a => p.Kind[a] == AgentKind.Faculty && p.TeachingScore[a] == 95);
        Assert.Contains("mcguffey", w.Campaign!.Unlocked);
    }

    [Fact]
    public void Advisors_SuggestAHall_WhenEveryoneBoardsInTown()
    {
        var w = Chapter1();
        RunUntil(w, new DateOnly(1826, 1, 16));
        Assert.Contains(w.Campaign!.TakeMessages(), m => m.StartsWith("VP Student Life: Every student still boards in town"));
    }

    [Fact]
    public void Winning_Needs250StudentsAndAHall()
    {
        var w = Chapter1();
        var p = w.Population;
        var g = w.Campus.Grid;
        // Stand-ins: 250 students and a residence hall (in play: years of building and intakes).
        while (p.StudentCount < 250) p.Year[p.Add(AgentKind.Student)] = 1;
        w.Simulation.Tick();
        Assert.Equal(CampaignOutcome.Playing, w.Campaign!.Outcome); // no hall yet
        var old = w.Campus.Buildings[0];
        w.Campus.Add(new CampusBuilding { Index = (short)w.Campus.Buildings.Count, DefId = "boarding_house", Kind = BuildingKind.Residence,
            X = old.X, Y = old.Y, W = 1, H = 1, EntranceTile = old.EntranceTile });
        while (w.Simulation.Time.HourOfDay != 0) w.Simulation.Tick();
        w.Simulation.Tick();
        Assert.Equal(CampaignOutcome.Won, w.Campaign.Outcome);
        Assert.StartsWith("Chapter complete", w.Campaign.View().OutcomeText);
    }

    [Fact]
    public void BeingDismissed_EndsTheChapter()
    {
        var w = Chapter1();
        w.Budget!.AdjustConfidence(-100, "Test.");
        w.Simulation.Tick();
        Assert.Equal(CampaignOutcome.Dismissed, w.Campaign!.Outcome);
    }
}
