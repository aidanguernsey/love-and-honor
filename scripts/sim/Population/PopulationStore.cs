namespace LoveAndHonor.Sim.Population;

public enum AgentKind : byte { Student = 0, Faculty = 1 }

public enum Activity : byte
{
    Sleep, Eat, Class, Study, Social, Exercise, Leisure, Teach, Work,
}

public enum FacultyRank : byte { None, Lecturer, Assistant, Associate, Full, Distinguished }

public enum HousingType : byte { OnCampus, OffCampus }

/// <summary>
/// Every student and faculty member as structure-of-arrays (§30.2). Agents are indices, never objects or
/// Godot nodes. Students occupy [0, StudentCount), faculty [StudentCount, Count).
/// Per-agent blocks (needs, class sections) are stored flat: needs of agent a are Needs[a*NeedCount .. +NeedCount).
/// </summary>
public sealed class PopulationStore
{
    public const int NeedCount = 10;
    public const int MaxSections = 8;
    public const short NoBuilding = -1;

    public int Count { get; }
    public int StudentCount { get; }
    public int FacultyCount => Count - StudentCount;

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

    public PopulationStore(int students, int faculty)
    {
        StudentCount = students;
        Count = students + faculty;
        int n = Count;
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

    /// <summary>Approximate managed memory used by the arrays, for reporting.</summary>
    public long ApproximateBytes()
    {
        long perAgent = 4 + 1 + 1 + 1 + 2 + 1 + 1 + 1 + 1 + 2 * 6 + 5 + 1
                        + MaxSections * (2 + 1 + 1) + NeedCount * 4 + 4 + 2 + 1 + 2 + 2 + 4 + 4;
        return perAgent * Count;
    }
}
