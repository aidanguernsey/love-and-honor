using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Engine;

/// <summary>One recorded month: the date and every tile that changed since the previous frame (5 bytes each).</summary>
public sealed record TimelapseFrame(DateOnly Date, int[] Tiles, byte[] States, byte[] Owners, byte[] Types, byte[] PathSurfaces, byte[] PathTypes);

/// <summary>
/// Time-lapse recording (§5.1b "scrub a timeline to watch your campus grow", Phase 1 1k): a keyframe of the map layers
/// at the start, then once a month only the tiles whose land state, owner, walking surface, path surface or path class
/// changed. Buildings come from the placement sites' order/finish dates. Frames are immutable and the frame list is
/// replaced (not changed) when a month is added, so the UI can read it from the main thread while the sim records.
/// Reconstruction is pure. Memory: a few KB a month.
/// </summary>
public sealed class TimelapseRecorder
{
    private readonly int _tiles;
    // The keyframe (start) and the last recorded state, for diffs (sim thread only).
    private readonly byte[] _k0, _k1, _k2, _k3, _k4;
    private readonly byte[] _l0, _l1, _l2, _l3, _l4;
    private volatile TimelapseFrame[] _frames = [];

    public DateOnly Start { get; private set; }
    public IReadOnlyList<TimelapseFrame> Frames => _frames;

    public TimelapseRecorder(DateOnly start, TileGrid g, byte[] pathSurface)
    {
        _tiles = g.Width * g.Height;
        Start = start;
        _k0 = Bytes(g.LandState); _k1 = Bytes(g.Ownership); _k2 = Bytes(g.Types); _k3 = (byte[])pathSurface.Clone(); _k4 = Bytes(g.PathType);
        _l0 = (byte[])_k0.Clone(); _l1 = (byte[])_k1.Clone(); _l2 = (byte[])_k2.Clone(); _l3 = (byte[])_k3.Clone(); _l4 = (byte[])_k4.Clone();
    }

    private static byte[] Bytes<T>(T[] a) where T : unmanaged =>
        System.Runtime.InteropServices.MemoryMarshal.AsBytes(a.AsSpan()).ToArray();

    /// <summary>Records the month (sim thread).</summary>
    public void Record(DateOnly date, TileGrid g, byte[] pathSurface)
    {
        var s = System.Runtime.InteropServices.MemoryMarshal.AsBytes(g.LandState.AsSpan());
        var o = System.Runtime.InteropServices.MemoryMarshal.AsBytes(g.Ownership.AsSpan());
        var ty = System.Runtime.InteropServices.MemoryMarshal.AsBytes(g.Types.AsSpan());
        var pt = System.Runtime.InteropServices.MemoryMarshal.AsBytes(g.PathType.AsSpan());
        var tiles = new List<int>();
        for (int t = 0; t < _tiles; t++)
            if (s[t] != _l0[t] || o[t] != _l1[t] || ty[t] != _l2[t] || pathSurface[t] != _l3[t] || pt[t] != _l4[t]) tiles.Add(t);
        var f = new TimelapseFrame(date, [.. tiles], new byte[tiles.Count], new byte[tiles.Count], new byte[tiles.Count], new byte[tiles.Count], new byte[tiles.Count]);
        for (int i = 0; i < tiles.Count; i++)
        {
            int t = tiles[i];
            f.States[i] = _l0[t] = s[t]; f.Owners[i] = _l1[t] = o[t]; f.Types[i] = _l2[t] = ty[t];
            f.PathSurfaces[i] = _l3[t] = pathSurface[t]; f.PathTypes[i] = _l4[t] = pt[t];
        }
        _frames = [.. _frames, f];
    }

    /// <summary>The map layers as they were after <paramref name="frames"/> recorded months (0 = the start). Pure.</summary>
    public void Reconstruct(int frames, LandState[] states, Ownership[] owners, TileType[] types, byte[] pathSurfaces, PathType[] pathTypes)
    {
        var fr = _frames;
        for (int t = 0; t < _tiles; t++)
        {
            states[t] = (LandState)_k0[t]; owners[t] = (Ownership)_k1[t]; types[t] = (TileType)_k2[t]; pathSurfaces[t] = _k3[t]; pathTypes[t] = (PathType)_k4[t];
        }
        for (int i = 0; i < Math.Min(frames, fr.Length); i++)
        {
            var f = fr[i];
            for (int k = 0; k < f.Tiles.Length; k++)
            {
                int t = f.Tiles[k];
                states[t] = (LandState)f.States[k]; owners[t] = (Ownership)f.Owners[k]; types[t] = (TileType)f.Types[k];
                pathSurfaces[t] = f.PathSurfaces[k]; pathTypes[t] = (PathType)f.PathTypes[k];
            }
        }
    }

    public void WriteState(BinaryWriter w)
    {
        w.Write(Start);
        w.WriteArray(_k0); w.WriteArray(_k1); w.WriteArray(_k2); w.WriteArray(_k3); w.WriteArray(_k4);
        w.WriteArray(_l0); w.WriteArray(_l1); w.WriteArray(_l2); w.WriteArray(_l3); w.WriteArray(_l4);
        var fr = _frames;
        w.Write(fr.Length);
        foreach (var f in fr)
        {
            w.Write(f.Date); w.WriteArray(f.Tiles); w.WriteArray(f.States); w.WriteArray(f.Owners); w.WriteArray(f.Types);
            w.WriteArray(f.PathSurfaces); w.WriteArray(f.PathTypes);
        }
    }

    public void ReadState(BinaryReader r)
    {
        Start = r.ReadDate();
        r.ReadArrayInto(_k0); r.ReadArrayInto(_k1); r.ReadArrayInto(_k2); r.ReadArrayInto(_k3); r.ReadArrayInto(_k4);
        r.ReadArrayInto(_l0); r.ReadArrayInto(_l1); r.ReadArrayInto(_l2); r.ReadArrayInto(_l3); r.ReadArrayInto(_l4);
        var frames = new TimelapseFrame[r.ReadInt32()];
        for (int i = 0; i < frames.Length; i++)
            frames[i] = new TimelapseFrame(r.ReadDate(), r.ReadArray<int>(), r.ReadArray<byte>(), r.ReadArray<byte>(), r.ReadArray<byte>(),
                r.ReadArray<byte>(), r.ReadArray<byte>());
        _frames = frames;
    }
}
