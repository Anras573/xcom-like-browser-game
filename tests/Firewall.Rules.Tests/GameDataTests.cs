using Firewall.Rules.Data;

namespace Firewall.Rules.Tests;

public class GameDataTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private static string Read(string file) => File.ReadAllText(Path.Combine(DataDir, file));

    private static GameData Load(Func<string, string?>? patch = null) =>
        GameData.Load(f => patch?.Invoke(f) ?? Read(f));

    private static readonly GameData Real = Load();

    [Fact]
    public void RealDataLoadsAndValidates()
    {
        Assert.Empty(Real.Validate());
    }

    [Fact]
    public void EveryDataFileIsListed()
    {
        var onDisk = Directory.GetFiles(DataDir, "*.json").Select(Path.GetFileName).Order();
        Assert.Equal(onDisk, GameDataFiles.All.Order());
    }

    [Fact]
    public void WeaponsMatchGdd()
    {
        var rifle = Real.Weapon(WeaponClass.Rifle, 1);
        Assert.Equal((3, 5, 4, 10), (rifle.DmgMin, rifle.DmgMax, rifle.Clip, rifle.Crit));
        var rifle3 = Real.Weapon(WeaponClass.Rifle, 3);
        Assert.Equal((7, 9), (rifle3.DmgMin, rifle3.DmgMax));
        var shotgun = Real.Weapon(WeaponClass.Shotgun, 2);
        Assert.Equal((6, 8, 4, 20), (shotgun.DmgMin, shotgun.DmgMax, shotgun.Clip, shotgun.Crit));
        var sniper = Real.Weapon(WeaponClass.Sniper, 1);
        Assert.Equal((4, 6, 3, 25), (sniper.DmgMin, sniper.DmgMax, sniper.Clip, sniper.Crit));
        Assert.Contains(WeaponFlag.NoFireAfterMove, sniper.Flags);
        var lmg = Real.Weapon(WeaponClass.Lmg, 3);
        Assert.Equal((8, 10, 3, 0), (lmg.DmgMin, lmg.DmgMax, lmg.Clip, lmg.Crit));
    }

    [Fact]
    public void RangeTablesMatchGdd()
    {
        var rifle = Real.Weapon(WeaponClass.Rifle, 1);
        Assert.Equal(10, rifle.RangeMod(3));
        Assert.Equal(0, rifle.RangeMod(12));
        Assert.Equal(-3, rifle.RangeMod(13));
        Assert.Equal(-9, rifle.RangeMod(15));

        var shotgun = Real.Weapon(WeaponClass.Shotgun, 1);
        Assert.Equal(30, shotgun.RangeMod(2));
        Assert.Equal(15, shotgun.RangeMod(4));
        Assert.Equal(0, shotgun.RangeMod(7));
        Assert.Equal(-10, shotgun.RangeMod(9));

        var sniper = Real.Weapon(WeaponClass.Sniper, 1);
        Assert.Equal(-20, sniper.RangeMod(4));
        Assert.Equal(0, sniper.RangeMod(5));

        var lmg = Real.Weapon(WeaponClass.Lmg, 1);
        Assert.Equal(-5, lmg.RangeMod(1));
        Assert.Equal(-5, lmg.RangeMod(12));
        Assert.Equal(-9, lmg.RangeMod(14));
    }

    [Fact]
    public void EnemiesMatchGdd()
    {
        var warden = Real.Enemies["warden"];
        Assert.Equal((6, 60, 1), (warden.Hp, warden.Aim, warden.Armor));
        Assert.Equal(
            (2, 4, 6, 10, 11),
            (warden.DmgMin, warden.DmgMax, warden.Mobility, warden.Defense, warden.Sight)
        );

        var husk = Real.Enemies["husk"];
        Assert.Equal((4, 85, 8, 9), (husk.Hp, husk.Aim, husk.Mobility, husk.Sight));
        Assert.Equal((3, 4), (husk.PodMin, husk.PodMax));
        Assert.Empty(husk.Loot);

        var guardian = Real.Enemies["cradle_guardian"];
        Assert.Equal((30, 75, 3, 14), (guardian.Hp, guardian.Aim, guardian.Armor, guardian.Sight));
        Assert.Equal("warden", guardian.Summon!.EnemyId);

        var relay = Real.Enemies["relay_node"];
        Assert.Equal(EnemyKind.Object, relay.Kind);
        Assert.Equal((12, 1), (relay.Hp, relay.Armor));

        var enforcer = Real.Enemies["enforcer"];
        Assert.Contains(enforcer.Loot, l => l.Resource == Resource.Cores && l.ChancePercent == 20);
        Assert.True(Real.Enemies["proxy"].Capturable);
    }

    [Fact]
    public void SoldierClassesAndArmorMatchGdd()
    {
        var s = Real.Soldier;
        Assert.Equal(
            (4, 65, 3, 6, 0, 12),
            (s.Hp, s.Aim, s.AimPerRank, s.Mobility, s.Defense, s.Sight)
        );
        Assert.Equal([0, 2, 6, 14, 26], s.Ranks.Select(r => r.Xp));

        Assert.Equal(4, Real.Classes.Count);
        var tech = Real.Classes["tech"];
        Assert.Equal(WeaponClass.Rifle, tech.WeaponClass);
        Assert.Equal(Rank.Sergeant, tech.ExtraUtilitySlotRank);
        Assert.Equal(
            ["hack", "sprinter"],
            tech.AbilityTree.Single(r => r.Rank == Rank.Lieutenant).Abilities
        );

        var aegis = Real.Armor["aegis_rig"];
        Assert.Equal(
            (5, 1, 1, "aegis_composites"),
            (aegis.Hp, aegis.Armor, aegis.Mobility, aegis.UnlockedBy)
        );
    }

    [Fact]
    public void ResearchAndFacilitiesMatchGdd()
    {
        Assert.Equal(11, Real.Research.Count);
        Assert.Equal(14, Real.Research["arc_weapons"].BaseDays);
        Assert.Equal(["relay_hunt"], Real.Research["proxy_interrogation"].Unlocks);
        Assert.Equal(["hard_shutdown"], Real.Research["meridian_killcode"].Unlocks);

        var infirmary = Real.Facilities["infirmary"];
        Assert.Equal((120, 10), (infirmary.Cost, infirmary.BuildDays));
        Assert.Equal(2, Real.Facilities["lab_annex"].MaxCount);
        Assert.Equal(3, Real.Facilities["generator"].Effects["powersFacilities"]);
        Assert.Equal(7, Real.Districts.Count);
    }

    [Fact]
    public void UnknownFieldsAreRejected()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            Load(f => f == GameDataFiles.Armor ? Read(f).Replace("\"hp\"", "\"hitPoints\"") : null)
        );
        Assert.Contains("armor.json", ex.Message);
    }

    private static IReadOnlyList<string> ValidateWith(string file, Func<string, string> edit) =>
        Load(f => f == file ? edit(Read(f)) : null).Validate();

    [Fact]
    public void MissingRequiredListsAreRejected()
    {
        var noRange = Assert.Throws<InvalidDataException>(() =>
            Load(f => f == GameDataFiles.Weapons ? Read(f).Replace("\"range\"", "\"rng\"") : null)
        );
        Assert.Contains("weapons.json", noRange.Message);

        var noNames = Assert.Throws<InvalidDataException>(() =>
            Load(f =>
                f == GameDataFiles.Names ? Read(f).Replace("\"lastNames\"", "\"surnames\"") : null
            )
        );
        Assert.Contains("names.json", noNames.Message);
    }

    [Fact]
    public void ValidationCatchesBrokenFixtures()
    {
        // dmgMin > dmgMax
        Assert.Contains(
            ValidateWith(GameDataFiles.Weapons, t => t.Replace("\"dmgMin\": 3", "\"dmgMin\": 6")),
            e => e.Contains("rifle_t1") && e.Contains("dmgMin")
        );

        // negative clip
        Assert.Contains(
            ValidateWith(GameDataFiles.Weapons, t => t.Replace("\"clip\": 4", "\"clip\": -4")),
            e => e.Contains("clip")
        );

        // duplicate id
        Assert.Contains(
            ValidateWith(
                GameDataFiles.Armor,
                t => t.Replace("\"salvage_plating\"", "\"tac_vest\"")
            ),
            e => e.Contains("duplicate armor id 'tac_vest'")
        );

        // research unlocking an unknown item
        Assert.Contains(
            ValidateWith(
                GameDataFiles.Research,
                t => t.Replace("\"lace_jammer\"", "\"missing_item\"")
            ),
            e => e.Contains("husk_autopsy") && e.Contains("missing_item")
        );

        // class referencing an unknown ability
        Assert.Contains(
            ValidateWith(GameDataFiles.Classes, t => t.Replace("\"run_and_gun\"", "\"nope\"")),
            e => e.Contains("breacher") && e.Contains("nope")
        );

        // enemy summoning an unknown enemy
        Assert.Contains(
            ValidateWith(
                GameDataFiles.Enemies,
                t => t.Replace("\"enemyId\": \"warden\"", "\"enemyId\": \"ghost\"")
            ),
            e => e.Contains("ghost")
        );

        // armor unlocked by unknown research
        Assert.Contains(
            ValidateWith(GameDataFiles.Armor, t => t.Replace("\"drone_teardown\"", "\"nope\"")),
            e => e.Contains("salvage_plating") && e.Contains("nope")
        );
    }
}
