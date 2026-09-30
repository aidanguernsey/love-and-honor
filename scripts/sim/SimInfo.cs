namespace LoveAndHonor.Sim;

/// <summary>Identity of the simulation core. The sim library never references Godot.</summary>
public static class SimInfo
{
    public const string Version = "0.1.0-phase0";

#if DEBUG
    public const bool IsOptimizedBuild = false;
#else
    public const bool IsOptimizedBuild = true;
#endif

    public static string Describe() =>
        $"LoveAndHonor.Sim {Version} on .NET {Environment.Version}";
}
