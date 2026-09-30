using LoveAndHonor.Tools;

// Usage: dotnet run --project tools/DataValidator [-- <path-to-data-dir>]
// Defaults to the repo's /data folder (found by walking up to project.godot).
var dataDir = args.Length > 0 ? args[0] : FindRepoData();
Console.WriteLine($"Validating {Path.GetFullPath(dataDir)}");

var issues = DataValidation.ValidateDirectory(dataDir);
foreach (var issue in issues) Console.Error.WriteLine($"  ERROR {issue}");

if (issues.Count > 0)
{
    Console.Error.WriteLine($"{issues.Count} problem(s) found.");
    return 1;
}
Console.WriteLine("All data files valid.");
return 0;

static string FindRepoData()
{
    for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "project.godot")))
            return Path.Combine(dir.FullName, "data");
    throw new DirectoryNotFoundException("Could not find project.godot above the current directory; pass the data dir explicitly.");
}
