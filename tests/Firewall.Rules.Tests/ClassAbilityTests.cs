using Firewall.Rules.Data;
using Firewall.Rules.Rng;
using Firewall.Rules.Tactical;

namespace Firewall.Rules.Tests;

/// <summary>The four classes and their abilities against the numbers of GDD §5.2 (#30).</summary>
public class ClassAbilityTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private static readonly GameData Data = GameData.Load(f =>
        File.ReadAllText(Path.Combine(DataDir, f))
    );

    // Three rows of floor, 24 wide. Crates ('c', half cover) go in the middle row.
    private static TacticalState Yard(ulong seed = 7, int[]? crates = null, int wallX = -1)
    {
        var rows = new[] { new string('.', 24), new string('.', 24), new string('.', 24) }
            .Select(r => r.ToCharArray())
            .ToArray();
        foreach (var x in crates ?? [])
            rows[1][x] = 'c';
        if (wallX >= 0)
            foreach (var row in rows)
                row[wallX] = '#';
        var map = MapParser.Parse(
            new MapFile
            {
                Id = "t",
                Name = "T",
                Biome = Biome.Industrial,
                Rows = rows.Select(r => new string(r)).ToArray(),
            },
            Data.Tiles
        );
        return new TacticalState(map, new Pcg32Random(seed));
    }

    /// <summary>A real soldier from the builder; aim 200 makes every shot hit unless a test says otherwise.</summary>
    private static TacticalUnit Soldier(
        TacticalState state,
        string? classId,
        Rank rank,
        int x,
        string[]? picks = null,
        int aim = 200,
        int hp = 30
    )
    {
        var spec = new SoldierSpec
        {
            ClassId = classId,
            Rank = rank,
            Picks = picks ?? [],
        };
        var unit = SoldierBuilder.Build(state, Data, spec, "Test", "soldier1");
        unit.Position = new GridPos(x, 1);
        unit.Aim = aim;
        unit.Hp = unit.MaxHp = hp;
        state.AddUnit(unit);
        return unit;
    }

    // Captain picks, in rank order (Sergeant, Lieutenant, Captain).
    private static readonly string[] BreacherDefault =
    [
        "close_combat",
        "rapid_fire",
        "untouchable",
    ];
    private static readonly string[] MarksmanDefault = ["snap_shot", "sentinel", "headshot"];
    private static readonly string[] GunnerDefault = ["shredder", "grenadier", "mayhem"];
    private static readonly string[] TechDefault = ["smoke_screen", "hack", "overclock"];

    private static TacticalUnit Breacher(TacticalState s, int x, params string[] picks) =>
        Soldier(s, "breacher", Rank.Captain, x, picks.Length == 3 ? picks : BreacherDefault);

    private static TacticalUnit Marksman(TacticalState s, int x, params string[] picks) =>
        Soldier(s, "marksman", Rank.Captain, x, picks.Length == 3 ? picks : MarksmanDefault);

    private static TacticalUnit Gunner(TacticalState s, int x, params string[] picks) =>
        Soldier(s, "gunner", Rank.Captain, x, picks.Length == 3 ? picks : GunnerDefault);

    private static TacticalUnit Tech(TacticalState s, int x, params string[] picks) =>
        Soldier(s, "tech", Rank.Captain, x, picks.Length == 3 ? picks : TechDefault);

    private static TacticalUnit Rookie(TacticalState s, int x) => Soldier(s, null, Rank.Rookie, x);

    private static TacticalUnit Enemy(
        TacticalState state,
        string defId,
        int x,
        int hp = 100,
        int y = 1
    )
    {
        var def = Data.Enemies[defId];
        var unit = new TacticalUnit(state.NextUnitId(), Faction.Meridian, defId, def.Name)
        {
            Position = new GridPos(x, y),
            Hp = hp,
            MaxHp = hp,
            Aim = 200,
            Mobility = 20,
            Defense = def.Defense,
            Armor = def.Armor,
            Sight = 30,
            ActionPoints = 2,
        };
        unit.Abilities.UnionWith(def.Abilities);
        unit.Abilities.Add(AbilityRegistry.Shoot);
        state.AddUnit(unit);
        return unit;
    }

    private sealed class ScriptAi(Func<TacticalUnit, IEnumerable<ICommand>> script) : IAiController
    {
        public void TakeTurn(TacticalUnit unit, AiContext ctx)
        {
            foreach (var cmd in script(unit))
                ctx.Execute(cmd);
        }
    }

    private static TacticalEngine Engine(TacticalState state, IAiController? ai = null) =>
        new(state, Data, ai);

    private static UseAbilityCommand Use(
        TacticalUnit unit,
        string id,
        TacticalUnit? target = null
    ) => new(unit.Id, id, target is null ? Target.None : Target.Unit(target.Id));

    private static IReadOnlyList<GameEvent> Fire(
        TacticalEngine e,
        TacticalUnit unit,
        string id,
        TacticalUnit target
    ) => e.Execute(Use(unit, id, target));

    // A tile the unit can only reach by dashing.
    private static GridPos DashTile(TacticalEngine e, TacticalUnit u)
    {
        var reach = Pathfinder.Reachable(e.State, u, u.Mobility * 4);
        return e
            .State.NearestFreeTiles(u.Position)
            .First(p => reach.CostTo(p) is { } c && c > u.Mobility * 2);
    }

    // ------------------------------------------------------------------ Breacher

    [Fact]
    public void RunAndGunNeedsADash()
    {
        var state = Yard();
        var breacher = Breacher(state, 0, "close_combat", "rapid_fire", "untouchable");
        breacher.Abilities.Add(SoldierPerks.RunAndGun);
        Enemy(state, "husk", 10);
        var e = Engine(state);

        Assert.Contains("dash", e.Validate(Use(breacher, SoldierPerks.RunAndGun)).Reason);
    }

    [Fact]
    public void RunAndGunGrantsOneApThatOnlyAShotCanSpend()
    {
        var state = Yard();
        var breacher = Breacher(state, 0);
        breacher.Mobility = 3;
        var husk = Enemy(state, "husk", 10);
        var e = Engine(state);

        e.Execute(new MoveCommand(breacher.Id, DashTile(e, breacher)));
        Assert.Equal(0, breacher.ActionPoints);
        var events = e.Execute(Use(breacher, SoldierPerks.RunAndGun));

        Assert.Equal(1, breacher.ActionPoints);
        Assert.Contains(events, ev => ev is AbilityTriggered { Text: "RUN & GUN" });
        Assert.Equal(3, breacher.Cooldowns[SoldierPerks.RunAndGun]);
        // Moving and Overwatch are no shots.
        var step = new GridPos(breacher.Position.X + 1, 1);
        Assert.Contains("no action points", e.Validate(new MoveCommand(breacher.Id, step)).Reason);
        Assert.False(e.Validate(Use(breacher, AbilityRegistry.Overwatch)).IsValid);
        // A shot can.
        Assert.True(e.Validate(Use(breacher, AbilityRegistry.Shoot, husk)).IsValid);
        Fire(e, breacher, AbilityRegistry.Shoot, husk);
        Assert.Equal(0, breacher.ActionPoints);
        Assert.Contains("cooldown", e.Validate(Use(breacher, SoldierPerks.RunAndGun)).Reason);
    }

    [Fact]
    public void RunAndGunKeepsTheTurnOpenForTheBonusShot()
    {
        var state = Yard();
        var breacher = Breacher(state, 0);
        breacher.Mobility = 3;
        Enemy(state, "husk", 10);
        var e = Engine(state);

        var dash = e.Execute(new MoveCommand(breacher.Id, DashTile(e, breacher)));
        // With Run & Gun ready the soldier still has something to do.
        Assert.DoesNotContain(dash, ev => ev is AllSoldiersExhausted);
        breacher.Abilities.Remove(SoldierPerks.RunAndGun);
        Assert.True(e.AllSoldiersExhausted);
    }

    [Fact]
    public void CloseCombatAddsCritWithinFourTiles()
    {
        var state = Yard();
        var breacher = Breacher(state, 0);
        var near = Enemy(state, "husk", 4);
        var far = Enemy(state, "husk", 5, y: 0);
        var e = Engine(state);

        var close = e.PreviewShot(breacher, near)!;
        Assert.Contains(("Close Combat", 20), close.CritBreakdown);
        Assert.DoesNotContain(
            e.PreviewShot(breacher, far)!.CritBreakdown,
            c => c.Label == "Close Combat"
        );
    }

    [Fact]
    public void LightningReflexesMissesOnlyTheFirstReactionShot()
    {
        var state = Yard();
        var breacher = Soldier(
            state,
            "breacher",
            Rank.Captain,
            0,
            ["close_combat", "rapid_fire", "untouchable"]
        );
        breacher.Abilities.Add(SoldierPerks.LightningReflexes);
        var first = Enemy(state, "husk", 8);
        var second = Enemy(state, "husk", 9);
        foreach (var watcher in new[] { first, second })
            watcher.Reactions.Add(
                new ReactionTrigger(AbilityRegistry.Overwatch, StatusFlags.Overwatch)
            );
        var e = Engine(state);

        var events = e.Execute(new MoveCommand(breacher.Id, new GridPos(1, 1)));

        var shots = events.OfType<ShotResolved>().ToList();
        Assert.Equal(2, shots.Count);
        Assert.False(shots[0].Hit);
        Assert.Equal(0, shots[0].HitChance);
        Assert.True(shots[1].Hit);
        Assert.Contains(events, ev => ev is AbilityTriggered { Text: "LIGHTNING REFLEXES" });
        Assert.True(breacher.Perks.LightningReflexesSpent);

        // A new turn brings it back.
        e.EndPlayerPhase();
        Assert.False(breacher.Perks.LightningReflexesSpent);
    }

    [Fact]
    public void LightningReflexesIgnoresOrdinaryShots()
    {
        var state = Yard();
        var breacher = Breacher(state, 0);
        breacher.Abilities.Add(SoldierPerks.LightningReflexes);
        var husk = Enemy(state, "husk", 8);

        var plain = HitCalculator.Preview(state, husk, breacher, ShotContext.For(Data, husk));
        Assert.Null(plain.AutoMissBy);
        Assert.True(plain.HitChance > 0);
    }

    [Fact]
    public void RapidFireTakesTwoShotsAtMinusFifteenEach()
    {
        var state = Yard();
        var breacher = Breacher(state, 0);
        var husk = Enemy(state, "husk", 3);
        var e = Engine(state);

        var preview = e.PreviewShot(breacher, husk, SoldierPerks.RapidFire)!;
        Assert.Contains(("Rapid Fire", -15), preview.Breakdown);
        var ammo = breacher.Weapon!.Ammo;

        var events = Fire(e, breacher, SoldierPerks.RapidFire, husk);

        Assert.Equal(2, events.OfType<ShotResolved>().Count());
        Assert.Equal(ammo - 2, breacher.Weapon.Ammo);
        Assert.Equal(0, breacher.ActionPoints); // 1 AP, then the turn ends
        Assert.Equal(3, breacher.Cooldowns[SoldierPerks.RapidFire]);
        Assert.Contains("cooldown", e.Validate(Use(breacher, SoldierPerks.RapidFire, husk)).Reason);
    }

    [Fact]
    public void RapidFireWithOneRoundFiresOnce()
    {
        var state = Yard();
        var breacher = Breacher(state, 0);
        var husk = Enemy(state, "husk", 3);
        breacher.Weapon!.Ammo = 1;
        var e = Engine(state);

        var events = Fire(e, breacher, SoldierPerks.RapidFire, husk);

        Assert.Single(events.OfType<ShotResolved>());
        Assert.Equal(0, breacher.Weapon.Ammo);
    }

    [Fact]
    public void RapidFireStopsOnceTheTargetIsDown()
    {
        var state = Yard();
        var breacher = Breacher(state, 0);
        var husk = Enemy(state, "husk", 3, hp: 1);
        var e = Engine(state);

        var events = Fire(e, breacher, SoldierPerks.RapidFire, husk);

        Assert.Single(events.OfType<ShotResolved>());
        Assert.Contains(events, ev => ev is UnitKilled k && k.Unit == husk.Id);
    }

    [Fact]
    public void HitAndRunRefundsOneApOnAFlankedTargetOncePerTurn()
    {
        var state = Yard();
        var breacher = Soldier(
            state,
            "breacher",
            Rank.Captain,
            0,
            ["close_combat", "hit_and_run", "untouchable"]
        );
        var husk = Enemy(state, "husk", 3);
        var e = Engine(state);

        Assert.True(e.PreviewShot(breacher, husk)!.Flanked);
        var events = Fire(e, breacher, AbilityRegistry.Shoot, husk);
        Assert.Equal(1, breacher.ActionPoints);
        Assert.Contains(events, ev => ev is AbilityTriggered { Text: "HIT & RUN" });

        Fire(e, breacher, AbilityRegistry.Shoot, husk);
        Assert.Equal(0, breacher.ActionPoints); // once per turn: the second shot ends the turn
    }

    [Fact]
    public void HitAndRunNeedsAFlankedTarget()
    {
        var state = Yard(crates: [5]);
        var breacher = Soldier(
            state,
            "breacher",
            Rank.Captain,
            0,
            ["close_combat", "hit_and_run", "untouchable"]
        );
        var husk = Enemy(state, "husk", 6);
        var e = Engine(state);

        Assert.False(e.PreviewShot(breacher, husk)!.Flanked);
        Fire(e, breacher, AbilityRegistry.Shoot, husk);
        Assert.Equal(0, breacher.ActionPoints);
    }

    [Fact]
    public void UntouchableMakesTheNextAttackAfterAKillMiss()
    {
        var state = Yard();
        var breacher = Breacher(state, 0);
        var victim = Enemy(state, "husk", 3, hp: 1);
        var first = Enemy(state, "husk", 8);
        var second = Enemy(state, "husk", 9);
        var e = Engine(
            state,
            new ScriptAi(u => u.Id == victim.Id ? [] : [Use(u, AbilityRegistry.Shoot, breacher)])
        );

        Assert.False(breacher.Perks.UntouchableArmed);
        Fire(e, breacher, AbilityRegistry.Shoot, victim);
        Assert.True(breacher.Perks.UntouchableArmed);

        var shots = e.EndPlayerPhase().OfType<ShotResolved>().ToList();
        Assert.Equal(2, shots.Count);
        Assert.False(shots[0].Hit); // the first attacker (id order) is the one missed
        Assert.True(shots[1].Hit);
        Assert.False(breacher.Perks.UntouchableArmed);
        Assert.NotNull(first);
        Assert.NotNull(second);
    }

    [Fact]
    public void UntouchableDoesNothingWithoutAKill()
    {
        var state = Yard();
        var breacher = Breacher(state, 0);
        var husk = Enemy(state, "husk", 8);

        var preview = HitCalculator.Preview(state, husk, breacher, ShotContext.For(Data, husk));
        Assert.Null(preview.AutoMissBy);
    }

    [Fact]
    public void KillingZoneFiresAtThreeMoversOneEach()
    {
        var state = Yard();
        var breacher = Breacher(state, 0, "close_combat", "rapid_fire", "killing_zone");
        var husks = new[] { 8, 9, 10, 11 }.Select(x => Enemy(state, "husk", x)).ToList();
        var e = Engine(
            state,
            new ScriptAi(u => [new MoveCommand(u.Id, new GridPos(u.Position.X - 1, 1))])
        );

        Fire(e, breacher, SoldierPerks.KillingZone, null!);
        Assert.Equal(0, breacher.ActionPoints);
        Assert.True(breacher.Statuses.Overwatch);

        var triggers = e.EndPlayerPhase().OfType<ReactionShotTriggered>().ToList();

        Assert.Equal(3, triggers.Count);
        Assert.Equal(3, triggers.Select(t => t.Mover).Distinct().Count());
        Assert.All(triggers, t => Assert.Equal(breacher.Id, t.Shooter));
        Assert.Equal(4, husks.Count);
    }

    [Fact]
    public void PlainOverwatchStillFiresOnce()
    {
        var state = Yard();
        var breacher = Breacher(state, 0);
        foreach (var x in new[] { 8, 9, 10 })
            Enemy(state, "husk", x);
        var e = Engine(
            state,
            new ScriptAi(u => [new MoveCommand(u.Id, new GridPos(u.Position.X - 1, 1))])
        );

        e.Execute(Use(breacher, AbilityRegistry.Overwatch));

        Assert.Single(e.EndPlayerPhase().OfType<ReactionShotTriggered>());
    }

    // ------------------------------------------------------------------ Marksman

    [Fact]
    public void SquadsightTargetsAnEnemyEighteenTilesAwayThatASquadmateSees()
    {
        var state = Yard();
        var marksman = Marksman(state, 0);
        Rookie(state, 15);
        var husk = Enemy(state, "husk", 18);
        var e = Engine(state);

        var preview = e.PreviewShot(marksman, husk)!;
        Assert.True(preview.CanFire, preview.Reason);
        // The sniper's range mod still applies: 0 beyond 4 tiles.
        Assert.Equal(
            marksman.Aim + Data.Weapon(WeaponClass.Sniper, 1).RangeMod(18),
            preview.RawHitChance
        );
        Assert.True(e.Validate(Use(marksman, AbilityRegistry.Shoot, husk)).IsValid);
    }

    [Fact]
    public void SquadsightNeedsASquadmateToSeeTheEnemy()
    {
        var state = Yard();
        var marksman = Marksman(state, 0);
        var husk = Enemy(state, "husk", 18);
        var e = Engine(state);

        Assert.Contains("line of sight", e.PreviewShot(marksman, husk)!.Reason);
    }

    [Fact]
    public void WithoutSquadsightFarEnemiesStayOutOfReach()
    {
        var state = Yard();
        var marksman = Marksman(state, 0);
        marksman.Abilities.Remove(SoldierPerks.Squadsight);
        Rookie(state, 15);
        var husk = Enemy(state, "husk", 18);
        var e = Engine(state);

        Assert.False(e.PreviewShot(marksman, husk)!.CanFire);
    }

    [Fact]
    public void SquadsightNeedsALineFromTheMarksmansOwnTile()
    {
        var state = Yard(wallX: 8);
        var marksman = Marksman(state, 0);
        Rookie(state, 15);
        var husk = Enemy(state, "husk", 18);
        var e = Engine(state);

        Assert.False(e.PreviewShot(marksman, husk)!.CanFire);
    }

    [Fact]
    public void SnapShotFiresAfterMovingAtMinusTwenty()
    {
        var state = Yard();
        var marksman = Marksman(state, 0);
        var husk = Enemy(state, "husk", 8);
        var e = Engine(state);

        e.Execute(new MoveCommand(marksman.Id, new GridPos(1, 1)));
        var preview = e.PreviewShot(marksman, husk)!;

        Assert.True(preview.CanFire, preview.Reason);
        Assert.Contains(("Snap Shot", -20), preview.Breakdown);
    }

    [Fact]
    public void WithoutSnapShotASniperCannotFireAfterMoving()
    {
        var state = Yard();
        var marksman = Marksman(state, 0, "steady_aim", "sentinel", "headshot");
        var husk = Enemy(state, "husk", 8);
        var e = Engine(state);

        e.Execute(new MoveCommand(marksman.Id, new GridPos(1, 1)));

        Assert.Contains("after moving", e.PreviewShot(marksman, husk)!.Reason);
    }

    [Fact]
    public void SteadyAimHelpsOnlyTheFirstShotOfTheMission()
    {
        var state = Yard();
        var marksman = Marksman(state, 0, "steady_aim", "sentinel", "headshot");
        var husk = Enemy(state, "husk", 8);
        var e = Engine(state);

        Assert.Contains(("Steady Aim", 15), e.PreviewShot(marksman, husk)!.Breakdown);
        Fire(e, marksman, AbilityRegistry.Shoot, husk);
        e.EndPlayerPhase();
        Assert.DoesNotContain(
            e.PreviewShot(marksman, husk)!.Breakdown,
            b => b.Label == "Steady Aim"
        );
    }

    [Fact]
    public void LowProfileTurnsHalfCoverIntoFull()
    {
        var state = Yard(crates: [2]);
        var marksman = Marksman(state, 1, "snap_shot", "low_profile", "headshot");
        var plain = Rookie(state, 1);
        plain.Position = new GridPos(1, 0); // not behind the crate: just a baseline of nothing
        var husk = Enemy(state, "husk", 8);

        var cover = HitCalculator.Preview(state, husk, marksman, ShotContext.For(Data, husk));
        Assert.Contains(("Full Cover (Low Profile)", -40), cover.Breakdown);

        marksman.Abilities.Remove(SoldierPerks.LowProfile);
        var half = HitCalculator.Preview(state, husk, marksman, ShotContext.For(Data, husk));
        Assert.Contains(("Half Cover", -20), half.Breakdown);
    }

    [Fact]
    public void InTheZoneRefundsAFlankedKill()
    {
        var state = Yard();
        var marksman = Marksman(state, 0, "snap_shot", "sentinel", "in_the_zone");
        var husk = Enemy(state, "husk", 8, hp: 1);
        var e = Engine(state);

        var events = Fire(e, marksman, AbilityRegistry.Shoot, husk);

        Assert.Equal(2, marksman.ActionPoints);
        Assert.Contains(events, ev => ev is AbilityTriggered { Text: "IN THE ZONE" });
    }

    [Fact]
    public void InTheZoneDoesNotRefundAKillAgainstCover()
    {
        var state = Yard(crates: [7]);
        var marksman = Marksman(state, 0, "snap_shot", "sentinel", "in_the_zone");
        var husk = Enemy(state, "husk", 8, hp: 1);
        var e = Engine(state);

        Assert.False(e.PreviewShot(marksman, husk)!.Flanked);
        Fire(e, marksman, AbilityRegistry.Shoot, husk);

        Assert.Equal(0, marksman.ActionPoints);
    }

    [Fact]
    public void InTheZoneDoesNotRefundAMiss()
    {
        var state = Yard();
        var marksman = Marksman(state, 0, "snap_shot", "sentinel", "in_the_zone");
        marksman.Aim = 0;
        var husk = Enemy(state, "husk", 8, hp: 100);
        husk.Defense = 100;
        var e = Engine(state);

        Fire(e, marksman, AbilityRegistry.Shoot, husk);

        Assert.Equal(0, marksman.ActionPoints);
    }

    [Fact]
    public void InTheZoneRefundsThreeKillsPerTurn()
    {
        var state = Yard();
        var marksman = Marksman(state, 0, "snap_shot", "sentinel", "in_the_zone");
        marksman.Weapon!.Ammo = 10;
        var husks = new[] { 6, 7, 8, 9 }.Select(x => Enemy(state, "husk", x, hp: 1)).ToList();
        Enemy(state, "husk", 23); // keeps the mission going
        var e = Engine(state);

        for (var i = 0; i < 3; i++)
        {
            Fire(e, marksman, AbilityRegistry.Shoot, husks[i]);
            Assert.Equal(2, marksman.ActionPoints);
        }
        Fire(e, marksman, AbilityRegistry.Shoot, husks[3]);
        Assert.Equal(0, marksman.ActionPoints);

        // The count starts over with the next turn.
        e.EndPlayerPhase();
        Assert.Equal(0, marksman.Perks.InTheZoneUses);
    }

    [Fact]
    public void HeadshotAddsThreeDamageAndCoolsDown()
    {
        ShotResolved Shot(string ability, ulong seed, out TacticalUnit marksman)
        {
            var state = Yard(seed: seed, crates: [7]); // cover keeps the crit chance low
            marksman = Marksman(state, 0, "snap_shot", "sentinel", "headshot");
            var husk = Enemy(state, "husk", 8);
            var e = Engine(state);
            return Fire(e, marksman, ability, husk).OfType<ShotResolved>().Single();
        }

        var uncrit = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var plain = Shot(AbilityRegistry.Shoot, seed, out _);
            var head = Shot(SoldierPerks.Headshot, seed, out var marksman);
            Assert.Equal(plain.Hit, head.Hit);
            if (plain.Crit || head.Crit)
                continue;
            // Same rolls: the same damage plus 3.
            Assert.Equal(plain.Damage + 3, head.Damage);
            uncrit++;
            Assert.Equal(3, marksman.Cooldowns[SoldierPerks.Headshot]);
            Assert.Equal(0, marksman.ActionPoints);
        }
        Assert.True(uncrit > 5);
    }

    // ------------------------------------------------------------------ Gunner

    [Fact]
    public void ShredderRemovesOneArmorPermanently()
    {
        var state = Yard();
        var gunner = Gunner(state, 0);
        var warden = Enemy(state, "warden", 6);
        var e = Engine(state);
        Assert.Equal(1, warden.Armor);

        var events = Fire(e, gunner, AbilityRegistry.Shoot, warden);

        Assert.Equal(0, warden.Armor);
        Assert.Equal(0, events.OfType<UnitDamaged>().Single().ArmorAfter);
    }

    [Fact]
    public void WithoutShredderArmorStays()
    {
        var state = Yard();
        var gunner = Gunner(state, 0, "bulletstorm", "grenadier", "mayhem");
        var warden = Enemy(state, "warden", 6);
        var e = Engine(state);

        Fire(e, gunner, AbilityRegistry.Shoot, warden);

        Assert.Equal(1, warden.Armor);
    }

    [Fact]
    public void BulletstormKeepsTheTurnOpenForAStationaryGunner()
    {
        var state = Yard();
        var gunner = Gunner(state, 0, "bulletstorm", "grenadier", "mayhem");
        var husk = Enemy(state, "husk", 6);
        var e = Engine(state);

        Fire(e, gunner, AbilityRegistry.Shoot, husk);
        Assert.Equal(1, gunner.ActionPoints);
    }

    [Fact]
    public void BulletstormEndsTheTurnAfterMoving()
    {
        var state = Yard();
        var gunner = Gunner(state, 0, "bulletstorm", "grenadier", "mayhem");
        var husk = Enemy(state, "husk", 6);
        var e = Engine(state);

        e.Execute(new MoveCommand(gunner.Id, new GridPos(1, 1)));
        Fire(e, gunner, AbilityRegistry.Shoot, husk);
        Assert.Equal(0, gunner.ActionPoints);
    }

    [Fact]
    public void DangerZoneSuppressesTheNeighboursOfTheTarget()
    {
        var state = Yard();
        var gunner = Gunner(state, 0, "shredder", "danger_zone", "mayhem");
        var target = Enemy(state, "husk", 6);
        var beside = Enemy(state, "husk", 7);
        var far = Enemy(state, "husk", 12);
        var e = Engine(state);

        var events = Fire(e, gunner, SoldierPerks.Suppression, target);

        Assert.True(target.Statuses.Suppressed);
        Assert.True(beside.Statuses.Suppressed);
        Assert.Equal(gunner.Id, beside.Statuses.SuppressedBy);
        Assert.False(far.Statuses.Suppressed);
        Assert.Contains(events, ev => ev is UnitSuppressed s && s.Unit == beside.Id);
        Assert.Equal(2, gunner.Reactions.Count);
    }

    [Fact]
    public void SuppressionWithoutDangerZoneHitsOnlyTheTarget()
    {
        var state = Yard();
        var gunner = Gunner(state, 0);
        var target = Enemy(state, "husk", 6);
        var beside = Enemy(state, "husk", 7);
        var e = Engine(state);

        Fire(e, gunner, SoldierPerks.Suppression, target);

        Assert.True(target.Statuses.Suppressed);
        Assert.False(beside.Statuses.Suppressed);
    }

    [Fact]
    public void MayhemAddsTwoDamageToSuppressionReactionShots()
    {
        int Damage(bool mayhem, ulong seed)
        {
            var state = Yard(seed: seed);
            var gunner = Gunner(state, 0, "shredder", "grenadier", mayhem ? "mayhem" : "rupture");
            var husk = Enemy(state, "husk", 8);
            var e = Engine(
                state,
                new ScriptAi(u => [new MoveCommand(u.Id, new GridPos(u.Position.X - 1, 1))])
            );
            Fire(e, gunner, SoldierPerks.Suppression, husk);
            var shot = e.EndPlayerPhase().OfType<ShotResolved>().Single();
            Assert.False(shot.Crit); // reaction shots cannot crit
            return shot.Damage;
        }

        // Same seed, same rolls: the only difference is Mayhem's +2.
        for (ulong seed = 1; seed <= 10; seed++)
            Assert.Equal(Damage(mayhem: false, seed) + 2, Damage(mayhem: true, seed));
    }

    [Fact]
    public void RuptureAlwaysCritsRupturesTheTargetAndCoolsDown()
    {
        var state = Yard();
        var gunner = Gunner(state, 0, "shredder", "grenadier", "rupture");
        var husk = Enemy(state, "husk", 6);
        var e = Engine(state);

        Assert.Equal(100, e.PreviewShot(gunner, husk, SoldierPerks.Rupture)!.CritChance);
        var events = Fire(e, gunner, SoldierPerks.Rupture, husk);

        Assert.True(events.OfType<ShotResolved>().Single().Crit);
        Assert.True(husk.Statuses.Ruptured);
        Assert.Contains(
            events,
            ev => ev is StatusAdded(var id, StatusFlags.Ruptured) && id == husk.Id
        );
        Assert.Equal(1, gunner.ActionPoints); // 1 AP, the turn stays open
        Assert.Equal(4, gunner.Cooldowns[SoldierPerks.Rupture]);
        Assert.Contains("cooldown", e.Validate(Use(gunner, SoldierPerks.Rupture, husk)).Reason);
    }

    [Fact]
    public void ARupturedTargetTakesThreeMoreFromEverySource()
    {
        var state = Yard();
        var gunner = Gunner(state, 0, "shredder", "grenadier", "rupture");
        var rookie = Rookie(state, 1);
        var husk = Enemy(state, "husk", 6);
        var e = Engine(state);
        var before = e.PreviewShot(rookie, husk)!;

        Fire(e, gunner, SoldierPerks.Rupture, husk);
        var after = e.PreviewShot(rookie, husk)!;

        Assert.Equal(before.DamageMin + 3, after.DamageMin);
        Assert.Equal(before.DamageMax + 3, after.DamageMax);
    }

    [Fact]
    public void ARupturedSoldierTakesThreeMoreFromAnEnemy()
    {
        var state = Yard();
        var rookie = Rookie(state, 0);
        var husk = Enemy(state, "husk", 1);
        var plain = HitCalculator.Preview(state, husk, rookie, ShotContext.For(Data, husk));

        rookie.Statuses.Set(StatusFlags.Ruptured);
        var ruptured = HitCalculator.Preview(state, husk, rookie, ShotContext.For(Data, husk));

        Assert.Equal(plain.DamageMin + 3, ruptured.DamageMin);
        Assert.Equal(plain.DamageMax + 3, ruptured.DamageMax);
    }

    [Fact]
    public void ARupturedTargetTakesThreeMoreFromAReactionShot()
    {
        int Damage(bool ruptured, ulong seed)
        {
            var state = Yard(seed: seed);
            var rookie = Rookie(state, 0);
            var husk = Enemy(state, "husk", 8);
            if (ruptured)
                husk.Statuses.Set(StatusFlags.Ruptured);
            var e = Engine(
                state,
                new ScriptAi(u => [new MoveCommand(u.Id, new GridPos(u.Position.X - 1, 1))])
            );
            e.Execute(Use(rookie, AbilityRegistry.Overwatch));
            return e.EndPlayerPhase().OfType<ShotResolved>().Single().Damage;
        }

        for (ulong seed = 1; seed <= 10; seed++)
            Assert.Equal(Damage(false, seed) + 3, Damage(true, seed));
    }

    // ------------------------------------------------------------------ Tech

    [Fact]
    public void HackChanceFollowsRankAndRobotTier()
    {
        var def = Data.Abilities[SoldierPerks.Hack];
        var tech = Tech(Yard(), 0);

        tech.Rank = Rank.Corporal;
        Assert.Equal(50 + 5 - 15, HackAbility.Chance(def, tech, robotTier: 1));
        tech.Rank = Rank.Captain;
        Assert.Equal(50 + 20 - 15, HackAbility.Chance(def, tech, robotTier: 1));
        Assert.Equal(50 + 20 - 30, HackAbility.Chance(def, tech, robotTier: 2));
        Assert.Equal(25, HackAbility.Chance(def, tech, robotTier: 3));
    }

    [Fact]
    public void HackStunsARobotOnSuccessAndCoolsDownEitherWay()
    {
        var successes = 0;
        var failures = 0;
        for (ulong seed = 1; seed <= 60; seed++)
        {
            var state = Yard(seed: seed);
            var tech = Tech(state, 0);
            var warden = Enemy(state, "warden", 5);
            var e = Engine(state);
            Assert.Equal(55, e.PreviewShot(tech, warden, SoldierPerks.Hack)!.HitChance);

            var events = Fire(e, tech, SoldierPerks.Hack, warden);

            Assert.Equal(1, tech.ActionPoints); // 1 AP, does not end the turn
            Assert.Equal(3, tech.Cooldowns[SoldierPerks.Hack]);
            if (warden.Statuses.Stunned)
            {
                successes++;
                Assert.Contains(events, ev => ev is AbilityTriggered { Text: "HACKED" });
            }
            else
            {
                failures++;
                Assert.Contains(events, ev => ev is AbilityTriggered { Text: "HACK FAILED" });
            }
        }
        Assert.InRange(successes, 20, 45);
        Assert.True(failures > 0);
    }

    [Fact]
    public void AHackedRobotLosesItsNextTurn()
    {
        for (ulong seed = 1; seed <= 100; seed++)
        {
            var state = Yard(seed: seed);
            var tech = Tech(state, 0);
            var warden = Enemy(state, "warden", 5);
            var e = Engine(
                state,
                new ScriptAi(u =>
                    u.ActionPoints > 0
                        ? [new MoveCommand(u.Id, new GridPos(u.Position.X + 1, 1))]
                        : []
                )
            );
            Fire(e, tech, SoldierPerks.Hack, warden);
            if (!warden.Statuses.Stunned)
                continue;

            var events = e.EndPlayerPhase();

            Assert.DoesNotContain(events, ev => ev is UnitMoved m && m.Unit == warden.Id);
            Assert.False(warden.Statuses.Stunned); // one turn only
            return;
        }
        Assert.Fail("No hack succeeded in 100 seeds.");
    }

    [Fact]
    public void HackRejectsTheWrongTargets()
    {
        var state = Yard();
        var tech = Tech(state, 0);
        var husk = Enemy(state, "husk", 4);
        var far = Enemy(state, "warden", 8);
        var guardian = Enemy(state, "cradle_guardian", 5, y: 2);
        var e = Engine(state);

        Assert.Contains("not a robot", e.Validate(Use(tech, SoldierPerks.Hack, husk)).Reason);
        Assert.Contains("out of range", e.Validate(Use(tech, SoldierPerks.Hack, far)).Reason);
        Assert.Contains("immune", e.Validate(Use(tech, SoldierPerks.Hack, guardian)).Reason);
    }

    [Fact]
    public void HackRejectsAnObject()
    {
        var state = Yard();
        var tech = Tech(state, 0);
        var relay = Enemy(state, "relay_node", 4);
        var e = Engine(state);

        Assert.Contains("not a robot", e.Validate(Use(tech, SoldierPerks.Hack, relay)).Reason);
    }

    // Hacks the target on the first seed that succeeds, then hands back the engine.
    private static TacticalEngine HackUntilItWorks(
        Func<ulong, (TacticalEngine Engine, TacticalUnit Tech, TacticalUnit Target)> setup,
        out IReadOnlyList<GameEvent> events,
        out TacticalUnit target
    )
    {
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var (e, tech, hacked) = setup(seed);
            var result = Fire(e, tech, SoldierPerks.Hack, hacked);
            if (!hacked.Statuses.Stunned)
                continue;
            events = result;
            target = hacked;
            return e;
        }
        throw new InvalidOperationException("No hack succeeded in 200 seeds.");
    }

    [Fact]
    public void AHackedRobotCannotBeOverclockedIntoActing()
    {
        TacticalUnit warden = null!;
        var e = HackUntilItWorks(
            seed =>
            {
                var state = Yard(seed: seed);
                var tech = Tech(state, 0);
                warden = Enemy(state, "warden", 5);
                Enemy(state, "proxy", 6);
                // The real AI: the Proxy would love to boost a robot that is out of AP.
                return (Engine(state, new UtilityAi(Data)), tech, warden);
            },
            out _,
            out var hacked
        );

        var events = e.EndPlayerPhase();

        Assert.DoesNotContain(
            events,
            ev => ev is AbilityUsed { AbilityId: AbilityRegistry.ProxyOverclock }
        );
        Assert.DoesNotContain(events, ev => ev is UnitMoved m && m.Unit == hacked.Id);
        Assert.Equal(0, hacked.ActionPoints);
    }

    [Fact]
    public void AHackedEnforcerReleasesItsSuppression()
    {
        TacticalUnit rookie = null!;
        HackUntilItWorks(
            seed =>
            {
                var state = Yard(seed: seed);
                var tech = Tech(state, 0);
                rookie = Rookie(state, 1);
                var enforcer = Enemy(state, "enforcer", 5);
                rookie.Statuses.SuppressedBy = enforcer.Id;
                rookie.Statuses.Set(StatusFlags.Suppressed);
                enforcer.Reactions.Add(
                    new ReactionTrigger(SoldierPerks.Suppression, StatusFlags.None)
                    {
                        Only = rookie.Id,
                    }
                );
                return (Engine(state), tech, enforcer);
            },
            out var events,
            out var hacked
        );

        Assert.False(rookie.Statuses.Suppressed);
        Assert.Null(rookie.Statuses.SuppressedBy);
        Assert.Empty(hacked.Reactions);
        Assert.Contains(
            events,
            ev => ev is StatusRemoved(var id, StatusFlags.Suppressed) && id == rookie.Id
        );
    }

    [Fact]
    public void AHackedOverwatcherLosesItsOverwatch()
    {
        HackUntilItWorks(
            seed =>
            {
                var state = Yard(seed: seed);
                var tech = Tech(state, 0);
                var warden = Enemy(state, "warden", 5);
                warden.Reactions.Add(
                    new ReactionTrigger(AbilityRegistry.Overwatch, StatusFlags.Overwatch)
                );
                warden.Statuses.Set(StatusFlags.Overwatch);
                return (Engine(state), tech, warden);
            },
            out var events,
            out var hacked
        );

        Assert.False(hacked.Statuses.Overwatch);
        Assert.Empty(hacked.Reactions);
        Assert.Contains(
            events,
            ev => ev is StatusRemoved(var id, StatusFlags.Overwatch) && id == hacked.Id
        );
    }

    [Fact]
    public void HackNeedsLineOfSight()
    {
        var state = Yard(wallX: 3);
        var tech = Tech(state, 0);
        var warden = Enemy(state, "warden", 5);
        var e = Engine(state);

        Assert.Contains("line of sight", e.Validate(Use(tech, SoldierPerks.Hack, warden)).Reason);
    }

    [Fact]
    public void OverclockGivesAnAllyOneMoreApThisTurn()
    {
        var state = Yard();
        var tech = Tech(state, 0);
        var ally = Rookie(state, 2);
        Enemy(state, "husk", 23);
        var e = Engine(state);

        e.Execute(Use(tech, SoldierPerks.Overclock, ally));

        Assert.Equal(3, ally.ActionPoints);
        Assert.Equal(1, tech.ActionPoints);
        Assert.Equal(4, tech.Cooldowns[SoldierPerks.Overclock]);
        Assert.Contains("cooldown", e.Validate(Use(tech, SoldierPerks.Overclock, ally)).Reason);

        // It is for this turn only.
        e.EndPlayerPhase();
        Assert.Equal(2, ally.ActionPoints);
    }

    [Fact]
    public void OverclockTakesAnAllyOffOverwatchAndHunker()
    {
        var state = Yard(crates: [3]);
        var tech = Tech(state, 0);
        var ally = Rookie(state, 2);
        Enemy(state, "husk", 23);
        var e = Engine(state);
        e.Execute(Use(ally, AbilityRegistry.Overwatch));
        Assert.True(ally.Statuses.Overwatch);

        var events = e.Execute(Use(tech, SoldierPerks.Overclock, ally));

        Assert.False(ally.Statuses.Overwatch);
        Assert.Empty(ally.Reactions);
        Assert.Contains(
            events,
            ev => ev is StatusRemoved(var id, StatusFlags.Overwatch) && id == ally.Id
        );
        Assert.Equal(1, ally.ActionPoints);
    }

    [Fact]
    public void OverclockNeedsAnotherSoldier()
    {
        var state = Yard();
        var tech = Tech(state, 0);
        var e = Engine(state);

        Assert.Contains("another", e.Validate(Use(tech, SoldierPerks.Overclock, tech)).Reason);
    }

    [Fact]
    public void CooldownsTickAtTheStartOfTheOwnersTurn()
    {
        var state = Yard();
        var tech = Tech(state, 0);
        var ally = Rookie(state, 2);
        Enemy(state, "husk", 23);
        var e = Engine(state);
        e.Execute(Use(tech, SoldierPerks.Overclock, ally));

        for (var turn = 1; turn <= 3; turn++)
        {
            e.EndPlayerPhase();
            Assert.Equal(4 - turn, tech.Cooldowns[SoldierPerks.Overclock]);
        }
        e.EndPlayerPhase();
        Assert.False(tech.Cooldowns.ContainsKey(SoldierPerks.Overclock));
        Assert.True(e.Validate(Use(tech, SoldierPerks.Overclock, ally)).IsValid);
    }

    // ------------------------------------------------------------------ item passives (consumed by #31)

    private static IReadOnlySet<string> Abilities(params string[] ids) => new HashSet<string>(ids);

    [Fact]
    public void ItemPassivesChangeChargesDamageRadiusAndHealing()
    {
        var frag = Data.Items["frag_grenade"];
        var smoke = Data.Items["smoke_grenade"];
        var medkit = Data.Items["medkit"];
        var lace = Data.Items["lace_jammer"];

        Assert.Equal(2, SoldierPerks.ItemCharges(Data, Abilities("grenadier"), frag));
        Assert.Equal(1, SoldierPerks.ItemCharges(Data, Abilities("grenadier"), smoke));
        Assert.Equal(1, SoldierPerks.ItemCharges(Data, Abilities("grenadier"), lace));
        Assert.Equal(2, SoldierPerks.ItemCharges(Data, Abilities("smoke_screen"), smoke));
        Assert.Equal(1, SoldierPerks.ItemCharges(Data, Abilities("smoke_screen"), frag));
        Assert.Equal(3, SoldierPerks.ItemCharges(Data, Abilities("field_medic"), medkit));
        Assert.Equal(2, SoldierPerks.ItemCharges(Data, Abilities(), medkit));

        Assert.Equal(1, SoldierPerks.ExplosiveDamageBonus(Data, Abilities("grenadier")));
        Assert.Equal(2, SoldierPerks.ExplosiveDamageBonus(Data, Abilities("mayhem")));
        Assert.Equal(1, SoldierPerks.ItemRadiusBonus(Data, Abilities("danger_zone"), frag));
        Assert.Equal(0, SoldierPerks.ItemRadiusBonus(Data, Abilities("danger_zone"), smoke));
        Assert.Equal(1, SoldierPerks.ItemRadiusBonus(Data, Abilities("smoke_screen"), smoke));

        Assert.Equal(4, SoldierPerks.MedkitHeal(Data, Abilities(), medkit));
        Assert.Equal(6, SoldierPerks.MedkitHeal(Data, Abilities("revive"), medkit));
        Assert.Equal(8, SoldierPerks.MedkitHeal(Data, Abilities("savior"), medkit));
        Assert.Equal(1, SoldierPerks.MedkitReviveHp(Data, Abilities("revive")));
        Assert.Equal(0, SoldierPerks.MedkitReviveHp(Data, Abilities()));
    }

    [Fact]
    public void FieldMedicReachesDiagonals()
    {
        var from = new GridPos(2, 2);
        Assert.True(SoldierPerks.MedkitReaches(Abilities(), from, new GridPos(3, 2)));
        Assert.False(SoldierPerks.MedkitReaches(Abilities(), from, new GridPos(3, 3)));
        Assert.True(SoldierPerks.MedkitReaches(Abilities("field_medic"), from, new GridPos(3, 3)));
        Assert.False(SoldierPerks.MedkitReaches(Abilities("field_medic"), from, new GridPos(4, 2)));
    }

    // ------------------------------------------------------------------ builder

    [Theory]
    [InlineData("breacher", WeaponClass.Shotgun)]
    [InlineData("marksman", WeaponClass.Sniper)]
    [InlineData("gunner", WeaponClass.Lmg)]
    [InlineData("tech", WeaponClass.Rifle)]
    public void EachClassCarriesItsWeapon(string classId, WeaponClass expected)
    {
        var unit = Soldier(Yard(), classId, Rank.Corporal, 0);
        Assert.Equal(expected, unit.Weapon!.Def.Class);
    }

    [Fact]
    public void ARookieCarriesARifleAndHasNoAbilities()
    {
        var unit = Rookie(Yard(), 0);
        Assert.Equal(WeaponClass.Rifle, unit.Weapon!.Def.Class);
        Assert.Empty(
            unit.Abilities.Except(AbilityRegistry.StandardActions)
                .Except([AbilityRegistry.Carry, AbilityRegistry.Drop])
        );
    }

    [Fact]
    public void WeaponTierPicksTheWeapon()
    {
        var state = Yard();
        var unit = SoldierBuilder.Build(
            state,
            Data,
            new SoldierSpec
            {
                ClassId = "gunner",
                Rank = Rank.Corporal,
                WeaponTier = 3,
            },
            "T",
            "soldier1"
        );
        Assert.Equal("lmg_t3", unit.Weapon!.Def.Id);
    }

    [Fact]
    public void RankSetsHpAimAndAbilities()
    {
        var state = Yard();
        var corporal = SoldierBuilder.Build(
            state,
            Data,
            SoldierSpec.Default(Data, "breacher", Rank.Corporal),
            "A",
            "soldier1"
        );
        Assert.Equal(5, corporal.MaxHp);
        Assert.Equal(68, corporal.Aim);
        Assert.Contains(SoldierPerks.RunAndGun, corporal.Abilities);
        Assert.DoesNotContain(SoldierPerks.CloseCombat, corporal.Abilities);

        var captain = SoldierBuilder.Build(
            state,
            Data,
            new SoldierSpec
            {
                ClassId = "breacher",
                Rank = Rank.Captain,
                Picks = ["lightning_reflexes", "hit_and_run", "killing_zone"],
            },
            "B",
            "soldier1"
        );
        Assert.Equal(7, captain.MaxHp);
        Assert.Equal(77, captain.Aim);
        Assert.Equal(Rank.Captain, captain.Rank);
        Assert.Superset(
            new HashSet<string>([
                "run_and_gun",
                "lightning_reflexes",
                "hit_and_run",
                "killing_zone",
            ]),
            captain.Abilities
        );
        Assert.DoesNotContain(SoldierPerks.CloseCombat, captain.Abilities);
        Assert.DoesNotContain(SoldierPerks.Untouchable, captain.Abilities);
    }

    [Fact]
    public void ArmorItemsAndSprinterShapeTheStats()
    {
        var state = Yard();
        var tech = SoldierBuilder.Build(
            state,
            Data,
            new SoldierSpec
            {
                ClassId = "tech",
                Rank = Rank.Lieutenant,
                Picks = ["revive", "sprinter"],
                ArmorId = "aegis_rig",
                ItemIds = ["medkit", "targeting_scope"],
            },
            "T",
            "soldier1"
        );
        // 4 base +2 ranks (Corporal, Lieutenant) +5 armor; mobility 6 +1 armor +3 Sprinter.
        Assert.Equal(11, tech.MaxHp);
        Assert.Equal(1, tech.Armor);
        Assert.Equal(10, tech.Mobility);
        Assert.Equal(65 + 9 + 10, tech.Aim);
        Assert.Equal(["medkit", "targeting_scope"], tech.Items.Select(i => i.ItemId));
        // Field Medic's extra charge.
        Assert.Equal(3, tech.Items[0].Charges);
    }

    [Fact]
    public void GrenadierAndSmokeScreenAddChargesAtBuild()
    {
        var gunner = SoldierBuilder.Build(
            Yard(),
            Data,
            new SoldierSpec
            {
                ClassId = "gunner",
                Rank = Rank.Lieutenant,
                Picks = ["shredder", "grenadier"],
                ItemIds = ["frag_grenade"],
            },
            "G",
            "soldier1"
        );
        Assert.Equal(2, gunner.Items.Single().Charges);
    }

    [Fact]
    public void TheTechGetsASecondUtilitySlotAtSergeant()
    {
        var tech = Data.Classes["tech"];
        var breacher = Data.Classes["breacher"];
        Assert.Equal(1, SoldierBuilder.ItemSlots(tech, Rank.Corporal));
        Assert.Equal(2, SoldierBuilder.ItemSlots(tech, Rank.Sergeant));
        Assert.Equal(1, SoldierBuilder.ItemSlots(breacher, Rank.Captain));
        Assert.Equal(1, SoldierBuilder.ItemSlots(null, Rank.Rookie));
    }

    [Fact]
    public void ABadSpecIsRejectedWithReasons()
    {
        string[] Errors(SoldierSpec spec) => [.. SoldierBuilder.Validate(Data, spec)];

        Assert.NotEmpty(Errors(new SoldierSpec { ClassId = "breacher", Rank = Rank.Rookie }));
        Assert.NotEmpty(Errors(new SoldierSpec { Rank = Rank.Corporal }));
        Assert.NotEmpty(Errors(new SoldierSpec { ClassId = "nope", Rank = Rank.Corporal }));
        // A Sergeant must pick once, and from that rank's two abilities.
        Assert.NotEmpty(Errors(new SoldierSpec { ClassId = "breacher", Rank = Rank.Sergeant }));
        Assert.NotEmpty(
            Errors(
                new SoldierSpec
                {
                    ClassId = "breacher",
                    Rank = Rank.Sergeant,
                    Picks = ["rapid_fire"],
                }
            )
        );
        Assert.Empty(
            Errors(
                new SoldierSpec
                {
                    ClassId = "breacher",
                    Rank = Rank.Sergeant,
                    Picks = ["close_combat"],
                }
            )
        );
        Assert.NotEmpty(Errors(new SoldierSpec { WeaponTier = 4 }));
        Assert.NotEmpty(Errors(new SoldierSpec { ArmorId = "plate" }));
        Assert.NotEmpty(Errors(new SoldierSpec { ItemIds = ["medkit", "frag_grenade"] }));
        Assert.Throws<ArgumentException>(() =>
            SoldierBuilder.Build(Yard(), Data, new SoldierSpec { WeaponTier = 0 }, "x", "y")
        );
    }

    // ------------------------------------------------------------------ data

    [Fact]
    public void EveryClassAbilityHasADefinitionAnIconAndWorkingText()
    {
        var registry = AbilityRegistry.CreateDefault();
        foreach (var cls in Data.Classes)
        {
            Assert.False(string.IsNullOrEmpty(cls.Icon), cls.Id);
            Assert.False(string.IsNullOrEmpty(cls.Description), cls.Id);
            foreach (var id in cls.AbilityTree.SelectMany(r => r.Abilities))
            {
                var def = Data.Abilities[id];
                Assert.False(string.IsNullOrEmpty(def.Icon), id);
                Assert.DoesNotContain("{", def.Describe());
                // Everything the player can press has a handler.
                Assert.True(
                    def.Targeting == TargetingType.Passive || registry.TryGet(id, out _),
                    $"{id} is active but has no handler"
                );
            }
        }
    }

    [Fact]
    public void DescriptionNumbersComeFromTheParameters()
    {
        var def = Data.Abilities[SoldierPerks.CloseCombat];
        Assert.Equal("+20 crit within 4 tiles.", def.Describe());
        Assert.Contains("-15", Data.Abilities[SoldierPerks.RapidFire].Describe());
        Assert.Contains("3 turns", Data.Abilities[SoldierPerks.Headshot].Describe());
    }
}
