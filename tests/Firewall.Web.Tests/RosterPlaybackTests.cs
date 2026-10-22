using System.Numerics;
using Firewall.Rules;
using Firewall.Rules.Tactical;
using Firewall.Web.Playback;
using Firewall.Web.Units;

namespace Firewall.Web.Tests;

/// <summary>Playback of the roster's new events (#29): summons and suppression.</summary>
public class RosterPlaybackTests
{
    private static readonly UnitId SoldierId = new(1);
    private static readonly UnitId EnforcerId = new(2);
    private static readonly UnitId SpawnId = new(3);

    private static (EventPlayer Player, ViewModel Vm) Setup(bool visible)
    {
        var vm = new ViewModel();
        vm.Units[SoldierId] = new UnitVm(
            new TacticalUnit(SoldierId, Faction.Firewall, "x", "S")
            {
                Position = new(2, 2),
                Hp = 4,
                MaxHp = 4,
            },
            false,
            Vector2.UnitX
        );
        vm.Units[EnforcerId] = new UnitVm(
            new TacticalUnit(EnforcerId, Faction.Meridian, "enforcer", "E")
            {
                Position = new(8, 2),
                Hp = 10,
                MaxHp = 10,
            },
            true,
            -Vector2.UnitX
        );
        var ctx = new EventPlayerContext { IsTileVisible = _ => visible };
        return (new EventPlayer(vm, ctx, AnimationFactory.Create), vm);
    }

    private static void RunToEnd(EventPlayer player)
    {
        for (var i = 0; i < 1000 && player.IsBusy; i++)
            player.Update(0.016f);
        Assert.False(player.IsBusy);
    }

    [Fact]
    public void ASummonedUnitJoinsTheViewModelAndFadesIn()
    {
        var (player, vm) = Setup(visible: true);
        player.Enqueue([
            new UnitSpawned(SpawnId, "husk", "zombie1", false, new(5, 5), 4, 0, EnforcerId),
        ]);

        player.Update(0.1f);
        var spawned = vm.Units[SpawnId];
        Assert.InRange(spawned.Alpha, 0.01f, 0.99f);
        Assert.Equal(new GridPos(5, 5), spawned.Tile);
        RunToEnd(player);

        Assert.Equal(1f, spawned.Alpha);
        Assert.Equal((4, 4), (spawned.Hp, spawned.MaxHp));
        Assert.Contains(vm.Floaters, f => f.Text == "SUMMONED");
    }

    [Fact]
    public void ASummonOutOfSightAppearsAtOnceWithoutEffects()
    {
        var (player, vm) = Setup(visible: false);
        player.Enqueue([
            new UnitSpawned(SpawnId, "husk", "zombie1", false, new(5, 5), 4, 0, EnforcerId),
        ]);

        player.Update(0.016f);

        Assert.False(player.IsBusy);
        Assert.Equal(1f, vm.Units[SpawnId].Alpha);
        Assert.Empty(vm.Effects);
    }

    [Fact]
    public void SuppressionLinksTheTargetToItsSuppressorUntilTheStatusEnds()
    {
        var (player, vm) = Setup(visible: true);
        player.Enqueue([
            new AbilityUsed(EnforcerId, "suppression", Target.Unit(SoldierId)),
            new StatusAdded(SoldierId, StatusFlags.Suppressed),
        ]);
        RunToEnd(player);
        Assert.Equal(EnforcerId, vm.Units[SoldierId].SuppressedBy);
        Assert.True(vm.Units[SoldierId].Statuses.Suppressed);

        player.Enqueue([new StatusRemoved(SoldierId, StatusFlags.Suppressed)]);
        RunToEnd(player);
        Assert.Null(vm.Units[SoldierId].SuppressedBy);
        Assert.False(vm.Units[SoldierId].Statuses.Suppressed);
    }

    [Fact]
    public void SuppressedUnitsShowTheirStatusIcon()
    {
        var statuses = new StatusSet();
        statuses.Set(StatusFlags.Suppressed);
        Assert.Equal(["status.suppressed"], UnitPresentation.StatusIcons(statuses));
    }

    [Fact]
    public void RelayAndSuppressionPulsesStayInBounds()
    {
        for (var t = 0f; t < 3f; t += 0.05f)
        {
            Assert.InRange(UnitPresentation.RelayPulse(t), 0.54f, 1.001f);
            Assert.InRange(UnitPresentation.SuppressionPulse(t), 0.34f, 0.91f);
        }
    }
}
