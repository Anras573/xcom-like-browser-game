using System.Text.Json;
using Firewall.Rules.Data;
using Firewall.Rules.Rng;

namespace Firewall.Rules.Tests;

public class Pcg32RandomTests
{
    private static int[] Draw(IRandom rng, int count) =>
        Enumerable.Range(0, count).Select(_ => rng.Next(0, 1_000_000)).ToArray();

    [Fact]
    public void SameSeedGivesSameSequence()
    {
        Assert.Equal(Draw(new Pcg32Random(42), 100), Draw(new Pcg32Random(42), 100));
        Assert.NotEqual(Draw(new Pcg32Random(42), 100), Draw(new Pcg32Random(43), 100));
    }

    [Fact]
    public void MatchesReferencePcg32Output()
    {
        // pcg32 demo: srandom(42, 54) -> 0xa15c02b7, 0x7b47f409, 0xba1d3330.
        var rng = new Pcg32Random(42, 54);
        Assert.Equal(0xa15c02b7u, rng.NextUInt());
        Assert.Equal(0x7b47f409u, rng.NextUInt());
        Assert.Equal(0xba1d3330u, rng.NextUInt());
    }

    [Fact]
    public void ForkIsDeterministicAndIndependentOfDrawCount()
    {
        var a = new Pcg32Random(7);
        var b = new Pcg32Random(7);
        Draw(b, 50); // advancing the parent must not change its forks

        Assert.Equal(Draw(a.Fork("mission-17-map"), 20), Draw(b.Fork("mission-17-map"), 20));
    }

    [Fact]
    public void ForkedStreamsDifferFromEachOtherAndTheParent()
    {
        var parent = new Pcg32Random(7);
        var map = Draw(parent.Fork("map"), 20);
        var loot = Draw(parent.Fork("loot"), 20);
        Assert.NotEqual(map, loot);
        Assert.NotEqual(map, Draw(new Pcg32Random(7), 20));
        Assert.NotEqual(map, Draw(new Pcg32Random(8).Fork("map"), 20));
    }

    [Fact]
    public void ForkDoesNotConsumeParentNumbers()
    {
        var a = new Pcg32Random(9);
        var b = new Pcg32Random(9);
        a.Fork("x");
        Assert.Equal(Draw(a, 10), Draw(b, 10));
    }

    [Fact]
    public void RestoredStateContinuesTheSequence()
    {
        var rng = new Pcg32Random(123);
        Draw(rng, 37);
        var saved = rng.GetState();

        var expected = Draw(rng, 50);
        var restored = Pcg32Random.FromState(saved);

        Assert.Equal(expected, Draw(restored, 50));
    }

    [Fact]
    public void StateSurvivesJsonRoundTrip()
    {
        var rng = new Pcg32Random(ulong.MaxValue - 5);
        Draw(rng, 10);
        var json = JsonSerializer.Serialize(rng.GetState(), RulesJsonContext.Default.Pcg32State);
        var state = JsonSerializer.Deserialize(json, RulesJsonContext.Default.Pcg32State);

        Assert.Equal(Draw(rng.Fork("f"), 5), Draw(Pcg32Random.FromState(state).Fork("f"), 5));
        Assert.Equal(Draw(rng, 20), Draw(Pcg32Random.FromState(state), 20));
    }

    [Fact]
    public void NextStaysInRange()
    {
        var rng = new Pcg32Random(1);
        for (var i = 0; i < 10_000; i++)
        {
            var v = rng.Next(-3, 4);
            Assert.InRange(v, -3, 3);
            var d = rng.NextDouble();
            Assert.True(d >= 0 && d < 1);
        }
        Assert.Equal(5, rng.Next(5, 6));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.Next(3, 3));
    }

    [Fact]
    public void RollIsWithinOnePercentOverOneHundredThousandRolls()
    {
        IRandom rng = new Pcg32Random(2024);
        const int n = 100_000;
        var hits = Enumerable.Range(0, n).Count(_ => rng.Roll(30));

        Assert.InRange(hits / (double)n, 0.29, 0.31);
        Assert.False(rng.Roll(0));
        Assert.True(rng.Roll(100));
    }
}
