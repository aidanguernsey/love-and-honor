using LoveAndHonor.Sim.Core;

namespace LoveAndHonor.Sim.Tests;

public class DeterministicRngTests
{
    [Fact]
    public void SameSeed_ProducesSameSequence()
    {
        var a = new DeterministicRng(12345);
        var b = new DeterministicRng(12345);
        for (int i = 0; i < 1000; i++)
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
    }

    [Fact]
    public void DifferentSeeds_Diverge()
    {
        var a = new DeterministicRng(1);
        var b = new DeterministicRng(2);
        Assert.NotEqual(a.NextUInt64(), b.NextUInt64());
    }

    [Fact]
    public void Sequence_IsPinned()
    {
        // Golden values: if this fails, the RNG algorithm changed and old saves/bug reports won't replay.
        var rng = new DeterministicRng(42);
        var first = new[] { rng.NextUInt64(), rng.NextUInt64(), rng.NextUInt64() };
        Assert.Equal(GoldenSeed42, first);
    }

    // Cross-checked against an independent Python xoshiro256** + SplitMix64 implementation.
    private static readonly ulong[] GoldenSeed42 = [0x15780B2E0C2EC716UL, 0x6104D9866D113A7EUL, 0xAE17533239E499A1UL];

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(400)]
    [InlineData(int.MaxValue)]
    public void NextInt_StaysInRange(int max)
    {
        var rng = new DeterministicRng(7);
        for (int i = 0; i < 10_000; i++)
        {
            int v = rng.NextInt(max);
            Assert.InRange(v, 0, max - 1);
        }
    }

    [Fact]
    public void NextInt_IsRoughlyUniform()
    {
        var rng = new DeterministicRng(99);
        var counts = new int[10];
        const int n = 100_000;
        for (int i = 0; i < n; i++) counts[rng.NextInt(10)]++;
        foreach (var c in counts) Assert.InRange(c, n / 10 * 0.95, n / 10 * 1.05);
    }

    [Fact]
    public void NextDoubleAndFloat_InUnitInterval()
    {
        var rng = new DeterministicRng(5);
        for (int i = 0; i < 10_000; i++)
        {
            Assert.InRange(rng.NextDouble(), 0.0, 0.9999999999999999);
            Assert.InRange(rng.NextFloat(), 0f, 0.99999994f);
        }
    }
}
