using System.Globalization;
using System.Text.Json;

namespace LoveAndHonor.Sim.Data;

/// <summary>Typed view of data/rendering.json (presentation settings, not simulation).</summary>
public sealed class RenderingConfig
{
    public CrowdSection Crowd { get; init; } = new();
    public AgentColorsSection AgentColors { get; init; } = new();
    public BuildingsSection Buildings { get; init; } = new();
    public GroundSection Ground { get; init; } = new();

    public sealed class CrowdSection
    {
        public int MaxRenderedAgents { get; init; }
        public float VisualWalkSpeedMps { get; init; }
        public float LateralSpreadM { get; init; }
        public int SpawnAttemptsPerFreeSlot { get; init; }
        public float ViewMarginM { get; init; }
        /// <summary>Walkers are only drawn within (factor × camera distance) of the look-at point: farther ones are sub-pixel.</summary>
        public float DetailRadiusFactor { get; init; }
        public float DetailRadiusMinM { get; init; }
        public float AgentHeightM { get; init; }
        public float AgentRadiusM { get; init; }
        public float SizeCompensationReferenceDistanceM { get; init; }
        public float SizeCompensationMaxScale { get; init; }
    }

    public sealed class AgentColorsSection
    {
        public string[] StudentByYear { get; init; } = [];
        public string Faculty { get; init; } = "";
    }

    public sealed class BuildingsSection
    {
        public Dictionary<string, float> HeightM { get; init; } = [];
        public Dictionary<string, string> Color { get; init; } = [];
    }

    public sealed class GroundSection
    {
        public string Grass { get; init; } = "";
        public string Path { get; init; } = "";
        public string Building { get; init; } = "";
        public string Wear { get; init; } = "";
        public float WearFullAtWalkers { get; init; }
        public string HeatmapLow { get; init; } = "";
        public string HeatmapHigh { get; init; } = "";
        public float RefreshSeconds { get; init; }
    }

    public static RenderingConfig Load(IDataSource source) =>
        SimJson.Parse<RenderingConfig>(source.ReadText("rendering.json"), "rendering.json");
}

/// <summary>Linear 0-1 RGB colour, engine-agnostic.</summary>
public readonly record struct Rgb(float R, float G, float B)
{
    public static Rgb Lerp(Rgb a, Rgb b, float t) => new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
}

/// <summary>
/// Resolves colour strings from rendering.json: "#RRGGBB", or a reference into branding.json "colors"
/// ("lawn" = first entry of an array or the single value, "brick:2" = third entry). Keeps the §28.2 palette in
/// one place and swaps with the branding profile.
/// </summary>
public sealed class Palette
{
    private readonly Dictionary<string, string[]> _named = new(StringComparer.Ordinal);

    public Palette(string brandingJson)
    {
        using var doc = JsonDocument.Parse(brandingJson);
        foreach (var prop in doc.RootElement.GetProperty("colors").EnumerateObject())
        {
            _named[prop.Name] = prop.Value.ValueKind == JsonValueKind.Array
                ? prop.Value.EnumerateArray().Select(e => e.GetString() ?? "").ToArray()
                : [prop.Value.GetString() ?? ""];
        }
    }

    public Rgb Resolve(string spec)
    {
        if (spec.StartsWith('#')) return ParseHex(spec);
        var parts = spec.Split(':');
        int index = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
        if (!_named.TryGetValue(parts[0], out var values) || index >= values.Length)
            throw new InvalidDataException($"Unknown palette colour '{spec}' (not in branding.json colors).");
        return ParseHex(values[index]);
    }

    public static Rgb ParseHex(string hex)
    {
        int v = int.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new Rgb(((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f);
    }
}
