using System.Numerics;
using Firewall.Rules;
using Firewall.Rules.Data;
using Firewall.Rules.Rng;
using Firewall.Rules.Tactical;
using Firewall.Web.Input;
using Firewall.Web.Ui;

namespace Firewall.Web.Tests;

public class HudModelTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private static readonly GameData Data = GameData.Load(f =>
        File.ReadAllText(Path.Combine(DataDir, f))
    );

    private static TacticalUnit Soldier(int id, int ap = 2)
    {
        var unit = new TacticalUnit(new UnitId(id), Faction.Firewall, "soldier", $"Sol {id}")
        {
            Hp = 5,
            MaxHp = 5,
            ActionPoints = ap,
        };
        unit.Abilities.UnionWith(AbilityRegistry.StandardActions);
        return unit;
    }

    [Fact]
    public void BarStartsWithTheFourStandardActionsInOrder()
    {
        var slots = HudModel.AbilitySlots(Soldier(1), Data, onEvacTile: false);
        Assert.Equal(
            [
                AbilityRegistry.Shoot,
                AbilityRegistry.Overwatch,
                AbilityRegistry.Reload,
                AbilityRegistry.HunkerDown,
            ],
            slots.Select(s => s.AbilityId)
        );
        Assert.Equal([1, 2, 3, 4], slots.Select(s => s.Slot));
        Assert.Equal(GameAction.Ability2, slots[1].Key);
    }

    [Fact]
    public void EvacAppearsOnlyOnTheEvacTile()
    {
        var slots = HudModel.AbilitySlots(Soldier(1), Data, onEvacTile: true);
        Assert.Equal([AbilityRegistry.Evac], slots.Skip(4).Select(s => s.AbilityId));
        Assert.Equal(5, slots[4].Slot);
    }

    [Fact]
    public void ItemsFollowTheStandardActionsWithTheirChargesAndCarryComesAfter()
    {
        var unit = Soldier(1);
        unit.Abilities.UnionWith([AbilityRegistry.Carry, AbilityRegistry.Drop]);
        unit.Abilities.UnionWith([AbilityRegistry.Medkit, AbilityRegistry.FragGrenade]);
        unit.Items.Add(new ItemState(AbilityRegistry.FragGrenade, 1));
        unit.Items.Add(new ItemState(AbilityRegistry.Medkit, 2));

        var slots = HudModel.AbilitySlots(unit, Data, onEvacTile: false);

        Assert.Equal(
            [AbilityRegistry.FragGrenade, AbilityRegistry.Medkit, AbilityRegistry.Carry],
            slots.Skip(4).Select(s => s.AbilityId)
        );
        Assert.Equal(1, slots[4].Charges);
        Assert.Equal(2, slots[5].Charges);
        Assert.Null(slots[6].Charges);
        Assert.Contains("Charges", HudModel.AbilityTooltip(slots[5], null));
    }

    [Fact]
    public void DropReplacesCarryWhileCarrying()
    {
        var unit = Soldier(1);
        unit.Abilities.UnionWith([AbilityRegistry.Carry, AbilityRegistry.Drop]);
        unit.Carrying = Soldier(2);

        var ids = HudModel
            .AbilitySlots(unit, Data, onEvacTile: false)
            .Select(s => s.AbilityId)
            .ToList();

        Assert.Contains(AbilityRegistry.Drop, ids);
        Assert.DoesNotContain(AbilityRegistry.Carry, ids);
    }

    [Fact]
    public void ClassAbilitiesFollowAndPassivesAreLeftOut()
    {
        var unit = Soldier(1);
        unit.Abilities.Add("sentinel"); // passive
        unit.Abilities.Add("rapid_fire");
        var ids = HudModel
            .AbilitySlots(unit, Data, onEvacTile: false)
            .Select(s => s.AbilityId)
            .ToList();
        Assert.Contains("rapid_fire", ids);
        Assert.DoesNotContain("sentinel", ids);
        Assert.Equal("rapid_fire", ids[4]);
    }

    [Fact]
    public void CooldownsShowOnTheirSlot()
    {
        var unit = Soldier(1);
        unit.Cooldowns[AbilityRegistry.Overwatch] = 2;
        var slots = HudModel.AbilitySlots(unit, Data, false);
        Assert.Equal(2, slots[1].Cooldown);
    }

    [Fact]
    public void TooltipExplainsWhyAnAbilityIsDisabled()
    {
        var slot = HudModel.AbilitySlots(Soldier(1), Data, false)[0];
        var tip = HudModel.AbilityTooltip(slot, "No AP left.");
        Assert.Contains("No AP left.", tip);
        Assert.Contains("[1]", tip);
        Assert.DoesNotContain("No AP", HudModel.AbilityTooltip(slot, null));
    }

    [Fact]
    public void WrapKeepsLinesWithinTheLimit()
    {
        var lines = HudModel.Wrap("one two three four five six seven", 10).ToList();
        Assert.All(lines, l => Assert.True(l.Length <= 10));
        Assert.Equal("one two three four five six seven", string.Join(' ', lines));
    }

    [Theory]
    [InlineData(2, ApPip.Full, ApPip.Full)]
    [InlineData(1, ApPip.Full, ApPip.Empty)]
    [InlineData(0, ApPip.Empty, ApPip.Empty)]
    public void ApPipsFillLeftToRight(int ap, ApPip first, ApPip second) =>
        Assert.Equal([first, second], HudModel.ApPips(ap));

    [Fact]
    public void StripStatesCoverEveryOutcome()
    {
        var u = Soldier(1);
        Assert.Equal(StripState.Ready, HudModel.StripStateOf(u));
        u.ActionPoints = 0;
        Assert.Equal(StripState.Spent, HudModel.StripStateOf(u));
        u.Statuses.BleedingOutTurnsLeft = 3;
        Assert.Equal(StripState.BleedingOut, HudModel.StripStateOf(u));
        u.Statuses.BleedingOutTurnsLeft = 0;
        u.Statuses.Set(StatusFlags.Stabilized);
        Assert.Equal(StripState.Stabilized, HudModel.StripStateOf(u));
        u.Evacuated = true;
        Assert.Equal(StripState.Evacuated, HudModel.StripStateOf(u));
        u.IsAlive = false;
        Assert.Equal(StripState.Dead, HudModel.StripStateOf(u));
    }

    [Fact]
    public void TabCyclesOnlySoldiersWithApAndWraps()
    {
        var a = Soldier(1);
        var b = Soldier(2, ap: 0);
        var c = Soldier(3);
        List<TacticalUnit> squad = [a, b, c];
        Assert.Equal(c, HudModel.NextWithAp(squad, a.Id, 1));
        Assert.Equal(a, HudModel.NextWithAp(squad, c.Id, 1));
        Assert.Equal(a, HudModel.NextWithAp(squad, c.Id, -1) is { } p && p == a ? a : null);
        Assert.Equal(c, HudModel.NextWithAp(squad, b.Id, 1));
    }

    [Fact]
    public void TabDoesNothingWhenNobodyHasAp()
    {
        List<TacticalUnit> squad = [Soldier(1, 0), Soldier(2, 0)];
        Assert.Null(HudModel.NextWithAp(squad, squad[0].Id, 1));
    }

    [Fact]
    public void SelectionStaysWhileApRemainsThenMovesOn()
    {
        var a = Soldier(1);
        var b = Soldier(2);
        List<TacticalUnit> squad = [a, b];
        Assert.Equal(a, HudModel.SelectionAfterAction(squad, a.Id));
        a.ActionPoints = 0;
        Assert.Equal(b, HudModel.SelectionAfterAction(squad, a.Id));
        b.ActionPoints = 0;
        Assert.Equal(a, HudModel.SelectionAfterAction(squad, a.Id)); // stays, for viewing
    }

    [Fact]
    public void SelectionFallsToAnyStandingSoldierWhenTheSelectedOneIsGone()
    {
        var a = Soldier(1);
        var b = Soldier(2, 0);
        a.Evacuated = true;
        Assert.Equal(b, HudModel.SelectionAfterAction([b], a.Id));
    }

    [Fact]
    public void BannerSlidesInHoldsAndSlidesOut()
    {
        var (startOffset, startAlpha) = HudModel.BannerAnimation(0f, 1f);
        var (midOffset, midAlpha) = HudModel.BannerAnimation(0.5f, 1f);
        var (endOffset, endAlpha) = HudModel.BannerAnimation(1f, 1f);
        Assert.Equal(-1f, startOffset);
        Assert.Equal(0f, startAlpha);
        Assert.Equal(0f, midOffset);
        Assert.Equal(1f, midAlpha);
        Assert.Equal(-1f, endOffset);
        Assert.Equal(0f, endAlpha);
    }

    [Fact]
    public void PhaseTextNamesTheTurn() =>
        Assert.Equal("PLAYER TURN - TURN 3", HudModel.PhaseText(TacticalPhase.Player, 3));

    [Fact]
    public void FocusPointSitsBelowTheTargetSoTheSoldierShowsAboveThePanel()
    {
        var focus = HudModel.FocusPoint(new Vector2(10f, 10f), 11f);
        Assert.Equal(10f, focus.X);
        Assert.True(focus.Y < 10f);
    }

    [Fact]
    public void PurgeObjectiveCountsKnownHostilesAndMarksUnexplored()
    {
        var map = MapParser.Parse(
            new MapFile
            {
                Id = "t",
                Name = "T",
                Biome = Biome.Industrial,
                Rows = [new string('.', 10)],
            },
            Data.Tiles
        );
        var state = new TacticalState(map, new Pcg32Random(1));
        TacticalUnit Enemy(int x, bool active)
        {
            var u = new TacticalUnit(state.NextUnitId(), Faction.Meridian, "husk", $"Husk {x}")
            {
                Position = new GridPos(x, 0),
                IsActive = active,
            };
            state.AddUnit(u);
            return u;
        }
        var seen = Enemy(1, active: false);
        Enemy(5, active: false);
        var objective = new PurgeObjective(Data);

        var text = HudModel.ObjectiveText(state, Data, objective, u => u == seen);
        Assert.Equal("Purge: 1 hostile remaining (estimated) ?", text);

        Assert.Equal(
            "Purge: 2 hostiles remaining (estimated)",
            HudModel.ObjectiveText(state, Data, objective, _ => true)
        );
    }
}
