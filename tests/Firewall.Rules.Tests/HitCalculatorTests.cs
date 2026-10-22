using Firewall.Rules.Data;
using Firewall.Rules.Rng;
using Firewall.Rules.Tactical;

namespace Firewall.Rules.Tests;

public class HitCalculatorTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private static readonly GameData Data = GameData.Load(f =>
        File.ReadAllText(Path.Combine(DataDir, f))
    );

    // Three rows of floor, 20 wide; an optional crate at (cx, 1).
    private static TacticalState Yard(int? crateX = null)
    {
        var middle = new string('.', 20).ToCharArray();
        if (crateX is { } cx)
            middle[cx] = 'c';
        var map = MapParser.Parse(
            new MapFile
            {
                Id = "t",
                Name = "T",
                Biome = Biome.Industrial,
                Rows = [new string('.', 20), new string(middle), new string('.', 20)],
            },
            Data.Tiles
        );
        return new TacticalState(map, new Pcg32Random(7));
    }

    private static TacticalUnit Rookie(TacticalState state, int x, string weaponId = "rifle_t1")
    {
        var weapon = Data.Weapons[weaponId];
        var unit = new TacticalUnit(state.NextUnitId(), Faction.Firewall, "soldier", "Rookie")
        {
            Position = new GridPos(x, 1),
            Hp = 4,
            MaxHp = 4,
            Aim = 65,
            Mobility = 6,
            Sight = 20,
            ActionPoints = 2,
            Weapon = new WeaponState(weapon, weapon.Clip),
            Abilities = { AbilityRegistry.Shoot },
        };
        state.AddUnit(unit);
        return unit;
    }

    private static TacticalUnit Enemy(TacticalState state, string defId, int x)
    {
        var def = Data.Enemies[defId];
        var unit = new TacticalUnit(state.NextUnitId(), Faction.Meridian, defId, def.Name)
        {
            Position = new GridPos(x, 1),
            Hp = def.Hp,
            MaxHp = def.Hp,
            Aim = def.Aim,
            Mobility = def.Mobility,
            Defense = def.Defense,
            Armor = def.Armor,
            Sight = def.Sight,
            ActionPoints = 2,
        };
        foreach (var a in def.Abilities)
            unit.Abilities.Add(a);
        state.AddUnit(unit);
        return unit;
    }

    private static void AssertAddsUp(ShotPreview p)
    {
        Assert.Equal(Math.Clamp(p.RawHitChance, 0, 100), p.HitChance);
        Assert.Equal(Math.Clamp(p.CritBreakdown.Sum(b => b.Value), 0, 100), p.CritChance);
    }

    [Fact]
    public void RookieRifleAt8TilesVsWardenInHalfCover_Is35()
    {
        var state = Yard(crateX: 7);
        var rookie = Rookie(state, 0);
        var warden = Enemy(state, "warden", 8);

        var p = HitCalculator.Preview(state, rookie, warden, ShotContext.Default);

        Assert.Equal(35, p.HitChance);
        Assert.Equal(
            [("Aim", 65), ("Range", 0), ("Target Defense", -10), ("Half Cover", -20)],
            p.Breakdown
        );
        Assert.False(p.Flanked);
        Assert.Equal(10, p.CritChance);
        Assert.Equal((3, 5), (p.DamageMin, p.DamageMax));
        Assert.True(p.CanFire);
        AssertAddsUp(p);
    }

    [Fact]
    public void SameWardenFlanked_Hit55Crit60()
    {
        var state = Yard(crateX: 7);
        var rookie = Rookie(state, 16); // east of the Warden; its crate is to the west
        var warden = Enemy(state, "warden", 8);

        var p = HitCalculator.Preview(state, rookie, warden, ShotContext.Default);

        Assert.Equal(55, p.HitChance);
        Assert.True(p.Flanked);
        Assert.Equal(60, p.CritChance);
        Assert.Equal([("Weapon", 10), ("Flanked", 50)], p.CritBreakdown);
        AssertAddsUp(p);
    }

    [Fact]
    public void ShotgunAt2TilesVsHuskInNoCover_Is95()
    {
        var state = Yard();
        var rookie = Rookie(state, 0, "shotgun_t1");
        var husk = Enemy(state, "husk", 2);

        var p = HitCalculator.Preview(state, rookie, husk, ShotContext.Default);

        Assert.Equal(95, p.HitChance);
        Assert.Equal([("Aim", 65), ("Range", 30)], p.Breakdown);
        AssertAddsUp(p);
    }

    [Theory]
    [InlineData(13, 0)] // beyond 12 tiles: no penalty
    [InlineData(3, -20)]
    public void SniperRangeModifier(int distance, int mod)
    {
        var state = Yard();
        var sniper = Rookie(state, 0, "sniper_t1");
        var husk = Enemy(state, "husk", distance);

        var p = HitCalculator.Preview(state, sniper, husk, ShotContext.Default);

        Assert.Equal(mod, p.Breakdown.Single(b => b.Label == "Range").Value);
        Assert.Equal(65 + mod, p.HitChance);
    }

    [Fact]
    public void SniperAfterMoving_CannotFire()
    {
        var state = Yard();
        var sniper = Rookie(state, 0, "sniper_t1");
        var husk = Enemy(state, "husk", 10);
        sniper.MovedThisTurn = true;

        var p = HitCalculator.Preview(state, sniper, husk, ShotContext.Default);

        Assert.False(p.CanFire);
        Assert.Equal("Sniper rifles can't fire after moving", p.Reason);
    }

    [Fact]
    public void OverwatchVsWardenInHalfCover_Is25WithNoCrit()
    {
        // (65 + 0 - 10 - 20) x 0.7 = 24.5, rounded half away from zero.
        var state = Yard(crateX: 7);
        var rookie = Rookie(state, 0);
        var warden = Enemy(state, "warden", 8);

        var p = HitCalculator.Preview(
            state,
            rookie,
            warden,
            ShotContext.Default with
            {
                Reaction = true,
            }
        );

        Assert.Equal(25, p.HitChance);
        Assert.Equal(("Overwatch x0.7", -10), p.Breakdown[^1]);
        Assert.Equal(0, p.CritChance);
        AssertAddsUp(p);
    }

    [Fact]
    public void HunkeredDoublesCoverAndSuppressionAndModsAddUp()
    {
        var state = Yard(crateX: 7);
        var rookie = Rookie(state, 0);
        var warden = Enemy(state, "warden", 8);
        warden.Statuses.Set(StatusFlags.Hunkered);
        rookie.Statuses.Set(StatusFlags.Suppressed);

        var p = HitCalculator.Preview(
            state,
            rookie,
            warden,
            ShotContext.Default with
            {
                AimMods = [("Targeting Scope", 10)],
            }
        );

        Assert.Contains(("Hunkered", -20), p.Breakdown);
        Assert.Contains(("Suppressed", -30), p.Breakdown);
        Assert.Contains(("Targeting Scope", 10), p.Breakdown);
        Assert.Equal(-5, p.RawHitChance); // 65 - 10 - 20 - 20 - 30 + 10
        Assert.Equal(0, p.HitChance);
        AssertAddsUp(p);
    }

    [Theory]
    [InlineData(3, false, 1, 2)] // rolled 3 against armor 1 deals 2
    [InlineData(1, false, 2, 1)] // rolled 1 against armor 2 deals the minimum 1
    [InlineData(5, true, 0, 8)] // crit: ceil(7.5) = 8
    [InlineData(4, true, 0, 6)]
    public void DamageAppliesCritThenArmorWithMinimumOne(
        int rolled,
        bool crit,
        int armor,
        int expected
    ) => Assert.Equal(expected, HitCalculator.Damage(rolled, crit, armor));

    [Theory]
    [InlineData(35, 25)]
    [InlineData(-35, -25)]
    [InlineData(10, 7)]
    [InlineData(0, 0)]
    public void RoundPercentRoundsHalfAwayFromZero(int value, int expected) =>
        Assert.Equal(expected, HitCalculator.RoundPercent(value, 70));

    [Fact]
    public void KnownSeedProducesTheExpectedHitCritDamageTriple()
    {
        var preview = new ShotPreview(
            80,
            50,
            3,
            5,
            [("Aim", 80)],
            [("Weapon", 50)],
            false,
            true,
            null
        );

        // Rolls come off the stream in this order: hit, crit, damage.
        var manual = new Pcg32Random(2024);
        var hitRoll = manual.Next(0, 100);
        var critRoll = manual.Next(0, 100);
        var damageRoll = manual.Next(3, 6);

        var outcome = HitCalculator.Roll(preview, 1, new Pcg32Random(2024));

        Assert.Equal(hitRoll < 80, outcome.Hit);
        Assert.Equal(critRoll < 50, outcome.Crit);
        Assert.Equal(damageRoll, outcome.Rolled);
        // Rolled 3, crit ceil(4.5) = 5, minus armor 1.
        Assert.Equal(new ShotOutcome(true, true, 3, 4), outcome);
    }

    [Fact]
    public void AMissRollsNothingElse()
    {
        var preview = new ShotPreview(0, 100, 3, 5, [("Aim", 0)], [], false, true, null);
        var rng = new Pcg32Random(3);
        Assert.Equal(new ShotOutcome(false, false, 0, 0), HitCalculator.Roll(preview, 0, rng));

        var reference = new Pcg32Random(3);
        reference.Next(0, 100);
        Assert.Equal(reference.Next(0, 1000), rng.Next(0, 1000));
    }

    [Fact]
    public void ShootingConsumesAmmoSpendsTheTurnAndKills()
    {
        var state = Yard();
        var rookie = Rookie(state, 0, "shotgun_t1");
        var husk = Enemy(state, "husk", 1);
        husk.Hp = 1;
        rookie.Aim = 100;
        var engine = new TacticalEngine(state, Data);

        var events = engine.Execute(
            new UseAbilityCommand(rookie.Id, AbilityRegistry.Shoot, Target.Unit(husk.Id))
        );

        Assert.Equal(0, rookie.ActionPoints);
        Assert.Equal(3, rookie.Weapon!.Ammo);
        Assert.False(husk.IsAlive);
        var kinds = events
            .Where(e => e is UnitFacing or ShotResolved or UnitDamaged or AmmoChanged or UnitKilled)
            .Select(e => e.GetType())
            .ToList();
        Assert.Equal(
            [
                typeof(UnitFacing),
                typeof(ShotResolved),
                typeof(UnitDamaged),
                typeof(AmmoChanged),
                typeof(UnitKilled),
            ],
            kinds
        );
        Assert.True(Assert.IsType<ShotResolved>(events.First(e => e is ShotResolved)).Hit);
    }

    [Fact]
    public void EmptyClipCannotFire()
    {
        var state = Yard();
        var rookie = Rookie(state, 0);
        var husk = Enemy(state, "husk", 5);
        rookie.Weapon!.Ammo = 0;
        var engine = new TacticalEngine(state, Data);

        var result = engine.Validate(
            new UseAbilityCommand(rookie.Id, AbilityRegistry.Shoot, Target.Unit(husk.Id))
        );

        Assert.False(result.IsValid);
        Assert.Equal("Out of ammo.", result.Reason);
    }

    [Fact]
    public void NoLineOfSightCannotFire()
    {
        var state = Yard();
        var rookie = Rookie(state, 0);
        var husk = Enemy(state, "husk", 5);
        state.Map.SetTile(new GridPos(2, 0), TileType.Wall);
        state.Map.SetTile(new GridPos(2, 1), TileType.Wall);
        state.Map.SetTile(new GridPos(2, 2), TileType.Wall);

        var p = HitCalculator.Preview(state, rookie, husk, ShotContext.Default);

        Assert.False(p.CanFire);
        Assert.Contains("line of sight", p.Reason);
    }

    [Fact]
    public void MeleeNeedsAdjacencyAndIgnoresCover()
    {
        var state = Yard(crateX: 3);
        var soldier = Rookie(state, 2);
        var husk = Enemy(state, "husk", 4);
        var ctx = ShotContext.For(Data, husk, RangeType.Melee);

        var far = HitCalculator.Preview(state, husk, soldier, ctx);
        Assert.False(far.CanFire);

        husk.Position = new GridPos(3, 2); // diagonal neighbour; the crate is next to the soldier
        var near = HitCalculator.Preview(state, husk, soldier, ctx);
        Assert.True(near.CanFire);
        Assert.Equal(85, near.HitChance);
        Assert.Equal([("Aim", 85)], near.Breakdown);
        Assert.Equal((2, 3), (near.DamageMin, near.DamageMax));
    }

    [Fact]
    public void ReplayOfShotsReproducesTheStateHash()
    {
        TacticalState Make(ulong seed)
        {
            var s = Yard();
            Rookie(s, 0);
            Rookie(s, 1);
            Enemy(s, "warden", 9);
            return s;
        }

        var engine = new TacticalEngine(Make(0), Data);
        var warden = engine.State.Units[^1];
        foreach (var soldier in engine.State.Units.Where(u => u.Faction == Faction.Firewall))
            engine.Execute(
                new UseAbilityCommand(soldier.Id, AbilityRegistry.Shoot, Target.Unit(warden.Id))
            );

        var replayed = TacticalEngine.Replay(0, engine.Log.Commands, Make, Data);
        Assert.Equal(engine.State.StateHash(), replayed.State.StateHash());
    }

    [Fact]
    public void HitChanceIsClampedAtBothEnds()
    {
        var state = Yard();
        var rookie = Rookie(state, 0, "shotgun_t1");
        var husk = Enemy(state, "husk", 2);

        var high = HitCalculator.Preview(
            state,
            rookie,
            husk,
            ShotContext.Default with
            {
                AimMods = [("Overclock", 50)],
            }
        );
        Assert.True(high.RawHitChance > 100);
        Assert.Equal(100, high.HitChance);

        var low = HitCalculator.Preview(
            state,
            rookie,
            husk,
            ShotContext.Default with
            {
                AimMods = [("Panic", -200)],
            }
        );
        Assert.True(low.RawHitChance < 0);
        Assert.Equal(0, low.HitChance);
    }

    [Fact]
    public void CritModifiersAddToTheWeaponAndFlankBonusAndClamp()
    {
        var state = Yard(crateX: 7);
        var rookie = Rookie(state, 16); // flanks the warden
        var warden = Enemy(state, "warden", 8);

        var p = HitCalculator.Preview(
            state,
            rookie,
            warden,
            ShotContext.Default with
            {
                CritMods = [("Laser Sight", 15)],
            }
        );
        Assert.Equal([("Weapon", 10), ("Flanked", 50), ("Laser Sight", 15)], p.CritBreakdown);
        Assert.Equal(75, p.CritChance);

        var capped = HitCalculator.Preview(
            state,
            rookie,
            warden,
            ShotContext.Default with
            {
                CritMods = [("Executioner", 80)],
            }
        );
        Assert.Equal(100, capped.CritChance);
    }

    [Fact]
    public void ReactionShotsOnlyCritWithSentinel()
    {
        var state = Yard(crateX: 7);
        var rookie = Rookie(state, 16);
        var warden = Enemy(state, "warden", 8);

        var plain = HitCalculator.Preview(
            state,
            rookie,
            warden,
            ShotContext.Default with
            {
                Reaction = true,
            }
        );
        var sentinel = HitCalculator.Preview(
            state,
            rookie,
            warden,
            ShotContext.Default with
            {
                Reaction = true,
                ReactionCrit = true,
            }
        );

        Assert.Equal(0, plain.CritChance);
        Assert.Equal(60, sentinel.CritChance);
    }
}
