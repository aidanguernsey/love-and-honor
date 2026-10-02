using System.Runtime.InteropServices;

namespace LoveAndHonor.Sim.Engine;

/// <summary>Little helpers for the save format (§31): arrays of unmanaged values as raw bytes, strings, dates.</summary>
public static class SaveIO
{
    public static void WriteArray<T>(this BinaryWriter w, T[] array, int count) where T : unmanaged
    {
        w.Write(count);
        w.Write(MemoryMarshal.AsBytes(array.AsSpan(0, count)));
    }

    public static void WriteArray<T>(this BinaryWriter w, T[] array) where T : unmanaged => w.WriteArray(array, array.Length);

    /// <summary>Reads into an existing array (its length must allow the saved count). Returns the count.</summary>
    public static int ReadArrayInto<T>(this BinaryReader r, T[] array) where T : unmanaged
    {
        int count = r.ReadInt32();
        if (count > array.Length) throw new InvalidDataException($"Saved array of {count} doesn't fit {array.Length}.");
        Fill(r, MemoryMarshal.AsBytes(array.AsSpan(0, count)));
        return count;
    }

    public static T[] ReadArray<T>(this BinaryReader r) where T : unmanaged
    {
        var array = new T[r.ReadInt32()];
        Fill(r, MemoryMarshal.AsBytes(array.AsSpan()));
        return array;
    }

    /// <summary>Reads exactly bytes.Length bytes (compressed streams return them in pieces and can't seek).</summary>
    private static void Fill(BinaryReader r, Span<byte> bytes)
    {
        int read = 0;
        while (read < bytes.Length)
        {
            int n = r.Read(bytes[read..]);
            if (n == 0) throw new EndOfStreamException();
            read += n;
        }
    }

    public static void Write(this BinaryWriter w, DateOnly d) => w.Write(d.DayNumber);
    public static DateOnly ReadDate(this BinaryReader r) => DateOnly.FromDayNumber(r.ReadInt32());

    public static void WriteStrings(this BinaryWriter w, IEnumerable<string> items)
    {
        var list = items.ToList();
        w.Write(list.Count);
        foreach (var s in list) w.Write(s);
    }

    public static List<string> ReadStrings(this BinaryReader r)
    {
        int n = r.ReadInt32();
        var list = new List<string>(n);
        for (int i = 0; i < n; i++) list.Add(r.ReadString());
        return list;
    }

    /// <summary>Section markers catch a reader drifting out of step with the writer.</summary>
    public static void Mark(this BinaryWriter w, string section) => w.Write("§" + section);

    public static void Expect(this BinaryReader r, string section)
    {
        string got = r.ReadString();
        if (got != "§" + section) throw new InvalidDataException($"Save file corrupt: expected section '{section}', found '{got}'.");
    }
}
