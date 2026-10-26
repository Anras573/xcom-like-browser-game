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

    /// <summary>The HUD line for the current state (counters, timers); <see cref="Description"/> by default.</summary>
    string Describe(TacticalState state) => Description;

    /// <summary>True when the HUD line should be shown in warning red (the Sabotage timer).</summary>
    bool IsUrgent(TacticalState state) => false;

    /// <summary>How each objective, primary first, came out when the mission ended as <paramref name="outcome"/>.</summary>
    IReadOnlyList<ObjectiveOutcome> Outcomes(TacticalState state, MissionOutcome outcome) =>
        [new ObjectiveOutcome("primary", true, outcome == MissionOutcome.Success)];
}

/// <summary>Whether one objective of a mission was achieved; the campaign turns these into unrest and resources.</summary>
public sealed record ObjectiveOutcome(string Id, bool Primary, bool Achieved);

/// <summary>Purge: succeeds once no hostile unit is left alive (a captured Proxy that was evacuated is gone). Map objects don't count.</summary>
public class PurgeObjective(GameData data) : IMissionObjective
{
    public virtual string Description => "Purge: eliminate all hostiles.";

    public virtual ObjectiveStatus Evaluate(TacticalState state) =>
        state.Units.Any(u =>
            u is { Faction: Faction.Meridian, IsAlive: true, Evacuated: false }
            && data.Enemies[u.DefId].Kind == EnemyKind.Unit
        )
            ? ObjectiveStatus.InProgress
            : ObjectiveStatus.Success;
}
