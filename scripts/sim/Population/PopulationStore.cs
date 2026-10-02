namespace LoveAndHonor.Sim.Population;

public enum AgentKind : byte { Student = 0, Faculty = 1 }

public enum Activity : byte
{
    Sleep, Eat, Class, Study, Social, Exercise, Leisure, Teach, Work,
    /// <summary>Away from Oxford (students over the summer and winter breaks, 1h).</summary>
    Away,
}

public enum FacultyRank : byte { None, Lecturer, Assistant, Associate, Full, Distinguished }

public enum HousingType : byte { OnCampus, OffCampus }

/// <summary>
/// Every student and faculty member as structure-of-arrays (§30.2). Agents are indices, never objects or
/// Godot nodes. Live agents occupy [0, Count) of arrays sized for <see cref="Capacity"/>; enrollment (1h) adds people
/// at the end and removes them by moving the last agent into the gap, at fixed times on the sim thread, so the order
/// stays deterministic. The initial population has students first, then faculty; after that kinds mix (use Kind).
/// Per-agent blocks (needs, class sections) are stored flat: needs of agent a are Needs[a*NeedCount .. +NeedCount).
/// </summary>
public sealed class PopulationStore
{
    public const int NeedCount = 10;
    public const int MaxSections = 8;
    public const short NoBuilding = -1;

    public int Capacity { get; }
    public int Count { get; private set; }
    public int StudentCount { get; private set; }
    public int FacultyCount => Count - StudentCount;
    private int _nextId;

    // Identity & profile (§10.1, §10.2)
    public readonly int[] Id;
    public readonly AgentKind[] Kind;
    public readonly byte[] Age;
    /// <summary>Students: 1-4. Faculty: 0.</summary>
    public readonly byte[] Year;
    /// <summary>Students: major's department index. Faculty: their department index.</summary>
    public readonly ushort[] Department;
    public readonly FacultyRank[] Rank;
    public readonly byte[] TeachingScore;
    public readonly byte[] ResearchScore;

    // Places the schedule refers to (building indices)
    public readonly HousingType[] Housing;
    public readonly short[] Home;
    public readonly short[] Dining;
    public readonly short[] StudySpot;
    public readonly short[] SocialSpot;
    public readonly short[] ExerciseSpot;
    /// <summary>Faculty office (department building). Students: NoBuilding.</summary>
    public readonly short[] Office;

    // Schedule (§10.1 daily schedule)
    public readonly byte[] WakeHour;
    public readonly byte[] BedHour;
    public readonly byte[] WeekendWakeHour;
    public readonly byte[] ArriveHour;
    public readonly byte[] LeaveHour;
    public readonly byte[] SectionCount;
    /// <summary>Per section: building index.</summary>
    public readonly short[] SectionBuilding;
    /// <summary>Per section: weekday bitmask, bit 0 = Monday.</summary>
    public readonly byte[] SectionDays;
    public readonly byte[] SectionHour;

    // Live state
    public readonly float[] Needs;
    public readonly float[] Happiness;
    public readonly short[] CurrentBuilding;
    public readonly Activity[] CurrentActivity;

    // Logical movement: "walking from A to B" between two absolute game minutes (§30.2 two-level movement).
    public readonly short[] WalkFrom;
    public readonly short[] WalkTo;
    public readonly int[] WalkDepartMinute;
    public readonly int[] WalkArriveMinute;

    /// <param name="capacity">Room for this many agents (at least students + faculty); enrollment can grow up to it.</param>
    public PopulationStore(int students, int faculty, int? capacity = null)
    {
        StudentCount = students;
        Count = students + faculty;
        Capacity = Math.Max(Count, capacity ?? Count);
        _nextId = Count;
        int n = Capacity;
        Id = new int[n];
        Kind = new AgentKind[n];
        Age = new byte[n];
        Year = new byte[n];
        Department = new ushort[n];
        Rank = new FacultyRank[n];
        TeachingScore = new byte[n];
        ResearchScore = new byte[n];
        Housing = new HousingType[n];
        Home = new short[n];
        Dining = new short[n];
        StudySpot = new short[n];
        SocialSpot = new short[n];
        ExerciseSpot = new short[n];
        Office = new short[n];
        WakeHour = new byte[n];
        BedHour = new byte[n];
        WeekendWakeHour = new byte[n];
        ArriveHour = new byte[n];
        LeaveHour = new byte[n];
        SectionCount = new byte[n];
        SectionBuilding = new short[n * MaxSections];
        SectionDays = new byte[n * MaxSections];
        SectionHour = new byte[n * MaxSections];
        Needs = new float[n * NeedCount];
        Happiness = new float[n];
        CurrentBuilding = new short[n];
        CurrentActivity = new Activity[n];
        WalkFrom = new short[n];
        WalkTo = new short[n];
        WalkDepartMinute = new int[n];
        WalkArriveMinute = new int[n];
        Array.Fill(Office, NoBuilding);
        Array.Fill(WalkFrom, NoBuilding);
        Array.Fill(WalkTo, NoBuilding);
        Array.Fill(WalkArriveMinute, int.MinValue);
    }

