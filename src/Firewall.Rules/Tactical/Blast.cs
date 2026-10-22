using Firewall.Rules.Data;

namespace Firewall.Rules.Tactical;

/// <summary>
/// Area math for grenades (GDD §5.5): a tile is in the blast when its <b>center</b> is within
/// <c>radius</c> tiles of the target tile's center. Radius 1.5 is the 3×3 block, radius 2 the plus-shaped
/// 13 tiles, radius 1 the plus of 5. Walls and cover do not shape the blast.
/// </summary>
public static class Blast
{
    /// <summary>The in-bounds tiles of the blast, Y then X.</summary>
    public static IReadOnlyList<GridPos> Tiles(TacticalMap map, GridPos center, double radius)
    {
        var tiles = new List<GridPos>();
        var reach = (int)Math.Ceiling(radius);
        var limit = radius * radius + 1e-9;
        for (var y = center.Y - reach; y <= center.Y + reach; y++)
        for (var x = center.X - reach; x <= center.X + reach; x++)
        {
            var pos = new GridPos(x, y);
            var dx = x - center.X;
            var dy = y - center.Y;
            if (map.InBounds(pos) && dx * dx + dy * dy <= limit)
                tiles.Add(pos);
        }
        return tiles;
    }

    /// <summary>The units standing in the blast (carried ones are with their carrier), by id.</summary>
    public static IEnumerable<TacticalUnit> Units(TacticalState state, IReadOnlyList<GridPos> tiles)
    {
        var set = tiles.ToHashSet();
        return state
            .Units.Where(u => u.IsOnMap && set.Contains(u.Position))
            .OrderBy(u => u.Id.Value);
    }

    /// <summary>
    /// What a blast would break: destructible tiles turn into their <see cref="TileDef.DestructibleInto"/>;
    /// with <paramref name="breachWalls"/> a wall becomes floor, except on the map edge.
    /// </summary>
    public static IEnumerable<TileChange> Breakage(
        TacticalMap map,
        IReadOnlyList<GridPos> tiles,
        bool destroyCover,
        bool breachWalls
    )
    {
        foreach (var pos in tiles)
        {
            var type = map[pos];
            if (breachWalls && type == TileType.Wall && !OnEdge(map, pos))
                yield return new TileChange(pos, type, TileType.Floor);
            else if (destroyCover && map.Def(type).DestructibleInto is { } into)
                yield return new TileChange(pos, type, into);
        }
    }

    public static bool OnEdge(TacticalMap map, GridPos pos) =>
        pos.X == 0 || pos.Y == 0 || pos.X == map.Width - 1 || pos.Y == map.Height - 1;
}
