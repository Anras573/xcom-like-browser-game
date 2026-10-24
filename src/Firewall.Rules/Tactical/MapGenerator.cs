using Firewall.Rules.Data;
using Firewall.Rules.Rng;

namespace Firewall.Rules.Tactical;

/// <summary>Which mission a generated map is for; decides whether objective blocks are placed.</summary>
public enum GenMission
{
    Purge,
    Rescue,
    Sabotage,
    Capture,
}

/// <summary>What <see cref="MapGenerator"/> builds a map from.</summary>
public sealed record MapGenParams
{
    public Biome Biome { get; init; }

    /// <summary>Size in blocks: 3x3 gives a 30x30 playfield, 4x3 gives 40x30 (before roads and the border).</summary>
    public int BlocksX { get; init; } = 3;

    public int BlocksY { get; init; } = 3;
    public GenMission Mission { get; init; }

    /// <summary>Null means the mission decides: Rescue and Sabotage need objective blocks.</summary>
    public bool? RequireObjectiveBlocks { get; init; }

    /// <summary>How many enemy spawn tiles the map gets.</summary>
    public int PodCount { get; init; } = 3;

    public bool NeedsObjective =>
        RequireObjectiveBlocks ?? Mission is GenMission.Rescue or GenMission.Sabotage;
}

/// <summary>A generated map and how it came to be.</summary>
/// <param name="CivilianSpots">Rescue only: where civilians stand (the <c>O</c> tiles of the objective blocks, made walkable).</param>
/// <param name="Attempts">How many layouts were tried, 1 when the first worked.</param>
/// <param name="FellBack">True when every attempt failed and <see cref="Map"/> is the handcrafted fallback.</param>
public sealed record GeneratedMap(
    TacticalMap Map,
    IReadOnlyList<GridPos> CivilianSpots,
    int Attempts,
    bool FellBack
);

