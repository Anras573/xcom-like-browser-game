using Firewall.Rules.Data;
using Firewall.Rules.Rng;

namespace Firewall.Rules.Tactical;

/// <summary>
/// The single entry point for tactical state changes: commands in, events out. Presentation and
/// the AI both go through <see cref="Validate"/> and <see cref="Execute"/>.
/// </summary>
public sealed class TacticalEngine
{
    public const int ApPerTurn = 2;

    private readonly GameData _data;
    private readonly IAiController _ai;
    private readonly AbilityRegistry _abilities;

    public TacticalEngine(
        TacticalState state,
        GameData data,
        IAiController? ai = null,
        AbilityRegistry? abilities = null
    )
    {
        State = state;
        _data = data;
        _ai = ai ?? new NullAiController();
        _abilities = abilities ?? new AbilityRegistry();
        Log = new CommandLog(state.Rng is Pcg32Random pcg ? pcg.GetState().Seed : 0UL);
        State.RefreshVision();
    }

    public TacticalState State { get; }

    public CommandLog Log { get; }

    /// <summary>Hooks run after each step of a move, in order.</summary>
    public List<IStepHook> StepHooks { get; } = [];

    /// <summary>True when no soldier can act any more (the "all soldiers exhausted" signal).</summary>
    public bool AllSoldiersExhausted =>
        State is { Phase: TacticalPhase.Player, Result: null }
        && State.Units.Any(IsOperationalSoldier)
        && !State.Units.Any(u => IsOperationalSoldier(u) && u.ActionPoints > 0);

    /// <summary>
    /// Replays <paramref name="commands"/> on a fresh state from <paramref name="createState"/>;
    /// the result's state hash must equal the original run's.
    /// </summary>
    public static TacticalEngine Replay(
        ulong seed,
        IEnumerable<ICommand> commands,
        Func<ulong, TacticalState> createState,
        GameData data,
        IAiController? ai = null,
        AbilityRegistry? abilities = null,
        IEnumerable<IStepHook>? hooks = null
    )
    {
        var engine = new TacticalEngine(createState(seed), data, ai, abilities);
        if (hooks is not null)
            engine.StepHooks.AddRange(hooks);
        foreach (var cmd in commands)
            engine.Execute(cmd);
        return engine;
    }

    public ValidationResult Validate(ICommand cmd)
    {
        if (State.Result is not null)
            return ValidationResult.Fail("The mission is over.");
        return cmd switch
        {
            EndTurnCommand => State.Phase == TacticalPhase.Player
                ? ValidationResult.Ok
                : ValidationResult.Fail("It is not the player phase."),
            MoveCommand m => ValidateMove(m),
            UseAbilityCommand a => ValidateAbility(a),
            _ => ValidationResult.Fail($"Unknown command {cmd.GetType().Name}."),
        };
    }

    public IReadOnlyList<GameEvent> Execute(ICommand cmd)
    {
        var events = new List<GameEvent>();
        ExecuteInternal(cmd, events, log: true);
        return events;
    }

    public IReadOnlyList<GameEvent> EndPlayerPhase() => Execute(new EndTurnCommand());

    internal IReadOnlyList<GameEvent> ExecuteInternal(
        ICommand cmd,
        List<GameEvent> sink,
        bool log = false
    )
    {
        var check = Validate(cmd);
        if (!check.IsValid)
            throw new InvalidOperationException($"Invalid command {cmd}: {check.Reason}");
        if (log)
            Log.Add(cmd);

        var start = sink.Count;
        var wasExhausted = AllSoldiersExhausted;
        switch (cmd)
        {
            case EndTurnCommand:
                RunPhases(sink);
                break;
            case MoveCommand m:
                ExecuteMove(m, sink);
                break;
            case UseAbilityCommand a:
                ExecuteAbility(a, sink);
                break;
        }
        // Tile changes and ability effects can alter what is seen; steps already refreshed.
        if (State.Result is null && State.RefreshVision() is { } vision)
            sink.Add(vision);
        CheckMissionEnd(sink);
        if (!wasExhausted && AllSoldiersExhausted)
            sink.Add(new AllSoldiersExhausted());
        return sink.GetRange(start, sink.Count - start);
    }

