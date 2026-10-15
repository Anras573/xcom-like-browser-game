using System.Diagnostics;
using Firewall.Rules.Data;
using Firewall.Rules.Rng;
using Firewall.Rules.Tactical;

namespace Firewall.Rules.Tests;

public class PathfinderTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private static readonly GameData Data = GameData.Load(f =>
        File.ReadAllText(Path.Combine(DataDir, f))
    );

    private static TacticalState Open(int width, int height) =>
        Build(Enumerable.Repeat(new string('.', width), height).ToArray());

    private static TacticalState Build(params string[] rows) =>
        new(
            MapParser.Parse(
                new MapFile
                {
                    Id = "t",
                    Name = "T",
                    Biome = Biome.Industrial,
                    Rows = rows,
                },
                Data.Tiles
            ),
            new Pcg32Random(1)
        );

    private static TacticalUnit Add(
        TacticalState state,
        int x,
        int y,
        int mobility = 6,
        int ap = 2,
        Faction faction = Faction.Firewall
    )
    {
        var unit = new TacticalUnit(state.NextUnitId(), faction, "x", "U")
        {
            Position = new GridPos(x, y),
            Mobility = mobility,
            ActionPoints = ap,
        };
        state.AddUnit(unit);
        return unit;
    }

    [Fact]
    public void DiagonalsAreAllowedInOpenGround()
    {
        var state = Open(5, 5);
        var unit = Add(state, 0, 0);
        var reach = Pathfinder.Reachable(state, unit, 100);

        Assert.Equal(3, reach.CostTo(new GridPos(1, 1)));
        Assert.Equal(
            new[] { new GridPos(0, 0), new GridPos(1, 1) },
            reach.PathTo(new GridPos(1, 1))
        );
    }

    [Theory]
    [InlineData("#")] // wall
    [InlineData("c")] // cover object
    public void CornersOfBlockersCannotBeCut(string blocker)
    {
        // Rows are top first: y=1 is "..", y=0 is "<blocker>.", so the blocker sits at (0,0).
        var state = Build("..", $"{blocker}.");
        var unit = Add(state, 1, 0);
        var reach = Pathfinder.Reachable(state, unit, 100);

        // (1,0) -> (0,1) would cut the corner of the blocker at (0,0).
        Assert.Equal(4, reach.CostTo(new GridPos(0, 1))); // via (1,1), 2 + 2
        Assert.Equal(
            new[] { new GridPos(1, 0), new GridPos(1, 1), new GridPos(0, 1) },
            reach.PathTo(new GridPos(0, 1))
        );
    }

    [Fact]
    public void UnitsAlsoBlockCornerCutting()
    {
        var state = Open(3, 3);
        var mover = Add(state, 0, 0);
        Add(state, 1, 0, faction: Faction.Meridian);
        var reach = Pathfinder.Reachable(state, mover, 100);

        // (0,0) -> (1,1) is diagonal past the unit at (1,0): not allowed.
        Assert.Equal(4, reach.CostTo(new GridPos(1, 1)));
    }

    [Fact]
    public void ThreeDiagonalsCostFourAndAHalfTiles()
    {
        var state = Open(5, 5);
        var unit = Add(state, 0, 0);
        var reach = Pathfinder.Reachable(state, unit, 100);

        Assert.Equal(9, reach.CostTo(new GridPos(3, 3)));
    }

    [Fact]
    public void BlueAndYellowBoundariesAreInclusive()
    {
        var state = Open(20, 1);
        var unit = Add(state, 0, 0, mobility: 6);
        var range = Pathfinder.MoveRange(state, unit);

        Assert.Contains(new GridPos(6, 0), range.Blue);
        Assert.DoesNotContain(new GridPos(7, 0), range.Blue);
        Assert.Contains(new GridPos(7, 0), range.Yellow);
        Assert.Contains(new GridPos(12, 0), range.Yellow);
        Assert.False(range.Reachable.Contains(new GridPos(13, 0)));
        Assert.Equal(1, range.ApCostTo(new GridPos(6, 0)));
        Assert.Equal(2, range.ApCostTo(new GridPos(7, 0)));
        Assert.Null(range.ApCostTo(new GridPos(13, 0)));
    }

    [Fact]
    public void CostSixPointFiveIsYellowWithMobilitySix()
    {
        // 3 diagonals (4.5) + 2 orthogonals (2.0) = 6.5.
        var state = Open(10, 4);
        var unit = Add(state, 0, 0, mobility: 6);
        var range = Pathfinder.MoveRange(state, unit);

        Assert.Equal(13, range.Reachable.CostTo(new GridPos(5, 3)));
        Assert.Contains(new GridPos(5, 3), range.Yellow);
        Assert.Equal(12, range.Reachable.CostTo(new GridPos(6, 0)));
        Assert.Contains(new GridPos(6, 0), range.Blue);
    }

    [Fact]
    public void OneApMeansNoYellow()
    {
        var state = Open(20, 1);
        var unit = Add(state, 0, 0, mobility: 6, ap: 1);
        var range = Pathfinder.MoveRange(state, unit);

        Assert.Empty(range.Yellow);
        Assert.Equal(6, range.Blue.Count);
        Assert.Null(range.ApCostTo(new GridPos(8, 0)));
    }

    [Fact]
    public void NoApMeansNoRange()
    {
        var state = Open(5, 1);
        var unit = Add(state, 0, 0, ap: 0);
        var range = Pathfinder.MoveRange(state, unit);

        Assert.Empty(range.Blue);
        Assert.Empty(range.Yellow);
    }

    [Fact]
    public void OccupiedTilesBlockButCanBeRoutedAround()
    {
        var state = Open(3, 3);
        var mover = Add(state, 0, 1);
        Add(state, 1, 1, faction: Faction.Firewall); // an ally still blocks
        var reach = Pathfinder.Reachable(state, mover, 100);

        Assert.False(reach.Contains(new GridPos(1, 1)));
        var path = reach.PathTo(new GridPos(2, 1))!;
        Assert.DoesNotContain(new GridPos(1, 1), path);
        Assert.Equal(8, reach.CostTo(new GridPos(2, 1))); // diagonals past the ally are blocked: 2 + 2 + 2 + 2
    }

    [Fact]
    public void FullyWalledOffTilesAreUnreachable()
    {
        var state = Build("..#..", "..#..", "..#..");
        var unit = Add(state, 0, 0);
        var reach = Pathfinder.Reachable(state, unit, 100);

        Assert.False(reach.Contains(new GridPos(4, 0)));
        Assert.Null(reach.PathTo(new GridPos(4, 0)));
    }

    [Fact]
    public void DeadUnitsDoNotBlock()
    {
        var state = Open(3, 1);
        var mover = Add(state, 0, 0);
        var dead = Add(state, 1, 0, faction: Faction.Meridian);
        dead.IsAlive = false;

        Assert.True(Pathfinder.Reachable(state, mover, 100).Contains(new GridPos(2, 0)));
    }

    [Fact]
    public void EqualCostTiesResolveTheSameEveryRun()
    {
        var first = PathsOnOpenMap();
        for (var i = 0; i < 5; i++)
            Assert.Equal(first, PathsOnOpenMap());

        static string PathsOnOpenMap()
        {
            var state = Open(9, 9);
            var unit = Add(state, 4, 4);
            var reach = Pathfinder.Reachable(state, unit, 24);
            return string.Join(
                ";",
                reach
                    .Tiles()
                    .Select(t => string.Join(">", reach.PathTo(t)!.Select(p => $"{p.X},{p.Y}")))
            );
        }
    }

    [Fact]
    public void ReachableIsFastOnALargeOpenMap()
    {
        var state = Open(40, 40);
        var unit = Add(state, 20, 20, mobility: 12);
        Pathfinder.Reachable(state, unit, 48); // warm up the JIT

        const int runs = 20;
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < runs; i++)
            Pathfinder.Reachable(state, unit, 24);
        watch.Stop();

        var perCall = watch.Elapsed.TotalMilliseconds / runs;
        Assert.True(perCall < 2.0, $"Reachable took {perCall:0.00} ms per call.");
    }
}
