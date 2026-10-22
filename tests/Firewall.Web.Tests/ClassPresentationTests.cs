using System.Text.Json;
using Firewall.Rules;
using Firewall.Rules.Data;
using Firewall.Rules.Rng;
using Firewall.Rules.Tactical;
using Firewall.Web.Assets;
using Firewall.Web.Scenes;
using Firewall.Web.Ui;
using Firewall.Web.Units;

namespace Firewall.Web.Tests;

/// <summary>The squad builder, the ability bar and the targeting for the class abilities (#30).</summary>
public class ClassPresentationTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private static readonly GameData Data = GameData.Load(f =>
        File.ReadAllText(Path.Combine(DataDir, f))
    );

    private static TacticalState Yard()
    {
        var map = MapParser.Parse(
            new MapFile
            {
                Id = "t",
                Name = "T",
                Biome = Biome.Industrial,
                Rows = [new string('.', 20), new string('.', 20), new string('.', 20)],
            },
            Data.Tiles
        );
        return new TacticalState(map, new Pcg32Random(1));
    }

    private static TacticalUnit Soldier(TacticalState state, string classId, int x)
    {
        var unit = SoldierBuilder.Build(
            state,
            Data,
            SoldierSpec.Default(Data, classId, Rank.Captain),
            classId,
            "soldier1"
        );
        unit.Position = new GridPos(x, 1);
        state.AddUnit(unit);
        return unit;
    }

    private static TacticalUnit Enemy(TacticalState state, string defId, int x)
    {
        var def = Data.Enemies[defId];
        var unit = PodSpawner.CreateUnit(state, def, def.Name);
        unit.Position = new GridPos(x, 1);
        state.AddUnit(unit);
        return unit;
    }

    // ------------------------------------------------------------------ squad builder

    [Fact]
    public void TheDefaultSquadIsOneCaptainOfEachClass()
    {
        var model = SquadBuilderModel.Default(Data);

        Assert.Equal(
            ["breacher", "marksman", "gunner", "tech"],
            model.Specs.Select(s => s.ClassId)
        );
        Assert.All(model.Specs, s => Assert.Empty(SoldierBuilder.Validate(Data, s)));
        Assert.All(model.Specs, s => Assert.Equal(Rank.Captain, s.Rank));
    }

    [Fact]
    public void EveryEditKeepsTheSoldierLegal()
    {
        var model = SquadBuilderModel.Default(Data);
        for (var step = 0; step < 12; step++)
        {
            for (var i = 0; i < SquadBuilderModel.Size; i++)
            {
                model.CycleClass(i, 1);
                model.CycleRank(i, step % 2 == 0 ? 1 : -1);
                model.CycleWeaponTier(i, 1);
                model.CycleArmor(i, 1);
                model.CycleItem(i, 0, 1);
                model.CycleItem(i, 1, 1);
                Assert.Empty(SoldierBuilder.Validate(Data, model.Specs[i]));
            }
        }
    }

    [Fact]
    public void ARookieHasNoRanksOrAbilities()
    {
        var model = SquadBuilderModel.Default(Data);

        model.CycleClass(0, -1); // Breacher -> Rookie
        Assert.Null(model.Specs[0].ClassId);
        Assert.Equal(Rank.Rookie, model.Specs[0].Rank);
        Assert.Empty(model.Specs[0].Picks);
        model.CycleRank(0, 1);
        Assert.Equal(Rank.Rookie, model.Specs[0].Rank);

        model.CycleClass(0, 1); // and back to a Breacher at Corporal
        Assert.Equal("breacher", model.Specs[0].ClassId);
        Assert.Equal(Rank.Corporal, model.Specs[0].Rank);
    }

    [Fact]
    public void RankChangesRefitThePicksAndKeepWhatStillFits()
    {
        var model = SquadBuilderModel.Default(Data);
        Assert.True(model.SetPick(0, 1, "hit_and_run"));
        Assert.Equal(["close_combat", "hit_and_run", "untouchable"], model.Specs[0].Picks);

        model.CycleRank(0, -1); // Captain -> Lieutenant
        Assert.Equal(Rank.Lieutenant, model.Specs[0].Rank);
        Assert.Equal(["close_combat", "hit_and_run"], model.Specs[0].Picks);

        model.CycleRank(0, 1);
        Assert.Equal(3, model.Specs[0].Picks.Count);
        Assert.Equal("hit_and_run", model.Specs[0].Picks[1]);
    }

    [Fact]
    public void OnlyAbilitiesOfThatRankCanBePicked()
    {
        var model = SquadBuilderModel.Default(Data);

        Assert.False(model.SetPick(0, 0, "rapid_fire")); // a Lieutenant ability
        Assert.False(model.SetPick(0, 5, "close_combat"));
        Assert.True(model.SetPick(0, 0, "lightning_reflexes"));
        Assert.Equal("lightning_reflexes", model.Specs[0].Picks[0]);
    }

    [Fact]
    public void OnlyTheTechGetsASecondUtilitySlot()
    {
        var model = SquadBuilderModel.Default(Data);

        Assert.Equal(1, model.ItemSlots(0));
        Assert.Equal(2, model.ItemSlots(3));
        model.CycleItem(3, 1, 1);
        Assert.NotNull(model.ItemAt(3, 0) ?? model.ItemAt(3, 1));
        model.CycleItem(0, 1, 1); // no second slot
        Assert.Equal(0, model.Specs[0].ItemIds.Count);

        // Losing the rank takes the second item away.
        model.CycleItem(3, 0, 1);
        model.CycleItem(3, 1, 1);
        Assert.Equal(2, model.Specs[3].ItemIds.Count);
        model.CycleRank(3, 1); // Captain -> Corporal
        Assert.Single(model.Specs[3].ItemIds);
    }

    [Fact]
    public void EverySquadBuilderSoldierBuildsOnTheScenesScratchMap()
    {
        // The scene reads each soldier's stats off a one-tile map.
        var map = MapParser.Parse(
            new MapFile
            {
                Id = "stub",
                Name = "Stub",
                Biome = Biome.None,
                Rows = ["."],
            },
            Data.Tiles
        );
        var state = new TacticalState(map, new Pcg32Random(1));
        foreach (var spec in SquadBuilderModel.Default(Data).Specs)
            Assert.NotNull(SoldierBuilder.Build(state, Data, spec, "", ""));
    }

    // ------------------------------------------------------------------ ability bar

    [Fact]
    public void EveryClassBarFitsTheNineHotkeys()
    {
        foreach (var cls in Data.Classes)
        {
            var unit = Soldier(Yard(), cls.Id, 0);
            var slots = HudModel.AbilitySlots(unit, Data, onEvacTile: true, canStabilize: true);
            Assert.True(
                slots.Count <= HudModel.MaxSlots,
                $"{cls.Id} has {slots.Count} abilities for the bar"
            );
            Assert.Equal(
                unit.Abilities.Count(a => Data.Abilities[a].Targeting != TargetingType.Passive),
                slots.Count
            );
        }
    }

    [Fact]
    public void ActiveClassAbilitiesGetSlotsWithIconsAndFilledInText()
    {
        var unit = Soldier(Yard(), "breacher", 0);
        var slots = HudModel.AbilitySlots(unit, Data, false, false);

        var runAndGun = Assert.Single(slots, s => s.AbilityId == "run_and_gun");
        Assert.Equal("icon.forward", runAndGun.Icon);
        Assert.Contains("1 AP", runAndGun.Description);
        Assert.Contains("3 turns", runAndGun.Description);
        Assert.All(slots, s => Assert.DoesNotContain("{", s.Description));
        // Passives are not on the bar.
        Assert.DoesNotContain(slots, s => s.AbilityId == "close_combat");
    }

    [Fact]
    public void EveryAbilityIconExistsInTheUiAtlas()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Firewall.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var manifest = JsonSerializer.Deserialize(
            File.ReadAllText(
                Path.Combine(dir.FullName, "src", "Firewall.Web", "wwwroot", "assets", "ui.json")
            ),
            AssetJsonContext.Default.UiManifest
        )!;
        var regions = manifest.Regions.Select(r => r.Key).ToHashSet();

        foreach (var def in Data.Abilities)
            Assert.True(
                def.Icon is not null && regions.Contains(def.Icon),
                $"{def.Id} icon '{def.Icon}'"
            );
        foreach (var cls in Data.Classes)
            Assert.Contains(cls.Icon, regions);
    }

    // ------------------------------------------------------------------ targeting

    [Fact]
    public void OverclockTargetsSquadmatesAndNeverTheCaster()
    {
        var state = Yard();
        var tech = Soldier(state, "tech", 0);
        tech.Abilities.Add("overclock");
        var breacher = Soldier(state, "breacher", 2);
        Enemy(state, "husk", 8);
        var engine = new TacticalEngine(state, Data);

        var mode = TargetingMode.TryBegin(engine, tech, _ => true, out _, "overclock")!;

        Assert.True(mode.TargetsAllies);
        Assert.Equal([breacher.Id], mode.Candidates.Select(c => c.Unit.Id));
        Assert.Null(mode.Current.Preview);
        Assert.Equal(
            new UseAbilityCommand(tech.Id, "overclock", Target.Unit(breacher.Id)),
            mode.Command
        );
    }

    [Fact]
    public void HackCandidatesShowTheirSuccessChance()
    {
        var state = Yard();
        var tech = Soldier(state, "tech", 0);
        tech.Abilities.Add("hack");
        var warden = Enemy(state, "warden", 5);
        Enemy(state, "husk", 4); // not a robot
        var engine = new TacticalEngine(state, Data);

        var mode = TargetingMode.TryBegin(engine, tech, _ => true, out _, "hack")!;

        var candidate = Assert.Single(mode.Candidates);
        Assert.Equal(warden.Id, candidate.Unit.Id);
        Assert.Equal(55, candidate.Preview!.HitChance); // 50 + 5 x 4 - 15 x tier 1
        Assert.False(mode.TargetsAllies);
    }

    [Fact]
    public void SuppressionHasATargetButNoOdds()
    {
        var state = Yard();
        var gunner = Soldier(state, "gunner", 0);
        var husk = Enemy(state, "husk", 6);
        var engine = new TacticalEngine(state, Data);

        var mode = TargetingMode.TryBegin(engine, gunner, _ => true, out _, "suppression")!;

        Assert.Equal(husk.Id, mode.Current.Unit.Id);
        Assert.Null(mode.Current.Preview);
    }

    [Fact]
    public void RapidFireTargetingShowsTheShotOdds()
    {
        var state = Yard();
        var breacher = Soldier(state, "breacher", 0);
        Enemy(state, "husk", 3);
        var engine = new TacticalEngine(state, Data);

        var plain = TargetingMode.TryBegin(engine, breacher, _ => true, out _)!;
        var rapid = TargetingMode.TryBegin(engine, breacher, _ => true, out _, "rapid_fire")!;

        Assert.Equal(plain.Current.Preview!.HitChance - 15, rapid.Current.Preview!.HitChance);
    }

    [Fact]
    public void AnAbilityOnCooldownGivesItsReason()
    {
        var state = Yard();
        var tech = Soldier(state, "tech", 0);
        tech.Abilities.Add("hack");
        Enemy(state, "warden", 5);
        var engine = new TacticalEngine(state, Data);
        tech.Cooldowns["hack"] = 2;

        Assert.Null(TargetingMode.TryBegin(engine, tech, _ => true, out var reason, "hack"));
        Assert.Contains("cooldown", reason);
    }
}
