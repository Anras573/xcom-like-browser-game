namespace Firewall.Rules.Tactical.Vision;

/// <summary>
/// Line of sight (GDD §4.4): a supercover line between tile centers, with step-out. Units never
/// block it; only tiles with <c>blocksLos</c> do. Pure and stateless.
/// </summary>
public static class LosTracer
{
    private static readonly GridPos[] Cardinals = [new(0, 1), new(1, 0), new(0, -1), new(-1, 0)];

    /// <summary>
    /// True when no <i>intermediate</i> tile on the segment from <paramref name="a"/> to
    /// <paramref name="b"/> blocks LOS; the endpoints never count.
    /// </summary>
    /// <remarks>
    /// The line visits every tile it touches. When it passes exactly through a corner shared by two
    /// tiles, LOS is blocked only if <b>both</b> block; if only one does, LOS passes. The trace always
    /// runs from the lexicographically smaller endpoint, so <c>Trace(a, b) == Trace(b, a)</c>.
    /// </remarks>
    public static bool Trace(TacticalMap map, GridPos a, GridPos b)
    {
        if (a.X > b.X || (a.X == b.X && a.Y > b.Y))
            (a, b) = (b, a);

        var dx = b.X - a.X; // never negative after canonicalising
        var dy = Math.Abs(b.Y - a.Y);
        var xInc = 1;
        var yInc = b.Y > a.Y ? 1 : -1;
        var x = a.X;
        var y = a.Y;
        var n = 1 + dx + dy;
        var error = dx - dy;
        dx *= 2;
        dy *= 2;

        for (; n > 0; n--)
        {
            if (x == b.X && y == b.Y)
                break;
            if ((x != a.X || y != a.Y) && map.BlocksLos(x, y))
                return false;

            if (error > 0)
            {
                x += xInc;
                error -= dy;
            }
            else if (error < 0)
            {
                y += yInc;
                error += dx;
            }
            else
            {
                // Exactly through a corner: both tiles beside it are touched.
                if (map.BlocksLos(x + xInc, y) && map.BlocksLos(x, y + yInc))
                    return false;
                x += xInc;
                y += yInc;
                error += dx - dy;
                n--;
            }
        }
        return true;
    }

    /// <summary>
    /// <see cref="Trace"/> from <paramref name="from"/>, or from a step-out tile: for each cardinal
    /// direction where the neighbour blocks LOS, the two tiles perpendicular to it are tried when
    /// walkable. The unit does not actually move.
    /// </summary>
    public static bool HasLos(TacticalMap map, GridPos from, GridPos to)
    {
        if (Trace(map, from, to))
            return true;
        foreach (var dir in Cardinals)
        {
            if (!map.BlocksLos(new GridPos(from.X + dir.X, from.Y + dir.Y)))
                continue;
            // Perpendiculars of (x, y) are (-y, x) and (y, -x).
            var left = new GridPos(from.X - dir.Y, from.Y + dir.X);
            var right = new GridPos(from.X + dir.Y, from.Y - dir.X);
            if (CanPeekFrom(map, left, to) || CanPeekFrom(map, right, to))
                return true;
        }
        return false;
    }

    private static bool CanPeekFrom(TacticalMap map, GridPos tile, GridPos to) =>
        map.IsWalkable(tile) && Trace(map, tile, to);

    /// <summary>Within <paramref name="sight"/> tiles (Euclidean, tile centers, inclusive) and <see cref="HasLos"/>.</summary>
    public static bool CanSee(TacticalMap map, GridPos from, int sight, GridPos to) =>
        InRange(from, to, sight) && HasLos(map, from, to);

    internal static bool InRange(GridPos from, GridPos to, int sight)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        return dx * dx + dy * dy <= sight * sight;
    }

    /// <summary>Whether <paramref name="viewer"/> can see <paramref name="target"/>'s tile.</summary>
    public static bool CanSee(TacticalState state, TacticalUnit viewer, TacticalUnit target) =>
        CanSee(state.Map, viewer.Position, viewer.Sight, target.Position);
}
