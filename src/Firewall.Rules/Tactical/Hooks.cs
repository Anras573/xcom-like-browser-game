namespace Firewall.Rules.Tactical;

public enum StepResult
{
    Continue,
    Interrupt,
}

/// <summary>What a step hook sees after a unit enters a tile.</summary>
public sealed record StepContext(
    TacticalState State,
    TacticalUnit Mover,
    GridPos From,
    GridPos To,
    List<GameEvent> Events
);

/// <summary>
/// Runs after every step of a move: reaction fire, pod activation, visibility. Hooks append
/// events and may interrupt the move. A mover that dies, goes down or is stunned always stops.
/// </summary>
public interface IStepHook
{
    StepResult OnUnitStepped(StepContext ctx);
}

/// <summary>Plays the enemy phase. Uses the same commands as the player, so the same validation.</summary>
public interface IAiController
{
    void TakeTurn(TacticalUnit unit, AiContext ctx);
}

/// <summary>The AI's way into the engine: validate and execute commands.</summary>
public sealed class AiContext(TacticalEngine engine, List<GameEvent> sink)
{
    public TacticalState State => engine.State;

    public ValidationResult Validate(ICommand cmd) => engine.Validate(cmd);

    /// <summary>Where <paramref name="unit"/> can walk right now, planned the way a move will run.</summary>
    public MoveZones MoveRange(TacticalUnit unit) => engine.MoveRange(unit);

    /// <summary>Runs the command (it throws if invalid); its events join the enemy phase's events.</summary>
    public IReadOnlyList<GameEvent> Execute(ICommand cmd) => engine.ExecuteInternal(cmd, sink);
}

/// <summary>The "do nothing" AI: for tests and tools that want a still enemy phase. The game plays <see cref="UtilityAi"/>.</summary>
public sealed class NullAiController : IAiController
{
    public void TakeTurn(TacticalUnit unit, AiContext ctx) { }
}
