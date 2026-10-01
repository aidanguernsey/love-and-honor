namespace LoveAndHonor.Sim.Data;

// Typed views of the /data JSON files. Property names map to snake_case JSON keys (see SimJson).
// Only fields the sim currently uses are modelled; other keys are ignored on load.
// Validation (ranges, required keys) is the job of the JSON schemas + tools/DataValidator.

public sealed class IntRange
{
    public int Min { get; init; }
    public int Max { get; init; }
}

// ---------- balance.json ----------

public sealed class BalanceConfig
{
    public TimeSection Time { get; init; } = new();
    public PopulationSection Population { get; init; } = new();
    public PerformanceSection Performance { get; init; } = new();
    public MapSection Map { get; init; } = new();
    public NeedsSection Needs { get; init; } = new();
    public WalkingSection Walking { get; init; } = new();

    public sealed class TimeSection
    {
        public double RealSecondsPerGameDay { get; init; }
        public float[] Speeds { get; init; } = [];
        public int TicksPerGameDay { get; init; }
        public float ClassChangeWindowMinutes { get; init; }
    }

    public sealed class PopulationSection
    {
        public int TargetStudents { get; init; }
        public int TargetFaculty { get; init; }
        public int RenderedAgentsMin { get; init; }
        public int RenderedAgentsMax { get; init; }
    }

    public sealed class PerformanceSection
    {
        public double TickBudgetMs { get; init; }
        public string TickBudgetStatistic { get; init; } = "p95";
        public int MinSpecCpuCores { get; init; }
        public int TargetFps { get; init; }
        public int SimWorkerThreads { get; init; }
        public int AgentChunkSize { get; init; }
        public int BenchmarkTicks { get; init; }
        public int BenchmarkWarmupTicks { get; init; }
        public int PathingRebuildLatencyTicks { get; init; } = 6;
        public int PathingRebuildThreads { get; init; } = 2;
        public int BenchmarkEditEveryTicks { get; init; }
    }

    public sealed class MapSection
    {
        public float TileSizeM { get; init; }
        public int GridWidth { get; init; }
        public int GridHeight { get; init; }
    }

    public sealed class NeedsSection
    {
        public string[] Ids { get; init; } = [];
        public Dictionary<string, float> HappinessWeights { get; init; } = [];
        public float InitialMin { get; init; }
        public float InitialMax { get; init; }
        public Dictionary<string, float> BaselinePerHour { get; init; } = [];
        public Dictionary<string, Dictionary<string, float>> ActivityEffects { get; init; } = [];
    }

    public sealed class WalkingSection
    {
        public float SpeedMPerMin { get; init; }
        public float GrassCostMultiplier { get; init; }
        public float RoughCostMultiplier { get; init; }
        public float PathExitPenaltyM { get; init; }
        public float CommutePenaltyPerMinute { get; init; }
        public DesirePathSection DesirePaths { get; init; } = new();

        public LoveAndHonor.Sim.Pathing.FlowFieldSet.WalkCosts Costs => new(GrassCostMultiplier, RoughCostMultiplier, PathExitPenaltyM);
    }

    public sealed class DesirePathSection
    {
        public int MinWalkersPerDay { get; init; }
        public float DaysToWear { get; init; }
        public float DaysToRegrow { get; init; }
    }
}

// ---------- schedules.json ----------

public sealed class ScheduleConfig
{
    public ClassSlotsSection ClassSlots { get; init; } = new();
    public StudentSection Student { get; init; } = new();
    public FacultySection Faculty { get; init; } = new();

    public sealed class ClassSlotsSection
    {
        public Dictionary<string, string[]> Patterns { get; init; } = [];
        public int FirstHour { get; init; }
        public int LastHour { get; init; }
    }

    public sealed class StudentSection
    {
        public IntRange WakeHour { get; init; } = new();
        public IntRange BedHour { get; init; } = new();
        public IntRange WeekendWakeHour { get; init; } = new();
        public int LunchHour { get; init; }
        public int DinnerHour { get; init; }
        public int Sections { get; init; }
        public int SectionsInMajorBuilding { get; init; }
        public float StudyAtLibraryProbability { get; init; }
        public Dictionary<string, float> WeekdayFree { get; init; } = [];
        public Dictionary<string, float> WeekdayEvening { get; init; } = [];
        public Dictionary<string, float> Weekend { get; init; } = [];
        public int EveningStartHour { get; init; }
        public int FreeBlockHours { get; init; } = 1;
    }

    public sealed class FacultySection
    {
        public IntRange WakeHour { get; init; } = new();
        public IntRange BedHour { get; init; } = new();
        public IntRange ArriveHour { get; init; } = new();
        public IntRange LeaveHour { get; init; } = new();
        public int LunchHour { get; init; }
        public IntRange Sections { get; init; } = new();
    }
}

// ---------- buildings/*.json ----------

public sealed class BuildingDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public FootprintSize Footprint { get; init; } = new();
    /// <summary>Modern dollars (× the era's price multiplier); null = can't be built new (landmark / Heritage Project only).</summary>
    public double? CostUsd { get; init; }
    public CapacityInfo Capacity { get; init; } = new();
    public string Style { get; init; } = "";
    public string UnlockEra { get; init; } = "";
    /// <summary>First era in which it's no longer offered (era-specific buildings), or null.</summary>
    public string? RetireEra { get; init; }
    /// <summary>Overrides the size-class construction time (placement.json).</summary>
    public double? ConstructionMonths { get; init; }
    public string[] NeedsServed { get; init; } = [];

    public sealed class FootprintSize
    {
        public int W { get; init; }
        public int H { get; init; }
    }

    public sealed class CapacityInfo
    {
        public string Kind { get; init; } = "";
        public int Value { get; init; }
    }
}

// ---------- departments.json ----------

public sealed class DepartmentsConfig
{
    public Entry[] Colleges { get; init; } = [];
    public Department[] Departments { get; init; } = [];

    public sealed class Entry
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
    }

    public sealed class Department
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string College { get; init; } = "";
        public string SpaceType { get; init; } = "";
    }
}

// ---------- spikes/population_spike.json ----------

public sealed class PopulationSpikeConfig
{
    public ulong Seed { get; init; }
    public string StartDate { get; init; } = "";
    public CampusSection Campus { get; init; } = new();
    public StudentSection Students { get; init; } = new();
    public FacultySection Faculty { get; init; } = new();

    public sealed class CampusSection
    {
        public int CoreBlocks { get; init; }
        public int BlockSizeTiles { get; init; }
        public int CorePathWidth { get; init; }
        public int OuterPathSpacingTiles { get; init; }
        public float PathSegmentRemovalFraction { get; init; }
        public int TownBlockCandidates { get; init; }
        public BuildingCount[] Buildings { get; init; } = [];
    }

    public sealed class BuildingCount
    {
        public string Def { get; init; } = "";
        public int Count { get; init; }
    }

    public sealed class StudentSection
    {
        public float[] YearShares { get; init; } = [];
        public int[] OnCampusYears { get; init; } = [];
    }

    public sealed class FacultySection
    {
        public Dictionary<string, float> RankShares { get; init; } = [];
        public int ScoreMin { get; init; }
        public int ScoreMax { get; init; }
    }
}
