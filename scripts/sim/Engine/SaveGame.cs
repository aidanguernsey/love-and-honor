using System.IO.Compression;
using System.Text;
using System.Text.Json;
using LoveAndHonor.Sim.World;

namespace LoveAndHonor.Sim.Engine;

/// <summary>
/// Saves (§31), Phase 1 1k: "LHSV", a little-endian int32 header length, a JSON header (metadata shown in lists; no
/// personal data), then the sim state, Deflate-compressed: the clock, the tile layers, buildings added in play, land and
/// money, construction, people, enrollment, budget, campaign and the time-lapse, each in a marked section. A save
/// is restored over a freshly created world of the same scenario (the map, real buildings and content come from data),
/// and flow fields are rebuilt from the restored map (bit-identical to the incrementally updated ones).
///
/// Exact saves: when no flow-field update is in flight, a loaded game continues exactly as the original would have
/// (tested). The bridge asks for saves at tick boundaries and waits for that; a save made while paused with an
/// update pending is marked inexact (the update is applied at once on load).
/// </summary>
public static class SaveGame
{
    /// <summary>Bump when the payload changes; add a migration in <see cref="Restore"/> for older numbers.</summary>
    public const int Format = 3;
    private static readonly byte[] Magic = "LHSV"u8.ToArray();

    public sealed class Header
    {
        public int Format { get; init; }
        public string Name { get; init; } = "";
        public string Scenario { get; init; } = "";
        public string ScenarioName { get; init; } = "";
        public string Date { get; init; } = "";
        public int Students { get; init; }
        public long CashCents { get; init; }
        public string SavedAtUtc { get; init; } = "";
        public bool Exact { get; init; }
        public string GameVersion { get; init; } = "";
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true };

    /// <summary>True when a save now would let a loaded game continue exactly (no flow-field update pending).</summary>
    public static bool CanSaveExactly(SimWorld w) => w.Simulation.RebuildApplyTick < 0;

    /// <summary>Writes the world (sim thread, between ticks).</summary>
    public static Header Write(Stream output, SimWorld w, string name, string savedAtUtc, string gameVersion)
    {
        var sim = w.Simulation;
        sim.SyncFootTraffic();
        var header = new Header
        {
            Format = Format, Name = name, Scenario = w.Scenario?.Id ?? "", ScenarioName = w.Scenario?.Name ?? "",
            Date = sim.Time.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            Students = w.Population.StudentCount, CashCents = w.Land?.Treasury.Cents ?? 0, SavedAtUtc = savedAtUtc,
            Exact = CanSaveExactly(w), GameVersion = gameVersion,
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(header, Json);
        output.Write(Magic);
        output.Write(BitConverter.GetBytes(json.Length));
        output.Write(json);
        using var zip = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true);
        using var bw = new BinaryWriter(zip, Encoding.UTF8, leaveOpen: true);
        bw.Mark("time"); bw.Write(sim.Time.Tick);
        bw.Mark("grid"); w.Campus.Grid.WriteState(bw);
        bw.Mark("campus");
        bw.Write(w.InitialBuildingCount);
        bw.Write(w.Campus.Buildings.Count);
        foreach (var b in w.Campus.Buildings.Skip(w.InitialBuildingCount))
        {
            bw.Write(b.Index); bw.Write(b.DefId); bw.Write((byte)b.Kind); bw.Write(b.X); bw.Write(b.Y); bw.Write(b.W); bw.Write(b.H);
            bw.Write(b.EntranceTile); bw.Write(b.Weight);
        }
        bw.Mark("population"); w.Population.WriteState(bw);
        bw.Mark("land"); bw.Write(w.Land is not null); w.Land?.WriteState(bw);
        bw.Mark("placement"); bw.Write(w.Placement is not null); w.Placement?.WriteState(bw);
        bw.Mark("enrollment"); bw.Write(w.Enrollment is not null); w.Enrollment?.WriteState(bw);
        bw.Mark("budget"); bw.Write(w.Budget is not null); w.Budget?.WriteState(bw);
        bw.Mark("campaign"); bw.Write(w.Campaign is not null); w.Campaign?.WriteState(bw);
        bw.Mark("timelapse"); bw.Write(sim.Timelapse is not null); sim.Timelapse?.WriteState(bw);
        bw.Mark("reputation"); bw.Write(w.Reputation is not null); w.Reputation?.WriteState(bw);
        bw.Mark("end");
        return header;
    }

