// Chapter 1 balance runner (balance pass, 2026-10-02): plays the chapter headlessly with the scripted players
// (idle, sensible, minimal, spender) and prints a year-by-year table for each, so tuning changes can be judged in seconds.
// Usage: dotnet run -c Release --project tools/BalanceRunner [-- --data <dir>] [--player idle|sensible|minimal|spender] [--log]
using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Engine;

string? dataArg = null, only = null;
bool log = false;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--data" && i + 1 < args.Length) dataArg = args[++i];
    else if (args[i] == "--player" && i + 1 < args.Length) only = args[++i];
    else if (args[i] == "--log") log = true;
}
var dataDir = Path.GetFullPath(dataArg ?? FindRepoData());
var source = new FileSystemDataSource(dataDir);
var data = SimData.Load(source);

var players = new (string Name, Func<SimWorld, ScriptedPlayer> Make)[]
{
    ("idle", ScriptedPlayer.Idle), ("sensible", ScriptedPlayer.Sensible), ("minimal", ScriptedPlayer.Minimal),
    ("spender", ScriptedPlayer.Spender),
};
foreach (var (name, make) in players.Where(p => only is null || p.Name == only))
{
    var world = SimWorld.CreateScenario(data, source, "chapter1_the_hill", threads: 4);
    var result = make(world).PlayChapter();
    Console.WriteLine($"== {name}: {result.Outcome} on {result.End:yyyy-MM-dd}, {result.Students} students, cash {result.Cash:N0}");
    Console.WriteLine($"   {result.OutcomeText}");
    Console.WriteLine("   year  students faculty applicants hallbeds seats      cash  trustees  reputation quality");
    foreach (var y in result.Years)
        Console.WriteLine($"   {y.Year}  {y.Students,8} {y.Faculty,7} {y.Applicants,10} {y.HallBeds,8} {y.Seats,5} {y.Cash,9:N0} {y.Confidence,9:0} {y.Reputation,11:0.0} {y.Quality,7:0.0}");
    if (log) foreach (var line in result.Log) Console.WriteLine("   " + line);
}

static string FindRepoData()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "project.godot"))) return Path.Combine(dir.FullName, "data");
    throw new DirectoryNotFoundException("Couldn't find project.godot above the runner.");
}
