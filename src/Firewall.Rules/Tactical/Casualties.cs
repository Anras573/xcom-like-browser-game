using Firewall.Rules.Data;
using Firewall.Rules.Rng;

namespace Firewall.Rules.Tactical;

/// <summary>
/// What happens when a unit reaches 0 HP (GDD §4.7). Soldiers roll for bleed-out and enemies die;
/// downed and evacuated units also drop their overwatch and hunker.
/// </summary>
public static class Casualties
{
    /// <summary>Environment phases a bleeding-out soldier has left when they go down.</summary>
    public const int BleedOutTurns = 3;

    /// <summary>40 + 10 × rank index (Rookie = 0, Captain = 4).</summary>
    public static int BleedOutChance(Rank rank) => 40 + 10 * (int)rank;

    /// <summary>
    /// Takes <paramref name="unit"/>, now at 0 HP, out of the fight. A soldier is
    /// <see cref="UnitBleedingOut"/> for <see cref="BleedOutTurns"/> on a successful roll, otherwise
    /// <see cref="UnitKilled"/>; anyone else is killed. Returns true when the unit died.
    /// </summary>
    public static bool Drop(TacticalUnit unit, IRandom rng, List<GameEvent> events)
    {
        if (unit.Faction == Faction.Firewall && rng.Roll(BleedOutChance(unit.Rank)))
        {
            unit.Statuses.BleedingOutTurnsLeft = BleedOutTurns;
            StandDown(unit, events);
            events.Add(new UnitBleedingOut(unit.Id, BleedOutTurns));
            return false;
        }
        unit.IsAlive = false;
        unit.Reactions.Clear();
        events.Add(new UnitKilled(unit.Id));
        return true;
    }

    /// <summary>
    /// For a unit that can no longer act (downed or evacuated): no AP, no reaction triggers, no
    /// overwatch or hunker.
    /// </summary>
    public static void StandDown(TacticalUnit unit, List<GameEvent> events)
    {
        if (unit.ActionPoints != 0)
        {
            unit.ActionPoints = 0;
            events.Add(new ApChanged(unit.Id, 0));
        }
        unit.Reactions.Clear();
        foreach (var flag in new[] { StatusFlags.Overwatch, StatusFlags.Hunkered })
        {
            if (!unit.Statuses.Has(flag))
                continue;
            unit.Statuses.Set(flag, false);
            events.Add(new StatusRemoved(unit.Id, flag));
        }
    }
}
