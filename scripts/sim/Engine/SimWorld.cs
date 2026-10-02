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
    /// <summary>Set for scenarios with enrollment (Phase 1 1h).</summary>
    public EnrollmentSystem? Enrollment { get; init; }
    /// <summary>Set for scenario worlds (Phase 1 1i): the budget and Trustee Confidence.</summary>
    public Economy.BudgetSystem? Budget { get; init; }
    /// <summary>Set for scenario worlds (Phase 1 1j): events, the History Book, advisors, goals.</summary>
    public CampaignSystem? Campaign { get; init; }
    public CampaignContent? Content { get; init; }
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
            new Treasury((long)Math.Round((startingCash ?? s.StartingCash) * 100)), PathConfig.Load(source));
        var catalog = BuildingCatalog.Load(source, data, eras, timeline, historic, map.Grid.Width, map.Grid.Height);
        // Heritage Projects whose real building already stands at the start (Old Main in 1824) are done.
        var standing = catalog.Items.Where(i => i.Site is { } site && site.RealYear <= s.MapYear
                                                && (site.DemolishedYear is null || s.MapYear < site.DemolishedYear))
            .Select(i => i.HeritageId!);
        var placement = new PlacementSystem(map.Grid, map.Heights, land, campus, catalog, standing);
        EnrollmentSetup? enrollment = null;
        // A campus building's definition: player buildings by id, real ones through their timeline entry's building_def.
        var timelineDefs = timeline.Entries.Where(e => e.BuildingDef is not null).ToDictionary(e => e.Id, e => e.BuildingDef!);
        BuildingDef? DefOf(CampusBuilding b)
        {
            string? def = data.Buildings.ContainsKey(b.DefId) ? b.DefId : timelineDefs.GetValueOrDefault(b.DefId);
            return def is not null && data.Buildings.TryGetValue(def, out var d) ? d : null;
        }
        var budgetCfg = Economy.BudgetConfig.Load(source);
        double UpkeepOf(CampusBuilding b) => DefOf(b)?.UpkeepUsdPerYear
            ?? budgetCfg.DefaultUpkeep.GetValueOrDefault(b.Kind.ToString().ToLowerInvariant(), budgetCfg.DefaultUpkeep["other"]);
        var budgetSetup = new BudgetSetup(budgetCfg, eras, land.Treasury, UpkeepOf, EnrollmentConfig.Load(source).IntakeDate,
            CampaignContent.Load(source), s.Goals);
        if (s.Enrollment)
        {
            // Capacity of a campus building: its building definition, else enrollment.json's default for its kind.
            var cfg = EnrollmentConfig.Load(source);
            int CapacityOf(CampusBuilding b)
            {
                if (DefOf(b) is { } d) return d.Capacity.Value;
                return cfg.DefaultCapacity.GetValueOrDefault(b.Kind.ToString().ToLowerInvariant());
            }
            enrollment = new EnrollmentSetup(cfg, eras, CapacityOf, s.PopulationCapacity);
        }
        return Assemble(data, rng, campus, sw.Elapsed.TotalMilliseconds, threads, students ?? s.Students, faculty ?? s.Faculty,
            chunkSize, map, report, s.Start, land, s, placement, enrollment, budgetSetup);
    }

    private sealed record EnrollmentSetup(EnrollmentConfig Config, EraTable Eras, Func<CampusBuilding, int> CapacityOf, int? Capacity);
    private sealed record BudgetSetup(Economy.BudgetConfig Config, EraTable Eras, Treasury Treasury, Func<CampusBuilding, double> UpkeepOf, string MoveIn,
        CampaignContent Content, GoalsConfig? Goals);

    private static SimWorld Assemble(SimData data, RngStreams rng, Campus campus, double campusMs, int? threads, int? students,
        int? faculty, int? chunkSize, RealMap? map, RealCampusReport? report,
        DateOnly? startDate = null, LandSystem? land = null, ScenarioConfig? scenario = null, PlacementSystem? placement = null,
        EnrollmentSetup? enrollment = null, BudgetSetup? budget = null)
    {
        int t = threads ?? data.Balance.Performance.SimWorkerThreads;
        var sw = Stopwatch.StartNew();
        var fields = new FlowFieldSet(campus, data.Balance.Walking.Costs, t, data.Balance.Performance.PathingRebuildThreads);
        fields.Build();
        double fieldsMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        var start = startDate ?? DateOnly.Parse(data.Spike.StartDate, System.Globalization.CultureInfo.InvariantCulture);
        SectionPlan? plan = enrollment is null ? null
            : SectionPlan.FromEra(enrollment.Config.EraFor(enrollment.Eras.At(start.Year).Id), data.Schedules.Student.SectionsInMajorBuilding);
        var pop = PopulationGenerator.Generate(data, campus, fields, rng, students, faculty, enrollment?.Capacity, plan);
        var enrollmentSystem = enrollment is null ? null
            : new EnrollmentSystem(data, enrollment.Config, enrollment.Eras, campus, fields, pop, rng, enrollment.CapacityOf, start);
        var budgetSystem = budget is null ? null
            : new Economy.BudgetSystem(budget.Config, budget.Eras, budget.Treasury, pop, campus, budget.UpkeepOf,
                data.Balance.Trustees.StartingConfidence, start);
        if (enrollmentSystem is not null && budgetSystem is not null) enrollmentSystem.ApplicantFactor = () => budgetSystem.ApplicantFactor;
        CampaignSystem? campaign = null;
        if (budget is not null)
        {
            PeopleContext People(DateOnly date) => new(data, campus, fields,
                enrollmentSystem?.PlanFor(date.Year) ?? SectionPlan.FromSchedules(data.Schedules));
            campaign = new CampaignSystem(budget.Content, budget.Goals, start, rng, pop, campus, enrollmentSystem, budgetSystem, placement,
                budget.Treasury, People, data.Balance.Needs.Ids) { EraIdOf = y => budget.Eras.At(y).Id };
        }
        double popMs = sw.Elapsed.TotalMilliseconds;

        return new SimWorld
        {
            Data = data, Rng = rng, Campus = campus, Fields = fields, Population = pop,
            Simulation = new Simulation(data, campus, fields, pop, rng, t, chunkSize, startDate, land, placement, enrollmentSystem,
                budgetSystem, budget?.MoveIn ?? "08-19", campaign),
            Map = map, CampusReport = report, Land = land, Placement = placement, Enrollment = enrollmentSystem, Budget = budgetSystem,
            Campaign = campaign, Content = budget?.Content,
            Scenario = scenario,
            CampusMs = campusMs, FlowFieldsMs = fieldsMs, PopulationMs = popMs,
        };
    }
}
