using LoveAndHonor.Sim.Core;

namespace LoveAndHonor.Sim.Tests;

public class RngStreamsTests
{
    [Fact]
    public void SameSystem_SameMasterSeed_IsReproducible()
    {
        var a = new RngStreams(2026).For("population");
        var b = new RngStreams(2026).For("population");
        Assert.Equal(a.NextUInt64(), b.NextUInt64());
    }

    [Fact]
    public void DifferentSystems_GetIndependentStreams()
    {
        var streams = new RngStreams(2026);
        Assert.NotEqual(streams.For("population").NextUInt64(), streams.For("admissions").NextUInt64());
        Assert.NotEqual(streams.For("population", 0).NextUInt64(), streams.For("population", 1).NextUInt64());
    }

    [Fact]
    public void StreamSeed_DoesNotDependOnCreationOrder()
    {
        var s1 = new RngStreams(7);
        _ = s1.For("weather");
        var afterOther = s1.For("population").NextUInt64();
        var fresh = new RngStreams(7).For("population").NextUInt64();
        Assert.Equal(fresh, afterOther);
    }

    [Fact]
    public void StableHash_IsFixed()
    {
        // FNV-1a 64 test vectors.
        Assert.Equal(14695981039346656037UL, RngStreams.StableHash(""));
        Assert.Equal(0xAF63DC4C8601EC8CUL, RngStreams.StableHash("a"));
    }
}