    /// <summary>Reads just the header (for save lists and to know which scenario to create).</summary>
    public static Header ReadHeader(Stream input)
    {
        var magic = new byte[4];
        input.ReadExactly(magic);
        if (!magic.AsSpan().SequenceEqual(Magic)) throw new InvalidDataException("Not a Love & Honor save.");
        var len = new byte[4];
        input.ReadExactly(len);
        var json = new byte[BitConverter.ToInt32(len)];
        input.ReadExactly(json);
        var header = JsonSerializer.Deserialize<Header>(json, Json) ?? throw new InvalidDataException("Save header unreadable.");
        if (header.Format > Format) throw new InvalidDataException($"This save is from a newer version of the game (format {header.Format}).");
        return header;
    }

    /// <summary>Restores a save over <paramref name="w"/>, a freshly created world of the header's scenario (sim not
    /// running yet). Returns the header.</summary>
    public static Header Restore(Stream input, SimWorld w)
    {
        var header = ReadHeader(input);
        if (header.Scenario != (w.Scenario?.Id ?? "")) throw new InvalidDataException($"The save is for scenario '{header.Scenario}'.");
        // Format migrations: 1 → 2 added the Slant Walk candidate (land section); 2 → 3 added Reputation (older saves
        // start it at today's Quality).
        using var zip = new DeflateStream(input, CompressionMode.Decompress, leaveOpen: true);
        using var br = new BinaryReader(zip, Encoding.UTF8, leaveOpen: true);
        var sim = w.Simulation;
        br.Expect("time"); sim.Time.Restore(br.ReadInt32());
        br.Expect("grid"); w.Campus.Grid.ReadState(br);
        br.Expect("campus");
        int initial = br.ReadInt32(), total = br.ReadInt32();
        if (initial != w.InitialBuildingCount || w.Campus.Buildings.Count != initial)
            throw new InvalidDataException("The save's map doesn't match this version of the scenario.");
        for (int i = initial; i < total; i++)
            w.Campus.Add(new CampusBuilding
            {
                Index = br.ReadInt16(), DefId = br.ReadString(), Kind = (BuildingKind)br.ReadByte(), X = br.ReadInt32(), Y = br.ReadInt32(),
                W = br.ReadInt32(), H = br.ReadInt32(), EntranceTile = br.ReadInt32(), Weight = br.ReadSingle(),
            });
        br.Expect("population"); w.Population.ReadState(br);
        br.Expect("land"); if (br.ReadBoolean()) w.Land!.ReadState(br, header.Format);
        br.Expect("placement"); if (br.ReadBoolean()) w.Placement!.ReadState(br);
        br.Expect("enrollment"); if (br.ReadBoolean()) w.Enrollment!.ReadState(br);
        br.Expect("budget"); if (br.ReadBoolean()) w.Budget!.ReadState(br);
        br.Expect("campaign"); if (br.ReadBoolean()) w.Campaign!.ReadState(br);
        br.Expect("timelapse"); if (br.ReadBoolean()) sim.Timelapse!.ReadState(br);
        if (header.Format >= 3) { br.Expect("reputation"); if (br.ReadBoolean()) w.Reputation!.ReadState(br, sim.Time.Date); }
        else w.Reputation?.ResetTo(sim.Time.Date);
        br.Expect("end");
        sim.AfterRestore();
        return header;
    }
}
