using System.Text.Json;
using LoveAndHonor.Sim.Data;

namespace LoveAndHonor.Sim.World;

/// <summary>The real Oxford map produced by tools/map_pipeline: tile data layer + heightmap + metadata.</summary>
public sealed class RealMap
{
    public required MapMetadata Meta { get; init; }
    public required TileGrid Grid { get; init; }
    public required Heightmap Heights { get; init; }
    /// <summary>Raw pipeline land-cover code per tile (see Meta.Codes.Landcover), kept for the timeline and inspection.</summary>
    public required byte[] LandCover { get; init; }
    public required bool[] HasBuilding { get; init; }

    public float SizeM => Grid.Width * Grid.TileSizeM;

    /// <summary>Tile index under world (x, z), or -1 outside the map.</summary>
    public int TileAt(float x, float z)
    {
        int tx = (int)MathF.Floor(x / Grid.TileSizeM), tz = (int)MathF.Floor(z / Grid.TileSizeM);
        return Grid.InBounds(tx, tz) ? Grid.Index(tx, tz) : -1;
    }

    public string LandCoverName(int tile) =>
        Meta.Codes.Landcover.FirstOrDefault(kv => kv.Value == LandCover[tile]).Key ?? LandCover[tile].ToString();
}

public sealed class MapMetadata
{
    public int Tiles { get; init; }
    public float TileSizeM { get; init; }
    public CenterInfo Center { get; init; } = new();
    public float SizeM { get; init; }
    public HeightmapInfo Heightmap { get; init; } = new();
    public Dictionary<string, RasterInfo> Rasters { get; init; } = [];
    public CodeTables Codes { get; init; } = new();
    public string FeaturesFile { get; init; } = "";

    public sealed class CenterInfo
    {
        public double Lat { get; init; }
        public double Lon { get; init; }
    }

    public sealed class HeightmapInfo
    {
        public string File { get; init; } = "";
        public int Width { get; init; }
        public int Height { get; init; }
        public float ResolutionM { get; init; }
        public float ElevationMinM { get; init; }
        public float ElevationMaxM { get; init; }
    }

    public sealed class RasterInfo
    {
        public string File { get; init; } = "";
        public int Width { get; init; }
        public int Height { get; init; }
    }

    public sealed class CodeTables
    {
        public Dictionary<string, int> Landcover { get; init; } = [];
        public Dictionary<string, int> Ownership { get; init; } = [];
        public int OwnershipProtectedBit { get; init; }
        public Dictionary<string, int> Paths { get; init; } = [];
    }
}

/// <summary>data/map/land_states.json.</summary>
public sealed class LandStateRules
{
    public string[] States { get; init; } = [];
    public Rule[] PresentDayRules { get; init; } = [];

    public sealed class Rule
    {
        public bool? Protected { get; init; }
        public string[]? Landcover { get; init; }
        public string[]? Ownership { get; init; }
        public string State { get; init; } = "";
    }
}

public static class RealMapLoader
{
    /// <summary>Loads data/map/&lt;prefix&gt;_map.json and its binary layers, and derives the present-day tile data layer.</summary>
    public static RealMap Load(IDataSource source)
    {
        using var config = JsonDocument.Parse(source.ReadText("map/map_config.json"));
        string prefix = config.RootElement.GetProperty("output_prefix").GetString()!;
        var meta = SimJson.Parse<MapMetadata>(source.ReadText($"map/{prefix}_map.json"), $"{prefix}_map.json");
        var rules = SimJson.Parse<LandStateRules>(source.ReadText("map/land_states.json"), "land_states.json");

        var h = meta.Heightmap;
        var heights = Heightmap.FromR16(source.ReadBytes($"map/{h.File}"), h.Width, h.Height, h.ResolutionM, h.ElevationMinM, h.ElevationMaxM);

        byte[] Raster(string name)
        {
            var info = meta.Rasters[name];
            var bytes = source.ReadBytes($"map/{info.File}");
            if (bytes.Length != meta.Tiles * meta.Tiles || info.Width != meta.Tiles || info.Height != meta.Tiles)
                throw new InvalidDataException($"{info.File}: expected {meta.Tiles}² bytes, got {bytes.Length} (Git LFS pulled?)");
            return bytes;
        }
        byte[] landcover = Raster("landcover"), ownership = Raster("ownership"), paths = Raster("paths"), buildings = Raster("buildings");

        var grid = new TileGrid(meta.Tiles, meta.Tiles, meta.TileSizeM);
        var stateTable = BuildStateTable(meta.Codes, rules);
        var pathTable = BuildPathTable(meta.Codes.Paths);
        int protectedBit = meta.Codes.OwnershipProtectedBit;
        int waterCode = meta.Codes.Landcover.GetValueOrDefault("water", -1);
        int wetlandCode = meta.Codes.Landcover.GetValueOrDefault("wetland", -1);
        var hasBuilding = new bool[buildings.Length];

        for (int i = 0; i < landcover.Length; i++)
        {
            int own = ownership[i] & ~protectedBit;
            bool prot = (ownership[i] & protectedBit) != 0;
            grid.Ownership[i] = (Ownership)Math.Min(own, 2);
            grid.Protected[i] = prot;
            grid.LandState[i] = stateTable[landcover[i] * 6 + Math.Min(own, 2) * 2 + (prot ? 1 : 0)];
            grid.PathType[i] = pathTable[paths[i]];
            hasBuilding[i] = buildings[i] != 0;

            // Walkability: buildings block; paths (incl. footbridges) win over water; water blocks.
            grid.Types[i] = hasBuilding[i] ? TileType.Building
                : grid.PathType[i] is not (PathType.None or PathType.Railway) ? TileType.Path
                : landcover[i] == waterCode || landcover[i] == wetlandCode ? TileType.Water
                : TileType.Grass;
        }

        return new RealMap { Meta = meta, Grid = grid, Heights = heights, LandCover = landcover, HasBuilding = hasBuilding };
    }

    /// <summary>Evaluates the rule list once for every (landcover, ownership, protected) combination.</summary>
    internal static LandState[] BuildStateTable(MapMetadata.CodeTables codes, LandStateRules rules)
    {
        var landcoverName = codes.Landcover.ToDictionary(kv => kv.Value, kv => kv.Key);
        string[] ownershipNames = ["private", "town", "university"];
        var table = new LandState[256 * 6];
        for (int lc = 0; lc < 256; lc++)
            for (int own = 0; own < 3; own++)
                for (int p = 0; p < 2; p++)
                {
                    string lcName = landcoverName.GetValueOrDefault(lc, "");
                    var rule = rules.PresentDayRules.First(r =>
                        (r.Protected is null || r.Protected == (p == 1)) &&
                        (r.Landcover is null || r.Landcover.Contains(lcName)) &&
                        (r.Ownership is null || r.Ownership.Contains(ownershipNames[own])));
                    table[lc * 6 + own * 2 + p] = Enum.Parse<LandState>(rule.State, ignoreCase: true);
                }
        return table;
    }

    private static PathType[] BuildPathTable(Dictionary<string, int> codes)
    {
        var table = new PathType[256];
        foreach (var (name, code) in codes)
            if (Enum.TryParse<PathType>(name, ignoreCase: true, out var type)) table[code] = type;
        return table;
    }
}
