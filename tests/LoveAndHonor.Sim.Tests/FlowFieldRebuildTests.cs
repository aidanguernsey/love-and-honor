using LoveAndHonor.Sim.Core;
using LoveAndHonor.Sim.Pathing;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Tests;

/// <summary>Phase 1 (1a): incremental background rebuilds must give exactly what a full build gives.</summary>
public class FlowFieldRebuildTests
{
    private const float TileM = 10f;

    /// <summary>A random map: grass with path lines, some water and building blocks, a few 1-tile buildings
    /// (one of them off-campus housing, i.e. without a field).</summary>
    private static Campus RandomCampus(int seed, int size = 48)
    {
        var rng = new DeterministicRng((ulong)seed);
        var grid = new TileGrid(size, size, TileM);
        for (int i = 0; i < size; i += 6)
        {
            grid.FillRect(i, 0, 1, size, TileType.Path);
            grid.FillRect(0, i, size, 1, TileType.Path);
        }
        for (int k = 0; k < 25; k++)
            grid.FillRect(rng.NextInt(size - 3), rng.NextInt(size - 3), 1 + rng.NextInt(3), 1 + rng.NextInt(3),
                k % 3 == 0 ? TileType.Water : TileType.Building);
        var buildings = new List<CampusBuilding>();
        while (buildings.Count < 6)
        {
            int x = 1 + rng.NextInt(size - 2), y = 1 + rng.NextInt(size - 2);
            int b = grid.Index(x, y - 1), e = grid.Index(x, y);
            if (grid.Types[e] is TileType.Building or TileType.Water || buildings.Any(o => o.EntranceTile == e)) continue;
            grid.SetType(x, y - 1, TileType.Building);
            var index = (short)buildings.Count;
            grid.BuildingAt[b] = index;
            buildings.Add(new CampusBuilding
            {
                Index = index, DefId = "test", Kind = index == 5 ? BuildingKind.OffCampusHousing : BuildingKind.Academic,
                X = x, Y = y - 1, W = 1, H = 1, EntranceTile = e,
            });
        }
        return new Campus(grid, buildings);
    }

    private static void AssertSame(FlowFieldSet a, FlowFieldSet b, int buildings, string context)
    {
        for (int k = 0; k < buildings; k++)
        {
            Assert.Equal(a.HasField(k), b.HasField(k));
            if (!a.HasField(k)) continue;
            Assert.True(a.CostsTo(k).AsSpan().SequenceEqual(b.CostsTo(k)), $"{context}: costs differ for field {k}");
            Assert.True(a.DirectionsTo(k).AsSpan().SequenceEqual(b.DirectionsTo(k)), $"{context}: directions differ for field {k}");
        }
        for (int from = 0; from < buildings; from++)
            for (int to = 0; to < buildings; to++)
            {
                Assert.Equal(a.Distance(from, to), b.Distance(from, to));
                Assert.True(a.Route(from, to).AsSpan().SequenceEqual(b.Route(from, to)), $"{context}: route {from}->{to} differs");
            }
    }

    public static TheoryData<int> Seeds() => new(Enumerable.Range(1, 20));

    [Theory]
    [MemberData(nameof(Seeds))]
    public void IncrementalUpdates_MatchAFullRebuild(int seed)
    {
        var campus = RandomCampus(seed);
        var grid = campus.Grid;
        var incremental = new FlowFieldSet(campus, 1.3f, threads: 2, backgroundThreads: 2);
        incremental.Build();
        var rng = new DeterministicRng((ulong)(seed * 7919));
        var entrances = campus.Buildings.Select(b => b.EntranceTile).ToHashSet();

        for (int round = 0; round < 12; round++)
        {
            // A few random edits per round: paths laid/removed, and walkability changes (water/buildings appear or go).
            var changed = new HashSet<int>();
            for (int e = 0; e < 1 + rng.NextInt(4); e++)
            {
                int t = rng.NextInt(grid.Width * grid.Height);
                if (entrances.Contains(t) || grid.BuildingAt[t] >= 0) continue;
                var type = (TileType)rng.NextInt(4);
                if (grid.Types[t] == type) continue;
                grid.Types[t] = type;
                grid.MarkChanged();
                changed.Add(t);
            }
            incremental.BeginRebuild(changed);
            incremental.CompleteRebuild();

            var full = new FlowFieldSet(campus, 1.3f, threads: 1);
            full.Build();
            AssertSame(incremental, full, campus.Buildings.Count, $"seed {seed} round {round}");
        }
        Assert.True(incremental.Current.FieldsBuilt == 0, "later rounds should update incrementally, not rebuild");
    }

    [Fact]
    public void NewerRequest_SupersedesAPendingOne()
    {
        var campus = RandomCampus(9);
        var grid = campus.Grid;
        var fields = new FlowFieldSet(campus, 1.3f, 1, 1);
        fields.Build();
        int a = grid.Index(20, 21), b = grid.Index(33, 22);
        grid.Types[a] = TileType.Water; grid.MarkChanged();
        fields.BeginRebuild([a]);
        grid.Types[b] = TileType.Water; grid.MarkChanged();
        fields.BeginRebuild([a, b]); // union of changes since Current
        fields.CompleteRebuild();
        Assert.False(fields.RebuildPending);
        Assert.Equal(-1, fields.CompleteRebuild());

        var full = new FlowFieldSet(campus, 1.3f, 1);
        full.Build();
        AssertSame(fields, full, campus.Buildings.Count, "superseded");
    }

    [Fact]
    public void Readers_KeepTheOldSetUntilTheSwap()
    {
        var campus = RandomCampus(4);
        var grid = campus.Grid;
        var fields = new FlowFieldSet(campus, 1.3f, 1, 1);
        fields.Build();
        var before = fields.Current;
        int t = grid.Index(24, 24);
        grid.Types[t] = grid.Types[t] == TileType.Path ? TileType.Grass : TileType.Path;
        grid.MarkChanged();
        fields.BeginRebuild([t]);
        Assert.Same(before, fields.Current);
        Assert.True(fields.CompleteRebuild() >= 0);
        Assert.NotSame(before, fields.Current);
        Assert.Equal(grid.Version, fields.Current.GridVersion);
    }
}
