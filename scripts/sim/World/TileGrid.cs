namespace LoveAndHonor.Sim.World;

public enum TileType : byte
{
    Grass = 0,
    Path = 1,
    Building = 2,
}

/// <summary>
/// The 2D tile layer (§30.3). Spike A uses tile type, the building occupying a tile, and foot traffic.
/// Land state, ownership, utility coverage etc. are added in Spike B. Row-major: index = y * Width + x.
/// </summary>
public sealed class TileGrid
{
    public int Width { get; }
    public int Height { get; }
    public float TileSizeM { get; }
    public TileType[] Types { get; }
    /// <summary>Index into Campus.Buildings of the building on this tile, or -1.</summary>
    public short[] BuildingAt { get; }
    /// <summary>Cumulative walkers that crossed each tile. Grass tiles with high traffic become desire paths (§12.4).</summary>
    public int[] FootTraffic { get; }
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
    }

    public int Index(int x, int y) => y * Width + x;
    public bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;
    public bool IsWalkable(int index) => Types[index] != TileType.Building;

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
