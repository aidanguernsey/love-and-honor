using System.Diagnostics;
using LoveAndHonor.Sim.Core;
using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Pathing;
using LoveAndHonor.Sim.Population;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Engine;

/// <summary>Assembles the Spike A world (campus → flow fields → population → simulation) and times each stage.</summary>
public sealed class SpikeWorld
{
    public required SimData Data { get; init; }
    public required RngStreams Rng { get; init; }
    public required Campus Campus { get; init; }
    public required FlowFieldSet Fields { get; init; }
    public required PopulationStore Population { get; init; }
    public required Simulation Simulation { get; init; }
    public double CampusMs { get; init; }
    public double FlowFieldsMs { get; init; }
    public double PopulationMs { get; init; }

    public static SpikeWorld Create(SimData data, int? threads = null, int? students = null, int? faculty = null, int? chunkSize = null)
    {
        int t = threads ?? data.Balance.Performance.SimWorkerThreads;
        var rng = new RngStreams(data.Spike.Seed);

        var sw = Stopwatch.StartNew();
        var campus = SyntheticCampusGenerator.Generate(data, rng);
        double campusMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        var fields = new FlowFieldSet(campus, data.Balance.Walking.GrassCostMultiplier, t);
        fields.Build();
        double fieldsMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        var pop = PopulationGenerator.Generate(data, campus, fields, rng, students, faculty);
        double popMs = sw.Elapsed.TotalMilliseconds;

        return new SpikeWorld
        {
            Data = data, Rng = rng, Campus = campus, Fields = fields, Population = pop,
            Simulation = new Simulation(data, campus, fields, pop, rng, t, chunkSize),
            CampusMs = campusMs, FlowFieldsMs = fieldsMs, PopulationMs = popMs,
        };
    }
}
