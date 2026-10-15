namespace Firewall.Rules.Tactical;

/// <summary>Puts the player's squad on the map.</summary>
public static class SquadDeployer
{
    /// <summary><see cref="TacticalUnit.DefId"/> of every soldier.</summary>
    public const string SoldierDefId = "soldier";

    /// <summary>
    /// Adds <paramref name="squad"/> to the state, in order, on the map's <c>P</c> tiles. When the
    /// squad is larger than the spawn list, the extra soldiers take the free walkable tiles nearest
    /// to a spawn; occupied tiles are never used.
    /// </summary>
    public static void Deploy(TacticalState state, IReadOnlyList<TacticalUnit> squad)
    {
        var spawns = state.Map.PlayerSpawns;
        if (spawns.Count == 0 && squad.Count > 0)
            throw new InvalidOperationException($"Map '{state.Map.Id}' has no player spawn.");

        for (var i = 0; i < squad.Count; i++)
        {
            var unit = squad[i];
            var anchor = spawns[i % spawns.Count];
            unit.Position =
                !state.IsOccupied(anchor) && state.Map.IsWalkable(anchor)
                    ? anchor
                    : NearestFree(state, anchor);
            state.AddUnit(unit);
        }
    }

    private static GridPos NearestFree(TacticalState state, GridPos anchor)
    {
        foreach (var pos in state.NearestFreeTiles(anchor))
            return pos;
        throw new InvalidOperationException("No free walkable tile left for a soldier.");
    }
}
