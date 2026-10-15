using Firewall.Rules;
using Firewall.Rules.Data;
using Firewall.Rules.Tactical;

namespace Firewall.Web.Rendering;

/// <summary>Frame choices per biome. Names are keys of <c>tiles.json</c>.</summary>
public sealed record BiomeStyle(
    string[] Ground,
    string[] Road,
    string[] SmallCrate,
    string[] BigCrate,
    string[] Bush,
    string TreeSet,
    string Objective
);

/// <summary>Pure mapping from the rules grid to <c>Tilemap</c> cells and frame names.</summary>
public static class MapTiling
{
    /// <summary>
    /// <c>Tilemap</c> index of a grid cell. The grid is +Y up but tilemaps are row-major with row 0
    /// at the top, so <c>row = Height - 1 - y</c>.
    /// </summary>
    public static int TileIndex(int x, int y, int width, int height) =>
        RowOf(y, height) * width + x;

    public static int RowOf(int y, int height) => height - 1 - y;

    // The sheet has no asphalt, so roads are the darker concrete slabs (industrial, urban) and
    // dirt (suburban); urban "sidewalk" is the plain concrete.
    private static readonly BiomeStyle Industrial = new(
        ["ground.concrete.0", "ground.concrete.1", "ground.concrete.2"],
        ["ground.concrete.3", "ground.concrete.4"],
        ["prop.crate.small", "prop.crate.small_alt"],
        ["prop.crate.large", "prop.crate.large_alt"],
        ["prop.bush.small_orange", "prop.rock.0"],
        "orange",
        "prop.table.round"
    );

    private static readonly BiomeStyle Urban = new(
        ["ground.concrete.0", "ground.concrete.1"],
        ["ground.concrete.3", "ground.concrete.4"],
        ["prop.armchair.dark"],
        ["prop.crate.large_alt"],
        ["prop.plant.leaf"],
        "orange",
        "prop.table.round"
    );

    private static readonly BiomeStyle Suburban = new(
        ["ground.grass.0", "ground.grass.1", "ground.grass.2", "ground.grass.3"],
        ["ground.dirt.0", "ground.dirt.1"],
        ["prop.armchair.green", "prop.armchair.orange"],
        ["prop.crate.large"],
        ["prop.bush.green", "prop.bush.small_green"],
        "green",
        "prop.table.round"
    );

    public static BiomeStyle Style(Biome biome) =>
        biome switch
        {
            Biome.Urban => Urban,
            Biome.Suburban => Suburban,
            _ => Industrial,
        };

    public static IEnumerable<BiomeStyle> AllStyles => [Industrial, Urban, Suburban];

    /// <summary>Stable variant choice from the position (never the RNG), so a map always looks the same.</summary>
    public static int Hash(int x, int y, int salt = 0)
    {
        unchecked
        {
            var h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
            h ^= h >> 16;
            h *= 0x7feb352d;
            h ^= h >> 15;
            h *= 0x846ca68b;
            h ^= h >> 16;
            return (int)(h & 0x7fffffff);
        }
    }

    private static string Pick(string[] names, int x, int y, int salt) =>
        names[Hash(x, y, salt) % names.Length];

    /// <summary>The ground frame under a cell.</summary>
    public static string GroundFrame(Biome biome, TileType type, int x, int y) =>
        type switch
        {
            TileType.Water => Pick(["ground.water.0", "ground.water.1"], x, y, 1),
            TileType.Road => Pick(Style(biome).Road, x, y, 2),
            TileType.Doorway => Pick(
                ["floor.wood.0", "floor.wood.1", "floor.wood.2", "floor.wood.3"],
                x,
                y,
                3
            ),
            _ => Pick(Style(biome).Ground, x, y, 4),
        };

    /// <summary>
    /// The props frame for a cell, or null for none. <paramref name="isWall"/> says whether a cell
    /// counts as wall for auto-tiling (walls and windows; outside the map counts too, so border
    /// walls stay seamless).
    /// </summary>
    public static string? PropFrame(
        Biome biome,
        TileType type,
        int x,
        int y,
        Func<int, int, bool> isWall
    )
    {
        var style = Style(biome);
        switch (type)
        {
            case TileType.Wall:
            case TileType.Window:
                return WallAutoTiler.FrameName(WallAutoTiler.Compute(isWall, x, y));
            case TileType.SmallCrate:
                return Pick(style.SmallCrate, x, y, 5);
            case TileType.BigCrate:
                return Pick(style.BigCrate, x, y, 6);
            case TileType.Bush:
                return Pick(style.Bush, x, y, 7);
            case TileType.Tree:
                // Trees are 2x2 art. A 2x2 block of T aligned to even coordinates shows a whole
                // tree; the quadrant comes from the parity of the cell (north row has odd y).
                var top = (y & 1) == 1 ? "t" : "b";
                var left = (x & 1) == 0 ? "l" : "r";
                return $"prop.tree.{style.TreeSet}.{top}{left}";
            case TileType.Objective:
                return style.Objective;
            default:
                return null;
        }
    }

    /// <summary>Rubble left where a destructible prop used to be, or null.</summary>
    public static string? RubbleFrame(TileType old, TileType now, int x, int y) =>
        old is TileType.Window or TileType.SmallCrate or TileType.BigCrate or TileType.Bush
        && now is not (TileType.Window or TileType.SmallCrate or TileType.BigCrate or TileType.Bush)
            ? Pick(["decal.rubble.0", "decal.rubble.1"], x, y, 8)
            : null;

    /// <summary>Glass on top of a window's wall tile.</summary>
    public static string? DetailFrame(TileType type) =>
        type == TileType.Window ? "decal.shards" : null;

    public static bool CountsAsWall(TacticalMap map, int x, int y) =>
        !map.InBounds(new GridPos(x, y))
        || map[new GridPos(x, y)] is TileType.Wall or TileType.Window;
}
