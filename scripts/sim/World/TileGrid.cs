namespace LoveAndHonor.Sim.World;

/// <summary>How a tile affects walking (pathfinding cost / passability).</summary>
public enum TileType : byte
{
    /// <summary>Lawn: mown grass on campus and in town. Walkable at the grass cost; desire paths form here.</summary>
    Grass = 0,
    Path = 1,
    Building = 2,
    Water = 3,
    /// <summary>Fields, pasture, woods: walkable but costly (rough_cost_multiplier), so walkers keep to roads.</summary>
    Rough = 4,
}

/// <summary>§5.1b land states. 'Water' is an addition (creeks/ponds can't be built on) — flagged for the design doc.</summary>
public enum LandState : byte
{
    Forest, Pasture, Farmland, Town, University, Developed, Protected, Water,
}

/// <summary>§5.3 map ownership.</summary>
public enum Ownership : byte
{
    Private = 0, Town = 1, University = 2,
}

/// <summary>Path / road class of a tile (from OSM; era surfaces come later, §5.1b).</summary>
public enum PathType : byte
{
    None, Footway, Cycleway, Service, Residential, Tertiary, Secondary, Primary, Railway,
}

/// <summary>
/// The 2D tile data layer (§30.3): walkability, land state, ownership + protection, path type, foot traffic, and the
/// building occupying each tile. Utility coverage, heritage value, snow depth etc. come with their systems.
/// Row-major: index = y * Width + x, y = south.
/// </summary>
public sealed class TileGrid
{
    public int Width { get; }
    public int Height { get; }
    public float TileSizeM { get; }
    public TileType[] Types { get; }
    /// <summary>Index into Campus.Buildings of the building on this tile, or -1.</summary>
    public short[] BuildingAt { get; }
    /// <summary>Cumulative walkers that crossed each tile. Grass tiles with high traffic become desire paths (§12.4).
    /// Written by a background work item: call Simulation.SyncFootTraffic() before reading.</summary>
    public int[] FootTraffic { get; }
    /// <summary>FootTraffic as it was at the last midnight; today's walkers = FootTraffic - this. Same sync rule.</summary>
    public int[] TrafficAtMidnight { get; }
    /// <summary>
    /// Desire-path wear on lawn and rough ground, 0..1 (§12.4): rises on days a tile gets regular traffic, grows back
    /// otherwise. 1 = worn to dirt (a desire path). Updated at midnight; same sync rule as FootTraffic.
    /// </summary>
    public float[] Wear { get; }
    public LandState[] LandState { get; }
    public Ownership[] Ownership { get; }
    /// <summary>Protected natural area (nature reserve): building needs a Trustee vote (§5.3).</summary>
    public bool[] Protected { get; }
    public PathType[] PathType { get; }
    /// <summary>Bumped on every change that affects walking, so cached flow fields know they're stale.</summary>
    public int Version { get; private set; }

    public TileGrid(int width, int height, float tileSizeM)
    {
        Width = width;
        Height = height;
        TileSizeM = tileSizeM;
        Types = new TileType[width * height];
        BuildingAt = new short[width * height];
        Array.Fill(BuildingAt, (short)-1);
        FootTraffic = new int[width * height];
        TrafficAtMidnight = new int[width * height];
        Wear = new float[width * height];
        LandState = new LandState[width * height];
        Ownership = new Ownership[width * height];
        Protected = new bool[width * height];
        PathType = new PathType[width * height];
    }

    public int Index(int x, int y) => y * Width + x;
    public bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;
    public bool IsWalkable(int index) => Types[index] is not (TileType.Building or TileType.Water);
    /// <summary>Unpaved walkable ground (lawn or rough), where desire paths can form.</summary>
    public bool IsUnpaved(int index) => Types[index] is TileType.Grass or TileType.Rough;

    /// <summary>A worn tile counts as a desire path.</summary>
    public const float DesirePathWear = 0.999f; // 1 within float rounding of the daily steps

    /// <summary>
    /// End-of-day desire-path update (§12.4): unpaved tiles crossed by at least <paramref name="minWalkers"/> people
    /// today wear by <paramref name="wearPerDay"/>, all others grow back by <paramref name="regrowPerDay"/>. Starts a
    /// new day's count. Pure function of the grid, so deterministic.
    /// </summary>
    public void UpdateWear(int minWalkers, float wearPerDay, float regrowPerDay)
    {
        for (int i = 0; i < Wear.Length; i++)
        {
            int today = FootTraffic[i] - TrafficAtMidnight[i];
            if (IsUnpaved(i))
                Wear[i] = today >= minWalkers ? MathF.Min(1f, Wear[i] + wearPerDay) : MathF.Max(0f, Wear[i] - regrowPerDay);
            else
                Wear[i] = 0f;
            TrafficAtMidnight[i] = FootTraffic[i];
        }
    }

    public int CountDesirePaths()
    {
        int n = 0;
        foreach (float w in Wear) if (w >= DesirePathWear) n++;
        return n;
    }

    /// <summary>Call after writing <see cref="Types"/> directly, so cached flow fields know they're stale.</summary>
    public void MarkChanged() => Version++;

    public void SetType(int x, int y, TileType type)
    {
        if (!InBounds(x, y)) return;
        int i = Index(x, y);
        if (Types[i] == TileType.Building && type != TileType.Building) BuildingAt[i] = -1;
        Types[i] = type;
        Version++;
    }

    public void FillRect(int x0, int y0, int w, int h, TileType type)
    {
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
                SetType(x, y, type);
    }
}
