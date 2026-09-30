using LoveAndHonor.Sim.Data;

namespace LoveAndHonor.Sim.World;

/// <summary>data/map/land_history.json — PLACEHOLDER rules, see the file's comment.</summary>
public sealed class LandHistoryConfig
{
    public int StartYear { get; init; }
    public int PresentYear { get; init; }
    public OriginSection TownOrigin { get; init; } = new();
    public float TownSpreadMPerYear { get; init; }
    /// <summary>"square" (Chebyshev distance, like the Mile Square plat) or "circle".</summary>
    public string TownGrowthShape { get; init; } = "square";
    public ClearingSection Clearing { get; init; } = new();
    public UniversitySection University { get; init; } = new();
    public int ProtectedFromYear { get; init; }
    public RoadsSection Roads { get; init; } = new();

    public sealed class OriginSection { public float[] Tile { get; init; } = [0, 0]; public int FoundedYear { get; init; } }
    public sealed class ClearingSection { public int StartYear { get; init; } public float SpreadMPerYear { get; init; } public int FarmlandDelayYears { get; init; } }
    public sealed class UniversitySection { public float SpreadMPerYear { get; init; } public int MaxYear { get; init; } }
    public sealed class RoadsSection { public int RailwayYear { get; init; } public int RuralRoadDelayYears { get; init; } }

    public static LandHistoryConfig Load(IDataSource source) =>
        SimJson.Parse<LandHistoryConfig>(source.ReadText("map/land_history.json"), "land_history.json");
}

/// <summary>
/// Derives each tile's land state in any year from its present-day state (§5.1b evolving map):
///  - water stays water; present-day forest stays forest; protected areas are forest until protected_from_year;
///  - open land is forest until cleared (spreading out from Uptown), then pasture, then farmland;
///  - town/developed land urbanises outward from Uptown after the town's founding;
///  - university land develops outward from each dated campus building.
/// Transition years are clamped so the present year always reproduces today's map exactly.
/// These are placeholder rules until historic maps are traced (§30.5 step 3).
/// </summary>
public sealed class LandHistory
{
    private readonly TileGrid _grid;
    private readonly LandHistoryConfig _cfg;
    private readonly LandState[] _present;
    private readonly PathType[] _paths;
    private readonly short[] _clearYear;   // forest → pasture
    private readonly short[] _urbanYear;   // → present town / developed / university state

    public int PresentYear => _cfg.PresentYear;
    public int StartYear => _cfg.StartYear;

    public LandHistory(TileGrid grid, LandHistoryConfig cfg, IEnumerable<(float X, float Y, int BuiltYear)> campusSeeds)
    {
        _grid = grid;
        _cfg = cfg;
        int n = grid.Width * grid.Height;
        _present = (LandState[])grid.LandState.Clone();
        _paths = (PathType[])grid.PathType.Clone();
        _clearYear = new short[n];
        _urbanYear = new short[n];

        var seeds = campusSeeds.ToArray();
        float tile = grid.TileSizeM, ox = cfg.TownOrigin.Tile[0], oy = cfg.TownOrigin.Tile[1];
        int present = cfg.PresentYear;
        int latestClear = present - Math.Max(cfg.Clearing.FarmlandDelayYears, cfg.Roads.RuralRoadDelayYears);

        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                int i = grid.Index(x, y);
                float cx = x + 0.5f, cy = y + 0.5f;
                float dTown = MathF.Sqrt((cx - ox) * (cx - ox) + (cy - oy) * (cy - oy)) * tile;
                float dTownGrowth = cfg.TownGrowthShape == "square" ? MathF.Max(MathF.Abs(cx - ox), MathF.Abs(cy - oy)) * tile : dTown;

                float clear = cfg.Clearing.StartYear + dTown / cfg.Clearing.SpreadMPerYear;
                _clearYear[i] = (short)Math.Clamp((int)clear, cfg.StartYear, latestClear);

                float urban = cfg.TownOrigin.FoundedYear + dTownGrowth / cfg.TownSpreadMPerYear;
                if (_present[i] == LandState.University)
                {
                    urban = cfg.University.MaxYear;
                    foreach (var s in seeds)
                    {
                        float d = MathF.Sqrt((cx - s.X) * (cx - s.X) + (cy - s.Y) * (cy - s.Y)) * tile;
                        urban = MathF.Min(urban, s.BuiltYear + d / cfg.University.SpreadMPerYear);
                    }
                }
                _urbanYear[i] = (short)Math.Clamp((int)urban, cfg.StartYear, present);
            }
    }

    public LandState PresentState(int tile) => _present[tile];

    public LandState StateAt(int tile, int year)
    {
        var p = _present[tile];
        switch (p)
        {
            case LandState.Water:
            case LandState.Forest:
                return p;
            case LandState.Protected:
                return year >= Math.Min(_cfg.ProtectedFromYear, _cfg.PresentYear) ? LandState.Protected : LandState.Forest;
            case LandState.Town or LandState.Developed or LandState.University when year >= _urbanYear[tile]:
                return p;
        }
        int clear = _clearYear[tile];
        if (year < clear) return LandState.Forest;
        if (p == LandState.Pasture) return LandState.Pasture;
        return year >= clear + _cfg.Clearing.FarmlandDelayYears ? LandState.Farmland : LandState.Pasture;
    }

    /// <summary>Is the tile's present-day path/road/railway there yet?</summary>
    public bool PathVisible(int tile, int year)
    {
        var path = _paths[tile];
        if (path == PathType.None) return false;
        if (path == PathType.Railway) return year >= Math.Min(_cfg.Roads.RailwayYear, _cfg.PresentYear);
        return _present[tile] is LandState.Town or LandState.Developed or LandState.University
            ? year >= _urbanYear[tile]
            : year >= _clearYear[tile] + _cfg.Roads.RuralRoadDelayYears;
    }

    public int UrbanYear(int tile) => _urbanYear[tile];
    public int ClearYear(int tile) => _clearYear[tile];
}
