using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Engine;

namespace LoveAndHonor.Sim.Tests;

/// <summary>
/// Chapter 1 balance pass: whole-chapter runs with scripted players guard the balance. Doing nothing must not win, a
/// sensible player must win before the deadline, and building everything at once must lose. Each run plays 1824-1841
/// headlessly (tens of seconds in Debug), one class each so xUnit runs them in parallel; skip them with
/// `--filter "Category!=Balance"`. `tools/BalanceRunner` prints the year-by-year tables.
/// </summary>
internal static class Chapter1Balance
{
    private static readonly FileSystemDataSource Source = new(RepoPaths.Data);
    private static readonly SimData Data = SimData.Load(Source);

    public static ScriptedPlayer.ChapterResult Play(Func<SimWorld, ScriptedPlayer> player) =>
        player(SimWorld.CreateScenario(Data, Source, "chapter1_the_hill", threads: 2)).PlayChapter();

    public static string Describe(ScriptedPlayer.ChapterResult r) =>
        $"{r.Outcome} on {r.End:yyyy-MM-dd}: {r.OutcomeText}\n" + string.Join("\n", r.Log.TakeLast(40));
}

[Trait("Category", "Balance")]
public class Chapter1BalanceIdleTests
{
    [Fact]
    public void DoingNothing_DoesNotWin_ButStaysSolvent()
    {
        var r = Chapter1Balance.Play(ScriptedPlayer.Idle);
        // Time runs out: it isn't money that stops an idle college (the old economy had it dismissed by 1835).
        Assert.True(r.Outcome == CampaignOutcome.OutOfTime, Chapter1Balance.Describe(r));
    }
}

[Trait("Category", "Balance")]
public class Chapter1BalanceSensibleTests
{
    [Fact]
    public void ASensiblePlayer_Wins_WithYearsToSpare()
    {
        var r = Chapter1Balance.Play(ScriptedPlayer.Sensible);
        Assert.True(r.Outcome == CampaignOutcome.Won, Chapter1Balance.Describe(r));
        Assert.True(r.End.Year <= 1839, $"won only in {r.End:yyyy}");
        Assert.True(r.Years.All(y => y.Confidence >= 40), "a careful player shouldn't come near dismissal");
    }
}

[Trait("Category", "Balance")]
public class Chapter1BalanceSpenderTests
{
    [Fact]
    public void BuildingEverythingAtOnce_Loses()
    {
        var r = Chapter1Balance.Play(ScriptedPlayer.Spender);
        Assert.True(r.Outcome is CampaignOutcome.Dismissed or CampaignOutcome.Bankrupt, Chapter1Balance.Describe(r));
    }
}
