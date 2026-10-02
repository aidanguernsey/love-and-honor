using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Engine;
using LoveAndHonor.Sim.World;
using Xunit.Abstractions;

namespace LoveAndHonor.Sim.Tests;

/// <summary>Phase 1 (1i): the operating budget, tuition, the Trustees.</summary>
public class BudgetTests(ITestOutputHelper output)
{
    private static readonly FileSystemDataSource Source = new(RepoPaths.Data);
    private static readonly SimData Data = SimData.Load(Source);

    private static SimWorld Chapter1(double? cash = null) =>
        SimWorld.CreateScenario(Data, Source, "chapter1_the_hill", threads: 2, chunkSize: 16, startingCash: cash);

    private static void RunUntil(SimWorld w, DateOnly date)
    {
        while (w.Simulation.Time.Date < date) w.Simulation.Tick();
    }

    [Fact]
    public void AFiscalYear_EndsWithAReportAndTheTrusteesReview()
    {
        var w = Chapter1();
        var b = w.Budget!;
        RunUntil(w, new DateOnly(1825, 8, 2));
        var messages = b.TakeMessages();
        output.WriteLine(string.Join("\n", messages));
        var last = b.View(w.Simulation.Time.Date).LastYear!;
        foreach (var line in last.Lines) output.WriteLine($"{line.Name}: {LandSystem.Money(line.Cents)}");
        Assert.Contains(messages, m => m.StartsWith("Budget 1824–25"));
        Assert.Contains(messages, m => m.Contains("The Trustees' review"));
        string[] expected = ["tuition", "land_rents", "salaries", "administration", "upkeep"];
        Assert.All(expected, c => Assert.Contains(last.Lines, l => l.Category == c));
        Assert.True(last.Revenue > 0);
        Assert.Equal(last.Revenue + last.Lines.Where(l => l.Category is "salaries" or "administration" or "upkeep" or "other").Sum(l => l.Cents), last.Operating);
        Assert.NotEqual(Data.Balance.Trustees.StartingConfidence, b.Confidence);
        // The starting placeholders roughly balance an 1820s college (land rents carry it).
        Assert.True(last.Operating >= 0, $"operating {LandSystem.Money(last.Operating)}");
    }

    [Fact]
    public void Tuition_IsChargedEachTerm_ForEveryStudent()
    {
        var w = Chapter1();
        RunUntil(w, new DateOnly(1825, 1, 27));
        var tuition = w.Land!.Treasury.Ledger.Where(e => e.Category == "tuition").ToList();
        var spring = Assert.Single(tuition);
        Assert.Equal(new DateOnly(1825, 1, 26), spring.Date);
        Assert.Equal(w.Population.StudentCount * 30 * 100 / 2, spring.Cents); // $30 a year, half a term
    }

    [Fact]
    public void HigherTuition_MeansFewerApplicants()
    {
        int Applicants(double level)
        {
            var w = Chapter1();
            w.Budget!.SetTuitionLevel(level);
            w.Simulation.ApplyPendingCommands();
            RunUntil(w, new DateOnly(1825, 8, 20));
            return w.Enrollment!.View(w.Simulation.Time.Date).LastApplicants;
        }
        int normal = Applicants(1.0), dear = Applicants(1.5);
        output.WriteLine($"applicants at 100%: {normal}, at 150%: {dear}");
        Assert.True(dear < normal);
    }

    [Fact]
    public void RunningOutOfMoney_CostsTrusteeConfidence()
    {
        var w = Chapter1(cash: 0);
        double start = w.Budget!.Confidence;
        RunUntil(w, new DateOnly(1824, 12, 2)); // before January's land rents
        Assert.True(w.Land!.Treasury.Cents < 0);
        Assert.True(w.Budget.Confidence < start);
        Assert.Contains(w.Budget.TakeMessages(), m => m.StartsWith("Cash has run out"));
    }
}