    // ------------------------------------------------------------------ validation

    private static bool IsOperationalSoldier(TacticalUnit u) =>
        u.Faction == Faction.Firewall && u.IsAlive && !u.Statuses.IsBleedingOut;

    private ValidationResult ValidateActor(UnitId id, out TacticalUnit unit)
    {
        unit = null!;
        var found = State.Units.FirstOrDefault(u => u.Id == id);
        if (found is null)
            return ValidationResult.Fail("No such unit.");
        unit = found;
        if (!unit.IsAlive)
            return ValidationResult.Fail($"{unit.DisplayName} is dead.");
        var phaseFaction = State.Phase switch
        {
            TacticalPhase.Player => Faction.Firewall,
            TacticalPhase.Enemy => Faction.Meridian,
            _ => (Faction?)null,
        };
        if (phaseFaction is null || unit.Faction != phaseFaction)
            return ValidationResult.Fail("It is not this unit's phase.");
        if (!unit.IsActive)
            return ValidationResult.Fail($"{unit.DisplayName} is inactive.");
        if (unit.Statuses.IsBleedingOut)
            return ValidationResult.Fail($"{unit.DisplayName} is bleeding out.");
        if (unit.Statuses.Stunned)
            return ValidationResult.Fail($"{unit.DisplayName} is stunned.");
        return ValidationResult.Ok;
    }

    private static ValidationResult CheckAp(TacticalUnit unit, int cost) =>
        unit.ActionPoints >= cost
            ? ValidationResult.Ok
            : ValidationResult.Fail(
                unit.ActionPoints == 0
                    ? $"{unit.DisplayName} has no action points left."
                    : $"Not enough action points (needs {cost}, has {unit.ActionPoints})."
            );

    private ValidationResult ValidateMove(MoveCommand cmd)
    {
        var actor = ValidateActor(cmd.UnitId, out var unit);
        if (!actor.IsValid)
            return actor;
        var ap = CheckAp(unit, 1);
        if (!ap.IsValid)
            return ap;
        if (cmd.Dest == unit.Position)
            return ValidationResult.Fail("The unit is already there.");
        if (!State.Map.InBounds(cmd.Dest) || !State.Map.IsWalkable(cmd.Dest))
            return ValidationResult.Fail("That tile cannot be walked on.");
        if (State.IsOccupied(cmd.Dest))
            return ValidationResult.Fail("That tile is occupied.");
        var reachable = Pathfinder.Reachable(State, unit, unit.Mobility * 4);
        if (
            reachable.CostTo(cmd.Dest) is not { } pathCost
            || Pathfinder.ApCost(unit.Mobility, pathCost) is not { } cost
        )
            return ValidationResult.Fail("That tile is out of range.");
        return CheckAp(unit, cost);
    }

    private ValidationResult ValidateAbility(UseAbilityCommand cmd)
    {
        var actor = ValidateActor(cmd.UnitId, out var unit);
        if (!actor.IsValid)
            return actor;
        if (!unit.Abilities.Contains(cmd.AbilityId))
            return ValidationResult.Fail($"{unit.DisplayName} does not have that ability.");
        if (!_data.Abilities.TryGet(cmd.AbilityId, out var def))
            return ValidationResult.Fail($"Unknown ability '{cmd.AbilityId}'.");
        if (def.Targeting == TargetingType.Passive)
            return ValidationResult.Fail($"{def.Name} is passive.");
        if (unit.Cooldowns.TryGetValue(def.Id, out var cooldown) && cooldown > 0)
            return ValidationResult.Fail($"{def.Name} is on cooldown ({cooldown} turns).");
        var ap = CheckAp(unit, def.ApCost);
        if (!ap.IsValid)
            return ap;
        var shape = ValidateTargetShape(def, cmd.Target, unit);
        if (!shape.IsValid)
            return shape;
        if (!_abilities.TryGet(def.Id, out var handler))
            return ValidationResult.Fail($"{def.Name} is not implemented yet.");
        // A throwaway stream: validation runs every frame for UI reasons and must not advance the RNG.
        return handler.Validate(MakeContext(unit, cmd.Target, def, State.Rng.Fork("validate"), []));
    }

