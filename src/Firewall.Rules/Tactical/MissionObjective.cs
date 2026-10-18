using Firewall.Rules.Data;

namespace Firewall.Rules.Tactical;

public enum ObjectiveStatus
{
    InProgress,
    Success,
    Failure,
}

/// <summary>
/// The goal of a mission type (GDD §4.10). The engine evaluates it after every command and enemy
/// turn; the squad being wiped out or retreating fails any mission on top of it.
/// </summary>
public interface IMissionObjective
{
    /// <summary>One line for the HUD.</summary>
    string Description { get; }

    ObjectiveStatus Evaluate(TacticalState state);
}

/// <summary>Purge: succeeds once no hostile unit is left alive. Map objects don't count.</summary>
public sealed class PurgeObjective(GameData data) : IMissionObjective
{
    public string Description => "Purge: eliminate all hostiles.";

    public ObjectiveStatus Evaluate(TacticalState state) =>
        state.Units.Any(u =>
            u is { Faction: Faction.Meridian, IsAlive: true }
            && data.Enemies[u.DefId].Kind == EnemyKind.Unit
        )
            ? ObjectiveStatus.InProgress
            : ObjectiveStatus.Success;
}
