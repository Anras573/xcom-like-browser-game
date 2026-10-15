using Firewall.Rules.Data;
using Firewall.Rules.Tactical;

namespace Firewall.Rules.Tests;

public class TacticalMapTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private static readonly GameData Data = GameData.Load(f =>
        File.ReadAllText(Path.Combine(DataDir, f))
    );

    private static MapFile File_(params string[] rows) =>
        new()
        {
            Id = "t",
            Name = "T",
            Biome = Biome.Industrial,
            Rows = rows,
        };

    private static TacticalMap Parse(params string[] rows) =>
        MapParser.Parse(File_(rows), Data.Tiles);

    private static IReadOnlyList<MapError> Errors(params string[] rows)
    {
        Assert.False(MapParser.TryParse(File_(rows), Data.Tiles, out _, out var errors));
        return errors;
    }

    [Fact]
    public void GlyphsMapToTileTypes()
    {
        var map = Parse(".,#wcCTb~dO");
        var expected = new[]
        {
            TileType.Floor,
            TileType.Road,
            TileType.Wall,
            TileType.Window,
            TileType.SmallCrate,
            TileType.BigCrate,
            TileType.Tree,
            TileType.Bush,
            TileType.Water,
            TileType.Doorway,
            TileType.Objective,
        };
        Assert.Equal(expected.Length, map.Width);
        for (var x = 0; x < expected.Length; x++)
            Assert.Equal(expected[x], map[new GridPos(x, 0)]);
    }

    [Fact]
    public void EveryTileTypeHasAGlyph()
    {
        Assert.All(Enum.GetValues<TileType>(), t => Assert.Single(Data.Tiles, d => d.Type == t));
    }

    [Fact]
    public void TextRowsAreFlippedSoPlusYIsUp()
    {
        var map = Parse("#..", "...", "..c");
        Assert.Equal((3, 3), (map.Width, map.Height));
        Assert.Equal(TileType.Wall, map[new GridPos(0, 2)]); // first text row is the top
        Assert.Equal(TileType.SmallCrate, map[new GridPos(2, 0)]); // last text row is y = 0
        Assert.Equal(TileType.Floor, map[new GridPos(0, 0)]);
    }

    [Fact]
    public void SpawnsAreExtractedAndBecomeFloor()
    {
        var map = Parse("P.e", "O.E", "PeE");
        Assert.Equal([new GridPos(0, 2), new GridPos(0, 0)], map.PlayerSpawns);
        Assert.Equal([new GridPos(2, 2), new GridPos(1, 0)], map.EnemySpawns);
        Assert.Equal([new GridPos(2, 1), new GridPos(2, 0)], map.EvacTiles);
        Assert.Equal([new GridPos(0, 1)], map.Objectives);
        Assert.Equal(TileType.Floor, map[new GridPos(0, 2)]);
        Assert.Equal(TileType.Floor, map[new GridPos(2, 1)]);
        Assert.Equal(TileType.Objective, map[new GridPos(0, 1)]);
        Assert.True(map.IsWalkable(new GridPos(0, 2)));
    }

    [Fact]
    public void RaggedRowsReportLineAndColumn()
    {
        var errors = Errors("....", "...", ".....");
        Assert.Equal(
            [
                new MapError(2, 4, "row has 3 tiles, expected 4"),
                new(3, 5, "row has 5 tiles, expected 4"),
            ],
            errors
        );
    }

    [Fact]
    public void UnknownGlyphsReportLineAndColumn()
    {
        var errors = Errors("..", ".?");
        var error = Assert.Single(errors);
        Assert.Equal((2, 2), (error.Line, error.Column));
        Assert.Contains("'?'", error.Message);
    }

    [Fact]
    public void ParseThrowsWithAllErrors()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Parse("..", ".?", "!."));
        Assert.Contains("line 2, column 2", ex.Message);
        Assert.Contains("line 3, column 1", ex.Message);
    }

    [Fact]
    public void EmptyMapIsRejected()
    {
        Assert.Single(Errors());
        Assert.Single(Errors(""));
    }

    [Fact]
    public void TilePropertiesMatchGdd()
    {
        var map = Parse(".,#wcCTb~dO");
        TileDef D(TileType t) => map.Def(t);

        Assert.True(D(TileType.Floor).Walkable);
        Assert.True(D(TileType.Road).Walkable);
        Assert.True(D(TileType.Doorway).Walkable);
        Assert.False(D(TileType.Water).Walkable);

        (TileType Type, CoverLevel Cover, bool Los)[] expected =
        [
            (TileType.Floor, CoverLevel.None, false),
            (TileType.Road, CoverLevel.None, false),
            (TileType.Wall, CoverLevel.Full, true),
            (TileType.Window, CoverLevel.Half, false),
            (TileType.SmallCrate, CoverLevel.Half, false),
            (TileType.BigCrate, CoverLevel.Full, true),
            (TileType.Tree, CoverLevel.Full, true),
            (TileType.Bush, CoverLevel.Half, false),
            (TileType.Water, CoverLevel.None, false),
            (TileType.Doorway, CoverLevel.None, false),
            (TileType.Objective, CoverLevel.Full, false),
        ];
        foreach (var (type, cover, los) in expected)
        {
            Assert.Equal(cover, D(type).Cover);
            Assert.Equal(los, D(type).BlocksLos);
            if (type is not (TileType.Floor or TileType.Road or TileType.Doorway))
                Assert.False(D(type).Walkable, type.ToString());
        }

        Assert.Equal(TileType.Floor, D(TileType.Window).DestructibleInto);
        Assert.Equal(TileType.Floor, D(TileType.SmallCrate).DestructibleInto);
        Assert.Equal(TileType.SmallCrate, D(TileType.BigCrate).DestructibleInto);
        Assert.Equal(TileType.Floor, D(TileType.Bush).DestructibleInto);
        Assert.Null(D(TileType.Wall).DestructibleInto);
        Assert.Null(D(TileType.Tree).DestructibleInto);
        Assert.Null(D(TileType.Objective).DestructibleInto);
    }

    [Fact]
    public void QueriesUseTileData()
    {
        var map = Parse("#c.");
        Assert.False(map.IsWalkable(new GridPos(0, 0)));
        Assert.True(map.IsWalkable(new GridPos(2, 0)));
        Assert.Equal(CoverLevel.Full, map.CoverAt(new GridPos(0, 0)));
        Assert.Equal(CoverLevel.Half, map.CoverAt(new GridPos(1, 0)));
        Assert.True(map.BlocksLos(new GridPos(0, 0)));
        Assert.False(map.BlocksLos(new GridPos(1, 0)));
    }

    [Fact]
    public void OutOfBoundsIsSafe()
    {
        var map = Parse("..");
        var outside = new GridPos(-1, 0);
        Assert.False(map.InBounds(outside));
        Assert.False(map.InBounds(new GridPos(0, 1)));
        Assert.False(map.IsWalkable(outside));
        Assert.Equal(CoverLevel.None, map.CoverAt(outside));
        Assert.True(map.BlocksLos(outside));
        Assert.Throws<ArgumentOutOfRangeException>(() => map[outside]);
    }

    [Fact]
    public void SetTileRaisesTileChangedOnlyOnChange()
    {
        var map = Parse("#c.");
        var changes = new List<TileChange>();
        map.TileChanged += changes.Add;

        map.SetTile(new GridPos(1, 0), TileType.Floor);
        map.SetTile(new GridPos(1, 0), TileType.Floor);

        Assert.Equal(TileType.Floor, map[new GridPos(1, 0)]);
        Assert.True(map.IsWalkable(new GridPos(1, 0)));
        Assert.Equal(
            [new TileChange(new GridPos(1, 0), TileType.SmallCrate, TileType.Floor)],
            changes
        );
    }

    [Fact]
    public void ShippedMapsParseAndAreListed()
    {
        var onDisk = Directory
            .GetFiles(Path.Combine(DataDir, "maps"), "*.json")
            .Select(f => "maps/" + Path.GetFileName(f))
            .Order();
        Assert.Equal(onDisk, GameDataFiles.Maps.Order());

        foreach (var file in GameDataFiles.Maps)
            Assert.NotNull(Data.LoadMap(File.ReadAllText(Path.Combine(DataDir, file))));
    }

    [Fact]
    public void TestYardHasTheRequiredFeatures()
    {
        var map = Data.LoadMap(File.ReadAllText(Path.Combine(DataDir, "maps/test-yard.json")));
        Assert.Equal((30, 30), (map.Width, map.Height));
        Assert.Equal(4, map.PlayerSpawns.Count);
        Assert.Equal(3, map.EnemySpawns.Count);
        Assert.NotEmpty(map.EvacTiles);
        var types = Enumerable
            .Range(0, map.Width * map.Height)
            .Select(i => map[new GridPos(i % map.Width, i / map.Width)])
            .ToHashSet();
        foreach (var t in new[] { TileType.Wall, TileType.Window, TileType.Doorway, TileType.Road })
            Assert.Contains(t, types);
        foreach (
            var t in new[] { TileType.BigCrate, TileType.SmallCrate, TileType.Tree, TileType.Bush }
        )
            Assert.Contains(t, types);
        // Every player spawn must be able to reach an evac tile without crossing blockers.
        var reach = new HashSet<GridPos>();
        var queue = new Queue<GridPos>(map.PlayerSpawns);
        reach.UnionWith(map.PlayerSpawns);
        while (queue.TryDequeue(out var p))
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var n = new GridPos(p.X + dx, p.Y + dy);
                if (map.IsWalkable(n) && reach.Add(n))
                    queue.Enqueue(n);
            }
        Assert.All(map.EvacTiles, t => Assert.Contains(t, reach));
        Assert.All(map.EnemySpawns, t => Assert.Contains(t, reach));
    }
}
