using Firewall.Rules.Data;
using Firewall.Rules.Rng;
using Firewall.Rules.Tactical;

namespace Firewall.Rules.Tests;

/// <summary>Bleed-out and stabilize, evac and abort, mission objectives and MissionResult (#24).</summary>
public class MissionEndTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private static readonly GameData Data = GameData.Load(f =>
        File.ReadAllText(Path.Combine(DataDir, f))
    );

    // Three rows of floor, 20 wide; the bottom row (y = 0) starts with four evac tiles.
    private static TacticalState Yard(ulong seed = 7)
    {
        var map = MapParser.Parse(
            new MapFile
            {
                Id = "t",
                Name = "T",
                Biome = Biome.Industrial,
                Rows = [new string('.', 20), new string('.', 20), "EEEE" + new string('.', 16)],
            },
            Data.Tiles
        );
        return new TacticalState(map, new Pcg32Random(seed));
    }

    private static TacticalUnit Rookie(TacticalState state, int x, int y = 1)
    {
        var weapon = Data.Weapons["rifle_t1"];
        var unit = new TacticalUnit(state.NextUnitId(), Faction.Firewall, "soldier", "Rookie")
        {
            Position = new GridPos(x, y),
            Hp = 4,
            MaxHp = 4,
            Aim = 65,
            Mobility = 20,
            Sight = 5,
            ActionPoints = 2,
            Weapon = new WeaponState(weapon, weapon.Clip),
        };
        unit.Abilities.UnionWith(AbilityRegistry.StandardActions);
        unit.Abilities.Add(AbilityRegistry.Medkit);
        unit.Items.Add(new ItemState(AbilityRegistry.Medkit, 2));
        state.AddUnit(unit);
        return unit;
    }

    private static TacticalUnit Enemy(TacticalState state, string defId, int x, int y = 1)
    {
        var def = Data.Enemies[defId];
        var unit = new TacticalUnit(state.NextUnitId(), Faction.Meridian, defId, def.Name)
        {
            Position = new GridPos(x, y),
            Hp = def.Hp,
            MaxHp = def.Hp,
            Aim = def.Aim,
            Mobility = 20,
            Defense = def.Defense,
            Armor = def.Armor,
            Sight = 10,
            ActionPoints = 2,
        };
        unit.Abilities.UnionWith(def.Abilities);
        state.AddUnit(unit);
        return unit;
    }

    /// <summary>Each enemy runs its scripted commands in its turn.</summary>
    private sealed class ScriptAi(Func<TacticalUnit, IEnumerable<ICommand>> script) : IAiController
    {
        public void TakeTurn(TacticalUnit unit, AiContext ctx)
        {
            foreach (var cmd in script(unit))
                ctx.Execute(cmd);
        }
    }

    /// <summary>Always draws the same number, so a roll's branch is chosen by the test.</summary>
    private sealed class FixedRandom(int value) : IRandom
    {
        public int Next(int minInclusive, int maxExclusive) =>
            Math.Clamp(value, minInclusive, maxExclusive - 1);

        public double NextDouble() => value / 100.0;

        public IRandom Fork(string label) => this;
    }

    private static UseAbilityCommand Use(TacticalUnit unit, string id) =>
        new(unit.Id, id, Target.None);

    private static UseAbilityCommand Use(TacticalUnit unit, string id, TacticalUnit target) =>
        new(unit.Id, id, Target.Unit(target.Id));

    // Puts a soldier into bleed-out the way a hit to 0 HP does, with the roll forced to succeed.
    private static void BleedOut(TacticalUnit unit)
    {
        unit.Hp = 0;
        Assert.False(Casualties.Drop(unit, new FixedRandom(0), []));
    }

    private static MissionResult Ended(IReadOnlyList<GameEvent> events)
    {
        var ended = Assert.IsType<MissionEnded>(events[^1]);
        return ended.Result;
    }

    // ------------------------------------------------------------------ bleed-out

    [Theory]
    [InlineData(Rank.Rookie, 40)]
    [InlineData(Rank.Corporal, 50)]
    [InlineData(Rank.Sergeant, 60)]
    [InlineData(Rank.Lieutenant, 70)]
    [InlineData(Rank.Captain, 80)]
    public void BleedOutChanceGrowsWithRank(Rank rank, int chance) =>
        Assert.Equal(chance, Casualties.BleedOutChance(rank));

    [Fact]
    public void ASeededRollPicksBleedOutOrDeathByRank()
    {
        var outcomes = new HashSet<bool>();
        foreach (var rank in Enum.GetValues<Rank>())
            for (ulong seed = 0; seed < 20; seed++)
            {
                var state = Yard();
                var unit = Rookie(state, 0);
                unit.Rank = rank;
                unit.Hp = 0;
                var expectBleed = ((IRandom)new Pcg32Random(seed)).Roll(
                    Casualties.BleedOutChance(rank)
                );
                var events = new List<GameEvent>();

                var died = Casualties.Drop(unit, new Pcg32Random(seed), events);

                Assert.Equal(!expectBleed, died);
                Assert.Equal(!expectBleed, !unit.IsAlive);
                if (expectBleed)
                {
                    Assert.Equal(Casualties.BleedOutTurns, unit.Statuses.BleedingOutTurnsLeft);
                    Assert.Contains(events, e => e is UnitBleedingOut(_, Casualties.BleedOutTurns));
                }
                else
                    Assert.Contains(events, e => e is UnitKilled);
                outcomes.Add(expectBleed);
            }
        Assert.Equal(2, outcomes.Count); // both branches were exercised
    }

    [Fact]
    public void TheRollIsBelowTheChance()
    {
        var captain = Rookie(Yard(), 0);
        captain.Rank = Rank.Captain;
        Assert.False(Casualties.Drop(captain, new FixedRandom(79), []));
        Assert.True(captain.Statuses.IsBleedingOut);

        var rookie = Rookie(Yard(), 0);
        Assert.True(Casualties.Drop(rookie, new FixedRandom(40), []));
        Assert.False(rookie.IsAlive);
    }

    [Fact]
    public void EnemiesAtZeroHpAlwaysDie()
    {
        var husk = Enemy(Yard(), "husk", 5);
        husk.Hp = 0;
        Assert.True(Casualties.Drop(husk, new FixedRandom(0), []));
        Assert.False(husk.IsAlive);
        Assert.False(husk.Statuses.IsBleedingOut);
    }

    [Fact]
    public void ABleedingOutSoldierStaysOnItsTileCannotActAndIsNotTargeted()
    {
        var state = Yard();
        var downed = Rookie(state, 0);
        var buddy = Rookie(state, 0, 2);
        var warden = Enemy(state, "warden", 5);
        var e = new TacticalEngine(state, Data);
        downed.Statuses.Set(StatusFlags.Overwatch);
        downed.Reactions.Add(new ReactionTrigger(AbilityRegistry.Overwatch, StatusFlags.Overwatch));
        var events = new List<GameEvent>();
        downed.Hp = 0;

        Casualties.Drop(downed, new FixedRandom(0), events);

        Assert.True(downed.IsAlive);
        Assert.Equal(0, downed.ActionPoints);
        Assert.Empty(downed.Reactions);
        Assert.False(downed.Statuses.Overwatch);
        Assert.Contains(events, ev => ev is StatusRemoved(_, StatusFlags.Overwatch));
        Assert.Same(downed, state.UnitAt(downed.Position));
        downed.ActionPoints = 2;
        Assert.Contains(
            "bleeding out",
            e.Validate(new MoveCommand(downed.Id, new GridPos(3, 1))).Reason
        );
        var preview = e.PreviewShot(warden, downed)!;
        Assert.False(preview.CanFire);
        Assert.Contains("down", preview.Reason);
        Assert.True(e.PreviewShot(warden, buddy)!.CanFire);
    }

    [Fact]
    public void AnEnemyShotToZeroHpDropsTheSoldier()
    {
        var state = Yard();
        var target = Rookie(state, 0);
        Rookie(state, 0, 2);
        target.Hp = 1;
        var warden = Enemy(state, "warden", 5);
        warden.Aim = 200;
        var e = new TacticalEngine(
            state,
            Data,
            new ScriptAi(u => u.Id == warden.Id ? [Use(u, AbilityRegistry.Shoot, target)] : [])
        );

        var events = e.EndPlayerPhase();

        Assert.Equal(0, target.Hp);
        Assert.Contains(
            events,
            ev =>
                ev is UnitBleedingOut(var down, _) && down == target.Id
                || ev is UnitKilled(var dead) && dead == target.Id
        );
        Assert.Equal(1, warden.Stats.ShotsFired);
        Assert.Equal(1, warden.Stats.Hits);
        Assert.Equal(1, warden.Stats.DamageDealt); // overkill does not count
    }

    [Fact]
    public void BleedOutTicksToDeathAfterThreeEnvironmentPhases()
    {
        var state = Yard();
        var downed = Rookie(state, 0);
        Rookie(state, 0, 2);
        Enemy(state, "husk", 19, 2);
        var e = new TacticalEngine(state, Data);
        BleedOut(downed);

        Assert.Contains(e.EndPlayerPhase(), ev => ev is UnitBleedingOut(_, 2));
        Assert.Contains(e.EndPlayerPhase(), ev => ev is UnitBleedingOut(_, 1));
        Assert.True(downed.IsAlive);
        var events = e.EndPlayerPhase();
        Assert.Contains(events, ev => ev is UnitKilled(var id) && id == downed.Id);
        Assert.False(downed.IsAlive);
        Assert.Null(state.Result);
    }

    [Fact]
    public void StabilizingStopsTheBleedOut()
    {
        var state = Yard();
        var downed = Rookie(state, 0);
        var medic = Rookie(state, 1);
        var far = Rookie(state, 5);
        Enemy(state, "husk", 19, 2);
        var e = new TacticalEngine(state, Data);
        Assert.Contains(
            "full health",
            e.Validate(Use(medic, AbilityRegistry.Medkit, downed)).Reason
        );
        BleedOut(downed);
        Assert.Contains(
            "within reach",
            e.Validate(Use(far, AbilityRegistry.Medkit, downed)).Reason
        );

        var events = e.Execute(Use(medic, AbilityRegistry.Medkit, downed));

        Assert.Contains(events, ev => ev is UnitStabilized(var id) && id == downed.Id);
        Assert.Equal(1, medic.ActionPoints);
        Assert.True(downed.Statuses.Stabilized);
        Assert.False(downed.Statuses.IsBleedingOut);
        for (var i = 0; i < 4; i++)
            Assert.DoesNotContain(e.EndPlayerPhase(), ev => ev is UnitKilled);
        Assert.True(downed.IsAlive);
        Assert.Equal(0, downed.ActionPoints);
        Assert.Contains("is down", e.Validate(Use(downed, AbilityRegistry.Reload)).Reason);
    }

    // ------------------------------------------------------------------ purge and failure

    [Fact]
    public void PurgeSucceedsExactlyWhenTheLastEnemyDies()
    {
        var state = Yard();
        var shooter = Rookie(state, 0);
        shooter.Aim = 200;
        var other = Rookie(state, 0, 2);
        other.Aim = 200;
        var first = Enemy(state, "husk", 4);
        var last = Enemy(state, "husk", 4, 2);
        first.Hp = last.Hp = 1;
        var e = new TacticalEngine(state, Data);
        Assert.Equal("Purge: eliminate all hostiles.", e.Objective.Description);

        var events = e.Execute(Use(shooter, AbilityRegistry.Shoot, first));
        Assert.DoesNotContain(events, ev => ev is MissionEnded);
        Assert.Null(state.Result);

        events = e.Execute(Use(other, AbilityRegistry.Shoot, last));
        var result = Ended(events);
        Assert.Equal(MissionOutcome.Success, result.Outcome);
        Assert.Same(result, state.Result);
        Assert.Equal(1, result.Soldiers.Single(s => s.Unit == shooter.Id).Kills);
    }

    [Fact]
    public void PurgeSucceedsWhenAReactionShotKillsTheLastEnemyInTheEnemyPhase()
    {
        var state = Yard();
        var watcher = Rookie(state, 0);
        watcher.Aim = 200;
        var husk = Enemy(state, "husk", 15);
        husk.Hp = 1;
        husk.Sight = 3;
        var e = new TacticalEngine(
            state,
            Data,
            new ScriptAi(u => [new MoveCommand(u.Id, new GridPos(2, 1))])
        );
        e.Execute(Use(watcher, AbilityRegistry.Overwatch));

        var events = e.EndPlayerPhase();

        Assert.Contains(events, ev => ev is ReactionShotTriggered);
        Assert.False(husk.IsAlive);
        Assert.Equal(MissionOutcome.Success, Ended(events).Outcome);
        Assert.DoesNotContain(
            events,
            ev => ev is PhaseStarted { Phase: TacticalPhase.Environment }
        );
        Assert.Equal(1, Ended(events).Soldiers.Single().Kills);
    }

    [Fact]
    public void ATotalWipeIsAFailure()
    {
        var state = Yard();
        var target = Rookie(state, 0);
        target.Hp = 1;
        var warden = Enemy(state, "warden", 5);
        warden.Aim = 200;
        var e = new TacticalEngine(
            state,
            Data,
            new ScriptAi(u => [Use(u, AbilityRegistry.Shoot, target)])
        );

        var result = Ended(e.EndPlayerPhase());

        Assert.Equal(MissionOutcome.Failure, result.Outcome);
        // Killed outright or left bleeding out on a failed mission: dead either way.
        var fate = Assert.Single(result.Soldiers);
        Assert.Equal(SoldierFate.Dead, fate.Status);
        Assert.False(fate.Recovered);
        Assert.Equal(Loot.Empty, result.LootCollected);
        Assert.False(e.Validate(new EndTurnCommand()).IsValid);
    }

    // ------------------------------------------------------------------ evac and abort

    [Fact]
    public void EvacNeedsAnEvacTileAndTakesTheSoldierOffTheMap()
    {
        var state = Yard();
        var leaver = Rookie(state, 0, 0);
        var stayer = Rookie(state, 6, 0);
        Enemy(state, "husk", 19, 2);
        var e = new TacticalEngine(state, Data);
        Assert.Contains("evac zone", e.Validate(Use(stayer, AbilityRegistry.Evac)).Reason);

        var events = e.Execute(Use(leaver, AbilityRegistry.Evac));

        Assert.Contains(events, ev => ev is UnitEvacuated(var id) && id == leaver.Id);
        Assert.True(leaver.Evacuated);
        Assert.True(leaver.IsAlive);
        Assert.False(leaver.IsOnMap);
        Assert.Null(state.UnitAt(new GridPos(0, 0)));
        Assert.Contains("left the map", e.Validate(Use(leaver, AbilityRegistry.Reload)).Reason);
        Assert.Null(state.Result);
    }

    [Fact]
    public void EvacuatingEveryoneBeforeTheObjectiveIsARetreat()
    {
        var state = Yard();
        var wounded = Rookie(state, 0, 0);
        wounded.Hp = 2;
        var downed = Rookie(state, 1, 0);
        Enemy(state, "husk", 19, 2);
        var e = new TacticalEngine(state, Data);
        BleedOut(downed);

        var result = Ended(e.Execute(Use(wounded, AbilityRegistry.Evac)));

        Assert.Equal(MissionOutcome.Failure, result.Outcome);
        var fates = result.Soldiers.ToDictionary(s => s.Unit);
        Assert.Equal(SoldierFate.Wounded, fates[wounded.Id].Status);
        Assert.Equal(2, fates[wounded.Id].HpLost);
        Assert.True(fates[wounded.Id].Recovered);
        // Bleeding out on a failed mission, not carried out: dead.
        Assert.Equal(SoldierFate.Dead, fates[downed.Id].Status);
    }

    [Fact]
    public void AbortWithTwoOfFourOnEvacLeavesTwoMia()
    {
        var state = Yard();
        var squad = new[]
        {
            Rookie(state, 0, 0),
            Rookie(state, 3, 0),
            Rookie(state, 6, 0),
            Rookie(state, 8, 1),
        };
        Enemy(state, "husk", 19, 2);
        var e = new TacticalEngine(state, Data);

        var events = e.Execute(new AbortMissionCommand());

        var result = Ended(events);
        Assert.Equal(MissionOutcome.Aborted, result.Outcome);
        Assert.Equal(2, events.OfType<UnitEvacuated>().Count());
        Assert.Equal(
            [SoldierFate.Healthy, SoldierFate.Healthy, SoldierFate.Mia, SoldierFate.Mia],
            result.Soldiers.Select(s => s.Status)
        );
        Assert.True(squad[0].Evacuated && squad[1].Evacuated);
        Assert.False(squad[2].Evacuated || squad[3].Evacuated);
        Assert.Equal(Loot.Empty, result.LootCollected);
        Assert.Contains("\"type\":\"abort\"", e.Log.ToJson());
    }

    // ------------------------------------------------------------------ result

    [Fact]
    public void LootIsCollectedOnSuccessOnly()
    {
        const ulong seed = 11;
        var state = Yard(seed);
        var soldier = Rookie(state, 0);
        foreach (var (id, x) in new[] { ("warden", 10), ("warden", 12), ("enforcer", 14) })
            Enemy(state, id, x).IsAlive = false;
        Enemy(state, "husk", 16, 2).IsAlive = false;
        var e = new TacticalEngine(state, Data);
        var expectedCores = new Pcg32Random(seed).Fork("loot").Roll(20) ? 1 : 0;

        Assert.Equal(
            Loot.Empty,
            MissionResult.Build(state, Data, MissionOutcome.Failure).LootCollected
        );
        Assert.Equal(
            Loot.Empty,
            MissionResult.Build(state, Data, MissionOutcome.Aborted).LootCollected
        );
        var result = Ended(e.Execute(new MoveCommand(soldier.Id, new GridPos(2, 1))));

        Assert.Equal(MissionOutcome.Success, result.Outcome);
        Assert.Equal(new Loot(0, 8, expectedCores), result.LootCollected);
        Assert.Equal(
            new Dictionary<string, int>
            {
                ["enforcer"] = 1,
                ["husk"] = 1,
                ["warden"] = 2,
            },
            result.EnemiesKilled
        );
        Assert.Empty(result.Captured);
        Assert.Equal(1, result.TurnsTaken);
    }

    [Fact]
    public void TheEnforcerCoreRollGoesBothWays()
    {
        var seen = new HashSet<int>();
        for (ulong seed = 0; seed < 40; seed++)
        {
            var state = Yard(seed);
            Rookie(state, 0);
            Enemy(state, "enforcer", 10).IsAlive = false;
            seen.Add(MissionResult.Build(state, Data, MissionOutcome.Success).LootCollected.Cores);
        }
        Assert.Equal([0, 1], seen.Order());
    }

    [Fact]
    public void OnSuccessTheDownedAreWoundedAndTheDeadAreRecovered()
    {
        var state = Yard();
        var healthy = Rookie(state, 0);
        var downed = Rookie(state, 1);
        var stabilized = Rookie(state, 2);
        var dead = Rookie(state, 3);
        BleedOut(downed);
        BleedOut(stabilized);
        stabilized.Statuses.BleedingOutTurnsLeft = 0;
        stabilized.Statuses.Set(StatusFlags.Stabilized);
        dead.IsAlive = false;

        var result = MissionResult.Build(state, Data, MissionOutcome.Success);

        var fates = result.Soldiers.ToDictionary(s => s.Unit);
        Assert.Equal(SoldierFate.Healthy, fates[healthy.Id].Status);
        Assert.Equal((SoldierFate.Wounded, 4), (fates[downed.Id].Status, fates[downed.Id].HpLost));
        Assert.Equal(SoldierFate.Wounded, fates[stabilized.Id].Status);
        Assert.Equal(SoldierFate.Dead, fates[dead.Id].Status);
        Assert.All(result.Soldiers, s => Assert.True(s.Recovered));
    }

    [Fact]
    public void TheMvpDealtTheMostDamageThenHasTheMostKillsThenTheLowestId()
    {
        var state = Yard();
        var a = Rookie(state, 0);
        var b = Rookie(state, 1);
        var c = Rookie(state, 2);
        Assert.Null(MissionResult.Build(state, Data, MissionOutcome.Success).Mvp);

        (a.Stats.DamageDealt, b.Stats.DamageDealt, c.Stats.DamageDealt) = (5, 7, 7);
        (a.Stats.Kills, b.Stats.Kills, c.Stats.Kills) = (3, 1, 2);
        Assert.Equal(c.Id, MissionResult.Build(state, Data, MissionOutcome.Success).Mvp);

        b.Stats.Kills = 2;
        Assert.Equal(b.Id, MissionResult.Build(state, Data, MissionOutcome.Success).Mvp);
    }

    [Fact]
    public void ShotStatsAreCountedPerSoldier()
    {
        var state = Yard();
        var shooter = Rookie(state, 0);
        shooter.Aim = 200;
        var husk = Enemy(state, "husk", 3);
        husk.Hp = 2;
        Enemy(state, "husk", 19, 2);
        var e = new TacticalEngine(state, Data);

        e.Execute(Use(shooter, AbilityRegistry.Shoot, husk));

        Assert.Equal(1, shooter.Stats.ShotsFired);
        Assert.Equal(1, shooter.Stats.Hits);
        Assert.Equal(2, shooter.Stats.DamageDealt);
        Assert.Equal(1, shooter.Stats.Kills);
    }
}
