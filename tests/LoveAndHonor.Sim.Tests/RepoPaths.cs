namespace LoveAndHonor.Sim.Tests;

internal static class RepoPaths
{
    /// <summary>Repo root, found by walking up from the test binaries to project.godot.</summary>
    public static string Root { get; } = FindRoot();
    public static string Data => Path.Combine(Root, "data");

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "project.godot")))
                return dir.FullName;
        throw new DirectoryNotFoundException("project.godot not found above " + AppContext.BaseDirectory);
    }
}
