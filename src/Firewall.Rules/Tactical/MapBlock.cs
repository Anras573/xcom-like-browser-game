using System.Text.Json;
using System.Text.Json.Serialization;
using Firewall.Rules.Data;

namespace Firewall.Rules.Tactical;

/// <summary>What a block is for when <see cref="MapGenerator"/> assembles a map.</summary>
public enum BlockTag
{
    /// <summary>Holds the <c>P</c> and <c>E</c> tiles; placed on an edge of the grid.</summary>
    [JsonStringEnumMemberName("spawn-player")]
    SpawnPlayer,

    /// <summary>Holds an <c>O</c> tile: the relay node, or the spots civilians stand on.</summary>
    [JsonStringEnumMemberName("objective")]
    Objective,

    [JsonStringEnumMemberName("generic")]
    Generic,

    /// <summary>Filler with extra <c>e</c> spawn tiles; used like <see cref="Generic"/>.</summary>
    [JsonStringEnumMemberName("spawn-enemy")]
    SpawnEnemy,
}

/// <summary>The JSON shape of <c>data/blocks/&lt;biome&gt;/*.json</c>: a 10x10 piece of map, rows top first.</summary>
public sealed record MapBlockFile
{
    [JsonRequired]
    public string Id { get; init; } = "";

    [JsonRequired]
    public Biome Biome { get; init; }

    /// <summary>Width and height in tiles; always <see cref="BlockLibrary.BlockSize"/> square.</summary>
    [JsonRequired]
    public IReadOnlyList<int> Size { get; init; } = [];

    private IReadOnlyList<BlockTag>? _tags;

    public IReadOnlyList<BlockTag> Tags
    {
        get => _tags ?? [];
        init => _tags = value;
    }

    /// <summary>Relative chance among the blocks of the same biome and tag.</summary>
    [JsonRequired]
    public double Weight { get; init; }

    private IReadOnlyList<string>? _rows;

    [JsonRequired]
    public IReadOnlyList<string> Rows
    {
        get => _rows ?? [];
        init => _rows = value;
    }
}

/// <summary>Rotations and mirroring of block rows (text, top row first), so one block yields up to 8 layouts.</summary>
public static class BlockTransform
{
    /// <summary>Turns the rows 90 degrees clockwise; the new width is the old height.</summary>
    public static IReadOnlyList<string> RotateClockwise(IReadOnlyList<string> rows)
    {
        var height = rows.Count;
        var width = height == 0 ? 0 : rows[0].Length;
        var result = new string[width];
        for (var i = 0; i < width; i++)
        {
            var chars = new char[height];
            for (var j = 0; j < height; j++)
                chars[j] = rows[height - 1 - j][i];
            result[i] = new string(chars);
        }
        return result;
    }

    /// <summary>Flips the rows left to right.</summary>
    public static IReadOnlyList<string> Mirror(IReadOnlyList<string> rows) =>
        rows.Select(r => new string(r.Reverse().ToArray())).ToList();

    /// <summary>Mirrors if asked, then rotates clockwise <paramref name="quarterTurns"/> times (0-3).</summary>
    public static IReadOnlyList<string> Apply(
        IReadOnlyList<string> rows,
        int quarterTurns,
        bool mirror
    )
    {
        var result = mirror ? Mirror(rows) : rows;
        for (var i = 0; i < ((quarterTurns % 4) + 4) % 4; i++)
            result = RotateClockwise(result);
        return result;
    }
}

/// <summary>Every authored block, with the checks that keep them usable by the generator.</summary>
public sealed class BlockLibrary
{
    public const int BlockSize = 10;

    /// <summary>Walkable tiles each edge of a block must have so blocks connect.</summary>
    public const int MinEdgeWalkable = 4;

    private readonly List<MapBlockFile> _blocks;

    public BlockLibrary(IEnumerable<MapBlockFile> blocks) => _blocks = blocks.ToList();

    public static BlockLibrary Empty { get; } = new([]);

    public IReadOnlyList<MapBlockFile> All => _blocks;

    /// <summary>Blocks of a biome that carry a tag, in file order.</summary>
    public IReadOnlyList<MapBlockFile> Find(Biome biome, BlockTag tag) =>
        _blocks.Where(b => b.Biome == biome && b.Tags.Contains(tag)).ToList();

    /// <summary>Parses the block files (see <see cref="GameDataFiles.Blocks"/>); throws on malformed JSON.</summary>
    public static BlockLibrary Load(Func<string, string> readFile, IEnumerable<string> files)
    {
        var blocks = new List<MapBlockFile>();
        foreach (var file in files)
        {
            try
            {
                blocks.Add(
                    JsonSerializer.Deserialize(
                        readFile(file),
                        RulesJsonContext.Default.MapBlockFile
                    ) ?? throw new InvalidDataException("file is empty")
                );
            }
            catch (Exception e) when (e is JsonException or InvalidDataException)
            {
                throw new InvalidDataException($"{file}: {e.Message}", e);
            }
        }
        return new BlockLibrary(blocks);
    }

    /// <summary>
    /// Checks every block: unique ids, size, known glyphs, tags, border walkability, that spawn
    /// blocks hold <c>P</c> and <c>E</c> and objective blocks an <c>O</c>, and that everything in
    /// the block can be walked to from its border. Empty means all fine.
    /// </summary>
    public IReadOnlyList<string> Validate(IReadOnlyList<TileDef> tiles)
    {
        var errors = new List<string>();
        var walkable = new Dictionary<char, bool>();
        foreach (var def in tiles)
            if (def.Glyph.Length == 1)
                walkable.TryAdd(def.Glyph[0], def.Walkable);
        foreach (var glyph in new[] { MapParser.PlayerSpawn, MapParser.EnemySpawn, MapParser.Evac })
            walkable[glyph] = true;

        foreach (var dup in _blocks.GroupBy(b => b.Id).Where(g => g.Count() > 1))
            errors.Add($"block '{dup.Key}': duplicate id");
        foreach (var b in _blocks)
            ValidateBlock(b, walkable, errors);
        return errors;
    }

