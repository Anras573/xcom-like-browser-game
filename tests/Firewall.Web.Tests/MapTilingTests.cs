using System.Text.Json;
using Firewall.Rules;
using Firewall.Rules.Data;
using Firewall.Rules.Tactical;
using Firewall.Web.Assets;
using Firewall.Web.Rendering;

namespace Firewall.Web.Tests;

public class MapTilingTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (
            dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Firewall.Web"))
        )
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root not found");
    }

    private static string WwwRoot(string path) =>
        Path.Combine(Root, "src", "Firewall.Web", "wwwroot", path);

    private static readonly GameData Data = GameData.Load(f =>
        File.ReadAllText(WwwRoot(Path.Combine("data", f)))
    );

    private static AssetRegistry Registry() =>
        new(
            new[] { "tiles", "characters", "overlays", "fx" }.Select(n =>
                JsonSerializer.Deserialize(
                    File.ReadAllText(WwwRoot($"assets/{n}.json")),
                    AssetJsonContext.Default.GridManifest
                )!
            ),
            JsonSerializer.Deserialize(
                File.ReadAllText(WwwRoot("assets/ui.json")),
                AssetJsonContext.Default.UiManifest
            )!
        );

    // Crafted 3x3 neighbourhood: rows are written top (north) first, '#' is wall. Outside the
    // grid counts as non-wall. Returns the mask of the centre cell.
    private static WallMask Mask(string top, string mid, string bottom)
    {
        string[] rows = [top, mid, bottom];
        bool IsWall(int x, int y)
        {
            // Centre cell is (1, 1); +Y is north, so y = 2 is the top text row.
            var col = x;
            var row = 2 - y;
            return col is >= 0 and < 3 && row is >= 0 and < 3 && rows[row][col] == '#';
        }
        return WallAutoTiler.Compute(IsWall, 1, 1);
    }

    private static string Frame(string top, string mid, string bottom) =>
        WallAutoTiler.FrameName(Mask(top, mid, bottom)).Replace("wall.grey.", "");

    [Fact]
    public void TileIndexFlipsYAndHandlesCorners()
    {
        // 4 wide, 3 high: y = 2 is the top row of the Tilemap.
        Assert.Equal(0, MapTiling.TileIndex(0, 2, 4, 3)); // top-left
        Assert.Equal(3, MapTiling.TileIndex(3, 2, 4, 3)); // top-right
        Assert.Equal(8, MapTiling.TileIndex(0, 0, 4, 3)); // bottom-left
        Assert.Equal(11, MapTiling.TileIndex(3, 0, 4, 3)); // bottom-right
        Assert.Equal(5, MapTiling.TileIndex(1, 1, 4, 3));
        Assert.Equal(2, MapTiling.RowOf(0, 3));
        Assert.Equal(0, MapTiling.RowOf(2, 3));
    }

    [Fact]
    public void TilemapRowZeroIsTheTopOfTheMap()
    {
        var map = Data.LoadMap(
            """{"id":"m","name":"M","biome":"industrial","rows":["###","#.#","#c#"]}"""
        );
        var registry = Registry();
        var world = new Yaeger.ECS.World();
        using var tilemaps = MapTilemaps.Build(world, map, registry);

        var crate = MapTiling.PropFrame(
            Biome.Industrial,
            TileType.SmallCrate,
            1,
            0,
            (_, _) => true
        );
        Assert.NotNull(crate);
        // The crate is on the last text row (y = 0), so on the last Tilemap row.
        var props = tilemaps.Props;
        Assert.Equal(
            registry.Frame(crate!).FrameIndex,
            props.Tiles[MapTiling.TileIndex(1, 0, 3, 3)]
        );
        Assert.Equal(registry.Frame(crate!).FrameIndex, props.GetTile(1, 2));
        Assert.Equal(Yaeger.Graphics.Tilemap.EmptyTile, props.GetTile(1, 1)); // the floor cell
    }

    [Fact]
    public void WallMaskForCraftedNeighbourhoods()
    {
        var isolated = Mask("...", ".#.", "...");
        Assert.Equal(Sides.N | Sides.E | Sides.S | Sides.W, isolated.Edges);
        Assert.Equal(Corners.None, isolated.Knobs);

        var inside = Mask("###", "###", "###");
        Assert.Equal((Sides.None, Corners.None), (inside.Edges, inside.Knobs));

        // Wall continues east and west: rims north and south.
        Assert.Equal(Sides.N | Sides.S, Mask("...", "###", "...").Edges);
        // Wall continues north and east only: rims south and west.
        Assert.Equal(Sides.S | Sides.W, Mask(".#.", ".##", "...").Edges);

        // Concave corner: all four sides are wall, one diagonal is not.
        var knob = Mask("##.", "###", "###");
        Assert.Equal((Sides.None, Corners.NE), (knob.Edges, knob.Knobs));

        // A diagonal gap beside an open side is not a knob.
        var noKnob = Mask("#..", "###", "###");
        Assert.Equal((Sides.N, Corners.None), (noKnob.Edges, noKnob.Knobs));
    }

    // Rows are written north first; "open" sides are the ones without a wall neighbour.
    [Theory]
    [InlineData("...", ".#.", "...", "enclosed")]
    [InlineData("###", "###", "###", "interior")]
    [InlineData("...", "###", "...", "straight_h")]
    [InlineData(".#.", ".#.", ".#.", "straight_v")]
    [InlineData("...", ".##", ".##", "corner_nw.plain")] // rims north and west
    [InlineData("...", ".##", ".#.", "corner_nw")] // ... plus a knob where SE is open
    [InlineData("...", "##.", "##.", "corner_ne.plain")]
    [InlineData("...", "##.", ".#.", "corner_ne")]
    [InlineData(".##", ".##", "...", "corner_sw.plain")]
    [InlineData(".#.", ".##", "...", "corner_sw")]
    [InlineData("##.", "##.", "...", "corner_se.plain")]
    [InlineData(".#.", "##.", "...", "corner_se")]
    [InlineData("##.", "###", "...", "s")] // a T missing one knob falls back to the plain edge
    [InlineData(".#.", "###", "...", "t_s")]
    [InlineData("...", "###", ".#.", "t_n")]
    [InlineData("...", ".#.", ".#.", "end_s")]
    [InlineData(".#.", ".#.", "...", "end_n")]
    [InlineData("...", ".##", "...", "end_e")]
    [InlineData("...", "##.", "...", "end_w")]
    [InlineData("###", "###", "##.", "knob_se")]
    [InlineData("###", "###", "...", "s")]
    public void WallFrameForCraftedNeighbourhoods(
        string top,
        string mid,
        string bottom,
        string expected
    ) => Assert.Equal(expected, Frame(top, mid, bottom));

    [Fact]
    public void EveryMaskResolvesToAnExistingFrame()
    {
        var registry = Registry();
        Assert.All(
            WallAutoTiler.FrameNames,
            name => Assert.True(registry.TryFrame(name, out _), name)
        );

        // All 256 neighbourhoods give a tile, so there is never a missing-wall artifact.
        for (var bits = 0; bits < 256; bits++)
        {
            bool IsWall(int x, int y)
            {
                var i = (y + 1) * 3 + (x + 1);
                if (i == 4)
                    return true;
                return (bits >> (i > 4 ? i - 1 : i) & 1) == 1;
            }
            var name = WallAutoTiler.FrameName(WallAutoTiler.Compute(IsWall, 0, 0));
            Assert.True(registry.TryFrame(name, out _), $"{name} for {bits}");
        }
    }

    [Fact]
    public void EveryBiomeFrameExists()
    {
        var registry = Registry();
        foreach (var style in MapTiling.AllStyles)
        {
            foreach (
                var name in style
                    .Ground.Concat(style.Road)
                    .Concat(style.SmallCrate)
                    .Concat(style.BigCrate)
                    .Concat(style.Bush)
                    .Append(style.Objective)
            )
                Assert.True(registry.TryFrame(name, out _), name);
            foreach (var part in new[] { "tl", "tr", "bl", "br" })
                Assert.True(registry.TryFrame($"prop.tree.{style.TreeSet}.{part}", out _));
        }
        foreach (
            var name in new[]
            {
                "decal.rubble.0",
                "decal.rubble.1",
                "decal.shards",
                "floor.wood.0",
                "floor.wood.3",
                "ground.water.0",
                "ground.water.1",
            }
        )
            Assert.True(registry.TryFrame(name, out _), name);
    }

    [Fact]
    public void TreeQuadrantFollowsParity()
    {
        string Tree(int x, int y) =>
            MapTiling.PropFrame(Biome.Suburban, TileType.Tree, x, y, (_, _) => false)!;
        Assert.Equal("prop.tree.green.bl", Tree(0, 0));
        Assert.Equal("prop.tree.green.br", Tree(1, 0));
        Assert.Equal("prop.tree.green.tl", Tree(0, 1));
        Assert.Equal("prop.tree.green.tr", Tree(1, 1));
        Assert.Equal("prop.tree.green.tl", Tree(4, 7));
    }

    [Fact]
    public void VariantsAreStableAndSpreadOut()
    {
        Assert.Equal(MapTiling.Hash(3, 9, 1), MapTiling.Hash(3, 9, 1));
        Assert.NotEqual(MapTiling.Hash(3, 9, 1), MapTiling.Hash(3, 9, 2));
        var frames = Enumerable
            .Range(0, 100)
            .Select(i => MapTiling.GroundFrame(Biome.Industrial, TileType.Floor, i % 10, i / 10))
            .ToHashSet();
        Assert.True(frames.Count > 1);
    }

    [Fact]
    public void TestYardWallsLeaveNoSingleTileArtifacts()
    {
        var map = Data.LoadMap(File.ReadAllText(WwwRoot("data/maps/test-yard.json")));
        // Every wall cell must get an auto-tile frame (never a fallback plain tile or none).
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var type = map[new GridPos(x, y)];
            var frame = MapTiling.PropFrame(
                map.Biome,
                type,
                x,
                y,
                (cx, cy) => MapTiling.CountsAsWall(map, cx, cy)
            );
            if (type is TileType.Wall or TileType.Window)
                Assert.StartsWith("wall.grey.", frame);
        }
    }

    [Fact]
    public void DestroyingATileRefreshesOnlyItsNeighbourhood()
    {
        var map = Data.LoadMap(
            """{"id":"m","name":"M","biome":"industrial","rows":["#####","#####","##w##","#####","#####"]}"""
        );
        var registry = Registry();
        using var tilemaps = MapTilemaps.Build(new Yaeger.ECS.World(), map, registry);
        var before = (int[])tilemaps.Props.Tiles.Clone();

        map.SetTile(new GridPos(2, 2), TileType.Floor); // the window falls out of the wall

        var after = tilemaps.Props.Tiles;
        var changed = Enumerable.Range(0, after.Length).Where(i => after[i] != before[i]).ToList();
        Assert.NotEmpty(changed);
        // Only the 3x3 block around (2, 2) may change: tilemap rows 1..3, columns 1..3.
        Assert.All(
            changed,
            i => Assert.True(i % 5 is >= 1 and <= 3 && i / 5 is >= 1 and <= 3, $"{i}")
        );
        // The destroyed tile itself has no prop any more; its neighbours gained rims and knobs.
        Assert.Equal(Yaeger.Graphics.Tilemap.EmptyTile, tilemaps.Props.GetTile(2, 2));
        Assert.Equal(registry.Frame("wall.grey.s").FrameIndex, tilemaps.Props.GetTile(2, 1));
        Assert.Equal(registry.Frame("wall.grey.knob_se").FrameIndex, tilemaps.Props.GetTile(1, 1));
    }
}
