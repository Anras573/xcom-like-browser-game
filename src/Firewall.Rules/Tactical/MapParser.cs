using Firewall.Rules.Data;

namespace Firewall.Rules.Tactical;

/// <summary>A problem in a map file. <paramref name="Line"/> and <paramref name="Column"/> are 1-based.</summary>
public readonly record struct MapError(int Line, int Column, string Message)
{
    public override string ToString() => $"line {Line}, column {Column}: {Message}";
}

/// <summary>
/// The one place map text becomes a <see cref="TacticalMap"/> (also used for procedural blocks).
/// Glyphs come from the tile data; <c>P</c>, <c>e</c> and <c>E</c> are spawn markers on floor.
/// </summary>
public static class MapParser
{
    public const char PlayerSpawn = 'P';
    public const char EnemySpawn = 'e';
    public const char Evac = 'E';

    public static bool IsSpawnGlyph(char glyph) => glyph is PlayerSpawn or EnemySpawn or Evac;

    /// <summary>Parses a map; throws <see cref="InvalidDataException"/> listing every error.</summary>
    public static TacticalMap Parse(MapFile file, IReadOnlyList<TileDef> tiles) =>
        TryParse(file, tiles, out var map, out var errors)
            ? map!
            : throw new InvalidDataException(
                $"map '{file.Id}':\n" + string.Join('\n', errors.Select(e => e.ToString()))
            );

    public static bool TryParse(
        MapFile file,
        IReadOnlyList<TileDef> tiles,
        out TacticalMap? map,
        out IReadOnlyList<MapError> errors
    )
    {
        var list = new List<MapError>();
        errors = list;
        map = null;

        var rows = file.Rows;
        if (rows.Count == 0)
        {
            list.Add(new(1, 1, "map has no rows"));
            return false;
        }
        var width = rows[0].Length;
        if (width == 0)
        {
            list.Add(new(1, 1, "row is empty"));
            return false;
        }

        var byGlyph = new Dictionary<char, TileDef>();
        foreach (var def in tiles)
            if (def.Glyph.Length == 1)
                byGlyph.TryAdd(def.Glyph[0], def);

        var height = rows.Count;
        var grid = new TileType[width * height];
        var player = new List<GridPos>();
        var enemy = new List<GridPos>();
        var evac = new List<GridPos>();
        var objectives = new List<GridPos>();

        for (var r = 0; r < height; r++)
        {
            var row = rows[r];
            if (row.Length != width)
                list.Add(
                    new(
                        r + 1,
                        Math.Min(row.Length, width) + 1,
                        $"row has {row.Length} tiles, expected {width}"
                    )
                );
            // The file is written top row first, so text row r is y = Height - 1 - r.
            var y = height - 1 - r;
            for (var x = 0; x < Math.Min(row.Length, width); x++)
            {
                var glyph = row[x];
                var pos = new GridPos(x, y);
                var type = TileType.Floor;
                switch (glyph)
                {
                    case PlayerSpawn:
                        player.Add(pos);
                        break;
                    case EnemySpawn:
                        enemy.Add(pos);
                        break;
                    case Evac:
                        evac.Add(pos);
                        break;
                    default:
                        if (byGlyph.TryGetValue(glyph, out var def))
                        {
                            type = def.Type;
                            if (type == TileType.Objective)
                                objectives.Add(pos);
                        }
                        else
                            list.Add(new(r + 1, x + 1, $"unknown glyph '{glyph}'"));
                        break;
                }
                grid[y * width + x] = type;
            }
        }

        if (list.Count > 0)
            return false;
        map = new TacticalMap(
            file.Id,
            file.Name,
            file.Biome,
            width,
            height,
            grid,
            tiles,
            player,
            enemy,
            evac,
            objectives
        );
        return true;
    }
}
