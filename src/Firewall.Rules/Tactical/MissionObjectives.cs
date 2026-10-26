using Firewall.Rules.Data;

namespace Firewall.Rules.Tactical;

/// <summary>
/// Rescue (GDD §4.10): evacuate at least <paramref name="required"/> civilians, or have that many
/// still alive once no hostile is left. It fails as soon as too few can still be saved.
/// </summary>
public sealed class RescueObjective(GameData data, int required)
    : PurgeObjective(data),
        IMissionObjective
{
    public int Required { get; } = required;

    public override string Description => $"Rescue: evacuate {Required} civilians.";

    public string Describe(TacticalState state) =>
        $"Rescue {Required}: Civilians: {Civilians.Saved(state)} saved / {Civilians.Lost(state)} lost / {Civilians.Remaining(state)} remaining";

    public override ObjectiveStatus Evaluate(TacticalState state)
    {
        var saved = Civilians.Saved(state);
        if (saved >= Required)
            return ObjectiveStatus.Success;
        if (saved + Civilians.Remaining(state) < Required)
            return ObjectiveStatus.Failure;
        return base.Evaluate(state) == ObjectiveStatus.Success
            ? ObjectiveStatus.Success
            : ObjectiveStatus.InProgress;
    }
}

/// <summary>
/// Sabotage (GDD §4.10): destroy every Relay Node within <paramref name="turnLimit"/> turns, then
/// kill all hostiles or evacuate. The timer ticks in the environment phase; when it runs out the
/// mission fails.
/// </summary>
public sealed class SabotageObjective(GameData data, int turnLimit)
    : PurgeObjective(data),
        IMissionObjective
{
    public const string RelayId = "relay_node";

    /// <summary>The HUD timer turns red at this many turns left or fewer.</summary>
    public const int UrgentTurns = 2;

    private readonly GameData _data = data;

    public int TurnLimit { get; } = turnLimit;

    public override string Description =>
        $"Sabotage: destroy the Relay Node within {TurnLimit} turns.";

    public static bool RelayDestroyed(TacticalState state) =>
        state.Units.Any(u => u.DefId == RelayId)
        && !state.Units.Any(u => u.DefId == RelayId && u.IsAlive);

    /// <summary>Turns left to destroy the relay, counting the current one; 0 once it is too late.</summary>
    public int TurnsLeft(TacticalState state) =>
        Math.Max(
            0,
            TurnLimit - state.TurnNumber + 1 - (state.Phase == TacticalPhase.Environment ? 1 : 0)
        );

    public string Describe(TacticalState state) =>
        RelayDestroyed(state)
            ? "Sabotage: Relay destroyed. Evacuate or kill all hostiles."
            : $"Sabotage: destroy the Relay Node - {TurnsLeft(state)} {(TurnsLeft(state) == 1 ? "turn" : "turns")} left";

    public bool IsUrgent(TacticalState state) =>
        !RelayDestroyed(state) && TurnsLeft(state) <= UrgentTurns;

    public override ObjectiveStatus Evaluate(TacticalState state)
    {
        if (!RelayDestroyed(state))
            return TurnsLeft(state) == 0 ? ObjectiveStatus.Failure : ObjectiveStatus.InProgress;
        if (base.Evaluate(state) == ObjectiveStatus.Success)
            return ObjectiveStatus.Success;
        // The squad got out: someone evacuated and nobody is left to fight.
        var soldiers = state.Units.Where(u => u.Faction == Faction.Firewall).ToList();
        return
            soldiers.Any(u => u is { IsAlive: true, Evacuated: true })
            && !soldiers.Any(u => u.IsStanding)
            ? ObjectiveStatus.Success
            : ObjectiveStatus.InProgress;
    }
}

/// <summary>
/// Capture (GDD §4.10): the primary goal is Purge; the bonus is a Proxy taken out alive (stunned,
/// carried and evacuated).
/// </summary>
public sealed class CaptureObjective(GameData data) : PurgeObjective(data), IMissionObjective
{
    public const string BonusId = "capture_proxy";

    public override string Description => "Purge: eliminate all hostiles. Bonus: capture a Proxy.";

    public static bool ProxyCaptured(TacticalState state) =>
        state.Units.Any(u =>
            u is { Faction: Faction.Meridian, IsAlive: true, Evacuated: true }
            && u.Statuses.Capturable
        );

    /// <summary>The HUD line: <paramref name="purge"/> (the hostile count) plus the bonus with its tick.</summary>
    public string Describe(TacticalState state, string purge) =>
        $"{purge}  Bonus: capture a Proxy {(ProxyCaptured(state) ? "✓" : "✗")}";

    public string Describe(TacticalState state) =>
        Describe(state, "Purge: eliminate all hostiles.");

    public IReadOnlyList<ObjectiveOutcome> Outcomes(TacticalState state, MissionOutcome outcome) =>
        [
            new ObjectiveOutcome("primary", true, outcome == MissionOutcome.Success),
            new ObjectiveOutcome(BonusId, false, ProxyCaptured(state)),
        ];
}
