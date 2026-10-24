using Firewall.Rules.Data;
using Firewall.Rules.Rng;
using Firewall.Rules.Tactical;

namespace Firewall.Rules.Tests;

/// <summary>The mission setup pipeline and the Rescue, Sabotage and Capture rules (#33).</summary>
public class MissionSetupTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private static string Read(string file) => File.ReadAllText(Path.Combine(DataDir, file));

    private static readonly GameData Data = GameData.Load(Read);

    private static readonly MissionMaps Maps = new(
        BlockLibrary.Load(Read, GameDataFiles.Blocks),
        id => Data.LoadMap(Read($"maps/{id}.json"))
    );

    private static readonly Biome[] Biomes = [Biome.Industrial, Biome.Urban, Biome.Suburban];

    private static readonly MissionType[] Types =
    [
        MissionType.Purge,
        MissionType.Rescue,
        MissionType.Sabotage,
        MissionType.Capture,
    ];

    private static MissionSpec Spec(
        MissionType type,
        Biome biome = Biome.Industrial,
        ulong seed = 11,
        Difficulty difficulty = Difficulty.Normal
    ) =>
        new()
        {
            Type = type,
            Biome = biome,
            Seed = seed,
            ForceLevel = 3,
            Difficulty = difficulty,
        };

    private static TacticalState Create(MissionSpec spec) =>
        MissionSetup.Create(spec, null, Data, new Pcg32Random(spec.Seed), Maps);

    private static TacticalEngine Engine(TacticalState state) => new(state, Data);

    public static TheoryData<MissionType, Biome> Combos()
    {
        var data = new TheoryData<MissionType, Biome>();
        foreach (var type in Types)
        foreach (var biome in Biomes)
            data.Add(type, biome);
        return data;
    }

    // ------------------------------------------------------------------ setup

    [Theory]
    [MemberData(nameof(Combos))]
    public void SetupProducesAValidDeterministicState(MissionType type, Biome biome)
    {
        var spec = Spec(type, biome);
        var state = Create(spec);

        Assert.NotNull(state.Rule);
        Assert.Equal(4, state.Units.Count(u => u.Faction == Faction.Firewall));
        var (min, max) = EncounterBuilder.PodCountRange(spec.MissionId);
        Assert.InRange(state.Pods.Count, min, max);
        Assert.All(
            state.Units.Where(u => u.Faction != Faction.Neutral && u.DefId != "relay_node"),
            u => Assert.True(state.Map.IsWalkable(u.Position))
        );

        Assert.Equal(Create(spec).StateHash(), state.StateHash());
        Assert.NotEqual(Create(Spec(type, biome, seed: 12)).StateHash(), state.StateHash());
    }

    [Fact]
    public void RescuePlacesSixCiviliansFarFromTheSquad()
    {
        foreach (var biome in Biomes)
        {
            var state = Create(Spec(MissionType.Rescue, biome));
            var civilians = state.Units.Where(Civilians.IsCivilian).ToList();
            Assert.Equal(6, civilians.Count);
            Assert.All(
                civilians,
                c =>
                {
                    Assert.Equal(Faction.Neutral, c.Faction);
                    Assert.Equal(3, c.Hp);
                    Assert.Equal(6, c.Mobility);
                    Assert.False(c.Controllable);
                    Assert.True(state.Map.IsWalkable(c.Position));
                    Assert.All(
                        state.Map.PlayerSpawns,
                        p => Assert.True(p.DistanceTo(c.Position) >= 10)
                    );
                }
            );
            Assert.Equal(6, civilians.Select(c => c.Position).Distinct().Count());
        }
    }

    [Fact]
    public void SabotagePlacesTheRelayNodeAndTheTimerFollowsDifficulty()
    {
        foreach (
            var (difficulty, turns) in new[]
            {
                (Difficulty.Recruit, 10),
                (Difficulty.Normal, 8),
                (Difficulty.Veteran, 7),
            }
        )
        {
            var state = Create(Spec(MissionType.Sabotage, difficulty: difficulty));
            Assert.Contains(state.Units, u => u.DefId == "relay_node" && u.IsAlive);
            Assert.Equal(turns, Assert.IsType<SabotageObjective>(state.Rule).TurnLimit);
            Assert.Equal(difficulty == Difficulty.Veteran, state.PodsDriftToSquad);
        }
    }

    [Fact]
    public void CaptureAlwaysHasAProxyPod()
    {
        foreach (var biome in Biomes)
        {
            var state = Create(Spec(MissionType.Capture, biome));
            Assert.Contains(state.Units, u => u.DefId == "proxy");
            Assert.IsType<CaptureObjective>(state.Rule);
        }
    }

    [Fact]
    public void StoryMissionsUseTheHandcraftedMap()
    {
        var spec = Spec(MissionType.Story) with { StoryMapId = "test-yard" };
        var state = Create(spec);
        Assert.Equal("test-yard", state.Map.Id);
        Assert.IsType<PurgeObjective>(state.Rule);
    }

    // ------------------------------------------------------------------ rescue

    private static (
        TacticalState State,
        TacticalEngine Engine,
        TacticalUnit Soldier,
        TacticalUnit Civilian
    ) RescueYard()
    {
        var state = Create(Spec(MissionType.Rescue));
        var soldier = state.Units.First(u => u.Faction == Faction.Firewall);
        var civilian = state.Units.First(Civilians.IsCivilian);
        // Park the soldier two tiles from the civilian on free floor, one step from being adjacent.
        var spot = state
            .NearestFreeTiles(civilian.Position)
            .First(t =>
                Math.Max(Math.Abs(t.X - civilian.Position.X), Math.Abs(t.Y - civilian.Position.Y))
                == 2
            );
        soldier.Position = spot;
        soldier.Mobility = 20;
        return (state, Engine(state), soldier, civilian);
    }

    [Fact]
    public void ACivilianBecomesControllableOnceASoldierEndsAMoveNextToThem()
    {
        var (state, engine, soldier, civilian) = RescueYard();
        Assert.False(civilian.Controllable);
        Assert.False(
            engine
                .Validate(
                    new MoveCommand(civilian.Id, state.NearestFreeTiles(civilian.Position).First())
                )
                .IsValid
        );

        var adjacent = state
            .NearestFreeTiles(civilian.Position)
            .First(t =>
                Math.Max(Math.Abs(t.X - civilian.Position.X), Math.Abs(t.Y - civilian.Position.Y))
                == 1
            );
        var events = engine.Execute(new MoveCommand(soldier.Id, adjacent));

        Assert.True(civilian.Controllable);
        Assert.Equal(2, civilian.ActionPoints);
        Assert.Contains(events, e => e is CivilianJoined j && j.Civilian == civilian.Id);

        var dest = state
            .NearestFreeTiles(civilian.Position)
            .First(t => t != soldier.Position && t != civilian.Position);
        Assert.True(engine.Validate(new MoveCommand(civilian.Id, dest)).IsValid);
        // Civilians move and evac, nothing else.
        Assert.False(
            engine
                .Validate(new UseAbilityCommand(civilian.Id, AbilityRegistry.Shoot, Target.None))
                .IsValid
        );
    }

    [Fact]
    public void ControlledCiviliansGetApEachPlayerTurn()
    {
        var (state, engine, soldier, civilian) = RescueYard();
        civilian.Controllable = true;
        civilian.ActionPoints = 0;
        engine.EndPlayerPhase();
        Assert.Equal(TacticalPhase.Player, state.Phase);
        Assert.Equal(TacticalEngine.ApPerTurn, civilian.ActionPoints);
        Assert.False(engine.AllSoldiersExhausted);
        Assert.NotNull(soldier);
    }

    [Fact]
    public void RescueNeedsExactlyFourCivilians()
    {
        var state = Create(Spec(MissionType.Rescue));
        var rule = state.Rule!;
        var civilians = state.Units.Where(Civilians.IsCivilian).ToList();
        for (var i = 0; i < 3; i++)
            civilians[i].Evacuated = true;
        Assert.Equal(ObjectiveStatus.InProgress, rule.Evaluate(state));
        civilians[3].Evacuated = true;
        Assert.Equal(ObjectiveStatus.Success, rule.Evaluate(state));
    }

    [Fact]
    public void RescueFailsWhenTooFewCanStillBeSaved()
    {
        var state = Create(Spec(MissionType.Rescue));
        var civilians = state.Units.Where(Civilians.IsCivilian).ToList();
        civilians[0].IsAlive = false;
        civilians[1].IsAlive = false;
        Assert.Equal(ObjectiveStatus.InProgress, state.Rule!.Evaluate(state)); // 4 left
        civilians[2].IsAlive = false;
        Assert.Equal(ObjectiveStatus.Failure, state.Rule!.Evaluate(state));
    }

    [Fact]
    public void RescueSucceedsWhenNoHostileIsLeftAndEnoughCiviliansLive()
    {
        var state = Create(Spec(MissionType.Rescue));
        foreach (var u in state.Units.Where(u => u.Faction == Faction.Meridian))
            u.IsAlive = false;
        Assert.Equal(ObjectiveStatus.Success, state.Rule!.Evaluate(state));
    }

    [Fact]
    public void RescueResultCountsSavedAndLost()
    {
        var state = Create(Spec(MissionType.Rescue));
        var civilians = state.Units.Where(Civilians.IsCivilian).ToList();
        for (var i = 0; i < 4; i++)
            civilians[i].Evacuated = true;
        civilians[4].IsAlive = false;
        var result = MissionResult.Build(state, Data, MissionOutcome.Success, state.Rule);
        Assert.Equal(4, result.CiviliansSaved);
        Assert.Equal(1, result.CiviliansLost);
        Assert.Contains(
            "4 saved / 1 lost / 1 remaining",
            ((IMissionObjective)state.Rule!).Describe(state)
        );
    }

    private static TacticalState Street(int huskX, int civilianX, int soldierX)
    {
        var map = MapParser.Parse(
            new MapFile
            {
                Id = "street",
                Name = "Street",
                Biome = Biome.Industrial,
                Rows = [new string('.', 24), new string('.', 24), new string('.', 24)],
            },
            Data.Tiles
        );
        var state = new TacticalState(map, new Pcg32Random(3));
        var soldier = new TacticalUnit(state.NextUnitId(), Faction.Firewall, "soldier", "S")
        {
            Position = new GridPos(soldierX, 1),
            Hp = 4,
            MaxHp = 4,
            Sight = 10,
            ActionPoints = 2,
        };
        state.AddUnit(soldier);
        var civilian = Civilians.Create(state, "manOld", "C");
        civilian.Position = new GridPos(civilianX, 1);
        state.AddUnit(civilian);
        var def = Data.Enemies["husk"];
        var husk = PodSpawner.CreateUnit(state, def, "Husk");
        husk.Position = new GridPos(huskX, 1);
        husk.ActionPoints = 2;
        state.AddUnit(husk);
        return state;
    }

    [Fact]
    public void HusksPreferACivilianAtFourTilesToASoldierAtFive()
    {
        // Husk at 10; civilian 4 tiles west, soldier 5 tiles east.
        var state = Street(huskX: 10, civilianX: 6, soldierX: 15);
        var engine = new TacticalEngine(state, Data, new UtilityAi(Data));
        var husk = state.Units.First(u => u.DefId == "husk");
        var civilian = state.Units.First(Civilians.IsCivilian);
        var soldier = state.Units.First(u => u.Faction == Faction.Firewall);

        engine.EndPlayerPhase();

        Assert.True(husk.Position.X <= 7, $"the husk went to {husk.Position}");
        var gap = Math.Max(
            Math.Abs(husk.Position.X - civilian.Position.X),
            Math.Abs(husk.Position.Y - civilian.Position.Y)
        );
        Assert.True(gap <= 1, $"the husk stopped {gap} tiles from the civilian");
        Assert.Equal(soldier.MaxHp, soldier.Hp);
    }

    [Fact]
    public void OtherEnemiesIgnoreCivilians()
    {
        var state = Street(huskX: 10, civilianX: 6, soldierX: 15);
        var husk = state.Units.First(u => u.DefId == "husk");
        var warden = Data.Enemies["warden"];
        var other = PodSpawner.CreateUnit(state, warden, "Warden");
        other.Position = new GridPos(12, 2);
        other.ActionPoints = 2;
        state.AddUnit(other);
        husk.IsAlive = false;
        var engine = new TacticalEngine(state, Data, new UtilityAi(Data));
        var civilian = state.Units.First(Civilians.IsCivilian);

        engine.EndPlayerPhase();

        Assert.Equal(Civilians.Hp, civilian.Hp);
    }

    // ------------------------------------------------------------------ sabotage

    [Fact]
    public void SabotageFailsWhenTheTimerRunsOut()
    {
        var state = Create(Spec(MissionType.Sabotage));
        var engine = Engine(state);
        var rule = (SabotageObjective)state.Rule!;
        Assert.Equal(8, rule.TurnsLeft(state));
        for (var turn = 1; turn < 8; turn++)
        {
            engine.EndPlayerPhase();
            Assert.Null(state.Result);
        }
        Assert.Equal(1, rule.TurnsLeft(state));
        Assert.True(rule.IsUrgent(state));

        var events = engine.EndPlayerPhase();

        var ended = Assert.IsType<MissionEnded>(events[^1]);
        Assert.Equal(MissionOutcome.Failure, ended.Result.Outcome);
        Assert.Equal(8, ended.Result.TurnsTaken);
    }

    [Fact]
    public void SabotageTimerTurnsRedAtTwoTurnsLeft()
    {
        var state = Create(Spec(MissionType.Sabotage));
        var engine = Engine(state);
        var rule = (SabotageObjective)state.Rule!;
        for (var turn = 1; turn <= 5; turn++)
        {
            Assert.False(rule.IsUrgent(state));
            engine.EndPlayerPhase();
        }
        Assert.Equal(3, rule.TurnsLeft(state) - 0);
        engine.EndPlayerPhase();
        Assert.Equal(2, rule.TurnsLeft(state));
        Assert.True(rule.IsUrgent(state));
    }

    [Fact]
    public void DestroyingTheRelayThenKillingEveryoneSucceeds()
    {
        var state = Create(Spec(MissionType.Sabotage));
        var rule = state.Rule!;
        foreach (var u in state.Units.Where(u => u.Faction == Faction.Meridian))
            if (u.DefId == "relay_node")
                u.IsAlive = false;
        Assert.Equal(ObjectiveStatus.InProgress, rule.Evaluate(state));
        foreach (var u in state.Units.Where(u => u.Faction == Faction.Meridian))
            u.IsAlive = false;
        Assert.Equal(ObjectiveStatus.Success, rule.Evaluate(state));
    }

    [Fact]
    public void DestroyingTheRelayThenEvacuatingSucceeds()
    {
        var state = Create(Spec(MissionType.Sabotage));
        var rule = state.Rule!;
        state.Units.First(u => u.DefId == "relay_node").IsAlive = false;
        var soldiers = state.Units.Where(u => u.Faction == Faction.Firewall).ToList();
        soldiers[0].Evacuated = true;
        Assert.Equal(ObjectiveStatus.InProgress, rule.Evaluate(state));
        foreach (var s in soldiers)
            s.Evacuated = true;
        Assert.Equal(ObjectiveStatus.Success, rule.Evaluate(state));
    }

    [Fact]
    public void EvacuatingBeforeTheRelayFallsDoesNotWin()
    {
        var state = Create(Spec(MissionType.Sabotage));
        foreach (var s in state.Units.Where(u => u.Faction == Faction.Firewall))
            s.Evacuated = true;
        Assert.Equal(ObjectiveStatus.InProgress, state.Rule!.Evaluate(state));
    }

    // ------------------------------------------------------------------ capture

    [Fact]
    public void CaptureBonusNeedsTheProxyEvacuatedAlive()
    {
        var state = Create(Spec(MissionType.Capture));
        var proxy = state.Units.First(u => u.DefId == "proxy");

        bool Bonus() =>
            MissionResult
                .Build(state, Data, MissionOutcome.Success, state.Rule)
                .Objectives.Single(o => !o.Primary)
                .Achieved;

        Assert.False(Bonus());
        proxy.Statuses.Set(StatusFlags.Capturable);
        Assert.False(Bonus()); // stunned but left behind
        proxy.Evacuated = true;
        Assert.True(Bonus());
        proxy.IsAlive = false;
        Assert.False(Bonus());
    }

    [Fact]
    public void CaptureResultKeepsThePrimaryObjectiveSeparate()
    {
        var state = Create(Spec(MissionType.Capture));
        var result = MissionResult.Build(state, Data, MissionOutcome.Failure, state.Rule);
        var primary = result.Objectives.Single(o => o.Primary);
        Assert.False(primary.Achieved);
        Assert.Equal(2, result.Objectives.Count);
    }

    [Fact]
    public void ResultsWithoutBonusesListOnlyThePrimary()
    {
        var state = Create(Spec(MissionType.Purge));
        var result = MissionResult.Build(state, Data, MissionOutcome.Success, state.Rule);
        var only = Assert.Single(result.Objectives);
        Assert.True(only.Primary);
        Assert.True(only.Achieved);
    }
}
