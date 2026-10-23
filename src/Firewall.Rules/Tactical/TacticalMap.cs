using Firewall.Rules.Data;

namespace Firewall.Rules.Tactical;

/// <summary>A tile that changed through <see cref="TacticalMap.SetTile"/>.</summary>
public readonly record struct TileChange(GridPos Pos, TileType Old, TileType New);

/// <summary>
/// The tactical grid. Coordinates are <see cref="GridPos"/> with +Y up; <c>(0, 0)</c> is the
/// bottom-left tile. Tile properties come from <see cref="TileDef"/> data.
/// </summary>
public sealed class TacticalMap
{
    private readonly TileType[] _tiles; // index = y * Width + x
    private readonly TileDef[] _defs; // indexed by (int)TileType
    private readonly bool[] _blocksLos; // same index as _tiles; line-of-sight traces read it a lot

    public TacticalMap(
        string id,
        string name,
        Biome biome,
        int width,
        int height,
        TileType[] tiles,
        IReadOnlyList<TileDef> defs,
        IReadOnlyList<GridPos> playerSpawns,
        IReadOnlyList<GridPos> enemySpawns,
        IReadOnlyList<GridPos> evacTiles,
        IReadOnlyList<GridPos> objectives
    )
    {
        if (tiles.Length != width * height)
            throw new ArgumentException("Tile array does not match width * height.", nameof(tiles));
        Id = id;
        Name = name;
        Biome = biome;
        Width = width;
        Height = height;
        _tiles = tiles;
        _defs = new TileDef[Enum.GetValues<TileType>().Length];
        foreach (var def in defs)
            _defs[(int)def.Type] = def;
        _blocksLos = new bool[tiles.Length];
        for (var i = 0; i < tiles.Length; i++)
            _blocksLos[i] = _defs[(int)tiles[i]].BlocksLos;
        PlayerSpawns = playerSpawns;
        EnemySpawns = enemySpawns;
        EvacTiles = evacTiles;
        Objectives = objectives;
    }

    public string Id { get; }
    public string Name { get; }
    public Biome Biome { get; }
    public int Width { get; }
    public int Height { get; }

    /// <summary><c>P</c> glyphs, in file order (top row first, left to right).</summary>
    public IReadOnlyList<GridPos> PlayerSpawns { get; }

    /// <summary><c>e</c> glyphs: enemy pod spawn points.</summary>
    public IReadOnlyList<GridPos> EnemySpawns { get; }

    /// <summary><c>E</c> glyphs: the evac zone.</summary>
    public IReadOnlyList<GridPos> EvacTiles { get; }

    /// <summary><c>O</c> glyphs; the tiles stay <see cref="TileType.Objective"/>.</summary>
    public IReadOnlyList<GridPos> Objectives { get; }

    /// <summary>Counts <see cref="SetTile"/> changes; <see cref="TacticalState.MapVersion"/> is this.</summary>
    public int Version { get; private set; }

    /// <summary>Raised by <see cref="SetTile"/>, e.g. when a tile is destroyed.</summary>
    public event Action<TileChange>? TileChanged;

    public TileType this[GridPos pos] =>
        InBounds(pos)
            ? _tiles[pos.Y * Width + pos.X]
            : throw new ArgumentOutOfRangeException(nameof(pos), pos, "Outside the map.");

    public bool InBounds(GridPos pos) =>
        pos.X >= 0 && pos.X < Width && pos.Y >= 0 && pos.Y < Height;

    public TileDef Def(TileType type) => _defs[(int)type];

    /// <summary>False outside the map.</summary>
    public bool IsWalkable(GridPos pos) => InBounds(pos) && Def(this[pos]).Walkable;

    /// <summary>The cover a tile gives; none outside the map.</summary>
    public CoverLevel CoverAt(GridPos pos) =>
        InBounds(pos) ? Def(this[pos]).Cover : CoverLevel.None;

    /// <summary>True outside the map, so rays stop at its edge.</summary>
    public bool BlocksLos(GridPos pos) => !InBounds(pos) || _blocksLos[pos.Y * Width + pos.X];

    /// <summary><see cref="BlocksLos(GridPos)"/> without building a <see cref="GridPos"/>; the hot path of line-of-sight traces.</summary>
    internal bool BlocksLos(int x, int y) =>
        x < 0 || y < 0 || x >= Width || y >= Height || _blocksLos[y * Width + x];

    /// <summary>Replaces a tile and raises <see cref="TileChanged"/> if it differs.</summary>
    public void SetTile(GridPos pos, TileType type)
    {
        var old = this[pos];
        if (old == type)
            return;
        _tiles[pos.Y * Width + pos.X] = type;
        _blocksLos[pos.Y * Width + pos.X] = Def(type).BlocksLos;
        Version++;
        TileChanged?.Invoke(new TileChange(pos, old, type));
    }
}