/// <summary>
/// Assembles a map from hand-authored 10x10 blocks (GDD §4.1, §6.3): a player block on an edge,
/// objective blocks at least two blocks away, random generic blocks elsewhere, optional road
/// strips between rows or columns, and a 1-tile wall border. The result is validated; a bad
/// layout is retried and, after <see cref="MaxAttempts"/> tries, the fallback map is used.
/// </summary>
public sealed class MapGenerator(
    BlockLibrary blocks,
    IReadOnlyList<TileDef> tiles,
    Func<TacticalMap> fallback,
    Action<string>? log = null
)
{
    public const int MaxAttempts = 20;

    /// <summary>Enemy spawns keep at least this path distance (tiles) from every player spawn.</summary>
    public const int MinSpawnDistance = 14;

    /// <summary>Spawns invented on plain floor stay this many tiles from the map edge, so pods don't start pinned against the border.</summary>
    public const int SpawnEdgeMargin = 3;

    /// <summary>Enemy spawns are at least this many tiles apart.</summary>
    public const int MinPodSpacing = 6;

    public const int RoadWidth = 2;

    /// <summary>Blocks away from the player block that objective blocks must be (Manhattan, in cells).</summary>
    public const int MinObjectiveDistance = 2;

    // Distances are in half-tiles like Pathfinder: orthogonal 2, diagonal 3.
    private const int Unreached = int.MaxValue;

    public TacticalMap Generate(MapGenParams p, IRandom rng) => GenerateDetailed(p, rng).Map;

    public GeneratedMap GenerateDetailed(MapGenParams p, IRandom rng)
    {
        if (p.BlocksX < 1 || p.BlocksY < 1 || p.BlocksX * p.BlocksY < 2)
            throw new ArgumentException("The grid needs at least two blocks.", nameof(p));
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var random = attempt == 0 ? rng : rng.Fork($"retry-{attempt}");
            if (TryBuild(p, random, out var map, out var civilians))
                return new GeneratedMap(map!, civilians, attempt + 1, false);
        }
        log?.Invoke(
            $"Map generation failed {MaxAttempts} times for {p.Biome} {p.BlocksX}x{p.BlocksY}; using the fallback map."
        );
        return new GeneratedMap(fallback(), [], MaxAttempts, true);
    }

    private bool TryBuild(
        MapGenParams p,
        IRandom rng,
        out TacticalMap? map,
        out IReadOnlyList<GridPos> civilians
    )
    {
        map = null;
        civilians = [];
        var spawnBlocks = blocks.Find(p.Biome, BlockTag.SpawnPlayer);
        var objectiveBlocks = blocks.Find(p.Biome, BlockTag.Objective);
        var filler = blocks
            .Find(p.Biome, BlockTag.Generic)
            .Concat(blocks.Find(p.Biome, BlockTag.SpawnEnemy))
            .Distinct()
            .ToList();
        if (
            spawnBlocks.Count == 0
            || filler.Count == 0
            || (p.NeedsObjective && objectiveBlocks.Count == 0)
        )
            throw new InvalidOperationException($"Not enough {p.Biome} blocks to generate a map.");

        var layout = rng.Fork("layout");
        var cells = Layout(p, layout, spawnBlocks, objectiveBlocks, filler);
        var (roadColumn, roadRow) = RoadGaps(p, rng.Fork("roads"));
        var grid = Assemble(p, cells, roadColumn, roadRow);

        var width = grid[0].Length;
        var height = grid.Length;
        var raw = Parse(p, grid);
        var spawns = ChooseEnemySpawns(raw, p.PodCount, rng.Fork("enemy-spawns"));

        // Only the chosen spawns stay; Rescue turns objective markers into civilian spots.
        var spots = new List<GridPos>();
        for (var r = 0; r < height; r++)
        for (var x = 0; x < width; x++)
        {
            if (grid[r][x] == MapParser.EnemySpawn)
                grid[r][x] = '.';
            else if (grid[r][x] == 'O' && p.Mission == GenMission.Rescue)
            {
                grid[r][x] = '.';
                spots.Add(new GridPos(x, height - 1 - r));
            }
        }
        foreach (var s in spawns)
            grid[height - 1 - s.Y][s.X] = MapParser.EnemySpawn;
        var result = Parse(p, grid);

        if (!IsValid(result, p, spawns, spots))
            return false;
        map = result;
        civilians = spots;
        return true;
    }

    private record struct Cell(MapBlockFile Block, int Turns, bool Mirror);

    private Cell[,] Layout(
        MapGenParams p,
        IRandom rng,
        IReadOnlyList<MapBlockFile> spawnBlocks,
        IReadOnlyList<MapBlockFile> objectiveBlocks,
        IReadOnlyList<MapBlockFile> filler
    )
    {
        var cells = new Cell?[p.BlocksX, p.BlocksY];

        MapBlockFile Pick(IReadOnlyList<MapBlockFile> pool, int cx, int cy, bool avoidNeighbours)
        {
            var ids = new HashSet<string>();
            if (avoidNeighbours)
                foreach (var (nx, ny) in Neighbours(cx, cy, p))
                    if (cells[nx, ny] is { } n)
                        ids.Add(n.Block.Id);
            var allowed = pool.Where(b => !ids.Contains(b.Id)).ToList();
            return WeightedPick(allowed.Count > 0 ? allowed : pool, rng);
        }

        Cell Place(MapBlockFile b) => new(b, rng.Next(0, 4), rng.Next(0, 2) == 1);

        // The player block sits on a random edge cell.
        var edge = new List<(int X, int Y)>();
        for (var x = 0; x < p.BlocksX; x++)
        for (var y = 0; y < p.BlocksY; y++)
            if (x == 0 || y == 0 || x == p.BlocksX - 1 || y == p.BlocksY - 1)
                edge.Add((x, y));
        var spawnCell = edge[rng.Next(0, edge.Count)];
        cells[spawnCell.X, spawnCell.Y] = Place(WeightedPick(spawnBlocks, rng));

        if (p.NeedsObjective)
        {
            var count = p.Mission == GenMission.Rescue ? 2 : 1;
            var open = new List<(int X, int Y)>();
            for (var x = 0; x < p.BlocksX; x++)
            for (var y = 0; y < p.BlocksY; y++)
                if ((x, y) != spawnCell)
                    open.Add((x, y));
            var far = open.Where(c => Manhattan(c, spawnCell) >= MinObjectiveDistance).ToList();
            // A tiny grid may have no cell that far; take the farthest ones instead.
            if (far.Count < count)
                far = open.OrderByDescending(c => Manhattan(c, spawnCell))
                    .Take(Math.Max(count, 1))
                    .ToList();
            var picked = new List<(int X, int Y)>();
            var usedIds = new HashSet<string>();
            for (var i = 0; i < count && far.Count > 0; i++)
            {
                // Later objectives prefer cells away from the earlier ones.
                var chosen =
                    picked.Count == 0
                        ? far[rng.Next(0, far.Count)]
                        : FarthestFrom(far, picked, rng);
                far.Remove(chosen);
                picked.Add(chosen);
                var pool = objectiveBlocks.Where(b => !usedIds.Contains(b.Id)).ToList();
                var block = WeightedPick(pool.Count > 0 ? pool : objectiveBlocks, rng);
                usedIds.Add(block.Id);
                cells[chosen.X, chosen.Y] = Place(block);
            }
        }

        for (var y = 0; y < p.BlocksY; y++)
        for (var x = 0; x < p.BlocksX; x++)
            if (cells[x, y] is null)
                cells[x, y] = Place(Pick(filler, x, y, avoidNeighbours: true));

        var result = new Cell[p.BlocksX, p.BlocksY];
        for (var y = 0; y < p.BlocksY; y++)
        for (var x = 0; x < p.BlocksX; x++)
            result[x, y] = cells[x, y]!.Value;
        return result;
    }

    private static (int X, int Y) FarthestFrom(
        List<(int X, int Y)> candidates,
        List<(int X, int Y)> others,
        IRandom rng
    )
    {
        int Score((int X, int Y) c) => others.Min(o => Manhattan(c, o));
        var best = candidates.Max(Score);
        var tied = candidates.Where(c => Score(c) == best).ToList();
        return tied[rng.Next(0, tied.Count)];
    }

    private static IEnumerable<(int X, int Y)> Neighbours(int x, int y, MapGenParams p)
    {
        if (x > 0)
            yield return (x - 1, y);
        if (x < p.BlocksX - 1)
            yield return (x + 1, y);
        if (y > 0)
            yield return (x, y - 1);
        if (y < p.BlocksY - 1)
            yield return (x, y + 1);
    }

    private static int Manhattan((int X, int Y) a, (int X, int Y) b) =>
        Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    private static MapBlockFile WeightedPick(IReadOnlyList<MapBlockFile> pool, IRandom rng)
    {
        var roll = rng.NextDouble() * pool.Sum(b => b.Weight);
        foreach (var b in pool)
        {
            roll -= b.Weight;
            if (roll < 0)
                return b;
        }
        return pool[^1];
    }

    /// <summary>
    /// A road strip goes between two block columns and/or two block rows. Urban maps always get
    /// them, suburban ones half the time, industrial ones rarely. -1 means no strip on that axis.
    /// </summary>
    private static (int Column, int Row) RoadGaps(MapGenParams p, IRandom rng)
    {
        var chance = p.Biome switch
        {
            Biome.Urban => 100,
            Biome.Suburban => 50,
            _ => 15,
        };
        int Gap(int blocks) => blocks >= 2 && rng.Roll(chance) ? rng.Next(0, blocks - 1) : -1;
        return (Gap(p.BlocksX), Gap(p.BlocksY));
    }

    /// <summary>Glyph rows (top first) of the whole map, border and roads included.</summary>
    private static char[][] Assemble(MapGenParams p, Cell[,] cells, int roadColumn, int roadRow)
    {
        const int size = BlockLibrary.BlockSize;
        var width = p.BlocksX * size + (roadColumn >= 0 ? RoadWidth : 0) + 2;
        var height = p.BlocksY * size + (roadRow >= 0 ? RoadWidth : 0) + 2;
        var grid = new char[height][];
        for (var r = 0; r < height; r++)
            grid[r] = Enumerable.Repeat('#', width).ToArray();

        // Where a block column or row starts, past the border and any road strip before it.
        int Start(int index, int gap) =>
            1 + index * size + (gap >= 0 && index > gap ? RoadWidth : 0);

        for (var cy = 0; cy < p.BlocksY; cy++)
        for (var cx = 0; cx < p.BlocksX; cx++)
        {
            var cell = cells[cx, cy];
            var rows = BlockTransform.Apply(cell.Block.Rows, cell.Turns, cell.Mirror);
            var x0 = Start(cx, roadColumn);
            var y0 = Start(cy, roadRow);
            for (var r = 0; r < size; r++)
            for (var c = 0; c < size; c++)
                grid[y0 + r][x0 + c] = rows[r][c];
        }

        if (roadColumn >= 0)
        {
            var x0 = Start(roadColumn, roadColumn) + size;
            for (var r = 1; r < height - 1; r++)
            for (var c = 0; c < RoadWidth; c++)
                grid[r][x0 + c] = ',';
        }
        if (roadRow >= 0)
        {
            var y0 = Start(roadRow, roadRow) + size;
            for (var r = 0; r < RoadWidth; r++)
            for (var c = 1; c < width - 1; c++)
                grid[y0 + r][c] = ',';
        }
        return grid;
    }

    private TacticalMap Parse(MapGenParams p, char[][] grid) =>
        MapParser.Parse(
            new MapFile
            {
                Id = $"generated-{p.Biome.ToString().ToLowerInvariant()}",
                Name = $"Generated {p.Biome}",
                Biome = p.Biome,
                Rows = grid.Select(r => new string(r)).ToList(),
            },
            tiles
        );

    /// <summary>
    /// The map's <c>e</c> tiles that are far enough from every <c>P</c>, picked greedily so they
    /// end up spread apart; short of <paramref name="count"/>, walkable tiles meeting the same
    /// rule fill the gap. May return fewer than asked on a map with no room.
    /// </summary>
    private static List<GridPos> ChooseEnemySpawns(TacticalMap map, int count, IRandom rng)
    {
        var distance = PathDistances(map, map.PlayerSpawns);
        bool Far(GridPos t) =>
            distance[t.Y * map.Width + t.X] is var d && d != Unreached && d >= MinSpawnDistance * 2;

        var chosen = new List<GridPos>();
        var fromMarkers = Shuffle(map.EnemySpawns.Where(Far).ToList(), rng);
        SpreadOut(chosen, fromMarkers, count);

        if (chosen.Count < count)
        {
            var open = new List<GridPos>();
            for (var y = 0; y < map.Height; y++)
            for (var x = 0; x < map.Width; x++)
            {
                var pos = new GridPos(x, y);
                if (
                    map[pos] is TileType.Floor or TileType.Road
                    && !map.PlayerSpawns.Contains(pos)
                    && !map.EvacTiles.Contains(pos)
                    && Far(pos)
                    && pos.X >= SpawnEdgeMargin
                    && pos.Y >= SpawnEdgeMargin
                    && pos.X < map.Width - SpawnEdgeMargin
                    && pos.Y < map.Height - SpawnEdgeMargin
                )
                    open.Add(pos);
            }
            SpreadOut(chosen, Shuffle(open, rng), count);
        }
        return chosen;
    }

    /// <summary>Adds candidates to <paramref name="chosen"/> one at a time, each the farthest from those already there (the first is simply the first candidate).</summary>
    private static void SpreadOut(List<GridPos> chosen, List<GridPos> candidates, int count)
    {
        candidates.RemoveAll(chosen.Contains);
        while (chosen.Count < count && candidates.Count > 0)
        {
            candidates.RemoveAll(c =>
                chosen.Any(o => SquaredDistance(o, c) < MinPodSpacing * MinPodSpacing)
            );
            if (candidates.Count == 0)
                break;
            var best = 0;
            if (chosen.Count > 0)
            {
                var bestScore = -1;
                for (var i = 0; i < candidates.Count; i++)
                {
                    var score = chosen.Min(c => SquaredDistance(c, candidates[i]));
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = i;
                    }
                }
            }
            chosen.Add(candidates[best]);
            candidates.RemoveAt(best);
        }
    }

    private static int SquaredDistance(GridPos a, GridPos b) =>
        (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

    private static List<GridPos> Shuffle(List<GridPos> list, IRandom rng)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }

    // dx, dy; the first four are orthogonal. Same rules as Pathfinder: no cutting corners.
    private static readonly int[] Dx = [0, 1, 0, -1, 1, 1, -1, -1];
    private static readonly int[] Dy = [1, 0, -1, 0, 1, -1, -1, 1];

    /// <summary>
    /// Shortest walking cost, in half-tiles, from the nearest of <paramref name="sources"/> to every
    /// tile (index y * width + x), or <see cref="Unreached"/>.
    /// </summary>
    private static int[] PathDistances(TacticalMap map, IEnumerable<GridPos> sources)
    {
        var width = map.Width;
        var cost = new int[width * map.Height];
        Array.Fill(cost, Unreached);
        var queue = new PriorityQueue<int, int>();
        foreach (var s in sources)
        {
            cost[s.Y * width + s.X] = 0;
            queue.Enqueue(s.Y * width + s.X, 0);
        }
        while (queue.TryDequeue(out var index, out var here))
        {
            if (here > cost[index])
                continue;
            int x = index % width,
                y = index / width;
            for (var d = 0; d < 8; d++)
            {
                var next = new GridPos(x + Dx[d], y + Dy[d]);
                if (!map.IsWalkable(next))
                    continue;
                var step = Pathfinder.OrthogonalCost;
                if (d >= 4)
                {
                    if (
                        !map.IsWalkable(new GridPos(next.X, y))
                        || !map.IsWalkable(new GridPos(x, next.Y))
                    )
                        continue;
                    step = Pathfinder.DiagonalCost;
                }
                var total = here + step;
                var n = next.Y * width + next.X;
                if (total >= cost[n])
                    continue;
                cost[n] = total;
                queue.Enqueue(n, total);
            }
        }
        return cost;
    }

    /// <summary>The walkable tiles connected to <paramref name="start"/>, indexed y * width + x.</summary>
    public static bool[] ReachableFrom(TacticalMap map, GridPos start)
    {
        var seen = new bool[map.Width * map.Height];
        if (!map.IsWalkable(start))
            return seen;
        var queue = new Queue<GridPos>();
        seen[start.Y * map.Width + start.X] = true;
        queue.Enqueue(start);
        while (queue.TryDequeue(out var pos))
            for (var d = 0; d < 4; d++)
            {
                var next = new GridPos(pos.X + Dx[d], pos.Y + Dy[d]);
                if (map.IsWalkable(next) && !seen[next.Y * map.Width + next.X])
                {
                    seen[next.Y * map.Width + next.X] = true;
                    queue.Enqueue(next);
                }
            }
        return seen;
    }

    /// <summary>
    /// All walkable tiles must form one region holding every <c>P</c>, <c>E</c>, civilian spot and
    /// enemy spawn, and every objective must touch it. Enemy spawns must also keep their distance from the squad.
    /// </summary>
    private static bool IsValid(
        TacticalMap map,
        MapGenParams p,
        IReadOnlyList<GridPos> spawns,
        IReadOnlyList<GridPos> civilians
    )
    {
        if (
            map.PlayerSpawns.Count == 0
            || map.EvacTiles.Count == 0
            || map.EnemySpawns.Count != p.PodCount
        )
            return false;
        if (spawns.Count != p.PodCount)
            return false;
        var reach = ReachableFrom(map, map.PlayerSpawns[0]);
        bool Reached(GridPos t) => reach[t.Y * map.Width + t.X];
        if (
            !map.PlayerSpawns.All(Reached)
            || !map.EvacTiles.All(Reached)
            || !map.EnemySpawns.All(Reached)
            || !civilians.All(Reached)
        )
            return false;
        // Blocks are single regions and their gates line up, so nothing should be cut off; check anyway.
        for (var i = 0; i < reach.Length; i++)
            if (!reach[i] && map.IsWalkable(new GridPos(i % map.Width, i / map.Width)))
                return false;
        if (p.Mission == GenMission.Sabotage && map.Objectives.Count == 0)
            return false;
        foreach (var o in map.Objectives)
            if (
                !Enumerable
                    .Range(0, 4)
                    .Any(d =>
                        map.InBounds(new GridPos(o.X + Dx[d], o.Y + Dy[d]))
                        && Reached(new GridPos(o.X + Dx[d], o.Y + Dy[d]))
                    )
            )
                return false;
        var distance = PathDistances(map, map.PlayerSpawns);
        return map.EnemySpawns.All(s => distance[s.Y * map.Width + s.X] >= MinSpawnDistance * 2);
    }
}
