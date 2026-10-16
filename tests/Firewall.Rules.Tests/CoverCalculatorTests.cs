using Firewall.Rules.Data;
using Firewall.Rules.Rng;
using Firewall.Rules.Tactical;

namespace Firewall.Rules.Tests;

public class CoverCalculatorTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private static readonly GameData Data = GameData.Load(f =>
        File.ReadAllText(Path.Combine(DataDir, f))
    );

    private static TacticalMap Map(params string[] rows) =>
        MapParser.Parse(
            new MapFile
            {
                Id = "t",
                Name = "T",
                Biome = Biome.Industrial,
                Rows = rows,
            },
            Data.Tiles
        );

    // Rows are top first. Target T stands at (1, 1) in every scenario below unless noted.

    [Theory]
    // . . .       Crate (c, half) directly east; attacker due east at (9, 1).
    // . . c  ...  dx = 8, dy = 0: east applies.
    // . . .
    [InlineData(8, 0, CoverLevel.Half, false, 20)]
    // Crate east; attacker at dx = 1, dy = 10 (about 5.7° off north): east does not apply and
    // there is no north cover, so the target is flanked.
    [InlineData(1, 10, CoverLevel.None, true, 0)]
    // Attacker directly behind (west) with only east cover: flanked.
    [InlineData(-5, 0, CoverLevel.None, true, 0)]
    // Same tile as the target: flanked, no crash.
    [InlineData(0, 0, CoverLevel.None, true, 0)]
    public void CrateEast(int dx, int dy, CoverLevel level, bool flanked, int bonus)
    {
        var map = Map("...", ".Pc", "...");
        var target = new GridPos(1, 1);
        var result = CoverCalculator.Evaluate(map, target, new GridPos(1 + dx, 1 + dy));
        Assert.Equal(new CoverResult(level, flanked, bonus), result);
    }

    [Fact]
    public void WallEastAndCrateNorthFromTheNorthEastDiagonalIsFull()
    {
        // . c .   Crate north (half), wall east (full). Attacker at a 45° NE diagonal:
        // . P #   dot with N and E is 0.707, so both apply and the maximum, Full, wins.
        // . . .
        var map = Map(".c.", ".P#", "...");
        var result = CoverCalculator.Evaluate(map, new GridPos(1, 1), new GridPos(5, 5));
        Assert.Equal(new CoverResult(CoverLevel.Full, false, 40), result);
    }

    [Fact]
    public void BothDirectionsApplyButOnlyTheCoveredOneCounts()
    {
        // Attacker NE, only the crate to the north gives cover: Half.
        var map = Map(".c.", ".P.", "...");
        var result = CoverCalculator.Evaluate(map, new GridPos(1, 1), new GridPos(4, 4));
        Assert.Equal(CoverLevel.Half, result.Level);
    }

    [Fact]
    public void ExactlySixtyDegreesApplies()
    {
        var profile = new CoverProfile(
            CoverLevel.None,
            CoverLevel.Half,
            CoverLevel.None,
            CoverLevel.None
        );
        var a = Math.PI / 3; // 60°, dot = 0.5
        Assert.False(CoverCalculator.Evaluate(profile, Math.Cos(a), Math.Sin(a)).Flanked);
    }

    [Fact]
    public void SixtyOneDegreesDoesNotApply()
    {
        var profile = new CoverProfile(
            CoverLevel.None,
            CoverLevel.Half,
            CoverLevel.None,
            CoverLevel.None
        );
        var a = 61 * Math.PI / 180;
        Assert.True(CoverCalculator.Evaluate(profile, Math.Cos(a), Math.Sin(a)).Flanked);
    }

    [Fact]
    public void ProfileReadsAdjacentTiles()
    {
        // . c .   north crate (half), east wall (full), south nothing, west big crate (full).
        // C P #
        // . . .
        var map = Map(".c.", "CP#", "...");
        Assert.Equal(
            new CoverProfile(CoverLevel.Half, CoverLevel.Full, CoverLevel.None, CoverLevel.Full),
            CoverCalculator.GetProfile(map, new GridPos(1, 1))
        );
    }

    [Fact]
    public void OutOfBoundsNeighboursGiveNoCoverAndDoNotThrow()
    {
        var map = Map("..", "..");
        foreach (
            var corner in new[]
            {
                new GridPos(0, 0),
                new GridPos(1, 0),
                new GridPos(0, 1),
                new GridPos(1, 1),
            }
        )
        {
            Assert.False(CoverCalculator.GetProfile(map, corner).HasAny);
            Assert.True(CoverCalculator.Evaluate(map, corner, new GridPos(0, 0)).Flanked);
        }
    }

    private static TacticalState State(params string[] rows) => new(Map(rows), new Pcg32Random(1));

    private static TacticalUnit Add(
        TacticalState state,
        Faction faction,
        int x,
        int y,
        bool alive = true,
        bool active = true
    )
    {
        var unit = new TacticalUnit(state.NextUnitId(), faction, "x", "U")
        {
            Position = new GridPos(x, y),
            IsAlive = alive,
            IsActive = active,
        };
        state.AddUnit(unit);
        return unit;
    }

    [Fact]
    public void IsFlankedByAnyChecksEveryHostile()
    {
        // Crate at (1, 1) (row 3 from the top of 5) is east of the tile at (0, 1).
        var state = State(".....", ".....", ".....", ".c...", ".....");
        var tile = new GridPos(0, 1);
        Assert.False(CoverCalculator.IsFlankedByAny(state, tile, Faction.Firewall));
        Add(state, Faction.Meridian, 4, 1); // due east: covered
        Assert.False(CoverCalculator.IsFlankedByAny(state, tile, Faction.Firewall));
        Add(state, Faction.Meridian, 0, 4); // due north: flanks
        Assert.True(CoverCalculator.IsFlankedByAny(state, tile, Faction.Firewall));
    }

    [Fact]
    public void IsFlankedByAnyIgnoresDeadInactiveFriendlyNeutralAndHiddenUnits()
    {
        var state = State(".....", ".....", ".....", ".c...", ".....");
        var tile = new GridPos(0, 1);
        Add(state, Faction.Meridian, 0, 4, alive: false);
        Add(state, Faction.Meridian, 0, 3, active: false);
        Add(state, Faction.Firewall, 0, 2);
        Add(state, Faction.Neutral, 0, 4);
        var hidden = Add(state, Faction.Meridian, 0, 4);
        Assert.False(
            CoverCalculator.IsFlankedByAny(state, tile, Faction.Firewall, u => u != hidden)
        );
        Assert.True(CoverCalculator.IsFlankedByAny(state, tile, Faction.Firewall));
    }
}
