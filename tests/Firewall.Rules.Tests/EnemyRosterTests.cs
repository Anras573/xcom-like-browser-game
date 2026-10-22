using Firewall.Rules.Data;
using Firewall.Rules.Rng;
using Firewall.Rules.Tactical;

namespace Firewall.Rules.Tests;

/// <summary>The MERIDIAN roster (#29): Overclock, Suppression, Relay Node, Cradle Guardian and elite tiers.</summary>
public class EnemyRosterTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private static readonly GameData Data = GameData.Load(f =>
        File.ReadAllText(Path.Combine(DataDir, f))
    );

    // A 24 x 9 yard of floor with an objective tile ('O') at (12, 4).
    private static TacticalState Yard(ulong seed = 3)
    {
        var rows = Enumerable.Range(0, 9).Select(_ => new string('.', 24)).ToArray();
        rows[4] = rows[4][..12] + "O" + rows[4][13..];
        var map = MapParser.Parse(
            new MapFile
            {
                Id = "yard",
                Name = "Yard",
                Biome = Biome.Industrial,
                Rows = rows,
            },
            Data.Tiles
        );
        return new TacticalState(map, new Pcg32Random(seed));
    }

    private static TacticalUnit Soldier(TacticalState state, int x, int y, int aim = 65, int hp = 4)
    {
        var weapon = Data.Weapons["rifle_t1"];
        var unit = new TacticalUnit(state.NextUnitId(), Faction.Firewall, "soldier", "Rookie")
        {
            Position = new GridPos(x, y),
            Hp = hp,
            MaxHp = hp,
            Aim = aim,
            Mobility = 6,
            Sight = 20,
            ActionPoints = 2,
            Weapon = new WeaponState(weapon, weapon.Clip),
        };
        unit.Abilities.UnionWith(AbilityRegistry.StandardActions);
        state.AddUnit(unit);
        return unit;
    }

    private static TacticalUnit Enemy(TacticalState state, string defId, int x, int y, int ap = 2)
    {
        var def = Data.Enemies[defId];
        var unit = PodSpawner.CreateUnit(state, def, def.Name);
        unit.Position = new GridPos(x, y);
        unit.ActionPoints = ap;
        state.AddUnit(unit);
        return unit;
    }

    private static TacticalEngine EnemyPhase(TacticalState state)
    {
        var engine = new TacticalEngine(state, Data);
        state.Phase = TacticalPhase.Enemy;
        return engine;
    }

    private static ValidationResult Use(
        TacticalEngine e,
        TacticalUnit u,
        string ability,
        TacticalUnit t
    ) => e.Validate(new UseAbilityCommand(u.Id, ability, Target.Unit(t.Id)));

    // ------------------------------------------------------------------ elite tiers

    [Fact]
    public void EliteVariantsAreGeneratedFromTheBaseDef()
    {
        var warden = Data.Enemies["warden"];
        var mk2 = Data.Enemies["warden_mk2"];
        Assert.Equal(("Warden Mk II", 1, "warden"), (mk2.Name, mk2.EliteTier, mk2.BaseId));
        Assert.Equal((warden.Hp + 2, warden.Aim + 5, warden.Armor), (mk2.Hp, mk2.Aim, mk2.Armor));
        Assert.Equal("#b48cff", mk2.Sprite.Tint);
        Assert.Equal(warden.Sprite.Set, mk2.Sprite.Set);

        var mk3 = Data.Enemies["enforcer_mk3"];
        var enforcer = Data.Enemies["enforcer"];
        Assert.Equal(
            (enforcer.Hp + 4, enforcer.Aim + 10, enforcer.Armor + 1),
            (mk3.Hp, mk3.Aim, mk3.Armor)
        );
        Assert.Equal("#ff5a5a", mk3.Sprite.Tint);
        Assert.Equal(2, mk3.EliteTier);
    }

    [Fact]
    public void EliteLootIsOneAndAHalfRoundedUp()
    {
        Assert.Equal([3], Data.Enemies["warden_mk2"].Loot.Select(l => l.Amount));
        Assert.Equal([2, 30], Data.Enemies["proxy_mk2"].Loot.Select(l => l.Amount));
        Assert.Equal([6, 2], Data.Enemies["enforcer_mk3"].Loot.Select(l => l.Amount));
    }

    [Fact]
    public void BossesAndObjectsHaveNoVariants()
    {
        Assert.False(Data.Enemies.Contains("cradle_guardian_mk2"));
        Assert.False(Data.Enemies.Contains("relay_node_mk2"));
        Assert.True(Data.Enemies.Contains("husk_mk3"));
    }

    [Fact]
    public void ElitesUnlockWithConvergence()
    {
        Assert.Equal(0, EliteTransform.TierAt(Data.Elites, 3));
        Assert.Equal(1, EliteTransform.TierAt(Data.Elites, 4));
        Assert.Equal(2, EliteTransform.TierAt(Data.Elites, 9));
    }

    // ------------------------------------------------------------------ Overclock

    [Fact]
    public void OverclockGivesExactlyOneApToAnAdjacentRobotThatActed()
    {
        var state = Yard();
        Soldier(state, 0, 0);
        var proxy = Enemy(state, "proxy", 5, 4);
        var warden = Enemy(state, "warden", 6, 3, ap: 0);
        var engine = EnemyPhase(state);

        var events = engine.Execute(
            new UseAbilityCommand(proxy.Id, "proxy_overclock", Target.Unit(warden.Id))
        );

        Assert.Equal(1, warden.ActionPoints);
        Assert.Contains(new ApChanged(warden.Id, 1), events);
        Assert.Equal(1, proxy.ActionPoints);
    }

    [Fact]
    public void OverclockRespectsItsCooldown()
    {
        var state = Yard();
        Soldier(state, 0, 0);
        var proxy = Enemy(state, "proxy", 5, 4);
        var warden = Enemy(state, "warden", 6, 4, ap: 0);
        var engine = EnemyPhase(state);
        engine.Execute(new UseAbilityCommand(proxy.Id, "proxy_overclock", Target.Unit(warden.Id)));
        warden.ActionPoints = 0;
        proxy.ActionPoints = 2;

        var again = Use(engine, proxy, "proxy_overclock", warden);
        Assert.False(again.IsValid);
        Assert.Contains("cooldown", again.Reason);
        Assert.Equal(3, proxy.Cooldowns["proxy_overclock"]);
    }

    [Fact]
    public void OverclockOnlyTargetsAdjacentRobotsThatHaveActed()
    {
        var state = Yard();
        Soldier(state, 0, 0);
        var proxy = Enemy(state, "proxy", 5, 4);
        var far = Enemy(state, "warden", 9, 4, ap: 0);
        var husk = Enemy(state, "husk", 4, 4, ap: 0);
        var fresh = Enemy(state, "warden", 5, 5, ap: 2);
        var engine = EnemyPhase(state);

        Assert.False(Use(engine, proxy, "proxy_overclock", far).IsValid);
        Assert.False(Use(engine, proxy, "proxy_overclock", husk).IsValid);
        Assert.False(Use(engine, proxy, "proxy_overclock", fresh).IsValid);
        Assert.False(Use(engine, proxy, "proxy_overclock", proxy).IsValid);
    }

    // ------------------------------------------------------------------ Suppression

    [Fact]
    public void SuppressionCutsTheTargetsAimInTheShotPreview()
    {
        var state = Yard();
        var soldier = Soldier(state, 3, 4);
        var enforcer = Enemy(state, "enforcer", 10, 4);
        var engine = EnemyPhase(state);
        var before = engine.PreviewShot(soldier, enforcer)!;

        engine.Execute(new UseAbilityCommand(enforcer.Id, "suppression", Target.Unit(soldier.Id)));
        var after = engine.PreviewShot(soldier, enforcer)!;

        Assert.True(soldier.Statuses.Suppressed);
        Assert.Equal(before.HitChance - 30, after.HitChance);
        Assert.Contains(("Suppressed", -30), after.Breakdown);
        Assert.Equal(0, enforcer.ActionPoints);
    }

    [Fact]
    public void ASuppressedTargetThatMovesDrawsAReactionShot()
    {
        var state = Yard();
        var soldier = Soldier(state, 3, 4);
        var enforcer = Enemy(state, "enforcer", 10, 4);
        var engine = EnemyPhase(state);
        engine.Execute(new UseAbilityCommand(enforcer.Id, "suppression", Target.Unit(soldier.Id)));
        state.Phase = TacticalPhase.Player;

        var events = engine.Execute(new MoveCommand(soldier.Id, new GridPos(3, 6)));

        Assert.Contains(new ReactionShotTriggered(enforcer.Id, soldier.Id), events);
        Assert.Single(events.OfType<ReactionShotTriggered>());
    }

    [Fact]
    public void SuppressionEndsWhenTheSuppressorDies()
    {
        var state = Yard();
        var soldier = Soldier(state, 3, 4, aim: 200);
        var enforcer = Enemy(state, "enforcer", 10, 4);
        var engine = EnemyPhase(state);
        engine.Execute(new UseAbilityCommand(enforcer.Id, "suppression", Target.Unit(soldier.Id)));
        state.Phase = TacticalPhase.Player;
        enforcer.Hp = 1;
        soldier.Statuses.Set(StatusFlags.Suppressed, false);
        soldier.Statuses.Set(StatusFlags.Suppressed);

        var events = engine.Execute(
            new UseAbilityCommand(soldier.Id, "shoot", Target.Unit(enforcer.Id))
        );

        Assert.False(enforcer.IsAlive);
        Assert.False(soldier.Statuses.Suppressed);
        Assert.Contains(new StatusRemoved(soldier.Id, StatusFlags.Suppressed), events);
    }

    [Fact]
    public void SuppressionEndsAtTheSuppressorsNextTurn()
    {
        var state = Yard();
        var soldier = Soldier(state, 3, 4);
        var enforcer = Enemy(state, "enforcer", 10, 4);
        var engine = EnemyPhase(state);
        engine.Execute(new UseAbilityCommand(enforcer.Id, "suppression", Target.Unit(soldier.Id)));
        state.Phase = TacticalPhase.Player;
        Assert.True(soldier.Statuses.Suppressed);

        engine.EndPlayerPhase();

        Assert.False(soldier.Statuses.Suppressed);
        Assert.Empty(enforcer.Reactions);
    }

    // ------------------------------------------------------------------ Relay Node

    [Fact]
    public void RelaySpawnsAHuskEverySecondEnvironmentPhaseUpToItsCap()
    {
        var state = Yard();
        Soldier(state, 0, 0, hp: 100);
        // Keeps the Purge from ending: a dormant enemy far away.
        var sentinel = Enemy(state, "warden", 23, 8);
        sentinel.IsActive = false;
        var relay = PodSpawner.SpawnObjects(state, Data, "relay_node").Single();
        var engine = new TacticalEngine(state, Data);

        var spawnedOn = new List<int>();
        for (var turn = 1; turn <= 16; turn++)
        {
            var events = engine.EndPlayerPhase();
            if (events.OfType<UnitSpawned>().Any())
                spawnedOn.Add(turn);
        }

        Assert.Equal([2, 4, 6, 8, 10, 12], spawnedOn);
        var husks = state.Units.Where(u => u.SummonedBy == relay.Id).ToList();
        Assert.Equal(6, husks.Count);
        Assert.All(husks, h => Assert.Equal("husk", h.DefId));
        Assert.All(
            husks,
            h => Assert.True(Math.Max(Math.Abs(h.Position.X - 12), Math.Abs(h.Position.Y - 4)) == 1)
        );
    }

    [Fact]
    public void ARelayThatIsDestroyedStopsSpawning()
    {
        var state = Yard();
        Soldier(state, 0, 0, hp: 100);
        Enemy(state, "warden", 23, 8).IsActive = false;
        var relay = PodSpawner.SpawnObjects(state, Data, "relay_node").Single();
        var engine = new TacticalEngine(state, Data);
        relay.IsAlive = false;

        for (var turn = 1; turn <= 4; turn++)
            Assert.DoesNotContain(engine.EndPlayerPhase(), e => e is UnitSpawned);
    }

    // ------------------------------------------------------------------ Cradle Guardian

    [Fact]
    public void TwinVolleyFiresTwiceWithTwoAp()
    {
        var state = Yard();
        var soldier = Soldier(state, 3, 4, hp: 100);
        var guardian = Enemy(state, "cradle_guardian", 10, 4);
        var engine = EnemyPhase(state);

        var shot = new UseAbilityCommand(guardian.Id, "shoot", Target.Unit(soldier.Id));
        var events = new List<GameEvent>();
        events.AddRange(engine.Execute(shot));
        Assert.Equal(1, guardian.ActionPoints);
        events.AddRange(engine.Execute(shot));

        Assert.Equal(2, events.OfType<ShotResolved>().Count());
        Assert.Equal(0, guardian.ActionPoints);
        Assert.False(engine.Validate(shot).IsValid);
    }

    [Fact]
    public void TheGuardianSummonsTwoWardensOnceAtHalfHp()
    {
        var state = Yard();
        var soldier = Soldier(state, 3, 4, aim: 200, hp: 100);
        var guardian = Enemy(state, "cradle_guardian", 10, 4);
        guardian.Hp = 16;
        var engine = new TacticalEngine(state, Data);
        var shoot = new UseAbilityCommand(soldier.Id, "shoot", Target.Unit(guardian.Id));

        var events = engine.Execute(shoot);

        Assert.True(guardian.Hp <= 15);
        var spawned = events.OfType<UnitSpawned>().ToList();
        Assert.Equal(2, spawned.Count);
        Assert.All(spawned, s => Assert.Equal("warden", s.DefId));
        Assert.All(spawned, s => Assert.True(s.At.DistanceTo(guardian.Position) <= 4 * 1.5));
        Assert.All(spawned, s => Assert.Equal(guardian.Id, s.Source));

        soldier.ActionPoints = 2;
        var more = engine.Execute(shoot);
        Assert.DoesNotContain(more, e => e is UnitSpawned);
        Assert.Equal(2, state.Units.Count(u => u.DefId == "warden"));
    }

    [Fact]
    public void SummonedWardensFollowTheConvergenceTier()
    {
        var state = Yard();
        state.Convergence = 4;
        var soldier = Soldier(state, 3, 4, aim: 200, hp: 100);
        var guardian = Enemy(state, "cradle_guardian", 10, 4);
        guardian.Hp = 16;
        var engine = new TacticalEngine(state, Data);

        engine.Execute(new UseAbilityCommand(soldier.Id, "shoot", Target.Unit(guardian.Id)));

        Assert.Equal(2, state.Units.Count(u => u.DefId == "warden_mk2"));
    }

    [Fact]
    public void TheGuardianIsImmuneToStun()
    {
        var state = Yard();
        Assert.True(Enemy(state, "cradle_guardian", 10, 4).ImmuneToStun);
        Assert.False(Enemy(state, "warden", 11, 4).ImmuneToStun);
    }

    // ------------------------------------------------------------------ encounter tables

    [Fact]
    public void EveryForceLevelHasEncounters()
    {
        Assert.Empty(Data.Validate());
        Assert.Equal(10, Data.Encounters.Count);
        Assert.Equal(
            [("husk", 3), ("warden", 2)],
            Data.Encounters["1"].Pods.Select(p => (p.Units[0], p.Units.Count))
        );
    }

    [Fact]
    public void TheBuilderIsDeterministic()
    {
        var a = EncounterBuilder.Build(Data, 5, 4, new Pcg32Random(11));
        var b = EncounterBuilder.Build(Data, 5, 4, new Pcg32Random(11));
        Assert.Equal(
            a.Select(p => (p.Name, string.Join(',', p.EnemyIds))),
            b.Select(p => (p.Name, string.Join(',', p.EnemyIds)))
        );
        Assert.Equal(4, a.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(9)]
    public void PodWeightsAreRespectedOverTenThousandSamples(int level)
    {
        var templates = Data.Encounters[level.ToString()].Pods;
        var total = templates.Sum(t => t.Weight);
        var counts = new int[templates.Count];
        var rng = new Pcg32Random(2024);
        const int samples = 10_000;
        for (var i = 0; i < samples; i++)
        {
            var spec = EncounterBuilder.Build(Data, level, 1, rng.Fork($"s{i}")).Single();
            var index = templates
                .Select((t, n) => (t, n))
                .First(x =>
                    EncounterBuilder.ToSpec(Data, x.t).EnemyIds.SequenceEqual(spec.EnemyIds)
                )
                .n;
            counts[index]++;
        }
        for (var n = 0; n < templates.Count; n++)
        {
            // Templates that spawn identical units share a bucket, so compare bucket totals.
            var same = Enumerable
                .Range(0, templates.Count)
                .Where(m =>
                    EncounterBuilder
                        .ToSpec(Data, templates[m])
                        .EnemyIds.SequenceEqual(
                            EncounterBuilder.ToSpec(Data, templates[n]).EnemyIds
                        )
                )
                .ToList();
            if (same[0] != n)
                continue;
            var expected = same.Sum(m => templates[m].Weight) / (double)total;
            var actual = same.Sum(m => counts[m]) / (double)samples;
            Assert.InRange(actual, expected - 0.02, expected + 0.02);
        }
    }

    [Fact]
    public void ElitesAppearFromForceLevelSevenAndMkThreeFromNine()
    {
        bool Any(int fl, int tier) => Data.Encounters[fl.ToString()].Pods.Any(p => p.Tier == tier);
        Assert.False(Any(6, 1));
        Assert.True(Any(7, 1));
        Assert.False(Any(8, 2));
        Assert.True(Any(9, 2));

        var pods = Enumerable
            .Range(0, 40)
            .SelectMany(i => EncounterBuilder.Build(Data, 7, 3, new Pcg32Random((ulong)i)));
        Assert.Contains(pods, p => p.EnemyIds.Any(id => id.EndsWith("_mk2")));
    }

    [Fact]
    public void PodCountsFollowTheMissionType()
    {
        for (ulong seed = 0; seed < 30; seed++)
        {
            var rng = new Pcg32Random(seed);
            Assert.InRange(EncounterBuilder.PodCount("purge", rng), 3, 4);
            Assert.Equal(3, EncounterBuilder.PodCount("rescue", rng));
            Assert.Equal(3, EncounterBuilder.PodCount("sabotage", rng));
            Assert.InRange(EncounterBuilder.PodCount("capture", rng), 2, 3);
        }
    }

    [Fact]
    public void CaptureAlwaysHasAProxyPodAndRescueLeansOnHusks()
    {
        for (var fl = 1; fl <= 10; fl++)
        for (ulong seed = 0; seed < 20; seed++)
        {
            var pods = EncounterBuilder.Build(Data, fl, 3, new Pcg32Random(seed), "capture");
            Assert.Contains(pods[0].EnemyIds, id => id.StartsWith("proxy"));
        }

        double HuskShare(string mission)
        {
            var units = Enumerable
                .Range(0, 300)
                .SelectMany(i =>
                    EncounterBuilder.Build(Data, 3, 3, new Pcg32Random((ulong)i), mission)
                )
                .SelectMany(p => p.EnemyIds)
                .ToList();
            return units.Count(u => u == "husk") / (double)units.Count;
        }
        Assert.True(HuskShare("rescue") > HuskShare("purge"));
    }

    [Fact]
    public void PodBannersNameTheStrongestMember()
    {
        var spec = EncounterBuilder.ToSpec(
            Data,
            new PodTemplateDef { Weight = 1, Units = ["warden", "proxy"] }
        );
        Assert.Equal("WARDEN PATROL", spec.Name);
        Assert.Equal(
            "ENFORCER SQUAD",
            EncounterBuilder
                .ToSpec(Data, new PodTemplateDef { Weight = 1, Units = ["enforcer", "warden"] })
                .Name
        );
        Assert.Equal(
            "PROXY CELL",
            EncounterBuilder.ToSpec(Data, new PodTemplateDef { Weight = 1, Units = ["proxy"] }).Name
        );
    }
}
