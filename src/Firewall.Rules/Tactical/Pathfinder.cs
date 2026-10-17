namespace Firewall.Rules.Tactical;

/// <summary>
/// Costs of every tile a unit can walk to, with the route back to the start. Costs are in
/// half-tiles (<see cref="Pathfinder.OrthogonalCost"/> 2, <see cref="Pathfinder.DiagonalCost"/> 3).
/// </summary>
public sealed class ReachableTiles
{
    private readonly int _width;
    private readonly int _height;
    private readonly int[] _cost; // -1 = unreachable
    private readonly int[] _parent; // tile index of the previous step, -1 at the start

    internal ReachableTiles(GridPos origin, int width, int height, int[] cost, int[] parent)
    {
        Origin = origin;
        _width = width;
        _height = height;
        _cost = cost;
        _parent = parent;
    }

    public GridPos Origin { get; }

    /// <summary>The cost to reach <paramref name="pos"/> (0 at the origin), or null if it is unreachable.</summary>
    public int? CostTo(GridPos pos) =>
        InBounds(pos) && _cost[pos.Y * _width + pos.X] is var c and >= 0 ? c : null;

    public bool Contains(GridPos pos) => CostTo(pos) is not null;

    /// <summary>Every reachable tile except the origin, in row-major order (Y then X).</summary>
    public IEnumerable<GridPos> Tiles()
    {
        for (var i = 0; i < _cost.Length; i++)
            if (_cost[i] > 0)
                yield return new GridPos(i % _width, i / _width);
    }

    /// <summary>The route from the origin to <paramref name="dest"/>, both included; null if unreachable.</summary>
    public IReadOnlyList<GridPos>? PathTo(GridPos dest)
    {
        if (!Contains(dest))
            return null;
        var path = new List<GridPos>();
        for (var i = dest.Y * _width + dest.X; i >= 0; i = _parent[i])
            path.Add(new GridPos(i % _width, i / _width));
        path.Reverse();
        return path;
    }

    private bool InBounds(GridPos p) => p.X >= 0 && p.X < _width && p.Y >= 0 && p.Y < _height;
}

/// <summary>Which tiles a unit can reach this turn: <c>Blue</c> costs 1 AP, <c>Yellow</c> (the dash band) 2 AP.</summary>
public sealed class MoveZones
{
    public MoveZones(
        ReachableTiles reachable,
        IReadOnlyList<GridPos> blue,
        IReadOnlyList<GridPos> yellow
    )
    {
        Reachable = reachable;
        Blue = blue;
        Yellow = yellow;
    }

    public ReachableTiles Reachable { get; }
    public IReadOnlyList<GridPos> Blue { get; }
    public IReadOnlyList<GridPos> Yellow { get; }

    /// <summary>1 for a blue tile, 2 for a yellow one, null if the unit cannot go there.</summary>
    public int? ApCostTo(GridPos pos) =>
        Reachable.CostTo(pos) is { } cost && cost > 0
            ? Pathfinder.ApCost(Mobility, cost, Aps)
            : null;

    internal int Mobility { get; init; }
    internal int Aps { get; init; }
}

/// <summary>
/// 8-directional Dijkstra over walkable tiles. Living units block their tiles (the engine plans with
/// only the units the mover's side can see), and a diagonal step
/// needs both orthogonal neighbours walkable and unoccupied, so units cannot cut corners.
/// Deterministic: a fixed neighbour order, ties in the queue broken by tile index, and a parent
/// is only replaced by a strictly cheaper route.
/// </summary>
public static class Pathfinder
{
    /// <summary>Cost of an orthogonal step: 1.0 tile in half-tile units.</summary>
    public const int OrthogonalCost = 2;

    /// <summary>Cost of a diagonal step: 1.5 tiles in half-tile units.</summary>
    public const int DiagonalCost = 3;

    // dx, dy, cost; orthogonal first so ties prefer straight lines.
    private static readonly int[] Dx = [0, 1, 0, -1, 1, 1, -1, -1];
    private static readonly int[] Dy = [1, 0, -1, 0, 1, -1, -1, 1];

