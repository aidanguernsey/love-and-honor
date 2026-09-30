using LoveAndHonor.Tools;

namespace LoveAndHonor.Sim.Tests;

public class DataValidationTests
{
    [Fact]
    public void RepoDataFiles_AreAllValid()
    {
        var issues = DataValidation.ValidateDirectory(RepoPaths.Data);
        Assert.True(issues.Count == 0, "Data validation failed:\n" + string.Join("\n", issues));
    }

    [Fact]
    public void Validator_CatchesSchemaAndReferenceErrors()
    {
        var dir = CopyDataToTemp();
        try
        {
            // Schema error: negative cost. Reference error: unknown era.
            var file = Path.Combine(dir, "buildings", "small_classroom_hall.json");
            var text = File.ReadAllText(file)
                .Replace("\"cost_usd\": 8000000", "\"cost_usd\": -1")
                .Replace("\"unlock_era\": \"founding\"", "\"unlock_era\": \"stone_age\"");
            File.WriteAllText(file, text);

            var issues = DataValidation.ValidateDirectory(dir);
            Assert.Contains(issues, i => i.File == "buildings/small_classroom_hall.json" && i.Location == "/cost_usd");
            Assert.Contains(issues, i => i.Message.Contains("unknown era 'stone_age'"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Validator_RequiresSchemaDeclaration()
    {
        var dir = CopyDataToTemp();
        try
        {
            File.WriteAllText(Path.Combine(dir, "orphan.json"), "{ \"x\": 1 }");
            var issues = DataValidation.ValidateDirectory(dir);
            Assert.Contains(issues, i => i.File == "orphan.json" && i.Message.Contains("$schema"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void BrandingProfiles_AreInterchangeable()
    {
        // Both the active profile and the stand-in must satisfy the same schema (§36.1 swappable branding).
        var issues = DataValidation.ValidateDirectory(RepoPaths.Data);
        Assert.DoesNotContain(issues, i => i.File.StartsWith("branding"));
        Assert.True(File.Exists(Path.Combine(RepoPaths.Data, "branding", "standin.json")));
    }

    private static string CopyDataToTemp()
    {
        var dest = Path.Combine(Path.GetTempPath(), "lh-data-" + Guid.NewGuid().ToString("N"));
        foreach (var src in Directory.EnumerateFiles(RepoPaths.Data, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(dest, Path.GetRelativePath(RepoPaths.Data, src));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(src, target);
        }
        return dest;
    }
}
