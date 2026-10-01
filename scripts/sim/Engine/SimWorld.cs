using System.Diagnostics;
using LoveAndHonor.Sim.Core;
using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Pathing;
using LoveAndHonor.Sim.Population;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Engine;

/// <summary>
/// Assembles a simulated world (campus → flow fields → population → simulation) and times each stage:
/// on the Spike A synthetic campus, or (Phase 1, 1a) on the real Oxford map.
/// </summary>
public sealed class SimWorld
{
    public required SimData Data { get; init; }
    public required RngStreams Rng { get; init; }
    public required Campus Campus { get; init; }
    public required FlowFieldSet Fields { get; init; }
    public required PopulationStore Population { get; init; }
    public required Simulation Simulation { get; init; }
    /// <summary>Set for real-map worlds.</summary>
    public RealMap? Map { get; init; }
    public RealCampusReport? CampusReport { get; init; }
    public double CampusMs { get; init; }
    public double FlowFieldsMs { get; init; }
    public double PopulationMs { get; init; }

    public static SimWorld CreateSynthetic(SimData data, int? threads = null, int? students = null, int? faculty = null, int? chunkSize = null)
    {
        var rng = new RngStreams(data.Spike.Seed);
        var sw = Stopwatch.StartNew();
        var campus = SyntheticCampusGenerator.Generate(data, rng);
        return Assemble(data, rng, campus, sw.Elapsed.TotalMilliseconds, threads, students, faculty, chunkSize, null, null);
    }

    /// <summary>
    /// The real Oxford map with Miami's buildings standing in <paramref name="year"/> (default: the present year of
    /// land_history.json). Uses the population settings of population_spike.json until Phase 1's own start data exists.
    /// </summary>
    public static SimWorld CreateReal(SimData data, IDataSource source, int? year = null, int? threads = null,
        int? students = null, int? faculty = null, int? chunkSize = null, RealMap? map = null)
    {
        var rng = new RngStreams(data.Spike.Seed);
        var sw = Stopwatch.StartNew();
        map ??= RealMapLoader.Load(source);
        var features = FeatureBuilding.Load(source, map.Meta.FeaturesFile);
        var timeline = TimelineData.Load(source);
        int present = LandHistoryConfig.Load(source).PresentYear;
        var campus = RealCampusBuilder.Build(map, features, timeline, RealCampusConfig.Load(source), year ?? present, present, out var report);
        return Assemble(data, rng, campus, sw.Elapsed.TotalMilliseconds, threads, students, faculty, chunkSize, map, report);
    }

    private static SimWorld Assemble(SimData data, RngStreams rng, Campus campus, double campusMs, int? threads, int? students,
        int? faculty, int? chunkSize, RealMap? map, RealCampusReport? report)
    {
        int t = threads ?? data.Balance.Performance.SimWorkerThreads;
        var sw = Stopwatch.StartNew();
        var fields = new FlowFieldSet(campus, data.Balance.Walking.Costs, t, data.Balance.Performance.PathingRebuildThreads);
        fields.Build();
        double fieldsMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        var pop = PopulationGenerator.Generate(data, campus, fields, rng, students, faculty);
        double popMs = sw.Elapsed.TotalMilliseconds;

        return new SimWorld
        {
            Data = data, Rng = rng, Campus = campus, Fields = fields, Population = pop,
            Simulation = new Simulation(data, campus, fields, pop, rng, t, chunkSize),
            Map = map, CampusReport = report,
            CampusMs = campusMs, FlowFieldsMs = fieldsMs, PopulationMs = popMs,
        };
    }
}