    public bool IsWalking(int agent, int gameMinute) => WalkArriveMinute[agent] > gameMinute;

    // ---------------- saves (§31) ----------------

    public void WriteState(BinaryWriter w)
    {
        w.Write(Count); w.Write(StudentCount); w.Write(_nextId);
        int n = Count;
        Engine.SaveIO.WriteArray(w, Id, n); Engine.SaveIO.WriteArray(w, Kind, n); Engine.SaveIO.WriteArray(w, Age, n);
        Engine.SaveIO.WriteArray(w, Year, n); Engine.SaveIO.WriteArray(w, Department, n); Engine.SaveIO.WriteArray(w, Rank, n);
        Engine.SaveIO.WriteArray(w, TeachingScore, n); Engine.SaveIO.WriteArray(w, ResearchScore, n); Engine.SaveIO.WriteArray(w, Housing, n);
        Engine.SaveIO.WriteArray(w, Home, n); Engine.SaveIO.WriteArray(w, Dining, n); Engine.SaveIO.WriteArray(w, StudySpot, n);
        Engine.SaveIO.WriteArray(w, SocialSpot, n); Engine.SaveIO.WriteArray(w, ExerciseSpot, n); Engine.SaveIO.WriteArray(w, Office, n);
        Engine.SaveIO.WriteArray(w, WakeHour, n); Engine.SaveIO.WriteArray(w, BedHour, n); Engine.SaveIO.WriteArray(w, WeekendWakeHour, n);
        Engine.SaveIO.WriteArray(w, ArriveHour, n); Engine.SaveIO.WriteArray(w, LeaveHour, n); Engine.SaveIO.WriteArray(w, SectionCount, n);
        Engine.SaveIO.WriteArray(w, SectionBuilding, n * MaxSections); Engine.SaveIO.WriteArray(w, SectionDays, n * MaxSections);
        Engine.SaveIO.WriteArray(w, SectionHour, n * MaxSections); Engine.SaveIO.WriteArray(w, Needs, n * NeedCount);
        Engine.SaveIO.WriteArray(w, Happiness, n); Engine.SaveIO.WriteArray(w, CurrentBuilding, n); Engine.SaveIO.WriteArray(w, CurrentActivity, n);
        Engine.SaveIO.WriteArray(w, WalkFrom, n); Engine.SaveIO.WriteArray(w, WalkTo, n);
        Engine.SaveIO.WriteArray(w, WalkDepartMinute, n); Engine.SaveIO.WriteArray(w, WalkArriveMinute, n);
    }

    public void ReadState(BinaryReader r)
    {
        int count = r.ReadInt32();
        if (count > Capacity) throw new InvalidDataException($"The save has {count:N0} people; this world has room for {Capacity:N0}.");
        for (int a = count; a < Capacity; a++) Clear(a);
        Count = count; StudentCount = r.ReadInt32(); _nextId = r.ReadInt32();
        Engine.SaveIO.ReadArrayInto(r, Id); Engine.SaveIO.ReadArrayInto(r, Kind); Engine.SaveIO.ReadArrayInto(r, Age);
        Engine.SaveIO.ReadArrayInto(r, Year); Engine.SaveIO.ReadArrayInto(r, Department); Engine.SaveIO.ReadArrayInto(r, Rank);
        Engine.SaveIO.ReadArrayInto(r, TeachingScore); Engine.SaveIO.ReadArrayInto(r, ResearchScore); Engine.SaveIO.ReadArrayInto(r, Housing);
        Engine.SaveIO.ReadArrayInto(r, Home); Engine.SaveIO.ReadArrayInto(r, Dining); Engine.SaveIO.ReadArrayInto(r, StudySpot);
        Engine.SaveIO.ReadArrayInto(r, SocialSpot); Engine.SaveIO.ReadArrayInto(r, ExerciseSpot); Engine.SaveIO.ReadArrayInto(r, Office);
        Engine.SaveIO.ReadArrayInto(r, WakeHour); Engine.SaveIO.ReadArrayInto(r, BedHour); Engine.SaveIO.ReadArrayInto(r, WeekendWakeHour);
        Engine.SaveIO.ReadArrayInto(r, ArriveHour); Engine.SaveIO.ReadArrayInto(r, LeaveHour); Engine.SaveIO.ReadArrayInto(r, SectionCount);
        Engine.SaveIO.ReadArrayInto(r, SectionBuilding); Engine.SaveIO.ReadArrayInto(r, SectionDays);
        Engine.SaveIO.ReadArrayInto(r, SectionHour); Engine.SaveIO.ReadArrayInto(r, Needs);
        Engine.SaveIO.ReadArrayInto(r, Happiness); Engine.SaveIO.ReadArrayInto(r, CurrentBuilding); Engine.SaveIO.ReadArrayInto(r, CurrentActivity);
        Engine.SaveIO.ReadArrayInto(r, WalkFrom); Engine.SaveIO.ReadArrayInto(r, WalkTo);
        Engine.SaveIO.ReadArrayInto(r, WalkDepartMinute); Engine.SaveIO.ReadArrayInto(r, WalkArriveMinute);
    }

