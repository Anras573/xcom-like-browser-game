using Firewall.Rules.Data;
using Firewall.Rules.Rng;
using Firewall.Rules.Tactical;

namespace Firewall.Rules.Tests;

public class TacticalEngineTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private static readonly GameData Data = GameData.Load(f =>
        File.ReadAllText(Path.Combine(DataDir, f))
    );

    private static readonly IReadOnlyList<PodSpec> YardPods =
    [
        new("Husk Pack", ["husk", "husk", "husk"]),
        new("Warden Patrol", ["warden", "warden"]),
    ];

    private static TacticalState Skirmish(ulong seed)
    {
        var map = Data.LoadMap(File.ReadAllText(Path.Combine(DataDir, "maps", "test-yard.json")));
        var state = new TacticalState(map, new Pcg32Random(seed));
        SquadDeployer.Deploy(state, DebugSquad.Create(state, Data, state.Rng.Fork("squad")));
        PodSpawner.Spawn(state, Data, YardPods, state.Rng.Fork("pods"));
        return state;
    }

    private static TacticalEngine NewEngine(ulong seed = 1) => new(Skirmish(seed), Data);

    private static TacticalUnit Soldier(TacticalEngine e) =>
        e.State.Units.First(u => u.Faction == Faction.Firewall);

    private static GridPos Free(TacticalEngine e, TacticalUnit u, int distance) =>
        e
            .State.NearestFreeTiles(u.Position)
            .First(p => p.DistanceTo(u.Position) >= distance && IsPlannable(e, u, p));

    private static bool IsPlannable(TacticalEngine e, TacticalUnit u, GridPos p) =>
        Pathfinder.Reachable(e.State, u, 4 * u.Mobility).Contains(p);

    [Fact]
    public void MoveWithinMobilityCostsOneAp()
    {
        var e = NewEngine();
        var unit = Soldier(e);
        var events = e.Execute(new MoveCommand(unit.Id, Free(e, unit, 3)));

        Assert.Equal(1, unit.ActionPoints);
        Assert.Contains(events, ev => ev is ApChanged(var id, 1) && id == unit.Id);
        var moved = Assert.IsType<UnitMoved>(Assert.Single(events, ev => ev is UnitMoved));
        Assert.False(moved.Dashed);
        Assert.Equal(unit.Position, moved.Path[^1]);
    }

    [Fact]
    public void DashCostsTwoApAndLeavesNone()
    {
        var e = NewEngine();
        var unit = Soldier(e);
        unit.Mobility = 3;
        var reach = Pathfinder.Reachable(e.State, unit, 12);
        var dest = e
            .State.NearestFreeTiles(unit.Position)
            .First(p => reach.CostTo(p) is > 6 and <= 12);
        var events = e.Execute(new MoveCommand(unit.Id, dest));

        Assert.Equal(0, unit.ActionPoints);
        Assert.True(Assert.IsType<UnitMoved>(events.First(ev => ev is UnitMoved)).Dashed);
    }

    [Fact]
    public void InsufficientApFailsWithReason()
    {
        var e = NewEngine();
        var unit = Soldier(e);
        unit.Mobility = 3;
        unit.ActionPoints = 1;
        var reach = Pathfinder.Reachable(e.State, unit, 12);
        var far = e
            .State.NearestFreeTiles(unit.Position)
            .First(p => reach.CostTo(p) is > 6 and <= 12);

        var result = e.Validate(new MoveCommand(unit.Id, far));
        Assert.False(result.IsValid);
        Assert.Contains("action points", result.Reason);
        Assert.Throws<InvalidOperationException>(() => e.Execute(new MoveCommand(unit.Id, far)));

        unit.ActionPoints = 0;
        Assert.False(e.Validate(new MoveCommand(unit.Id, Free(e, unit, 2))).IsValid);
    }

    [Fact]
    public void OutOfRangeAndOccupiedTilesFail()
    {
        var e = NewEngine();
        var unit = Soldier(e);
        unit.Mobility = 1;
        var farAway = e
            .State.NearestFreeTiles(unit.Position)
            .First(p => p.DistanceTo(unit.Position) > 6);
        Assert.Contains("range", e.Validate(new MoveCommand(unit.Id, farAway)).Reason);

        var other = e.State.Units.First(u => u.Faction == Faction.Firewall && u != unit);
        Assert.Contains("occupied", e.Validate(new MoveCommand(unit.Id, other.Position)).Reason);
    }

    [Fact]
    public void EndTurnRunsEnemyThenEnvironmentThenRestoresAp()
    {
        var e = NewEngine();
        var unit = Soldier(e);
        e.Execute(new MoveCommand(unit.Id, Free(e, unit, 3)));
        unit.Statuses.Set(StatusFlags.Overwatch);
        unit.Cooldowns["x"] = 2;

        var events = e.EndPlayerPhase();

        var phases = events.OfType<PhaseStarted>().ToList();
        Assert.Equal(
            [TacticalPhase.Enemy, TacticalPhase.Environment, TacticalPhase.Player],
            phases.Select(p => p.Phase)
        );
        Assert.Equal([1, 1, 2], phases.Select(p => p.Turn));
        Assert.Equal(2, e.State.TurnNumber);
        Assert.Equal(TacticalPhase.Player, e.State.Phase);
        Assert.All(
            e.State.Units.Where(u => u.Faction == Faction.Firewall),
            u => Assert.Equal(2, u.ActionPoints)
        );
        Assert.False(unit.Statuses.Overwatch);
        Assert.Equal(1, unit.Cooldowns["x"]);
        Assert.False(unit.MovedThisTurn);
    }

    [Fact]
    public void EnemiesCannotBeCommandedInThePlayerPhase()
    {
        var e = NewEngine();
        var enemy = e.State.Units.First(u => u.Faction == Faction.Meridian);
        Assert.False(
            e.Validate(
                new MoveCommand(enemy.Id, e.State.NearestFreeTiles(enemy.Position).First())
            ).IsValid
        );
    }

    private sealed class KillAtTile(GridPos tile) : IStepHook
    {
        public StepResult OnUnitStepped(StepContext ctx)
        {
            if (ctx.To != tile)
                return StepResult.Continue;
            ctx.Mover.IsAlive = false;
            ctx.Events.Add(new UnitKilled(ctx.Mover.Id));
            return StepResult.Continue;
        }
    }

    [Fact]
    public void StepHookThatKillsTheMoverInterruptsThePath()
    {
        var e = NewEngine();
        var unit = Soldier(e);
        var dest = Free(e, unit, 5);
        var path = Pathfinder.Reachable(e.State, unit, 40).PathTo(dest)!;
        var deathTile = path[2];
        e.StepHooks.Add(new KillAtTile(deathTile));

        var events = e.Execute(new MoveCommand(unit.Id, dest));

        Assert.Equal(deathTile, unit.Position);
        var moved = events.OfType<UnitMoved>().Last();
        Assert.Equal(deathTile, moved.Path[^1]);
        Assert.IsType<UnitKilled>(events[^1] is MissionEnded ? events[^2] : events[^1]);
        Assert.DoesNotContain(events.OfType<UnitMoved>(), m => m.Path.Contains(path[3]));
    }

    private sealed class ReactionAtTile(GridPos tile, UnitId shooter) : IStepHook
    {
        public StepResult OnUnitStepped(StepContext ctx)
        {
            if (ctx.To == tile)
                ctx.Events.Add(new ReactionShotTriggered(shooter, ctx.Mover.Id));
            return StepResult.Continue;
        }
    }

    [Fact]
    public void HookEventsSplitTheMoveAtTheRightTile()
    {
        var e = NewEngine();
        var unit = Soldier(e);
        var dest = Free(e, unit, 5);
        var path = Pathfinder.Reachable(e.State, unit, 40).PathTo(dest)!;
        e.StepHooks.Add(new ReactionAtTile(path[1], unit.Id));

        var events = e.Execute(new MoveCommand(unit.Id, dest));

        var kinds = events.Where(ev => ev is UnitMoved or ReactionShotTriggered).ToList();
        Assert.Equal(3, kinds.Count);
        Assert.Equal(path[1], ((UnitMoved)kinds[0]).Path[^1]);
        Assert.IsType<ReactionShotTriggered>(kinds[1]);
        Assert.Equal(path[1], ((UnitMoved)kinds[2]).Path[0]);
        Assert.Equal(dest, unit.Position);
    }

    private sealed class SetOverwatch : IAbilityHandler
    {
        public ValidationResult Validate(AbilityContext ctx) => ValidationResult.Ok;

        public void Execute(AbilityContext ctx)
        {
            ctx.User.Statuses.Set(StatusFlags.Overwatch);
            ctx.Events.Add(new StatusAdded(ctx.User.Id, StatusFlags.Overwatch));
        }
    }

    [Fact]
    public void AbilitiesSpendApAndRunTheirHandler()
    {
        var state = Skirmish(1);
        var registry = new AbilityRegistry();
        registry.Register("overwatch", new SetOverwatch());
        var e = new TacticalEngine(state, Data, abilities: registry);
        var unit = Soldier(e);

        Assert.Contains(
            "does not have",
            e.Validate(new UseAbilityCommand(unit.Id, "overwatch", Target.None)).Reason
        );
        unit.Abilities.Add("overwatch");
        var def = Data.Abilities["overwatch"];
        var events = e.Execute(new UseAbilityCommand(unit.Id, "overwatch", Target.None));

        Assert.True(unit.Statuses.Overwatch);
        Assert.Equal(def.EndsTurn ? 0 : 2 - def.ApCost, unit.ActionPoints);
        Assert.IsType<AbilityUsed>(events.First(ev => ev is AbilityUsed));
        Assert.Contains(events, ev => ev is StatusAdded);
    }

    private sealed class RollingHandler(List<int> rolls) : IAbilityHandler
    {
        public ValidationResult Validate(AbilityContext ctx)
        {
            ctx.Rng.Next(0, 1_000_000); // must not advance the real stream
            return ValidationResult.Ok;
        }

        public void Execute(AbilityContext ctx) => rolls.Add(ctx.Rng.Next(0, 1_000_000));
    }

    [Fact]
    public void EachAbilityUseGetsDifferentRollsAndReplayMatches()
    {
        var abilityId = "hunker_down";
        var registry = new AbilityRegistry();
        var rolls = new List<int>();
        registry.Register(abilityId, new RollingHandler(rolls));
        var def = Data.Abilities[abilityId];

        TacticalEngine Make(ulong seed)
        {
            var e = new TacticalEngine(Skirmish(seed), Data, abilities: registry);
            foreach (var u in e.State.Units.Where(u => u.Faction == Faction.Firewall))
                u.Abilities.Add(abilityId);
            return e;
        }

        var engine = Make(5);
        var before = engine.State.StateHash();
        foreach (var u in engine.State.Units.Where(u => u.Faction == Faction.Firewall).ToList())
        {
            // Validating alone leaves the state untouched.
            Assert.True(
                engine.Validate(new UseAbilityCommand(u.Id, abilityId, Target.None)).IsValid
            );
            Assert.Equal(before, engine.State.StateHash());
            engine.Execute(new UseAbilityCommand(u.Id, abilityId, Target.None));
            before = engine.State.StateHash();
        }
        Assert.Equal(4, rolls.Count);
        Assert.Equal(4, rolls.Distinct().Count());
        Assert.True(def.ApCost <= 2);

        rolls.Clear();
        var replayed = TacticalEngine.Replay(
            engine.Log.Seed,
            engine.Log.Commands,
            seed =>
            {
                var s = Skirmish(seed);
                foreach (var u in s.Units.Where(u => u.Faction == Faction.Firewall))
                    u.Abilities.Add(abilityId);
                return s;
            },
            Data,
            abilities: registry
        );
        Assert.Equal(engine.State.StateHash(), replayed.State.StateHash());
    }

    private sealed class WipeSquadAi : IAiController
    {
        public void TakeTurn(TacticalUnit unit, AiContext ctx)
        {
            foreach (var s in ctx.State.Units.Where(u => u.Faction == Faction.Firewall))
                s.IsAlive = false;
        }
    }

    [Fact]
    public void MissionEndedIsTheLastEvent()
    {
        var e = new TacticalEngine(Skirmish(1), Data, new WipeSquadAi());
        foreach (var u in e.State.Units.Where(u => u.Faction == Faction.Meridian))
            u.IsActive = true;
        var events = e.EndPlayerPhase();
        Assert.IsType<MissionEnded>(events[^1]);
        Assert.DoesNotContain(
            events,
            ev => ev is PhaseStarted { Phase: TacticalPhase.Environment }
        );
    }

    [Fact]
    public void UnregisteredAbilityFailsValidation()
    {
        var e = NewEngine();
        var unit = Soldier(e);
        unit.Abilities.Add("reload");
        Assert.Contains(
            "not implemented",
            e.Validate(new UseAbilityCommand(unit.Id, "reload", Target.None)).Reason
        );
    }

    [Fact]
    public void ExhaustedSoldiersAreSignalled()
    {
        var e = NewEngine();
        var soldiers = e.State.Units.Where(u => u.Faction == Faction.Firewall).ToList();
        foreach (var s in soldiers.Skip(1))
            s.ActionPoints = 0;
        var last = soldiers[0];
        var events = e.Execute(new MoveCommand(last.Id, Free(e, last, 2)));
        Assert.DoesNotContain(events, ev => ev is AllSoldiersExhausted);

        events = e.Execute(new MoveCommand(last.Id, Free(e, last, 2)));
        Assert.Contains(events, ev => ev is AllSoldiersExhausted);
        Assert.True(e.AllSoldiersExhausted);
    }

    [Fact]
    public void KillingEveryHostileEndsTheMission()
    {
        var e = NewEngine();
        foreach (var u in e.State.Units.Where(u => u.Faction == Faction.Meridian))
            u.IsAlive = false;
        var unit = Soldier(e);
        var events = e.Execute(new MoveCommand(unit.Id, Free(e, unit, 2)));
        Assert.Contains(events, ev => ev is MissionEnded(MissionResult.Victory));
        Assert.False(e.Validate(new EndTurnCommand()).IsValid);
    }

    [Fact]
    public void BleedOutCountsDownInTheEnvironmentPhaseThenKills()
    {
        var e = NewEngine();
        var unit = Soldier(e);
        unit.Statuses.BleedingOutTurnsLeft = 2;
        unit.ActionPoints = 0;

        Assert.Contains(
            e.EndPlayerPhase(),
            ev => ev is UnitBleedingOut(var id, 1) && id == unit.Id
        );
        Assert.Equal(0, unit.ActionPoints);
        var events = e.EndPlayerPhase();
        Assert.Contains(events, ev => ev is UnitKilled(var id) && id == unit.Id);
        Assert.False(unit.IsAlive);
    }

    [Fact]
    public void ReplayOfRandomCommandsReproducesTheStateHash()
    {
        const ulong seed = 12345;
        var e = new TacticalEngine(Skirmish(seed), Data);
        IRandom pick = new Pcg32Random(99);
        var executed = 0;
        while (executed < 50)
        {
            var movers = e
                .State.Units.Where(u => u.Faction == Faction.Firewall && u.ActionPoints > 0)
                .ToList();
            ICommand cmd = new EndTurnCommand();
            if (movers.Count > 0 && pick.Roll(85))
            {
                var unit = movers[pick.Next(0, movers.Count)];
                var dest = new GridPos(
                    unit.Position.X + pick.Next(-6, 7),
                    unit.Position.Y + pick.Next(-6, 7)
                );
                var move = new MoveCommand(unit.Id, dest);
                if (!e.Validate(move).IsValid)
                    continue;
                cmd = move;
            }
            e.Execute(cmd);
            executed++;
        }

        Assert.Equal(50, e.Log.Commands.Count);
        var replayed = TacticalEngine.Replay(e.Log.Seed, e.Log.Commands, Skirmish, Data);
        Assert.Equal(e.State.StateHash(), replayed.State.StateHash());
        Assert.NotEqual(Skirmish(seed).StateHash(), e.State.StateHash());
    }

    [Fact]
    public void LogSerialisesToJson()
    {
        var e = NewEngine(7);
        var unit = Soldier(e);
        e.Execute(new MoveCommand(unit.Id, Free(e, unit, 2)));
        e.EndPlayerPhase();
        var json = e.Log.ToJson();
        Assert.StartsWith("{\"seed\":7,", json);
        Assert.Contains("\"type\":\"move\"", json);
        Assert.Contains("\"type\":\"endTurn\"", json);
    }
}
