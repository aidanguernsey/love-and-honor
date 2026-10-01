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
    /// <summary>Set for scenario worlds (Phase 1 1d): the land layer and money, and the scenario itself.</summary>
    public LandSystem? Land { get; init; }
    /// <summary>Set for scenario worlds (Phase 1 1e): building placement and construction.</summary>
    public PlacementSystem? Placement { get; init; }
    public ScenarioConfig? Scenario { get; init; }
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

    /// <summary>
    /// A game start from data/scenarios/&lt;id&gt;.json (Phase 1 1d): Chapter 1 in 1824 (the map as it stood that year,
    /// the university's starting land, Old Main) or the 2026 preview. Includes the land layer and money.
    /// </summary>
    /// <param name="startingCash">Overrides the scenario's starting cash (dollars of the start year), for testing.</param>
    public static SimWorld CreateScenario(SimData data, IDataSource source, string scenarioId, int? threads = null,
        int? students = null, int? faculty = null, int? chunkSize = null, RealMap? map = null, double? startingCash = null)
    {
        var s = ScenarioConfig.Load(source, scenarioId);
        var rng = new RngStreams(s.Seed);
        var sw = Stopwatch.StartNew();
        map ??= RealMapLoader.Load(source);
        var features = FeatureBuilding.Load(source, map.Meta.FeaturesFile);
        var timeline = TimelineData.Load(source);
        var historyCfg = LandHistoryConfig.Load(source);
        int present = historyCfg.PresentYear;
        var historic = HistoricBuilding.Build(features, timeline, present, out _);
        var seeds = historic.Where(b => b.Entry?.BuiltYear is not null)
            .Select(b => { var c = b.Footprint.Centroid(); return (c.X, c.Y, b.BuiltYear); });
        var history = new LandHistory(map.Grid, historyCfg, seeds); // copies today's map before it's changed
        bool past = s.Campus is not null;
        bool[] pathOn = past ? HistoricMap.Apply(map, historic, history, s) : map.Grid.Types.Select(t => t == TileType.Path).ToArray();
        var campus = RealCampusBuilder.Build(map, features, timeline, RealCampusConfig.Load(source), s.MapYear, present, out var report, past);
        var eras = EraTable.Load(source);
        var land = new LandSystem(map.Grid, LandConfig.Load(source), eras, past ? history : null, pathOn,
            new Treasury((long)Math.Round((startingCash ?? s.StartingCash) * 100)));
        var catalog = BuildingCatalog.Load(source, data, eras, timeline, historic, map.Grid.Width, map.Grid.Height);
        // Heritage Projects whose real building already stands at the start (Old Main in 1824) are done.
        var standing = catalog.Items.Where(i => i.Site is { } site && site.RealYear <= s.MapYear
                                                && (site.DemolishedYear is null || s.MapYear < site.DemolishedYear))
            .Select(i => i.HeritageId!);
        var placement = new PlacementSystem(map.Grid, map.Heights, land, campus, catalog, standing);
        return Assemble(data, rng, campus, sw.Elapsed.TotalMilliseconds, threads, students ?? s.Students, faculty ?? s.Faculty,
            chunkSize, map, report, s.Start, land, s, placement);
    }

    private static SimWorld Assemble(SimData data, RngStreams rng, Campus campus, double campusMs, int? threads, int? students,
        int? faculty, int? chunkSize, RealMap? map, RealCampusReport? report,
        DateOnly? startDate = null, LandSystem? land = null, ScenarioConfig? scenario = null, PlacementSystem? placement = null)
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
            Simulation = new Simulation(data, campus, fields, pop, rng, t, chunkSize, startDate, land, placement),
            Map = map, CampusReport = report, Land = land, Placement = placement, Scenario = scenario,
            CampusMs = campusMs, FlowFieldsMs = fieldsMs, PopulationMs = popMs,
        };
    }
}
