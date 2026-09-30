using LoveAndHonor.Sim.Core;
using LoveAndHonor.Sim.Data;

namespace LoveAndHonor.Sim.World;

/// <summary>
/// Spike A test map: a flat grid with a campus core laid out in square blocks separated by brick paths
/// (some path segments deliberately missing so walkers cut across grass), a sparse outer sidewalk grid,
/// and off-campus housing blocks near the core. Fully determined by the spike seed.
/// </summary>
public static class SyntheticCampusGenerator
{
    public static Campus Generate(SimData data, RngStreams rng)
    {
        var map = data.Balance.Map;
        var cfg = data.Spike.Campus;
        var random = rng.For("campus");
        var grid = new TileGrid(map.GridWidth, map.GridHeight, map.TileSizeM);

        int coreSize = cfg.CoreBlocks * cfg.BlockSizeTiles;
        int origin = (map.GridWidth - coreSize) / 2;
        int originY = (map.GridHeight - coreSize) / 2;

        // Outer sidewalk grid (1 tile wide) across the whole map.
        var outer = new bool[grid.Width * grid.Height];
        for (int k = 0; k < Math.Max(grid.Width, grid.Height); k += cfg.OuterPathSpacingTiles)
        {
            for (int t = 0; t < grid.Height; t++) MarkPath(grid, outer, k, t);
            for (int t = 0; t < grid.Width; t++) MarkPath(grid, outer, t, k);
        }

        // Core path grid: lines every block, cfg.CorePathWidth wide.
        int w = cfg.CorePathWidth;
        for (int i = 0; i <= cfg.CoreBlocks; i++)
        {
            int line = i * cfg.BlockSizeTiles;
            grid.FillRect(origin + line, originY, w, coreSize + w, TileType.Path);
            grid.FillRect(origin, originY + line, coreSize + w, w, TileType.Path);
        }

        // Remove some interior segments (between intersections) so shortcuts across grass are tempting.
        for (int i = 1; i < cfg.CoreBlocks; i++)
        {
            int line = i * cfg.BlockSizeTiles;
            for (int s = 0; s < cfg.CoreBlocks; s++)
            {
                int segStart = s * cfg.BlockSizeTiles + w;
                int segLen = cfg.BlockSizeTiles - w;
                if (random.NextDouble() < cfg.PathSegmentRemovalFraction)
                    ClearSegment(grid, outer, origin + line, originY + segStart, w, segLen);          // vertical
                if (random.NextDouble() < cfg.PathSegmentRemovalFraction)
                    ClearSegment(grid, outer, origin + segStart, originY + line, segLen, w);          // horizontal
            }
        }

        // Campus buildings go into shuffled core blocks; town buildings into outer cells near the core.
        var coreBlocks = new List<(int bx, int by)>();
        for (int by = 0; by < cfg.CoreBlocks; by++)
            for (int bx = 0; bx < cfg.CoreBlocks; bx++)
                coreBlocks.Add((bx, by));
        Shuffle(coreBlocks, random);

        int centerX = grid.Width / 2, centerY = grid.Height / 2;
        var townCells = new List<(int cx, int cy)>();
        int s0 = cfg.OuterPathSpacingTiles;
        for (int cy = 0; cy + s0 <= grid.Height; cy += s0)
            for (int cx = 0; cx + s0 <= grid.Width; cx += s0)
            {
                bool overlapsCore = cx < origin + coreSize + w && cx + s0 > origin && cy < originY + coreSize + w && cy + s0 > originY;
                if (!overlapsCore) townCells.Add((cx, cy));
            }
        townCells = townCells
            .OrderBy(c => Math.Abs(c.cx + s0 / 2 - centerX) + Math.Abs(c.cy + s0 / 2 - centerY))
            .ThenBy(c => c.cy).ThenBy(c => c.cx)
            .Take(cfg.TownBlockCandidates).ToList();
        Shuffle(townCells, random);

        var buildings = new List<CampusBuilding>();
        int nextCore = 0, nextTown = 0;
        foreach (var entry in cfg.Buildings)
        {
            var def = data.Buildings[entry.Def];
            var kind = CampusBuilding.KindForCategory(def.Category);
            for (int n = 0; n < entry.Count; n++)
            {
                int cellX, cellY, cellSize, pathW, nextLineY;
                if (kind == BuildingKind.OffCampusHousing)
                {
                    if (nextTown >= townCells.Count) throw new InvalidOperationException("Not enough town cells for off-campus housing.");
                    var (cx, cy) = townCells[nextTown++];
                    cellX = cx; cellY = cy; cellSize = s0; pathW = 1; nextLineY = cy + s0;
                }
                else
                {
                    if (nextCore >= coreBlocks.Count) throw new InvalidOperationException("More campus buildings than core blocks.");
                    var (bx, by) = coreBlocks[nextCore++];
                    cellX = origin + bx * cfg.BlockSizeTiles; cellY = originY + by * cfg.BlockSizeTiles;
                    cellSize = cfg.BlockSizeTiles; pathW = w; nextLineY = cellY + cfg.BlockSizeTiles;
                }

                int bw = def.Footprint.W, bh = def.Footprint.H;
                int interior = cellSize - pathW;
                if (bw > interior - 2 || bh > interior - 3)
                    throw new InvalidOperationException($"{def.Id} footprint doesn't fit a block.");
                int x = cellX + pathW + (interior - bw) / 2;
                int y = cellY + pathW + (interior - bh) / 2;
                var index = (short)buildings.Count;

                grid.FillRect(x, y, bw, bh, TileType.Building);
                for (int ty = y; ty < y + bh; ty++)
                    for (int tx = x; tx < x + bw; tx++)
                        grid.BuildingAt[grid.Index(tx, ty)] = index;

                // Entrance on the south face, with a path spur down to the next path line.
                int ex = x + bw / 2, ey = y + bh;
                for (int ty = ey; ty < nextLineY && ty < grid.Height; ty++) grid.SetType(ex, ty, TileType.Path);

                buildings.Add(new CampusBuilding
                {
                    Index = index, DefId = def.Id, Kind = kind,
                    X = x, Y = y, W = bw, H = bh,
                    EntranceTile = grid.Index(ex, ey),
                });
            }
        }

        return new Campus(grid, buildings);
    }

    private static void MarkPath(TileGrid grid, bool[] outer, int x, int y)
    {
        if (!grid.InBounds(x, y)) return;
        grid.SetType(x, y, TileType.Path);
        outer[grid.Index(x, y)] = true;
    }

    private static void ClearSegment(TileGrid grid, bool[] outer, int x0, int y0, int w, int h)
    {
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
                if (grid.InBounds(x, y) && !outer[grid.Index(x, y)])
                    grid.SetType(x, y, TileType.Grass);
    }

    private static void Shuffle<T>(List<T> list, DeterministicRng rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.NextInt(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
