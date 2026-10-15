using System.Numerics;
using Firewall.Rules;
using Firewall.Rules.Data;
using Firewall.Rules.Rng;
using Firewall.Rules.Tactical;
using Firewall.Web.Units;

namespace Firewall.Web.Tests;

public class UnitPresentationTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private static readonly GameData Data = GameData.Load(f =>
        File.ReadAllText(Path.Combine(DataDir, f))
    );

    private static TacticalUnit Soldier(WeaponClass? weapon, int x = 0, int y = 0)
    {
        var unit = new TacticalUnit(new UnitId(1), Faction.Firewall, "soldier", "S")
        {
            Position = new GridPos(x, y),
            Hp = 4,
            MaxHp = 4,
            Sight = 12,
            AppearanceId = "soldier1",
        };
        if (weapon is { } w)
            unit.Weapon = new WeaponState(Data.Weapon(w, 1), 4);
        return unit;
    }

    private static TacticalUnit Enemy(string defId, int x, int y) =>
        new(new UnitId(x * 100 + y + 2), Faction.Meridian, defId, defId)
        {
            Position = new GridPos(x, y),
            Hp = 4,
            MaxHp = 4,
            Sight = 9,
            AppearanceId = Data.Enemies[defId].Sprite.Set,
        };

    private static TacticalState Empty(int w = 11, int h = 11)
    {
        var rows = Enumerable.Range(0, h).Select(_ => new string('.', w)).ToList();
        var map = MapParser.Parse(
            new MapFile
            {
                Id = "m",
                Name = "m",
                Biome = Biome.Industrial,
                Rows = rows,
            },
            Data.Tiles
        );
        return new TacticalState(map, new Pcg32Random(1));
    }

    [Theory]
    [InlineData(WeaponClass.Rifle, "machine")]
    [InlineData(WeaponClass.Shotgun, "machine")]
    [InlineData(WeaponClass.Lmg, "machine")]
    [InlineData(WeaponClass.Sniper, "silencer")]
    public void WeaponClassPicksPose(WeaponClass weapon, string pose) =>
        Assert.Equal(pose, UnitPresentation.PoseFor(Soldier(weapon), null, reloading: false));

    [Fact]
    public void ReloadingStunnedAndUnarmedSoldiers()
    {
        Assert.Equal("reload", UnitPresentation.PoseFor(Soldier(WeaponClass.Rifle), null, true));
        Assert.Equal("hold", UnitPresentation.PoseFor(Soldier(null), null, false));

        var stunned = Soldier(WeaponClass.Rifle);
        stunned.Statuses.Set(StatusFlags.Stunned);
        Assert.Equal("hold", UnitPresentation.PoseFor(stunned, null, true));
    }

    [Fact]
    public void EnemiesUseTheirDefinitionPose()
    {
        foreach (var def in new[] { "husk", "warden", "proxy", "enforcer" })
            Assert.Equal(
                Data.Enemies[def].Sprite.Pose,
                UnitPresentation.PoseFor(Enemy(def, 1, 1), Data.Enemies[def], false)
            );
        Assert.Equal(
            "unit.zombie1.hold",
            UnitPresentation.BodyFrame(Enemy("husk", 1, 1), Data.Enemies["husk"], false)
        );
    }

    [Fact]
    public void RotationIsAtan2OfDirection()
    {
        Assert.Equal(0f, UnitPresentation.RotationFor(Vector2.UnitX), 5);
        Assert.Equal(MathF.PI / 2f, UnitPresentation.RotationFor(Vector2.UnitY), 5);
        Assert.Equal(MathF.PI, UnitPresentation.RotationFor(new Vector2(-1f, 0f)), 5);
        Assert.Equal(-MathF.PI / 2f, UnitPresentation.RotationFor(-Vector2.UnitY), 5);
        Assert.Equal(0f, UnitPresentation.RotationFor(Vector2.Zero));
    }

    [Fact]
    public void TintParsesHexAndNull()
    {
        Assert.Null(UnitPresentation.ParseTint(null));
        var c = UnitPresentation.ParseTint("#e04040")!.Value;
        Assert.Equal((224, 64, 64, 255), (c.R, c.G, c.B, c.A));
        Assert.Throws<FormatException>(() => UnitPresentation.ParseTint("red"));
        Assert.Throws<FormatException>(() => UnitPresentation.ParseTint("#12zz56"));
    }

    [Fact]
    public void EveryShippedEnemyTintParses() =>
        Assert.All(Data.Enemies, e => UnitPresentation.ParseTint(e.Sprite.Tint));

    [Fact]
    public void FacesTheCenterWithoutEnemiesInSight()
    {
        var state = Empty();
        var unit = Soldier(WeaponClass.Rifle, 0, 0);
        state.AddUnit(unit);
        var dir = UnitPresentation.InitialFacing(unit, state);
        Assert.Equal(new Vector2(5f, 5f), dir);
    }

    [Fact]
    public void FacesTheNearestEnemyInSight()
    {
        var state = Empty();
        var unit = Soldier(WeaponClass.Rifle, 5, 0);
        state.AddUnit(unit);
        state.AddUnit(Enemy("husk", 9, 0)); // 4 away
        state.AddUnit(Enemy("husk", 5, 2)); // 2 away
        Assert.Equal(new Vector2(0f, 2f), UnitPresentation.InitialFacing(unit, state));
    }

    [Fact]
    public void IgnoresEnemiesBeyondSightAndTheDead()
    {
        var state = Empty(40, 11);
        var unit = Soldier(WeaponClass.Rifle, 0, 5);
        unit.Sight = 5;
        state.AddUnit(unit);
        state.AddUnit(Enemy("husk", 30, 5)); // out of sight
        var dead = Enemy("husk", 2, 5);
        dead.IsAlive = false;
        state.AddUnit(dead);
        var dir = UnitPresentation.InitialFacing(unit, state);
        Assert.Equal(new Vector2(19.5f, 0f), dir); // toward the map centre
    }

    [Fact]
    public void FacingNeverZero()
    {
        var state = Empty();
        var unit = Soldier(WeaponClass.Rifle, 5, 5); // on the centre
        state.AddUnit(unit);
        Assert.Equal(Vector2.UnitX, UnitPresentation.InitialFacing(unit, state));
    }

    [Fact]
    public void PipsAreFilledEmptyThenArmor()
    {
        Assert.Equal(
            [PipKind.Filled, PipKind.Filled, PipKind.Empty, PipKind.Empty, PipKind.Armor],
            UnitPresentation.Pips(2, 4, 1)
        );
        Assert.Empty(UnitPresentation.Pips(0, 0, 0));
    }

    [Fact]
    public void StatusIconsFollowStatuses()
    {
        var s = new StatusSet();
        Assert.Empty(UnitPresentation.StatusIcons(s));
        s.Set(StatusFlags.Overwatch);
        s.Set(StatusFlags.Hunkered);
        s.BleedingOutTurnsLeft = 2;
        Assert.Equal(
            ["status.overwatch", "status.hunker", "status.bleeding"],
            UnitPresentation.StatusIcons(s)
        );
    }

    [Fact]
    public void RingPulsesWithinBounds()
    {
        for (var t = 0f; t < 3f; t += 0.05f)
            Assert.InRange(UnitPresentation.RingAlpha(t), 0.44f, 1.001f);
    }

    [Fact]
    public void TooltipShowsNameHpArmorAndEnemyType()
    {
        var husk = Enemy("husk", 1, 1);
        husk.DisplayName = "Husk B";
        husk.Armor = 1;
        Assert.Equal(
            ["Husk B", "Husk", "HP 4/4", "Armor 1"],
            UnitPresentation.TooltipLines(husk, Data.Enemies["husk"])
        );
        Assert.Equal(["S", "HP 4/4"], UnitPresentation.TooltipLines(Soldier(null), null));
    }
}
