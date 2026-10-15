namespace Firewall.Rules.Tactical;

/// <summary>A planned route; costs are in half-tiles (orthogonal 2, diagonal 3).</summary>
public sealed record MovePlan(IReadOnlyList<GridPos> Path, int Cost);

/// <summary>
/// Minimal 8-directional Dijkstra over walkable, unoccupied tiles. Temporary: #19 replaces it with
/// the real pathfinder and reachable-range overlays.
/// </summary>
public static class MovePlanner
{
    private static readonly (int X, int Y, int Cost)[] Steps =
    [
        (0, 1, 2),
        (1, 0, 2),
        (0, -1, 2),
        (-1, 0, 2),
        (1, 1, 3),
        (1, -1, 3),
        (-1, -1, 3),
        (-1, 1, 3),
    ];

    public static MovePlan? Plan(TacticalState state, TacticalUnit unit, GridPos dest)
    {
        var map = state.Map;
        if (!map.IsWalkable(dest) || state.IsOccupied(dest) || dest == unit.Position)
            return null;

        var best = new Dictionary<GridPos, int> { [unit.Position] = 0 };
        var prev = new Dictionary<GridPos, GridPos>();
        var queue = new PriorityQueue<GridPos, (int Cost, int Y, int X)>();
        queue.Enqueue(unit.Position, (0, unit.Position.Y, unit.Position.X));
        while (queue.TryDequeue(out var pos, out var key))
        {
            if (key.Cost > best[pos])
                continue;
            if (pos == dest)
                break;
            foreach (var (dx, dy, cost) in Steps)
            {
                var next = new GridPos(pos.X + dx, pos.Y + dy);
                if (!map.IsWalkable(next) || state.IsOccupied(next))
                    continue;
                // No cutting corners around blocking tiles.
                if (
                    dx != 0
                    && dy != 0
                    && (
                        !map.IsWalkable(new GridPos(pos.X + dx, pos.Y))
                        || !map.IsWalkable(new GridPos(pos.X, pos.Y + dy))
                    )
                )
                    continue;
                var total = key.Cost + cost;
                if (best.TryGetValue(next, out var old) && old <= total)
                    continue;
                best[next] = total;
                prev[next] = pos;
                queue.Enqueue(next, (total, next.Y, next.X));
            }
        }

        if (!best.TryGetValue(dest, out var finalCost))
            return null;
        var path = new List<GridPos> { dest };
        for (var p = dest; prev.TryGetValue(p, out var before); p = before)
            path.Add(before);
        path.Reverse();
        return new MovePlan(path, finalCost);
    }
}
