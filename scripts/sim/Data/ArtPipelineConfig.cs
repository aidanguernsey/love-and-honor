using System.Globalization;
using System.Text.RegularExpressions;

namespace LoveAndHonor.Sim.Data;

/// <summary>Typed view of data/art_pipeline.json (docs/ART_PIPELINE.md). Presentation/tooling only, not simulation.</summary>
public sealed class ArtPipelineConfig
{
    public KitGridSection KitGrid { get; init; } = new();
    public Dictionary<string, CategorySection> Categories { get; init; } = [];
    public LodSection Lod { get; init; } = new();
    public Dictionary<string, MaterialSection> SpecialMaterials { get; init; } = [];

    public sealed class KitGridSection
    {
        public float ModuleWidthM { get; init; }
        public float StoreyHeightM { get; init; }
        public float WallThicknessM { get; init; }
        public float SnapM { get; init; }
    }

    public sealed class CategorySection
    {
        public string Folder { get; init; } = "";
        public int MaxTris { get; init; }
        /// <summary>[min, max] LOD levels, LOD0 included.</summary>
        public int[] LodLevels { get; init; } = [1, 1];
        public float[] LodDistancesM { get; init; } = [];
        public string[] Variants { get; init; } = [];
        public bool AllowDoubleSided { get; init; }
    }

    public sealed class LodSection
    {
        public float[] MaxRatioToLod0 { get; init; } = [];
        public float RangeMarginM { get; init; }
    }

    public sealed class MaterialSection
    {
        public string Color { get; init; } = "";
        public float Roughness { get; init; }
        public float Metallic { get; init; }
        public float EmissionEnergy { get; init; }
    }

    public const string File = "art_pipeline.json";

    public static ArtPipelineConfig Load(IDataSource source) => Parse(source.ReadText(File));

    public static ArtPipelineConfig Parse(string json) => SimJson.Parse<ArtPipelineConfig>(json, File);
}

/// <summary>
/// Material naming rule for imported models: <c>pal_&lt;branding colour&gt;[_&lt;index&gt;]</c> takes its colour from
/// branding.json "colors" (so it swaps with the branding profile), <c>mat_*</c> is a special material from
/// art_pipeline.json. Anything else is an error.
/// </summary>
public static partial class MaterialSlot
{
    public const string PalettePrefix = "pal_";

    [GeneratedRegex(@"^pal_([a-z]+)(?:_(\d+))?$")]
    private static partial Regex PaletteName();

    /// <summary>"pal_brick_1" → ("brick", 1) → the palette spec "brick:1" (see <see cref="Palette.Resolve"/>).</summary>
    public static bool TryParsePalette(string name, out string key, out int index)
    {
        var m = PaletteName().Match(name);
        key = m.Success ? m.Groups[1].Value : "";
        index = m.Success && m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
        return m.Success;
    }

    public static string PaletteSpec(string key, int index) => index == 0 ? key : $"{key}:{index}";
}
