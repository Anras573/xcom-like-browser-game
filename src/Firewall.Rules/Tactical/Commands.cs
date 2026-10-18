namespace Firewall.Rules.Tactical;

/// <summary>Outcome of <see cref="TacticalEngine.Validate"/>; the reason is shown in UI tooltips.</summary>
public readonly record struct ValidationResult(bool IsValid, string Reason)
{
    public static ValidationResult Ok { get; } = new(true, "");

    public static ValidationResult Fail(string reason) => new(false, reason);
}

/// <summary>What an ability is aimed at: nothing, a unit or a tile.</summary>
public abstract record Target
{
    public static Target None { get; } = new NoTarget();

    public static Target Unit(UnitId id) => new UnitTarget(id);

    public static Target Tile(GridPos pos) => new TileTarget(pos);
}

public sealed record NoTarget : Target;

public sealed record UnitTarget(UnitId Id) : Target;

public sealed record TileTarget(GridPos Pos) : Target;

/// <summary>A request to change tactical state. Presentation and AI send these to the engine.</summary>
public interface ICommand;

public sealed record MoveCommand(UnitId UnitId, GridPos Dest) : ICommand;

public sealed record UseAbilityCommand(UnitId UnitId, string AbilityId, Target Target) : ICommand;

public sealed record EndTurnCommand : ICommand;

/// <summary>
/// Abort Mission: every soldier who can act and stands on an evac tile leaves; everyone else on
/// the map is left behind (MIA). The mission ends <see cref="MissionOutcome.Aborted"/>.
/// </summary>
public sealed record AbortMissionCommand : ICommand;
