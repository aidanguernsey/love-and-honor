using System.Runtime.CompilerServices;
using LoveAndHonor.Sim.Data;
using LoveAndHonor.Sim.Population;

namespace LoveAndHonor.Sim.Engine;

/// <summary>
/// Hourly needs update and happiness (§10.1, §32): need += baseline + effect(activity), clamped to 0-100;
/// happiness = weighted mean of needs. Tables are flattened from balance.json at startup.
/// </summary>
public sealed class NeedsModel
{
    private const int N = PopulationStore.NeedCount;
    private static readonly int ActivityCount = Enum.GetValues<Activity>().Length;

    private readonly float[] _baseline = new float[N];
    private readonly float[] _effects;       // [activity * N + need]
    private readonly float[] _weights = new float[N];
    private readonly float _invWeightSum;

    public int CommuteIndex { get; }

    public NeedsModel(BalanceConfig.NeedsSection cfg)
    {
        if (cfg.Ids.Length != N) throw new InvalidDataException($"balance.json needs.ids must list {N} needs.");
        _effects = new float[ActivityCount * N];
        for (int i = 0; i < N; i++)
        {
            string id = cfg.Ids[i];
            _baseline[i] = cfg.BaselinePerHour.GetValueOrDefault(id);
            _weights[i] = cfg.HappinessWeights.GetValueOrDefault(id);
            for (int act = 0; act < ActivityCount; act++)
            {
                string actId = ((Activity)act).ToString().ToLowerInvariant();
                if (cfg.ActivityEffects.TryGetValue(actId, out var fx))
                    _effects[act * N + i] = fx.GetValueOrDefault(id);
            }
        }
        _invWeightSum = 1f / _weights.Sum();
        CommuteIndex = Array.IndexOf(cfg.Ids, "commute");
    }

    /// <summary>Applies one hour of needs change to agent <paramref name="a"/> and returns its new happiness.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Update(float[] needs, int a, Activity activity)
    {
        int nb = a * N;
        int eb = (int)activity * N;
        float h = 0;
        for (int i = 0; i < N; i++)
        {
            float v = needs[nb + i] + _baseline[i] + _effects[eb + i];
            v = v < 0f ? 0f : v > 100f ? 100f : v;
            needs[nb + i] = v;
            h += v * _weights[i];
        }
        return h * _invWeightSum;
    }

    public float Happiness(float[] needs, int a)
    {
        int nb = a * N;
        float h = 0;
        for (int i = 0; i < N; i++) h += needs[nb + i] * _weights[i];
        return h * _invWeightSum;
    }
}