    /// <summary>Adds an agent at index Count with cleared fields (the caller fills in the rest). Returns its index.</summary>
    public int Add(AgentKind kind)
    {
        if (Count >= Capacity) throw new InvalidOperationException($"Population is full ({Capacity:N0}).");
        int a = Count++;
        Clear(a);
        Id[a] = _nextId++;
        Kind[a] = kind;
        if (kind == AgentKind.Student) StudentCount++;
        return a;
    }

    /// <summary>Removes agent <paramref name="a"/>: the last agent moves into its slot (so indices above may change).</summary>
    public void RemoveAt(int a)
    {
        if ((uint)a >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(a));
        if (Kind[a] == AgentKind.Student) StudentCount--;
        int last = --Count;
        if (a != last) Move(last, a);
        Clear(last);
    }

    private void Clear(int a)
    {
        Id[a] = 0; Kind[a] = AgentKind.Student; Age[a] = 0; Year[a] = 0; Department[a] = 0; Rank[a] = FacultyRank.None;
        TeachingScore[a] = 0; ResearchScore[a] = 0; Housing[a] = HousingType.OffCampus;
        Home[a] = Dining[a] = StudySpot[a] = SocialSpot[a] = ExerciseSpot[a] = NoBuilding;
        Office[a] = NoBuilding;
        WakeHour[a] = BedHour[a] = WeekendWakeHour[a] = ArriveHour[a] = LeaveHour[a] = 0;
        SectionCount[a] = 0;
        Array.Clear(SectionBuilding, a * MaxSections, MaxSections);
        Array.Clear(SectionDays, a * MaxSections, MaxSections);
        Array.Clear(SectionHour, a * MaxSections, MaxSections);
        Array.Clear(Needs, a * NeedCount, NeedCount);
        Happiness[a] = 0; CurrentBuilding[a] = NoBuilding; CurrentActivity[a] = Activity.Sleep;
        WalkFrom[a] = WalkTo[a] = NoBuilding; WalkDepartMinute[a] = 0; WalkArriveMinute[a] = int.MinValue;
    }

    private void Move(int from, int to)
    {
        Id[to] = Id[from]; Kind[to] = Kind[from]; Age[to] = Age[from]; Year[to] = Year[from]; Department[to] = Department[from];
        Rank[to] = Rank[from]; TeachingScore[to] = TeachingScore[from]; ResearchScore[to] = ResearchScore[from];
        Housing[to] = Housing[from]; Home[to] = Home[from]; Dining[to] = Dining[from]; StudySpot[to] = StudySpot[from];
        SocialSpot[to] = SocialSpot[from]; ExerciseSpot[to] = ExerciseSpot[from]; Office[to] = Office[from];
        WakeHour[to] = WakeHour[from]; BedHour[to] = BedHour[from]; WeekendWakeHour[to] = WeekendWakeHour[from];
        ArriveHour[to] = ArriveHour[from]; LeaveHour[to] = LeaveHour[from]; SectionCount[to] = SectionCount[from];
        Array.Copy(SectionBuilding, from * MaxSections, SectionBuilding, to * MaxSections, MaxSections);
        Array.Copy(SectionDays, from * MaxSections, SectionDays, to * MaxSections, MaxSections);
        Array.Copy(SectionHour, from * MaxSections, SectionHour, to * MaxSections, MaxSections);
        Array.Copy(Needs, from * NeedCount, Needs, to * NeedCount, NeedCount);
        Happiness[to] = Happiness[from]; CurrentBuilding[to] = CurrentBuilding[from]; CurrentActivity[to] = CurrentActivity[from];
        WalkFrom[to] = WalkFrom[from]; WalkTo[to] = WalkTo[from];
        WalkDepartMinute[to] = WalkDepartMinute[from]; WalkArriveMinute[to] = WalkArriveMinute[from];
    }

    /// <summary>Approximate managed memory used by the arrays, for reporting.</summary>
    public long ApproximateBytes()
    {
        long perAgent = 4 + 1 + 1 + 1 + 2 + 1 + 1 + 1 + 1 + 2 * 6 + 5 + 1
                        + MaxSections * (2 + 1 + 1) + NeedCount * 4 + 4 + 2 + 1 + 2 + 2 + 4 + 4;
        return perAgent * Capacity;
    }
}
