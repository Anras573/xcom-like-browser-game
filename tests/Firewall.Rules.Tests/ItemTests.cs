using Firewall.Rules.Data;
using Firewall.Rules.Rng;
using Firewall.Rules.Tactical;
using Firewall.Rules.Tactical.Vision;

namespace Firewall.Rules.Tests;

/// <summary>Utility items, area targeting, destructible terrain and carrying (#31).</summary>
public class ItemTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private static readonly GameData Data = GameData.Load(f =>
        File.ReadAllText(Path.Combine(DataDir, f))
    );

    /// <summary>Every roll comes out as <paramref name="value"/> (0 = everything succeeds, 99 = everything fails).</summary>
    private sealed class FixedRandom(int value) : IRandom
    {
        public int Next(int minInclusive, int maxExclusive) =>
            Math.Clamp(value, minInclusive, maxExclusive - 1);

        public double NextDouble() => value / 100.0;

        public IRandom Fork(string label) => this;
    }

    private static TacticalState Board(string[] rows, IRandom? rng = null)
    {
        var map = MapParser.Parse(
            new MapFile
            {
                Id = "t",
                Name = "T",
                Biome = Biome.Industrial,
                Rows = rows,
            },
            Data.Tiles
        );
        return new TacticalState(map, rng ?? new FixedRandom(0));
    }

    // Five rows of floor, 24 wide; the soldiers and enemies stand in row 2 (y = 2 from the bottom).
    private static string[] Open(int width = 24, int height = 5) =>
        Enumerable.Repeat(new string('.', width), height).ToArray();

    private static TacticalUnit Soldier(
        TacticalState state,
        int x,
        int y = 2,
        string? classId = null,
        string[]? picks = null,
        params string[] items
    )
    {
        var spec = new SoldierSpec
        {
            ClassId = classId,
            Rank = classId is null ? Rank.Rookie : Rank.Captain,
            Picks = picks ?? [],
            ItemIds = items,
        };
        var unit = SoldierBuilder.Build(state, Data, spec, "Test", "soldier1");
        unit.Position = new GridPos(x, y);
        unit.Hp = unit.MaxHp = 30;
        unit.Sight = 30;
        state.AddUnit(unit);
        return unit;
    }

    private static TacticalUnit Enemy(
        TacticalState state,
        string defId,
        int x,
        int y = 2,
        int hp = 20
    )
    {
        var def = Data.Enemies[defId];
        var unit = new TacticalUnit(state.NextUnitId(), Faction.Meridian, defId, def.Name)
        {
            Position = new GridPos(x, y),
            Hp = hp,
            MaxHp = hp,
            Aim = 0,
            Mobility = 4,
            Defense = def.Defense,
            Armor = def.Armor,
            Sight = 30,
            ActionPoints = 2,
            ImmuneToStun = def.ImmuneToStun,
        };
        unit.Abilities.UnionWith(def.Abilities);
        state.AddUnit(unit);
        return unit;
    }

    private static TacticalEngine Engine(TacticalState state, IAiController? ai = null) =>
        new(state, Data, ai);

    private static UseAbilityCommand Throw(TacticalUnit unit, string item, int x, int y = 2) =>
        new(unit.Id, item, Target.Tile(new GridPos(x, y)));

    private static UseAbilityCommand On(TacticalUnit unit, string id, TacticalUnit target) =>
        new(unit.Id, id, Target.Unit(target.Id));

    private static UseAbilityCommand Self(TacticalUnit unit, string id) =>
        new(unit.Id, id, Target.None);

    // ------------------------------------------------------------------ radius math

    [Fact]
    public void Radius1Point5IsTheThreeByThreeBlock()
    {
        var map = Board(Open()).Map;

        var tiles = Blast.Tiles(map, new GridPos(5, 2), 1.5);

        Assert.Equal(9, tiles.Count);
        Assert.Equal(
            Enumerable
                .Range(1, 3)
                .SelectMany(y => Enumerable.Range(4, 3).Select(x => new GridPos(x, y))),
            tiles
        );
    }

    [Fact]
    public void Radius2IsTheThirteenTilesWithinTwoCenters()
    {
        var map = Board(Open()).Map;

        var tiles = Blast.Tiles(map, new GridPos(5, 2), 2).ToHashSet();

        // dx² + dy² <= 4, checked by hand: the plus of reach 2 and the 3x3 block, no (±2, ±1).
        GridPos[] expected =
        [
            new(5, 0),
            new(4, 1),
            new(5, 1),
            new(6, 1),
            new(3, 2),
            new(4, 2),
            new(5, 2),
            new(6, 2),
            new(7, 2),
            new(4, 3),
            new(5, 3),
            new(6, 3),
            new(5, 4),
        ];
        Assert.Equal(13, tiles.Count);
        Assert.True(tiles.SetEquals(expected));
    }

    [Fact]
    public void Radius1IsAPlusAndTheBlastIsClippedAtTheMapEdge()
    {
        var map = Board(Open()).Map;

        Assert.Equal(5, Blast.Tiles(map, new GridPos(5, 2), 1).Count);
        Assert.Equal(4, Blast.Tiles(map, new GridPos(0, 0), 1.5).Count);
    }

    // ------------------------------------------------------------------ frag grenade

    [Fact]
    public void FragHitsRegardlessOfCoverAndBreaksTheCrateSoTheTargetIsFlanked()
    {
        // The crate west of the enemy covers it from the thrower; the grenade lands on the enemy.
        var state = Board(["..........", ".....c....", ".........."]);
        var thrower = Soldier(state, 1, 1, items: "frag_grenade");
        var enemy = Enemy(state, "warden", 6, 1);
        enemy.Armor = 0;
        var e = Engine(state);
        var covered = HitCalculator.Preview(state, thrower, enemy, ShotContext.Default);
        Assert.False(covered.Flanked);
        var version = state.MapVersion;

        var events = e.Execute(Throw(thrower, "frag_grenade", 6, 1));

        Assert.Equal(17, enemy.Hp);
        Assert.Contains(events, ev => ev is UnitDamaged(var id, 3, 17, _) && id == enemy.Id);
        Assert.Equal(TileType.Floor, state.Map[new GridPos(5, 1)]);
        Assert.Contains(
            events,
            ev =>
                ev is TileChanged(var pos, TileType.SmallCrate, TileType.Floor)
                && pos == new GridPos(5, 1)
        );
        Assert.True(state.MapVersion > version);
        Assert.True(HitCalculator.Preview(state, thrower, enemy, ShotContext.Default).Flanked);
        Assert.Equal(0, thrower.Items[0].Charges);
    }

    [Fact]
    public void FragNeedsLosToTheTileNotToUnitsAndRespectsRangeAndCharges()
    {
        var state = Board(["..........#......", "..........#......", "..........#......"]);
        var thrower = Soldier(state, 1, 1, items: "frag_grenade");
        Enemy(state, "husk", 15, 0); // keeps the mission going
        var e = Engine(state);

        Assert.Contains("line of sight", e.Validate(Throw(thrower, "frag_grenade", 11, 1)).Reason);
        Assert.Contains("range", e.Validate(Throw(thrower, "frag_grenade", 12, 1)).Reason);
        // Throwing at the wall itself is fine: only the tiles in between block.
        Assert.True(e.Validate(Throw(thrower, "frag_grenade", 10, 1)).IsValid);
        Assert.True(e.Validate(Throw(thrower, "frag_grenade", 8, 1)).IsValid);

        e.Execute(Throw(thrower, "frag_grenade", 8, 1));
        Assert.Contains("no charges", e.Validate(Throw(thrower, "frag_grenade", 8, 1)).Reason);
    }

    [Fact]
    public void FragHurtsFriendliesAndStripsRobotArmorButNotDamage()
    {
        var state = Board(Open());
        var thrower = Soldier(state, 1, items: "frag_grenade");
        var friend = Soldier(state, 6);
        var robot = Enemy(state, "enforcer", 7); // armor 2
        var e = Engine(state);

        var preview = e.PreviewBlast(thrower, "frag_grenade", new GridPos(6, 2))!;
        Assert.Equal(1, preview.Enemies);
        Assert.Equal(1, preview.Allies);
        Assert.True(preview.CanThrow);
        Assert.Equal(9, preview.Tiles.Count);

        e.Execute(Throw(thrower, "frag_grenade", 6));

        Assert.Equal(27, friend.Hp);
        Assert.Equal(17, robot.Hp);
        Assert.Equal(1, robot.Armor);
    }

    [Fact]
    public void FragKillsDownedSoldiersAndCountsEnemyKillsForTheThrower()
    {
        var state = Board(Open());
        var thrower = Soldier(state, 1, items: "frag_grenade");
        var husk = Enemy(state, "husk", 6, hp: 3);
        var body = Soldier(state, 5);
        body.Hp = 0;
        Casualties.Drop(body, new FixedRandom(0), []);
        Assert.True(body.Statuses.IsBleedingOut);
        var e = Engine(state);

        e.Execute(Throw(thrower, "frag_grenade", 6));

        Assert.False(husk.IsAlive);
        Assert.False(body.IsAlive);
        Assert.Equal(1, thrower.Stats.Kills);
    }

    [Fact]
    public void GrenadierAndMayhemAddDamageAndDangerZoneAddsRadius()
    {
        var state = Board(Open());
        var gunner = Soldier(
            state,
            1,
            classId: "gunner",
            picks: ["shredder", "grenadier", "mayhem"],
            items: "frag_grenade"
        );
        var zoner = Soldier(
            state,
            1,
            3,
            classId: "gunner",
            picks: ["shredder", "danger_zone", "mayhem"],
            items: "frag_grenade"
        );
        var e = Engine(state);

        var preview = e.PreviewBlast(gunner, "frag_grenade", new GridPos(8, 2))!;
        var wide = e.PreviewBlast(zoner, "frag_grenade", new GridPos(8, 2))!;

        Assert.Equal(3 + 1 + 2, preview.Damage);
        Assert.Equal(2, gunner.Items[0].Charges);
        Assert.Equal(1.5, preview.Radius);
        Assert.Equal(2.5, wide.Radius);
        Assert.Equal(3 + 2, wide.Damage);
    }

    [Fact]
    public void FragWakesTheDormantPodItLandsOn()
    {
        var state = Board(Open());
        var thrower = Soldier(state, 1, items: "frag_grenade");
        var husk = Enemy(state, "husk", 10);
        var podId = state.AddPod("Test pod", husk.Position);
        var pod = state.Pods.Single(p => p.Id == podId);
        pod.Members.Add(husk.Id);
        husk.Pod = podId;
        husk.IsActive = false;
        var e = Engine(state);

        var events = e.Execute(Throw(thrower, "frag_grenade", 11));

        Assert.True(pod.Active);
        Assert.Contains(events, ev => ev is PodActivated);
    }

    // ------------------------------------------------------------------ breach charge

    [Fact]
    public void BreachChargeTurnsWallsToFloorAndOpensLineOfSight()
    {
        var state = Board([
            "...........",
            "....#......",
            "...........",
            "....#......",
            "...........",
        ]);
        // A solid wall column with a soldier on one side, an enemy on the other.
        for (var y = 0; y < 5; y++)
            state.Map.SetTile(new GridPos(4, y), TileType.Wall);
        var thrower = Soldier(state, 3, 2, items: "breach_charge");
        var enemy = Enemy(state, "husk", 6, 2);
        var e = Engine(state);
        Assert.False(LosTracer.HasLos(state.Map, thrower.Position, enemy.Position));

        var events = e.Execute(Throw(thrower, "breach_charge", 4));

        Assert.Equal(TileType.Floor, state.Map[new GridPos(4, 2)]);
        Assert.Equal(TileType.Floor, state.Map[new GridPos(4, 1)]);
        Assert.Equal(TileType.Floor, state.Map[new GridPos(4, 3)]);
        // Radius 1 is a plus: the walls diagonally past it stay, and so do the map-edge walls.
        Assert.Equal(TileType.Wall, state.Map[new GridPos(4, 0)]);
        Assert.Equal(TileType.Wall, state.Map[new GridPos(4, 4)]);
        Assert.Equal(3, events.OfType<TileChanged>().Count());
        Assert.True(LosTracer.HasLos(state.Map, thrower.Position, enemy.Position));
        Assert.Equal(25, thrower.Hp); // the blast reaches the neighbours of the charge, thrower included
    }

    [Fact]
    public void BreachChargeIsPlacedOnAnAdjacentTileOnly()
    {
        var state = Board(Open());
        var thrower = Soldier(state, 3, 2, items: "breach_charge");
        var e = Engine(state);

        Assert.True(e.Validate(Throw(thrower, "breach_charge", 4, 3)).IsValid);
        Assert.Contains("range", e.Validate(Throw(thrower, "breach_charge", 6)).Reason);
        Assert.Contains("close", e.Validate(Throw(thrower, "breach_charge", 3)).Reason);
    }

    [Fact]
    public void OnlyBreachChargesDestroyWalls()
    {
        var state = Board(Open());
        state.Map.SetTile(new GridPos(8, 2), TileType.Wall);
        var thrower = Soldier(state, 1, items: "frag_grenade");
        var e = Engine(state);

        e.Execute(Throw(thrower, "frag_grenade", 7));

        Assert.Equal(TileType.Wall, state.Map[new GridPos(8, 2)]);
    }

    [Fact]
    public void ABigCrateBreaksDownToASmallOne()
    {
        var state = Board(Open());
        state.Map.SetTile(new GridPos(8, 2), TileType.BigCrate);
        var thrower = Soldier(state, 1, items: "frag_grenade");

        Engine(state).Execute(Throw(thrower, "frag_grenade", 8));

        Assert.Equal(TileType.SmallCrate, state.Map[new GridPos(8, 2)]);
    }

    // ------------------------------------------------------------------ smoke and lace jammer

    [Fact]
    public void SmokeGivesDefenseForTwoTurnsAndDoesNotBlockSight()
    {
        var state = Board(Open());
        var thrower = Soldier(state, 1, items: "smoke_grenade");
        var target = Soldier(state, 8);
        var enemy = Enemy(state, "warden", 12);
        enemy.Aim = 80;
        var e = Engine(state);
        var before = HitCalculator.Preview(state, enemy, target, ShotContext.Default).HitChance;

        var events = e.Execute(Throw(thrower, "smoke_grenade", 8));

        var created = Assert.Single(events.OfType<SmokeCreated>());
        Assert.Equal(9, created.Tiles.Count);
        var preview = HitCalculator.Preview(state, enemy, target, ShotContext.Default);
        Assert.Equal(before - 20, preview.HitChance);
        Assert.Contains(preview.Breakdown, b => b is ("Smoke", -20));
        Assert.True(preview.CanFire); // the cloud does not block line of sight
        // Outside the cloud nothing changes.
        Assert.Equal(0, state.SmokeDefenseAt(new GridPos(12, 2)));

        Assert.DoesNotContain(e.EndPlayerPhase(), ev => ev is SmokeExpired);
        Assert.Equal(20, state.SmokeDefenseAt(new GridPos(8, 2)));
        Assert.Contains(e.EndPlayerPhase(), ev => ev is SmokeExpired);
        Assert.Equal(0, state.SmokeDefenseAt(new GridPos(8, 2)));
    }

    [Fact]
    public void SmokeScreenAddsARadiusStepAndACharge()
    {
        var state = Board(Open(width: 30, height: 9));
        var tech = Soldier(
            state,
            1,
            4,
            classId: "tech",
            picks: ["smoke_screen", "hack", "overclock"],
            items: "smoke_grenade"
        );
        var e = Engine(state);

        var preview = e.PreviewBlast(tech, "smoke_grenade", new GridPos(8, 4))!;

        Assert.Equal(2.5, preview.Radius);
        Assert.Equal(2, tech.Items[0].Charges);
    }

    [Fact]
    public void LaceJammerStunsHusksInRadiusTwoOnly()
    {
        var state = Board(Open());
        var thrower = Soldier(state, 1, items: "lace_jammer");
        var near = Enemy(state, "husk", 8);
        var edge = Enemy(state, "husk", 10); // exactly 2 from the target tile
        var far = Enemy(state, "husk", 11, 3);
        var warden = Enemy(state, "warden", 8, 3);
        var e = Engine(state);

        var events = e.Execute(Throw(thrower, "lace_jammer", 8));

        Assert.True(near.Statuses.Stunned);
        Assert.True(edge.Statuses.Stunned);
        Assert.False(far.Statuses.Stunned);
        Assert.False(warden.Statuses.Stunned);
        Assert.Equal(2, events.OfType<StatusAdded>().Count(s => s.Status == StatusFlags.Stunned));
        // Stunned for one turn: the stun wears off when its owner's turn comes round.
        near.ActionPoints = 2;
        e.EndPlayerPhase();
        Assert.False(near.Statuses.Stunned);
        Assert.True(near.Perks.LostTurnToStun);
        Assert.False(far.Perks.LostTurnToStun);
    }

    // ------------------------------------------------------------------ medkit

    [Fact]
    public void MedkitHealsUpToMaxHpAndRunsOutOfCharges()
    {
        var state = Board(Open());
        var medic = Soldier(state, 3, items: "medkit");
        var patient = Soldier(state, 4);
        patient.Hp = 28;
        Enemy(state, "husk", 22, 4);
        var e = Engine(state);

        var events = e.Execute(On(medic, "medkit", patient));

        Assert.Equal(30, patient.Hp);
        Assert.Contains(events, ev => ev is UnitHealed(var id, 2, 30) && id == patient.Id);
        Assert.Equal(1, medic.Items[0].Charges);
        Assert.Contains("full health", e.Validate(On(medic, "medkit", patient)).Reason);

        medic.Hp = 10;
        e.Execute(On(medic, "medkit", medic));
        Assert.Equal(14, medic.Hp);
        Assert.Equal(0, medic.Items[0].Charges);
        medic.ActionPoints = 2;
        Assert.Contains("no charges", e.Validate(On(medic, "medkit", medic)).Reason);
    }

    [Fact]
    public void MedkitReachIsSideBySideUnlessFieldMedic()
    {
        var state = Board(Open());
        var rookie = Soldier(state, 3, 2, items: "medkit");
        var tech = Soldier(
            state,
            10,
            2,
            classId: "tech",
            picks: ["smoke_screen", "hack", "overclock"],
            items: "medkit"
        );
        var diagonalA = Soldier(state, 4, 3);
        var diagonalB = Soldier(state, 11, 3);
        diagonalA.Hp = diagonalB.Hp = 5;
        var e = Engine(state);

        Assert.Contains("reach", e.Validate(On(rookie, "medkit", diagonalA)).Reason);
        Assert.True(e.Validate(On(tech, "medkit", diagonalB)).IsValid);
        Assert.Equal(3, tech.Items[0].Charges); // Field Medic: +1 charge
    }

    [Fact]
    public void MedkitStabilizesABleedingOutSoldier()
    {
        var state = Board(Open());
        var medic = Soldier(state, 3, items: "medkit");
        var downed = Soldier(state, 4);
        downed.Hp = 0;
        Casualties.Drop(downed, new FixedRandom(0), []);
        Enemy(state, "husk", 22, 4);
        var e = Engine(state);

        var events = e.Execute(On(medic, "medkit", downed));

        Assert.Contains(events, ev => ev is UnitStabilized(var id) && id == downed.Id);
        Assert.True(downed.Statuses.Stabilized);
        Assert.False(downed.Statuses.IsBleedingOut);
        Assert.Equal(0, downed.Hp);
        Assert.Contains("already stabilized", e.Validate(On(medic, "medkit", downed)).Reason);
    }

    [Fact]
    public void ReviveStandsABleedingOutSoldierUpAtOneHp()
    {
        var state = Board(Open());
        var medic = Soldier(
            state,
            3,
            classId: "tech",
            picks: ["revive", "hack", "savior"],
            items: "medkit"
        );
        var downed = Soldier(state, 4);
        downed.Hp = 0;
        Casualties.Drop(downed, new FixedRandom(0), []);

        var events = Engine(state).Execute(On(medic, "medkit", downed));

        Assert.Contains(events, ev => ev is UnitRevived(var id, 1) && id == downed.Id);
        Assert.Equal(1, downed.Hp);
        Assert.False(downed.Statuses.IsDown);
        Assert.True(downed.IsStanding);
    }

    // ------------------------------------------------------------------ arc stunner

    [Fact]
    public void ArcStunnerCapturesAProxyAtSixtyPercentHpOrLess()
    {
        var state = Board(Open());
        var soldier = Soldier(state, 3, items: "arc_stunner");
        var proxy = Enemy(state, "proxy", 4, hp: 5);
        proxy.Hp = 3; // 3/5 = 60 %
        var e = Engine(state);

        var events = e.Execute(On(soldier, "arc_stunner", proxy));

        Assert.True(proxy.Statuses.Stunned);
        Assert.True(proxy.Statuses.Capturable);
        Assert.Equal(3, proxy.Hp);
        Assert.Contains(events, ev => ev is AbilityTriggered { Text: "CAPTURABLE" });
        Assert.DoesNotContain(events, ev => ev is UnitDamaged);
        Assert.Equal(0, soldier.Items[0].Charges);
    }

    [Fact]
    public void ArcStunnerOnAHealthierProxyDoesTwoDamageAndAOneTurnStun()
    {
        var state = Board(Open());
        var soldier = Soldier(state, 3, items: "arc_stunner");
        var proxy = Enemy(state, "proxy", 4, hp: 5);
        proxy.Hp = 4; // 80 %
        var e = Engine(state);

        e.Execute(On(soldier, "arc_stunner", proxy));

        Assert.Equal(2, proxy.Hp);
        Assert.True(proxy.Statuses.Stunned);
        Assert.False(proxy.Statuses.Capturable);
        e.EndPlayerPhase();
        Assert.False(proxy.Statuses.Stunned);
    }

    [Fact]
    public void ArcStunnerStunsRobotsForOneTurnAndMissesWhenTheRollFails()
    {
        var state = Board(Open(), new FixedRandom(99));
        var soldier = Soldier(state, 3, items: "arc_stunner");
        var warden = Enemy(state, "warden", 4);
        var e = Engine(state);

        var missed = e.Execute(On(soldier, "arc_stunner", warden));

        Assert.Contains(missed, ev => ev is ShotResolved { Hit: false });
        Assert.False(warden.Statuses.Stunned);
        Assert.Equal(0, soldier.Items[0].Charges);
    }

    [Fact]
    public void ArcStunnerHitsRobotsForTwoAndStunsThem()
    {
        var state = Board(Open());
        var soldier = Soldier(state, 3, items: "arc_stunner");
        var warden = Enemy(state, "warden", 4, hp: 6);
        warden.Armor = 0;
        var e = Engine(state);

        e.Execute(On(soldier, "arc_stunner", warden));

        Assert.Equal(4, warden.Hp);
        Assert.True(warden.Statuses.Stunned);
        Assert.False(warden.Statuses.Capturable);
    }

    [Fact]
    public void ArcStunnerIsMeleeAndOnlyForRobotsAndProxies()
    {
        var state = Board(Open());
        var soldier = Soldier(state, 3, items: "arc_stunner");
        var husk = Enemy(state, "husk", 4);
        var far = Enemy(state, "warden", 9);
        var e = Engine(state);

        Assert.Contains("robot", e.Validate(On(soldier, "arc_stunner", husk)).Reason);
        Assert.Contains("adjacent", e.Validate(On(soldier, "arc_stunner", far)).Reason);
    }

    [Fact]
    public void ArcStunnerUsesTheHitCalculatorWithSeventyBaseAimAndIgnoresCover()
    {
        var state = Board(["..........", "..........", ".........."]);
        state.Map.SetTile(new GridPos(4, 0), TileType.SmallCrate);
        var soldier = Soldier(state, 3, 0, items: "arc_stunner");
        soldier.Aim = 10; // not the soldier's aim: the item's 70
        var proxy = Enemy(state, "proxy", 4, 1, hp: 5);
        proxy.Hp = 3;
        var e = Engine(state);

        var preview = e.PreviewShot(soldier, proxy, "arc_stunner")!;

        // A shot would be covered by the crate south of the Proxy; the stunner is melee.
        Assert.Contains(
            HitCalculator.Preview(state, soldier, proxy, ShotContext.Default).Breakdown,
            b => b.Label == "Half Cover"
        );
        Assert.Equal(70 - proxy.Defense, preview.HitChance);
        Assert.DoesNotContain(preview.Breakdown, b => b.Label.Contains("Cover"));
        Assert.True(preview.CanFire);
    }

    // ------------------------------------------------------------------ carry and drop

    private static void Down(TacticalUnit unit, bool stabilized = true)
    {
        unit.Hp = 0;
        Casualties.Drop(unit, new FixedRandom(0), []);
        if (!stabilized)
            return;
        unit.Statuses.BleedingOutTurnsLeft = 0;
        unit.Statuses.Set(StatusFlags.Stabilized);
    }

    [Fact]
    public void CarryingCostsMobilityAndTheAbilityToShootAndDropGivesThemBack()
    {
        var state = Board(Open());
        var carrier = Soldier(state, 3);
        var downed = Soldier(state, 4);
        Down(downed);
        var enemy = Enemy(state, "husk", 12);
        var e = Engine(state);
        var mobility = carrier.Mobility;
        Assert.True(e.Validate(On(carrier, "shoot", enemy)).IsValid);

        var events = e.Execute(On(carrier, "carry", downed));

        Assert.Contains(
            events,
            ev => ev is UnitCarried(var c, var d) && c == carrier.Id && d == downed.Id
        );
        Assert.Equal(1, carrier.ActionPoints);
        Assert.Equal(mobility - 2, carrier.Mobility);
        Assert.Contains("carrying", e.Validate(On(carrier, "shoot", enemy)).Reason);
        var another = Soldier(state, 3, 3);
        Down(another);
        Assert.Contains("Already carrying", e.Validate(On(carrier, "carry", another)).Reason);
        Assert.False(downed.IsOnMap);

        // The load rides along.
        carrier.ActionPoints = 2;
        e.Execute(new MoveCommand(carrier.Id, new GridPos(5, 2)));
        Assert.Equal(new GridPos(5, 2), downed.Position);
        Assert.Equal(carrier, state.UnitAt(new GridPos(5, 2)));

        carrier.ActionPoints = 2;
        var drop = e.Execute(Self(carrier, "drop"));
        var dropped = Assert.Single(drop.OfType<UnitDropped>());
        Assert.Equal(mobility, carrier.Mobility);
        Assert.True(downed.IsOnMap);
        Assert.Equal(dropped.At, downed.Position);
        Assert.NotEqual(carrier.Position, downed.Position);
        Assert.True(e.Validate(On(carrier, "shoot", enemy)).IsValid);
    }

    [Fact]
    public void CarryTakesBleedingOutSoldiersAndCapturableProxiesNothingElse()
    {
        var state = Board(Open());
        var carrier = Soldier(state, 3);
        var healthy = Soldier(state, 3, 3);
        var bleeding = Soldier(state, 2);
        Down(bleeding, stabilized: false);
        var proxy = Enemy(state, "proxy", 4, hp: 5);
        var husk = Enemy(state, "husk", 4, 3);
        var far = Soldier(state, 10);
        Down(far);
        var e = Engine(state);

        Assert.Contains("cannot be carried", e.Validate(On(carrier, "carry", healthy)).Reason);
        Assert.True(e.Validate(On(carrier, "carry", bleeding)).IsValid);
        Assert.Contains("cannot be carried", e.Validate(On(carrier, "carry", proxy)).Reason);
        Assert.Contains("cannot be carried", e.Validate(On(carrier, "carry", husk)).Reason);
        Assert.Contains("not adjacent", e.Validate(On(carrier, "carry", far)).Reason);
        proxy.Statuses.Set(StatusFlags.Stunned);
        Assert.Contains("cannot be carried", e.Validate(On(carrier, "carry", proxy)).Reason);
        proxy.Statuses.Set(StatusFlags.Capturable);
        Assert.True(e.Validate(On(carrier, "carry", proxy)).IsValid);
    }

    private sealed class ShootAi(UnitId victim) : IAiController
    {
        public void TakeTurn(TacticalUnit unit, AiContext ctx)
        {
            if (ctx.State.Unit(victim).IsStanding)
                ctx.Execute(new UseAbilityCommand(unit.Id, "shoot", Target.Unit(victim)));
        }
    }

    [Fact]
    public void ADeadCarrierDropsTheirLoadOnAnAdjacentFreeTile()
    {
        // Every roll fails: the shot hits for full damage and the carrier does not get to bleed out.
        var state = Board(Open(), new FixedRandom(99));
        var carrier = Soldier(state, 3);
        carrier.Hp = 1;
        var downed = Soldier(state, 2);
        Down(downed);
        Soldier(state, 20, 0); // someone left standing, so the mission goes on
        var enemy = Enemy(state, "proxy", 8, 2);
        enemy.Aim = 200;
        var e = Engine(state, new ShootAi(carrier.Id));
        e.Execute(On(carrier, "carry", downed));

        var events = e.EndPlayerPhase();

        Assert.False(carrier.IsAlive);
        var dropped = Assert.Single(events.OfType<UnitDropped>());
        Assert.Equal(downed.Id, dropped.Carried);
        Assert.True(downed.IsOnMap);
        Assert.Null(carrier.Carrying);
        Assert.Null(downed.CarriedBy);
        Assert.Equal(dropped.At, downed.Position);
        Assert.Equal(
            1,
            Math.Max(
                Math.Abs(dropped.At.X - carrier.Position.X),
                Math.Abs(dropped.At.Y - carrier.Position.Y)
            )
        );
        Assert.Equal(downed, state.UnitAt(dropped.At));
    }

    // ------------------------------------------------------------------ capture and evac

    private static string[] EvacYard() => ["........", "......EE", "........"];

    [Fact]
    public void ACapturedProxyCarriedToEvacCountsAsCaptured()
    {
        var state = Board(EvacYard());
        var soldier = Soldier(state, 5, 1, items: "arc_stunner");
        var proxy = Enemy(state, "proxy", 6, 1, hp: 5);
        proxy.Hp = 3;
        var e = Engine(state);

        e.Execute(On(soldier, "arc_stunner", proxy));
        Assert.True(proxy.Statuses.Capturable);
        e.Execute(On(soldier, "carry", proxy));
        Assert.Equal(proxy, soldier.Carrying);
        // Stay stunned through the turns it takes to get there.
        Assert.DoesNotContain(
            e.EndPlayerPhase(),
            ev => ev is StatusRemoved { Status: StatusFlags.Stunned }
        );
        Assert.True(proxy.Statuses.Stunned);

        e.Execute(new MoveCommand(soldier.Id, new GridPos(6, 1)));
        var events = e.Execute(Self(soldier, "evac"));

        Assert.True(proxy.Evacuated);
        Assert.Equal(2, events.OfType<UnitEvacuated>().Count());
        var ended = Assert.IsType<MissionEnded>(events[^1]);
        Assert.Equal(MissionOutcome.Success, ended.Result.Outcome);
        Assert.Equal(["proxy"], ended.Result.Captured);
        Assert.Empty(ended.Result.EnemiesKilled);
    }

    [Fact]
    public void EvacuatingACarriedSoldierExtractsBoth()
    {
        var state = Board(EvacYard());
        var carrier = Soldier(state, 6, 1);
        var downed = Soldier(state, 5, 1);
        Down(downed);
        Enemy(state, "husk", 0, 0);
        var e = Engine(state);

        e.Execute(On(carrier, "carry", downed));
        var events = e.Execute(Self(carrier, "evac"));

        Assert.True(carrier.Evacuated);
        Assert.True(downed.Evacuated);
        Assert.Null(carrier.Carrying);
        Assert.Equal(2, events.OfType<UnitEvacuated>().Count());
    }

    [Fact]
    public void ACapturableProxyMustBeHurtBeforeItIsStunnedForGood()
    {
        var state = Board(Open());
        var soldier = Soldier(state, 3, items: "arc_stunner");
        var proxy = Enemy(state, "proxy", 4, hp: 5);
        proxy.Hp = 3;
        var e = Engine(state);

        e.Execute(On(soldier, "arc_stunner", proxy));
        for (var i = 0; i < 3; i++)
            e.EndPlayerPhase();

        Assert.True(proxy.Statuses.Stunned);
        Assert.True(proxy.IsAlive);
        soldier.Items[0].Charges = 1;
        Assert.Contains("already stunned", e.Validate(On(soldier, "arc_stunner", proxy)).Reason);
    }

    // ------------------------------------------------------------------ MapVersion

    [Fact]
    public void MapVersionBumpsOnEveryTileChangeAndOpensAPathThroughADestroyedCrate()
    {
        var state = Board(["##########", "....c.....", "##########"]);
        var thrower = Soldier(state, 1, 1, items: "frag_grenade");
        var e = Engine(state);
        var version = state.MapVersion;
        var cutOff = Pathfinder.Reachable(state, thrower, 40);
        Assert.False(cutOff.Contains(new GridPos(7, 1)));

        e.Execute(Throw(thrower, "frag_grenade", 4, 1));

        Assert.Equal(version + 1, state.MapVersion);
        var open = Pathfinder.Reachable(state, thrower, 40);
        Assert.True(open.Contains(new GridPos(7, 1)));
        state.Map.SetTile(new GridPos(7, 1), TileType.Floor); // no change: no bump
        Assert.Equal(version + 1, state.MapVersion);
    }

    [Fact]
    public void VisionFollowsTheDestroyedCover()
    {
        var state = Board(["...........", "...C.......", "..........."]);
        var thrower = Soldier(state, 1, 1, items: "frag_grenade");
        thrower.Sight = 12;
        var enemy = Enemy(state, "husk", 9, 1);
        var e = Engine(state);
        Assert.False(state.PlayerVision.IsVisible(enemy.Position));

        // A big crate blocks sight; the first grenade only breaks it down to a small one (half cover).
        e.Execute(Throw(thrower, "frag_grenade", 3, 1));

        Assert.Equal(TileType.SmallCrate, state.Map[new GridPos(3, 1)]);
        Assert.True(state.PlayerVision.IsVisible(enemy.Position));
    }

    // ------------------------------------------------------------------ data

    [Fact]
    public void EveryItemHasAnAbilityAndEveryNonPassiveItemAHandler()
    {
        var registry = AbilityRegistry.CreateDefault();
        foreach (var item in Data.Items.Where(i => i.Kind != ItemKind.Passive))
        {
            Assert.True(Data.Abilities.Contains(item.Id), $"{item.Id} has no ability");
            Assert.True(registry.TryGet(item.Id, out _), $"{item.Id} has no handler");
        }
    }

    [Fact]
    public void ReplayingTheSameCommandsGivesTheSameState()
    {
        ulong Run()
        {
            var state = Board(Open(), new Pcg32Random(5));
            var thrower = Soldier(state, 1, items: "smoke_grenade");
            Enemy(state, "husk", 9);
            var e = Engine(state);
            e.Execute(Throw(thrower, "smoke_grenade", 6));
            e.EndPlayerPhase();
            return state.StateHash();
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void SmokeIsPartOfTheStateHash()
    {
        var state = Board(Open());
        Soldier(state, 1);
        Enemy(state, "husk", 9);
        var before = state.StateHash();

        state.AddSmoke([new GridPos(4, 2)], 2, 20);

        Assert.NotEqual(before, state.StateHash());
    }

    // ------------------------------------------------------------------ review fixes

    private static TacticalUnit Sleeping(TacticalState state, string defId, int x, int y = 2)
    {
        var husk = Enemy(state, defId, x, y);
        var podId = state.AddPod("Test pod", husk.Position);
        state.Pods.Single(p => p.Id == podId).Members.Add(husk.Id);
        husk.Pod = podId;
        husk.IsActive = false;
        return husk;
    }

    [Fact]
    public void ARupturedTargetTakesTheExtraDamageFromExplosivesAndThePreviewSaysSo()
    {
        var state = Board(Open());
        var thrower = Soldier(state, 1, items: "frag_grenade");
        var husk = Enemy(state, "husk", 6, hp: 5);
        husk.Statuses.Set(StatusFlags.Ruptured);
        var e = Engine(state);

        var preview = e.PreviewBlast(thrower, "frag_grenade", new GridPos(6, 2))!;
        var victim = Assert.Single(preview.Victims);
        Assert.Equal(6, victim.Damage);
        Assert.True(victim.Kills);

        e.Execute(Throw(thrower, "frag_grenade", 6));
        Assert.False(husk.IsAlive);
    }

    [Fact]
    public void ARupturedRobotTakesTheExtraDamageFromTheArcStunner()
    {
        var state = Board(Open());
        var soldier = Soldier(state, 3, items: "arc_stunner");
        var warden = Enemy(state, "warden", 4, hp: 10);
        warden.Armor = 0;
        warden.Statuses.Set(StatusFlags.Ruptured);
        var e = Engine(state);
        Assert.Equal(5, e.PreviewShot(soldier, warden, "arc_stunner")!.DamageMax);

        e.Execute(On(soldier, "arc_stunner", warden));

        Assert.Equal(5, warden.Hp);
    }

    [Fact]
    public void AFragOnlyWakesPodsWithAMemberInsideTheBlast()
    {
        var state = Board(Open());
        var thrower = Soldier(state, 1, items: "frag_grenade");
        var outside = Sleeping(state, "husk", 12);
        outside.Sight = 3; // too short-sighted to notice the squad on its own // two tiles straight out from the impact at x = 10
        var e = Engine(state);

        e.Execute(Throw(thrower, "frag_grenade", 10));

        Assert.False(state.Pods.Single().Active);
        Assert.False(outside.IsActive);
        Assert.Equal(outside.Hp, outside.MaxHp);
    }

    [Fact]
    public void AJammedSleepingHuskWakesItsPodButNeverMovesWhileStunned()
    {
        var state = Board(Open());
        var thrower = Soldier(state, 1, items: "lace_jammer");
        var jammed = Sleeping(state, "husk", 9);
        var other = Enemy(state, "husk", 10);
        state.Pods.Single().Members.Add(other.Id);
        other.Pod = jammed.Pod;
        other.IsActive = false;
        var e = Engine(state);

        var events = e.Execute(Throw(thrower, "lace_jammer", 9));

        Assert.True(state.Pods.Single().Active);
        Assert.True(jammed.Statuses.Stunned);
        Assert.Equal(new GridPos(9, 2), jammed.Position);
        Assert.DoesNotContain(
            events,
            ev => ev is UnitScampered { Unit: var id } && id == jammed.Id
        );
        e.EndPlayerPhase();
        Assert.False(jammed.Statuses.Stunned);
    }

    [Fact]
    public void ADormantStunnedMemberDoesNotPatrol()
    {
        var state = Board(Open());
        Soldier(state, 20, 4);
        var sleeper = Sleeping(state, "husk", 10);
        sleeper.Statuses.Set(StatusFlags.Stunned);
        var e = Engine(state);

        e.EndPlayerPhase();

        Assert.Equal(new GridPos(10, 2), sleeper.Position);
    }

    [Fact]
    public void OverwatchIsRefusedWhileCarrying()
    {
        var state = Board(Open());
        var carrier = Soldier(state, 3);
        var downed = Soldier(state, 4);
        Down(downed);
        Enemy(state, "husk", 20);
        var e = Engine(state);
        e.Execute(On(carrier, "carry", downed));

        Assert.Contains("carrying", e.Validate(Self(carrier, "overwatch")).Reason);
    }

    [Fact]
    public void AGrenadeAlsoHitsWhoeverIsCarriedByAUnitInTheBlast()
    {
        var state = Board(Open());
        var thrower = Soldier(state, 1, items: "frag_grenade");
        var carrier = Soldier(state, 6);
        var downed = Soldier(state, 7);
        Down(downed);
        Enemy(state, "husk", 20);
        var e = Engine(state);
        e.Execute(On(carrier, "carry", downed));
        Assert.False(downed.IsOnMap);

        var preview = e.PreviewBlast(thrower, "frag_grenade", new GridPos(6, 2))!;
        Assert.Equal(2, preview.Victims.Count);
        e.Execute(Throw(thrower, "frag_grenade", 6));

        Assert.False(downed.IsAlive);
        Assert.Null(carrier.Carrying);
        Assert.Equal(27, carrier.Hp);
    }
}
