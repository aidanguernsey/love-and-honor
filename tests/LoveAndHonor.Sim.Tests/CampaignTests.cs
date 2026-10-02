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
    public void Winning_Needs250StudentsAndABrickHall()
    {
        var w = Chapter1();
        var p = w.Population;
        var g = w.Campus.Grid;
        // Stand-ins: 250 students and a residence hall (in play: years of building and intakes).
        while (p.StudentCount < 250)
        {
            int s = p.Add(AgentKind.Student);
            p.Year[s] = 1;
            p.Home[s] = p.CurrentBuilding[s] = p.Dining[s] = p.StudySpot[s] = p.SocialSpot[s] = p.ExerciseSpot[s] = p.Home[0];
        }
        w.Simulation.Tick();
        Assert.Equal(CampaignOutcome.Playing, w.Campaign!.Outcome); // no hall yet
        var old = w.Campus.Buildings[0];
        // A wooden boarding house adds beds but isn't the residence hall the chapter asks for (answers to Q36).
        w.Campus.Add(new CampusBuilding { Index = (short)w.Campus.Buildings.Count, DefId = "boarding_house", Kind = BuildingKind.Residence,
            X = old.X, Y = old.Y, W = 1, H = 1, EntranceTile = old.EntranceTile });
        while (w.Simulation.Time.HourOfDay != 0) w.Simulation.Tick();
        w.Simulation.Tick();
        Assert.Equal(CampaignOutcome.Playing, w.Campaign.Outcome);
        w.Campus.Add(new CampusBuilding { Index = (short)w.Campus.Buildings.Count, DefId = "brick_residence_hall", Kind = BuildingKind.Residence,
            X = old.X, Y = old.Y, W = 1, H = 1, EntranceTile = old.EntranceTile });
        while (w.Simulation.Time.HourOfDay != 0) w.Simulation.Tick();
        w.Simulation.Tick();
        Assert.Equal(CampaignOutcome.Won, w.Campaign.Outcome);
        Assert.StartsWith("Chapter complete", w.Campaign.View().OutcomeText);
    }

    [Fact]
    public void The1820sDay_HasChapelAndSaturdayRecitations()
    {
        var w = Chapter1();
        var p = w.Population;
        // Monday Nov 8, 1824 at 20:00: evening prayers in Old Main (the first academic building).
        RunUntil(w, new DateOnly(1824, 11, 8));
        while (w.Simulation.Time.HourOfDay != 20) w.Simulation.Tick();
        w.Simulation.Tick();
        Assert.All(Enumerable.Range(0, p.Count), a => Assert.Equal(Activity.Chapel, p.CurrentActivity[a]));
        Assert.All(Enumerable.Range(0, p.Count), a => Assert.Equal(0, p.CurrentBuilding[a]));
        // Saturday Nov 13: recitations are held (only Sunday is free).
        RunUntil(w, new DateOnly(1824, 11, 13));
        int classes = 0;
        for (int h = 0; h < 13; h++)
        {
            w.Simulation.Tick();
            classes += Enumerable.Range(0, p.Count).Count(a => p.CurrentActivity[a] is Activity.Class or Activity.Teach);
        }
        Assert.True(classes > 0);
        // Sunday Nov 14: no chapel at 6 and no classes.
        RunUntil(w, new DateOnly(1824, 11, 14));
        for (int h = 0; h < 13; h++)
        {
            w.Simulation.Tick();
            Assert.DoesNotContain(Enumerable.Range(0, p.Count), a => p.CurrentActivity[a] is Activity.Class or Activity.Chapel);
        }
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
