namespace Firewall.Rules.Tactical;

/// <summary>A fingerprint of a map's layout (FNV-1a), to compare generated maps.</summary>
public static class MapHash
{
    public static ulong Of(TacticalMap map)
    {
        var h = 14695981039346656037UL;
        void Add(int value)
        {
            for (var i = 0; i < 4; i++)
                h = unchecked((h ^ (byte)(value >> (i * 8))) * 1099511628211UL);
        }
        void AddAll(IReadOnlyList<GridPos> list)
        {
            Add(list.Count);
            foreach (var p in list)
            {
                Add(p.X);
                Add(p.Y);
            }
        }

        Add(map.Width);
        Add(map.Height);
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
            Add((int)map[new GridPos(x, y)]);
        AddAll(map.PlayerSpawns);
        AddAll(map.EnemySpawns);
        AddAll(map.EvacTiles);
        AddAll(map.Objectives);
        return h;
    }
}
