using System.Diagnostics;
using Firewall.Rules.Data;
using Firewall.Rules.Rng;
using Firewall.Rules.Tactical;
using Firewall.Rules.Tactical.Vision;

namespace Firewall.Rules.Tests;

public class VisionTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private static readonly GameData Data = GameData.Load(f =>
        File.ReadAllText(Path.Combine(DataDir, f))
    );

    // Rows are top first, like map files.
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

    private static TacticalState State(params string[] rows) => new(Map(rows), new Pcg32Random(1));

    private static TacticalUnit Add(
        TacticalState state,
        int x,
        int y,
        Faction faction = Faction.Firewall,
        int sight = 12
    )
    {
        var unit = new TacticalUnit(
            state.NextUnitId(),
            faction,
            faction == Faction.Meridian ? "husk" : "soldier",
            "U"
        )
        {
            Position = new GridPos(x, y),
            Sight = sight,
        };
        state.AddUnit(unit);
        return unit;
    }

    [Theory]
    [InlineData("#", false)] // wall
    [InlineData("c", true)] // half cover
    [InlineData("T", false)] // tree
    [InlineData("w", true)] // window
    [InlineData(".", true)]
    public void ASingleTileBetweenTheEndpoints(string glyph, bool los)
    {
        var map = Map("." + glyph + ".");
        Assert.Equal(los, LosTracer.Trace(map, new GridPos(0, 0), new GridPos(2, 0)));
    }

    [Fact]
    public void EndpointsDoNotBlock()
    {
        var map = Map("#.#");
        Assert.True(LosTracer.Trace(map, new GridPos(0, 0), new GridPos(2, 0)));
    }

    [Fact]
    public void TwoDiagonalBlockersAtACornerBlock()
    {
        // The line (0,0)-(1,1) passes exactly through the corner shared by (1,0) and (0,1).
        var both = Map("#.", ".#"); // top row: (0,1) wall; bottom row: (1,0) wall
        Assert.False(LosTracer.Trace(both, new GridPos(0, 0), new GridPos(1, 1)));
        Assert.False(LosTracer.Trace(both, new GridPos(1, 1), new GridPos(0, 0)));
    }

    [Fact]
    public void OneBlockerAtACornerLetsLosThrough()
    {
        var north = Map("#.", ".."); // only (0,1)
        var east = Map("..", ".#"); // only (1,0)
        Assert.True(LosTracer.Trace(north, new GridPos(0, 0), new GridPos(1, 1)));
        Assert.True(LosTracer.Trace(east, new GridPos(0, 0), new GridPos(1, 1)));
    }

    [Fact]
    public void CornerRuleAlsoHoldsOnLongerDiagonals()
    {
        // The 45° line (0,0)-(4,4) goes through the center of (2,2), which blocks.
        var blocked = Map(".....", ".....", "..#..", ".....", ".....");
        Assert.False(LosTracer.Trace(blocked, new GridPos(0, 0), new GridPos(4, 4)));

        // Walls at (3,2) and (2,3) are the corner pair at (2.5, 2.5)... only one is on each corner.
        var one = Map(".....", "..#..", ".....", ".....", ".....");
        Assert.True(LosTracer.Trace(one, new GridPos(0, 0), new GridPos(4, 4)));
    }

    [Fact]
    public void TraceIsSymmetricOnTheTestYard()
    {
        var map = Data.LoadMap(File.ReadAllText(Path.Combine(DataDir, "maps", "test-yard.json")));
        var rng = new Pcg32Random(42);
        for (var i = 0; i < 1000; i++)
        {
            var a = new GridPos(rng.Next(0, map.Width), rng.Next(0, map.Height));
            var b = new GridPos(rng.Next(0, map.Width), rng.Next(0, map.Height));
            Assert.Equal(LosTracer.Trace(map, a, b), LosTracer.Trace(map, b, a));
        }
    }

    [Fact]
    public void StepOutLetsASoldierPeekAroundAWallCorner()
    {
        // y=5 .....   enemy at (1,5)
        // y=2 ..###   wall from (2,2) east
        // y=1 .....   soldier at (2,1), just south of the wall's west end
        var map = Map(".....", ".....", ".....", "..###", ".....", ".....");
        var soldier = new GridPos(2, 1);
        var enemy = new GridPos(1, 5);
        Assert.False(LosTracer.Trace(map, soldier, enemy));
        Assert.True(LosTracer.HasLos(map, soldier, enemy));
    }

    [Fact]
    public void StepOutNeedsAWalkableSideTile()
    {
        // A wall on the soldier's west side too: nothing to lean into.
        var map = Map(".....", ".....", ".....", ".####", ".#...", ".....");
        Assert.False(LosTracer.HasLos(map, new GridPos(2, 1), new GridPos(1, 5)));
    }

    [Fact]
    public void SightRadiusIsInclusive()
    {
        var state = State(new string('.', 20));
        var viewer = Add(state, 0, 0);
        var at12 = Add(state, 12, 0, Faction.Meridian);
        Assert.True(LosTracer.CanSee(state, viewer, at12));

        var wide = State(new string('.', 20), new string('.', 20));
        var v = Add(wide, 0, 0);
        var far = Add(wide, 12, 1, Faction.Meridian); // sqrt(145) = 12.04
        Assert.False(LosTracer.CanSee(wide, v, far));
    }

    [Fact]
    public void WalkingRevealsAndRemembersTiles()
    {
        var state = State(new string('.', 30));
        var soldier = Add(state, 0, 0, sight: 3);
        var map = state.PlayerVision;
        map.Recompute();
        Assert.Equal(TileVisibility.Visible, map[new GridPos(3, 0)]);
        Assert.Equal(TileVisibility.Unexplored, map[new GridPos(4, 0)]);

        soldier.Position = new GridPos(10, 0);
        map.Recompute();
        Assert.Equal(TileVisibility.Explored, map[new GridPos(3, 0)]);
        Assert.Equal(TileVisibility.Visible, map[new GridPos(13, 0)]);
        Assert.Equal(TileVisibility.Unexplored, map[new GridPos(4, 0)]);
    }

    [Fact]
    public void EnemiesBehindWallsAreNotSeenAndChangesAreReported()
    {
        var state = State("..#..", "..#..", "..#..");
        var soldier = Add(state, 0, 1);
        var enemy = Add(state, 4, 1, Faction.Meridian);
        var vision = state.PlayerVision;

        Assert.Null(vision.Recompute());
        Assert.False(vision.IsUnitVisible(enemy));

        state.Map.SetTile(new GridPos(2, 1), TileType.Floor);
        var shown = Assert.IsType<VisibilityChanged>(vision.Recompute());
        Assert.Equal([enemy.Id], shown.NewlyVisibleEnemies);
        Assert.True(vision.IsUnitVisible(enemy));
        Assert.Null(vision.Recompute());

        soldier.Position = new GridPos(0, 0);
        state.Map.SetTile(new GridPos(2, 0), TileType.Wall);
        state.Map.SetTile(new GridPos(2, 1), TileType.Wall);
        state.Map.SetTile(new GridPos(2, 2), TileType.Wall);
        var hidden = Assert.IsType<VisibilityChanged>(vision.Recompute());
        Assert.Equal([enemy.Id], hidden.NewlyHiddenEnemies);
    }

    [Fact]
    public void FriendlyAndNeutralUnitsAreAlwaysVisible()
    {
        var state = State("..#..");
        Add(state, 0, 0);
        var ally = Add(state, 4, 0);
        var neutral = Add(state, 4, 0, Faction.Neutral);
        state.PlayerVision.Recompute();
        Assert.True(state.PlayerVision.IsUnitVisible(ally));
        Assert.True(state.PlayerVision.IsUnitVisible(neutral));
    }

    [Fact]
    public void EnemyVisionRemembersSoldiersForThisAndThePreviousTurn()
    {
        var state = State(new string('.', 20));
        var enemy = Add(state, 0, 0, Faction.Meridian, sight: 5);
        var soldier = Add(state, 3, 0);
        var ai = state.EnemyVision;
        ai.Recompute();
        Assert.Contains(soldier.Id, ai.VisibleEnemies);
        Assert.Empty(ai.LastKnownEnemies(state.TurnNumber));

        soldier.Position = new GridPos(15, 0);
        state.TurnNumber++;
        ai.Recompute();
        var known = Assert.Single(ai.LastKnownEnemies(state.TurnNumber));
        Assert.Equal(new LastKnown(new GridPos(3, 0), 1), known.Value);

        Assert.Empty(ai.LastKnownEnemies(state.TurnNumber + 1));
        Assert.NotNull(enemy);
    }

    [Fact]
    public void TheEngineReportsVisibilityWhileMoving()
    {
        var state = State(".........", "....#....", "....#....");
        var soldier = Add(state, 0, 0);
        soldier.Mobility = 6;
        soldier.ActionPoints = 2;
        var enemy = Add(state, 8, 2, Faction.Meridian, sight: 0);
        var engine = new TacticalEngine(state, Data);

        var events = engine.Execute(new MoveCommand(soldier.Id, new GridPos(2, 0)));
        Assert.DoesNotContain(events, e => e is VisibilityChanged);

        // The wall tiles hide the enemy until the soldier is level with the open row.
        soldier.ActionPoints = 2;
        events = engine.Execute(new MoveCommand(soldier.Id, new GridPos(3, 2)));
        Assert.Contains(
            events,
            e => e is VisibilityChanged v && v.NewlyVisibleEnemies.Contains(enemy.Id)
        );
    }

    [Fact]
    public void AFullSquadRecomputeOn40By40IsFast()
    {
        var rows = Enumerable.Range(0, 40).Select(_ => new string('.', 40)).ToArray();
        var state = State(rows);
        var rng = new Pcg32Random(7);
        for (var i = 0; i < 160; i++)
            state.Map.SetTile(new GridPos(rng.Next(0, 40), rng.Next(0, 40)), TileType.Wall);
        for (var i = 0; i < 6; i++)
            Add(state, 5 + i * 5, 5 + (i % 3) * 12);
        var vision = state.PlayerVision;
        vision.Recompute(); // warm the JIT

        var times = new List<double>();
        for (var run = 0; run < 15; run++)
        {
            state.Map.SetTile(new GridPos(0, 0), run % 2 == 0 ? TileType.Wall : TileType.Floor); // drops the cache
            var sw = Stopwatch.StartNew();
            vision.Recompute();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        Assert.True(times[times.Count / 2] <= 3.0, $"median {times[times.Count / 2]:F2} ms");
    }
}
