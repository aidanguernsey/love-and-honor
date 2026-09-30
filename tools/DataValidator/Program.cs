using LoveAndHonor.Tools;

// Usage: dotnet run --project tools/DataValidator [-- <path-to-data-dir>]
// Defaults to the repo's /data folder (found by walking up to project.godot). Also checks the models in
// assets/models/ next to that data folder against data/art_pipeline.json (docs/ART_PIPELINE.md).
var dataDir = Path.GetFullPath(args.Length > 0 ? args[0] : FindRepoData());
Console.WriteLine($"Validating {dataDir}");

var issues = DataValidation.ValidateDirectory(dataDir);
var modelsDir = Path.Combine(Path.GetDirectoryName(dataDir)!, "assets", "models");
if (issues.Count == 0 && Directory.Exists(modelsDir))
{
    Console.WriteLine($"Checking models in {modelsDir}");
    issues.AddRange(ModelValidation.ValidateDirectory(modelsDir, dataDir));
}
foreach (var issue in issues) Console.Error.WriteLine($"  ERROR {issue}");

if (issues.Count > 0)
{
    Console.Error.WriteLine($"{issues.Count} problem(s) found.");
    return 1;
}
Console.WriteLine("All data files and models valid.");
return 0;

static string FindRepoData()
{
    for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "project.godot")))
            return Path.Combine(dir.FullName, "data");
    throw new DirectoryNotFoundException("Could not find project.godot above the current directory; pass the data dir explicitly.");
}