    private ValidationResult ValidateTargetShape(AbilityDef def, Target target, TacticalUnit user)
    {
        switch (def.Targeting)
        {
            case TargetingType.Self:
                return target is NoTarget || target is UnitTarget(var self) && self == user.Id
                    ? ValidationResult.Ok
                    : ValidationResult.Fail($"{def.Name} needs no target.");
            case TargetingType.Enemy:
            case TargetingType.Ally:
                if (target is not UnitTarget t)
                    return ValidationResult.Fail($"{def.Name} needs a unit target.");
                var other = State.Units.FirstOrDefault(u => u.Id == t.Id);
                if (other is null || !other.IsAlive)
                    return ValidationResult.Fail("That target is not valid.");
                var friendly = other.Faction == user.Faction;
                if (def.Targeting == TargetingType.Enemy && friendly)
                    return ValidationResult.Fail($"{def.Name} needs an enemy target.");
                if (def.Targeting == TargetingType.Ally && !friendly)
                    return ValidationResult.Fail($"{def.Name} needs an allied target.");
                return ValidationResult.Ok;
            case TargetingType.Tile:
                return target is TileTarget tile && State.Map.InBounds(tile.Pos)
                    ? ValidationResult.Ok
                    : ValidationResult.Fail($"{def.Name} needs a tile target.");
            default:
                return ValidationResult.Fail($"{def.Name} cannot be used.");
        }
    }

    private AbilityContext MakeContext(
        TacticalUnit unit,
        Target target,
        AbilityDef def,
        IRandom rng,
        List<GameEvent> events
    ) => new(State, unit, target, _data, def, rng, events);

    // ------------------------------------------------------------------ execution

    private void ExecuteAbility(UseAbilityCommand cmd, List<GameEvent> events)
    {
        var unit = State.Unit(cmd.UnitId);
        var def = _data.Abilities[cmd.AbilityId];
        _abilities.TryGet(def.Id, out var handler);

        SpendAp(unit, def.EndsTurn ? unit.ActionPoints : def.ApCost, events);
        if (def.Cooldown > 0)
            unit.Cooldowns[def.Id] = def.Cooldown;
        events.Add(new AbilityUsed(unit.Id, def.Id, cmd.Target));
        // The state's own stream, so every use rolls differently and the state hash covers it.
        handler.Execute(MakeContext(unit, cmd.Target, def, State.Rng, events));
    }

    private static void SpendAp(TacticalUnit unit, int amount, List<GameEvent> events)
    {
        if (amount == 0)
            return;
        unit.ActionPoints -= amount;
        events.Add(new ApChanged(unit.Id, unit.ActionPoints));
    }

    private void ExecuteMove(MoveCommand cmd, List<GameEvent> events)
    {
        var unit = State.Unit(cmd.UnitId);
        var reachable = Pathfinder.Reachable(State, unit, unit.Mobility * 4);
        var path = reachable.PathTo(cmd.Dest)!;
        var cost = Pathfinder.ApCost(unit.Mobility, reachable.CostTo(cmd.Dest)!.Value)!.Value;
        var dashed = cost == 2;
        SpendAp(unit, cost, events);
        unit.MovedThisTurn = true;

        // The move is cut into segments wherever a hook added events, so playback can show the
        // mover at the right tile when (say) a reaction shot goes off.
        var segment = new List<GridPos> { unit.Position };
        for (var i = 1; i < path.Count; i++)
        {
            var from = unit.Position;
            var step = path[i];
            unit.Position = step;
            segment.Add(step);

            var stepEvents = new List<GameEvent>();
            // Before the hooks, so they (reaction fire, pod activation) see the new view.
            if (State.RefreshVision() is { } vision)
                stepEvents.Add(vision);
            var ctx = new StepContext(State, unit, from, step, stepEvents);
            var interrupted = false;
            foreach (var hook in StepHooks)
                if (hook.OnUnitStepped(ctx) == StepResult.Interrupt)
                    interrupted = true;
            if (!unit.IsAlive || unit.Statuses.Stunned)
                interrupted = true;

            if (stepEvents.Count > 0 || interrupted || i == path.Count - 1)
            {
                events.Add(new UnitMoved(unit.Id, segment, dashed));
                events.AddRange(stepEvents);
                segment = [step];
            }
            if (interrupted)
                break;
        }
    }

