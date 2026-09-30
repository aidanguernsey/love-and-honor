using System.Text.Json;

namespace LoveAndHonor.Sim.Data;

public static class SimJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    public static T Parse<T>(string json, string sourceName) =>
        JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new InvalidDataException($"{sourceName}: deserialized to null");
}

/// <summary>Everything the sim loads from /data, parsed once at startup.</summary>
public sealed class SimData
{
    public required BalanceConfig Balance { get; init; }
    public required ScheduleConfig Schedules { get; init; }
    public required DepartmentsConfig Departments { get; init; }
    public required IReadOnlyDictionary<string, BuildingDef> Buildings { get; init; }
    public required PopulationSpikeConfig Spike { get; init; }

    public const string SpikeFile = "spikes/population_spike.json";

    public static SimData Load(IDataSource source)
    {
        var buildings = new Dictionary<string, BuildingDef>(StringComparer.Ordinal);
        foreach (var path in source.ListJson("buildings"))
        {
            var def = SimJson.Parse<BuildingDef>(source.ReadText(path), path);
            buildings.Add(def.Id, def);
        }

        return new SimData
        {
            Balance = SimJson.Parse<BalanceConfig>(source.ReadText("balance.json"), "balance.json"),
            Schedules = SimJson.Parse<ScheduleConfig>(source.ReadText("schedules.json"), "schedules.json"),
            Departments = SimJson.Parse<DepartmentsConfig>(source.ReadText("departments.json"), "departments.json"),
            Buildings = buildings,
            Spike = SimJson.Parse<PopulationSpikeConfig>(source.ReadText(SpikeFile), SpikeFile),
        };
    }
}