    private static void ValidateBlock(
        MapBlockFile b,
        Dictionary<char, bool> walkable,
        List<string> errors
    )
    {
        void Err(string message) => errors.Add($"block '{b.Id}': {message}");

        if (b.Id.Length == 0)
            Err("empty id");
        if (b.Weight <= 0)
            Err("weight must be positive");
        if (b.Tags.Count == 0)
            Err("no tags");
        if (b.Size.Count != 2 || b.Size[0] != BlockSize || b.Size[1] != BlockSize)
            Err($"size must be [{BlockSize},{BlockSize}]");
        if (b.Rows.Count != BlockSize || b.Rows.Any(r => r.Length != BlockSize))
        {
            Err($"rows must be {BlockSize}x{BlockSize}");
            return;
        }
        var unknown = b.Rows.SelectMany(r => r).Distinct().Where(g => !walkable.ContainsKey(g));
        foreach (var glyph in unknown)
            Err($"unknown glyph '{glyph}'");
        if (unknown.Any())
            return;

        bool Open(int x, int y) => walkable[b.Rows[y][x]];
        var last = BlockSize - 1;
        var edges = new (string Name, Func<int, bool> Tile)[]
        {
            ("top", i => Open(i, 0)),
            ("bottom", i => Open(i, last)),
            ("left", i => Open(0, i)),
            ("right", i => Open(last, i)),
        };
        foreach (var (name, tile) in edges)
        {
            var open = Enumerable.Range(0, BlockSize).Count(tile);
            if (open < MinEdgeWalkable)
                Err($"{name} edge has {open} walkable tiles, needs {MinEdgeWalkable}");
        }

        var all = b.Rows.SelectMany(r => r).ToList();
        if (b.Tags.Contains(BlockTag.SpawnPlayer))
        {
            if (!all.Contains(MapParser.PlayerSpawn))
                Err("spawn-player block has no 'P'");
            if (!all.Contains(MapParser.Evac))
                Err("spawn-player block has no 'E'");
        }
        if (b.Tags.Contains(BlockTag.Objective) && !all.Contains('O'))
            Err("objective block has no 'O'");

        // One region: a pocket cut off from the rest would leave unreachable tiles on the map.
        var regions = CountRegions(b.Rows, walkable);
        if (regions > 1)
            Err(
                $"walkable tiles form {regions} separate regions, blocks must be one connected area"
            );

        // Everything meant to be reached must connect to the border, or blocks would seal it off.
        var reach = FloodFromBorder(b.Rows, walkable);
        for (var y = 0; y < BlockSize; y++)
        for (var x = 0; x < BlockSize; x++)
        {
            var glyph = b.Rows[y][x];
            if (glyph is 'P' or 'e' or 'E' && !reach[y * BlockSize + x])
                Err($"'{glyph}' at column {x + 1}, row {y + 1} cannot be reached from the border");
            if (glyph == 'O' && !OrthogonalNeighbours(x, y).Any(n => reach[n.Y * BlockSize + n.X]))
                Err($"'O' at column {x + 1}, row {y + 1} has no reachable neighbour");
        }
    }

    private static IEnumerable<(int X, int Y)> OrthogonalNeighbours(int x, int y)
    {
        if (x > 0)
            yield return (x - 1, y);
        if (x < BlockSize - 1)
            yield return (x + 1, y);
        if (y > 0)
            yield return (x, y - 1);
        if (y < BlockSize - 1)
            yield return (x, y + 1);
    }

    private static int CountRegions(IReadOnlyList<string> rows, Dictionary<char, bool> walkable)
    {
        var seen = new bool[BlockSize * BlockSize];
        var regions = 0;
        for (var y = 0; y < BlockSize; y++)
        for (var x = 0; x < BlockSize; x++)
        {
            if (seen[y * BlockSize + x] || !walkable[rows[y][x]])
                continue;
            regions++;
            var queue = new Queue<(int X, int Y)>();
            seen[y * BlockSize + x] = true;
            queue.Enqueue((x, y));
            while (queue.TryDequeue(out var p))
                foreach (var n in OrthogonalNeighbours(p.X, p.Y))
                    if (!seen[n.Y * BlockSize + n.X] && walkable[rows[n.Y][n.X]])
                    {
                        seen[n.Y * BlockSize + n.X] = true;
                        queue.Enqueue(n);
                    }
        }
        return regions;
    }

    private static bool[] FloodFromBorder(
        IReadOnlyList<string> rows,
        Dictionary<char, bool> walkable
    )
    {
        var seen = new bool[BlockSize * BlockSize];
        var queue = new Queue<(int X, int Y)>();
        for (var y = 0; y < BlockSize; y++)
        for (var x = 0; x < BlockSize; x++)
            if (
                (x == 0 || y == 0 || x == BlockSize - 1 || y == BlockSize - 1)
                && walkable[rows[y][x]]
            )
            {
                seen[y * BlockSize + x] = true;
                queue.Enqueue((x, y));
            }
        while (queue.TryDequeue(out var p))
            foreach (var n in OrthogonalNeighbours(p.X, p.Y))
                if (!seen[n.Y * BlockSize + n.X] && walkable[rows[n.Y][n.X]])
                {
                    seen[n.Y * BlockSize + n.X] = true;
                    queue.Enqueue(n);
                }
        return seen;
    }
}