    // ------------------------------------------------------------------ phases

    private void RunPhases(List<GameEvent> events)
    {
        // Enemy phase
        State.Phase = TacticalPhase.Enemy;
        events.Add(new PhaseStarted(TacticalPhase.Enemy, State.TurnNumber));
        foreach (var unit in State.Units.Where(IsEnemyActor).ToList())
        {
            if (State.Result is not null)
                break;
            BeginUnitTurn(unit, events);
            if (unit.IsAlive && !unit.Statuses.Stunned)
                _ai.TakeTurn(unit, new AiContext(this, events));
            CheckMissionEnd(events);
        }

        // MissionEnded is always the last event.
        if (State.Result is not null)
            return;

        // Environment phase: bleed-outs tick down (relay spawns and timers join with #24 / #33).
        State.Phase = TacticalPhase.Environment;
        events.Add(new PhaseStarted(TacticalPhase.Environment, State.TurnNumber));
        foreach (var unit in State.Units.Where(u => u.IsAlive && u.Statuses.IsBleedingOut))
        {
            unit.Statuses.BleedingOutTurnsLeft--;
            if (unit.Statuses.BleedingOutTurnsLeft > 0)
            {
                events.Add(new UnitBleedingOut(unit.Id, unit.Statuses.BleedingOutTurnsLeft));
                continue;
            }
            unit.IsAlive = false;
            events.Add(new UnitKilled(unit.Id));
        }
        CheckMissionEnd(events);
        if (State.Result is not null)
            return;

        // Next player phase
        State.TurnNumber++;
        State.Phase = TacticalPhase.Player;
        events.Add(new PhaseStarted(TacticalPhase.Player, State.TurnNumber));
        foreach (var unit in State.Units.Where(IsOperationalSoldier).ToList())
            BeginUnitTurn(unit, events);
    }

    private bool IsEnemyActor(TacticalUnit u) =>
        u is { Faction: Faction.Meridian, IsAlive: true, IsActive: true }
        && _data.Enemies[u.DefId].Kind == EnemyKind.Unit;

    /// <summary>Refills AP and expires overwatch, hunker and cooldowns; a stunned unit loses the turn and the stun.</summary>
    private void BeginUnitTurn(TacticalUnit unit, List<GameEvent> events)
    {
        var stunned = unit.Statuses.Stunned;
        unit.MovedThisTurn = false;
        unit.ActionPoints = stunned ? 0 : ApPerTurn;
        events.Add(new ApChanged(unit.Id, unit.ActionPoints));

        ClearStatus(unit, StatusFlags.Overwatch, events);
        ClearStatus(unit, StatusFlags.Hunkered, events);
        if (stunned)
            ClearStatus(unit, StatusFlags.Stunned, events);

        foreach (var id in unit.Cooldowns.Keys.Order(StringComparer.Ordinal).ToList())
            if (--unit.Cooldowns[id] <= 0)
                unit.Cooldowns.Remove(id);
    }

    private static void ClearStatus(TacticalUnit unit, StatusFlags flag, List<GameEvent> events)
    {
        if (!unit.Statuses.Has(flag))
            return;
        unit.Statuses.Set(flag, false);
        events.Add(new StatusRemoved(unit.Id, flag));
    }

    private void CheckMissionEnd(List<GameEvent> events)
    {
        if (State.Result is not null)
            return;
        var soldiersLeft = State.Units.Any(u => u.Faction == Faction.Firewall && u.IsAlive);
        var hostilesLeft = State.Units.Any(u =>
            u is { Faction: Faction.Meridian, IsAlive: true }
            && _data.Enemies[u.DefId].Kind == EnemyKind.Unit
        );
        // Purge rules; other mission types join with #24.
        MissionResult? result =
            !soldiersLeft ? MissionResult.Defeat
            : !hostilesLeft ? MissionResult.Victory
            : null;
        if (result is null)
            return;
        State.Result = result;
        events.Add(new MissionEnded(result.Value));
    }
}
