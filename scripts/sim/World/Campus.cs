namespace LoveAndHonor.Sim.World;

/// <summary>What an agent can do in a building. Derived from the building definition's category.</summary>
public enum BuildingKind : byte
{
    Academic,
    Residence,
    Dining,
    Library,
    Recreation,
    OffCampusHousing,
    Other,
}

public sealed class CampusBuilding
{
    public required short Index { get; init; }
    public required string DefId { get; init; }
    public required BuildingKind Kind { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public required int W { get; init; }
    public required int H { get; init; }
    /// <summary>Walkable tile where agents enter/leave. Routes start and end here.</summary>
    public required int EntranceTile { get; init; }
    /// <summary>Relative share when picking homes (off-campus housing zones: building tiles they stand for).</summary>
    public float Weight { get; init; } = 1f;

    public static BuildingKind KindForCategory(string category) => category switch
    {
        "academic" => BuildingKind.Academic,
        "residence" => BuildingKind.Residence,
        "dining" => BuildingKind.Dining,
        "library" => BuildingKind.Library,
        "student_life" => BuildingKind.Recreation,
        "town" => BuildingKind.OffCampusHousing,
        _ => BuildingKind.Other,
    };
}

public sealed class Campus(TileGrid grid, IReadOnlyList<CampusBuilding> buildings)
{
    private readonly List<CampusBuilding> _buildings = [.. buildings];

    public TileGrid Grid { get; } = grid;
    public IReadOnlyList<CampusBuilding> Buildings => _buildings;

    /// <summary>Adds a finished building (Phase 1 1e) at index <see cref="Buildings"/>.Count. Sim thread only; the flow
    /// fields pick it up on their next rebuild.</summary>
    public void Add(CampusBuilding building)
    {
        if (building.Index != _buildings.Count) throw new ArgumentException("building index must be the next free index");
        _buildings.Add(building);
    }

    public IEnumerable<CampusBuilding> OfKind(BuildingKind kind) => Buildings.Where(b => b.Kind == kind);
}
