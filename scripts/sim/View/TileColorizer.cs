using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.View;

/// <summary>
/// Paints the per-tile colour texture for a given year and day (§5.1b evolving map, §28.1 seasons): land state from
/// <see cref="LandHistory"/>, seasonal colours blended between four anchor days, roads/paths once they exist, with the
/// era's road surface (eras.json) until the asphalt era. Output is RGBA8, sRGB, row-major (north row first).
/// Buildings are drawn as extruded meshes, not here.
/// </summary>
public sealed class TileColorizer
{
    public static readonly string[] Seasons = ["winter", "spring", "summer", "fall"];

    private readonly RealMap _map;
    private readonly LandHistory _history;
    private readonly EraTable _eras;
    private readonly Rgb[,] _seasonColors;          // [season, landState]
    private readonly int[] _anchorDays;              // per season
    private readonly Dictionary<string, Rgb> _roadSurface;
    private readonly Rgb[] _modernPaths;             // per PathType
    private readonly Rgb[] _blended;

    public byte[] Pixels { get; }

    public TileColorizer(RealMap map, LandHistory history, EraTable eras, RenderingConfig.TerrainSection t, Palette palette)
    {
        _map = map;
        _history = history;
        _eras = eras;
        int states = Enum.GetValues<LandState>().Length;
        _seasonColors = new Rgb[Seasons.Length, states];
        _anchorDays = new int[Seasons.Length];
        for (int s = 0; s < Seasons.Length; s++)
        {
            _anchorDays[s] = t.SeasonAnchorDays[Seasons[s]];
            foreach (var state in Enum.GetValues<LandState>())
                _seasonColors[s, (int)state] = palette.Resolve(t.SeasonColors[Seasons[s]][state.ToString().ToLowerInvariant()]);
        }
        _roadSurface = t.RoadSurfaceColors.ToDictionary(kv => kv.Key, kv => palette.Resolve(kv.Value));
        _modernPaths = new Rgb[Enum.GetValues<PathType>().Length];
        foreach (var p in Enum.GetValues<PathType>())
            if (p != PathType.None) _modernPaths[(int)p] = palette.Resolve(t.PathColors[p.ToString().ToLowerInvariant()]);
        _blended = new Rgb[states];
        Pixels = new byte[map.Grid.Width * map.Grid.Height * 4];
    }

    /// <summary>Weights of the four seasons for a day of the year: linear blend between the two surrounding anchor days (wrapping at year end).</summary>
    public static float[] SeasonWeights(int dayOfYear, int[] anchorDays)
    {
        var order = Enumerable.Range(0, anchorDays.Length).OrderBy(i => anchorDays[i]).ToArray();
        int prev = order.Length - 1; // before the first anchor → blend from the last anchor of the previous year
        for (int k = 0; k < order.Length; k++)
            if (anchorDays[order[k]] <= dayOfYear) prev = k;
        int a = order[prev], b = order[(prev + 1) % order.Length];
        float da = anchorDays[a], db = anchorDays[b];
        if (da > dayOfYear) da -= 365;
        while (db <= da) db += 365;
        float t = (dayOfYear - da) / (db - da);
        var w = new float[anchorDays.Length];
        w[a] = 1 - t;
        w[b] += t;
        return w;
    }

    public void Paint(int year, int dayOfYear)
    {
        var weights = SeasonWeights(dayOfYear, _anchorDays);
        for (int st = 0; st < _blended.Length; st++)
        {
            float r = 0, g = 0, b = 0;
            for (int s = 0; s < Seasons.Length; s++)
            {
                var c = _seasonColors[s, st];
                r += c.R * weights[s]; g += c.G * weights[s]; b += c.B * weights[s];
            }
            _blended[st] = new Rgb(r, g, b);
        }

        string surface = _eras.At(year).RoadType;
        bool modern = surface == "asphalt";
        var eraRoad = _roadSurface.GetValueOrDefault(surface, _roadSurface["dirt"]);
        var footway = surface is "brick" or "asphalt" ? _modernPaths[(int)PathType.Footway] : _roadSurface["dirt"];
        var grid = _map.Grid;

        for (int i = 0; i < grid.LandState.Length; i++)
        {
            Rgb c;
            var path = grid.PathType[i];
            if (path != PathType.None && _history.PathVisible(i, year))
                c = path == PathType.Railway ? _modernPaths[(int)PathType.Railway]
                    : path == PathType.Footway ? footway
                    : modern ? _modernPaths[(int)path] : eraRoad;
            else
                c = _blended[(int)_history.StateAt(i, year)];
            int p = i * 4;
            Pixels[p] = (byte)(c.R * 255 + 0.5f);
            Pixels[p + 1] = (byte)(c.G * 255 + 0.5f);
            Pixels[p + 2] = (byte)(c.B * 255 + 0.5f);
            Pixels[p + 3] = 255;
        }
    }
}