    /// <summary>
    /// Costs and parents of every tile within <paramref name="maxCost"/> (half-tiles) of the unit.
    /// The unit's own tile is the origin and never blocks. <paramref name="blocks"/> picks which
    /// other living units are obstacles (all of them by default); the UI passes the ones the squad
    /// can see, so the range does not give away hidden enemies.
    /// </summary>
    public static ReachableTiles Reachable(
        TacticalState state,
        TacticalUnit unit,
        int maxCost,
        Func<TacticalUnit, bool>? blocks = null
    )
    {
        var map = state.Map;
        var width = map.Width;
        var height = map.Height;
        var size = width * height;
        var cost = new int[size];
        var parent = new int[size];
        Array.Fill(cost, -1);
        Array.Fill(parent, -1);

        // Walkable and unoccupied, built once so the search is plain array reads.
        var open = new bool[size];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            open[y * width + x] = map.IsWalkable(new GridPos(x, y));
        foreach (var other in state.Units)
            if (
                other.IsAlive
                && other.Id != unit.Id
                && map.InBounds(other.Position)
                && (blocks is null || blocks(other))
            )
                open[other.Position.Y * width + other.Position.X] = false;

        var origin = unit.Position;
        if (!map.InBounds(origin))
            return new ReachableTiles(origin, width, height, cost, parent);
        var start = origin.Y * width + origin.X;
        open[start] = true;
        cost[start] = 0;

        // Priority = cost then tile index (Y then X): a total order, so runs never differ.
        var queue = new PriorityQueue<int, long>();
        queue.Enqueue(start, Key(0, start));
        while (queue.TryDequeue(out var index, out var key))
        {
            var here = (int)(key >> 32);
            if (here > cost[index])
                continue;
            int x = index % width,
                y = index / width;
            for (var d = 0; d < 8; d++)
            {
                int nx = x + Dx[d],
                    ny = y + Dy[d];
                if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                    continue;
                var next = ny * width + nx;
                if (!open[next])
                    continue;
                var step = OrthogonalCost;
                if (d >= 4)
                {
                    if (!open[y * width + nx] || !open[ny * width + x])
                        continue;
                    step = DiagonalCost;
                }
                var total = here + step;
                if (total > maxCost || (cost[next] >= 0 && cost[next] <= total))
                    continue;
                cost[next] = total;
                parent[next] = index;
                queue.Enqueue(next, Key(total, next));
            }
        }
        return new ReachableTiles(origin, width, height, cost, parent);
    }

    /// <summary>
    /// The unit's blue (cost ≤ Mobility) and yellow (≤ 2 × Mobility) tiles. Yellow needs 2 AP and
    /// nothing is reachable without AP.
    /// </summary>
    public static MoveZones MoveRange(
        TacticalState state,
        TacticalUnit unit,
        Func<TacticalUnit, bool>? blocks = null
    )
    {
        var aps = unit.ActionPoints;
        var limit =
            aps >= 2 ? unit.Mobility * 4
            : aps == 1 ? unit.Mobility * 2
            : 0;
        var reachable = Reachable(state, unit, limit, blocks);
        var blue = new List<GridPos>();
        var yellow = new List<GridPos>();
        foreach (var tile in reachable.Tiles())
            (reachable.CostTo(tile)!.Value <= unit.Mobility * 2 ? blue : yellow).Add(tile);
        return new MoveZones(reachable, blue, yellow) { Mobility = unit.Mobility, Aps = aps };
    }

    /// <summary>1 AP within mobility, 2 AP (a dash) within twice that, null beyond or without the AP.</summary>
    public static int? ApCost(int mobility, int cost, int availableAp = 2) =>
        cost <= mobility * 2 ? 1
        : cost <= mobility * 4 && availableAp >= 2 ? 2
        : null;

    private static long Key(int cost, int index) => ((long)cost << 32) | (uint)index;
}
