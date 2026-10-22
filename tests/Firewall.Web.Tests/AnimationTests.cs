using System.Numerics;
using Firewall.Rules;
using Firewall.Rules.Tactical;
using Firewall.Web.Playback;

namespace Firewall.Web.Tests;

public class AnimationTests
{
    private sealed class RecordingCamera : ICameraDirector
    {
        public int Moves { get; private set; }

        public void Focus(Vector2 world, float seconds = 0.35f) => Moves++;

        public void Frame(Vector2 a, Vector2 b, float seconds = 0.35f) => Moves++;
    }

    private static readonly UnitId SoldierId = new(1);
    private static readonly UnitId EnemyId = new(2);

    private static TacticalUnit Make(UnitId id, Faction faction, GridPos at, int hp = 6) =>
        new(id, faction, "x", $"U{id.Value}")
        {
            Position = at,
            Hp = hp,
            MaxHp = hp,
            AppearanceId = faction == Faction.Firewall ? "soldier1" : "zombie1",
        };

    private static (
        EventPlayer Player,
        ViewModel Vm,
        RecordingCamera Camera,
        HashSet<GridPos> Seen
    ) Setup(bool robot = false)
    {
        var vm = new ViewModel();
        vm.Units[SoldierId] = new UnitVm(
            Make(SoldierId, Faction.Firewall, new(2, 2)),
            false,
            Vector2.UnitX
        );
        vm.Units[EnemyId] = new UnitVm(
            Make(EnemyId, Faction.Meridian, new(8, 2)),
            robot,
            -Vector2.UnitX
        );
        var camera = new RecordingCamera();
        var seen = new HashSet<GridPos>();
        var ctx = new EventPlayerContext { Camera = camera, IsTileVisible = seen.Contains };
        return (new EventPlayer(vm, ctx, AnimationFactory.Create), vm, camera, seen);
    }

    private static void RunToEnd(EventPlayer player)
    {
        for (var i = 0; i < 1000 && player.IsBusy; i++)
            player.Update(0.016f);
        Assert.False(player.IsBusy);
    }

    [Fact]
    public void HiddenEnemyMovesInstantlyAndTheCameraStaysPut()
    {
        var (player, vm, camera, _) = Setup();
        player.Enqueue([new UnitMoved(EnemyId, [new(8, 2), new(7, 2), new(6, 2)], false)]);

        player.Update(0.016f);

        Assert.False(player.IsBusy);
        Assert.Equal(new GridPos(6, 2), vm.Units[EnemyId].Tile);
        Assert.Equal(new Vector2(6.5f, 2.5f), vm.Units[EnemyId].Center);
        Assert.Equal(0, camera.Moves);
    }

    [Fact]
    public void VisibleEnemyWalksTheWholePathFacingEachStep()
    {
        var (player, vm, camera, seen) = Setup();
        seen.Add(new GridPos(8, 2));
        player.Enqueue([new UnitMoved(EnemyId, [new(8, 2), new(8, 3), new(7, 3)], false)]);

        player.Update(0.016f);
        Assert.True(player.IsBusy);
        Assert.True(vm.Units[EnemyId].ForceVisible);
        RunToEnd(player);

        var enemy = vm.Units[EnemyId];
        Assert.Equal(new GridPos(7, 3), enemy.Tile);
        Assert.False(enemy.ForceVisible);
        Assert.Equal(MathF.PI, MathF.Abs(enemy.Rotation), 3); // last step was west
        Assert.True(camera.Moves > 0);
    }

    [Fact]
    public void DashIsFasterThanAWalk()
    {
        float Seconds(bool dash)
        {
            var (player, _, _, _) = Setup();
            player.Enqueue([
                new UnitMoved(SoldierId, [new(2, 2), new(3, 2), new(4, 2), new(5, 2)], dash),
            ]);
            var t = 0f;
            while (player.IsBusy)
            {
                player.Update(0.01f);
                t += 0.01f;
            }
            return t;
        }

        Assert.InRange(Seconds(false), 0.35f, 0.40f);
        Assert.InRange(Seconds(true), 0.26f, 0.30f);
    }

    [Fact]
    public void ShotFiresAMuzzleFlashAndTracerThenFadesThem()
    {
        var (player, vm, camera, _) = Setup();
        player.Enqueue([new ShotResolved(SoldierId, EnemyId, 70, true, false, 4)]);

        player.Update(0.05f);
        Assert.Empty(vm.Effects); // still winding up
        player.Update(0.07f);
        Assert.Contains(vm.Effects, e => e.Kind == EffectKind.MuzzleFlash);
        var tracer = Assert.Single(vm.Effects, e => e.Kind == EffectKind.Tracer);
        Assert.Equal(vm.Units[EnemyId].Center, tracer.To); // a hit ends on the target
        Assert.Equal(1, camera.Moves);
        RunToEnd(player);

        Assert.Empty(vm.Effects);
        Assert.Empty(vm.Floaters);
        Assert.Equal(0f, vm.Units[SoldierId].Rotation, 3); // faces the enemy to the east
    }

    [Fact]
    public void MissedShotLandsBesideTheTargetAndSaysMissed()
    {
        var (player, vm, _, _) = Setup();
        player.Enqueue([new ShotResolved(SoldierId, EnemyId, 40, false, false, 0)]);

        player.Update(0.11f);
        var tracer = Assert.Single(vm.Effects, e => e.Kind == EffectKind.Tracer);
        Assert.NotEqual(vm.Units[EnemyId].Center, tracer.To);
        Assert.True(Vector2.Distance(tracer.To, vm.Units[EnemyId].Center) < 1f);
        RunToEnd(player);

        Assert.Equal("MISSED", Assert.Single(vm.Floaters).Text);
    }

    [Fact]
    public void HiddenEnemyShootingHiddenGroundShowsNothing()
    {
        var (player, vm, camera, _) = Setup();
        var other = new UnitId(3);
        vm.Units[other] = new UnitVm(
            Make(other, Faction.Meridian, new(9, 2)),
            false,
            Vector2.UnitX
        );
        player.Enqueue([new ShotResolved(EnemyId, other, 50, false, false, 0)]);

        player.Update(0.016f);

        Assert.False(player.IsBusy);
        Assert.Empty(vm.Effects);
        Assert.Empty(vm.Floaters);
        Assert.Equal(0, camera.Moves);
    }

    [Fact]
    public void EnemyShootingASoldierIsShownEvenFromTheFog()
    {
        var (player, vm, camera, _) = Setup();
        player.Enqueue([new ShotResolved(EnemyId, SoldierId, 50, true, false, 3)]);

        player.Update(0.11f);

        Assert.Contains(vm.Effects, e => e.Kind == EffectKind.Tracer);
        Assert.Equal(1, camera.Moves);
    }

    [Fact]
    public void DamageDrainsHpFlashesAndFloatsTheNumber()
    {
        var (player, vm, _, _) = Setup();
        player.Enqueue([new UnitDamaged(EnemyId, 4, 2, 0)]);
        vm.Units[EnemyId].SnapTo(new GridPos(8, 2));
        var seen = new HashSet<GridPos> { new(8, 2) };
        player = new EventPlayer(
            vm,
            new EventPlayerContext { IsTileVisible = seen.Contains },
            AnimationFactory.Create
        );
        player.Enqueue([new UnitDamaged(EnemyId, 4, 2, 0)]);

        player.Update(0.05f);
        Assert.True(vm.Units[EnemyId].FlashAmount > 0f);
        RunToEnd(player);

        Assert.Equal(2, vm.Units[EnemyId].Hp);
        Assert.Equal(0f, vm.Units[EnemyId].FlashAmount);
        Assert.Equal("-4", Assert.Single(vm.Floaters).Text);
    }

    [Fact]
    public void CritAfterAShotReadsCritAndArmorLossGetsItsOwnText()
    {
        var (_, vm, _, _) = Setup();
        vm.Units[SoldierId].Armor = 2;
        var player = new EventPlayer(vm, new EventPlayerContext(), AnimationFactory.Create);
        player.Enqueue([
            new ShotResolved(EnemyId, SoldierId, 60, true, true, 8),
            new UnitDamaged(SoldierId, 8, 0, 1),
        ]);

        RunToEnd(player);

        Assert.Contains(vm.Floaters, f => f.Text == "CRIT -8" && f.Size > 22);
        Assert.Contains(vm.Floaters, f => f.Text == "-1 ARMOR");
        Assert.Equal(1, vm.Units[SoldierId].Armor);
        // Both sit over the same spot, one stacked above the other.
        Assert.Equal([0, 1], vm.Floaters.Select(f => f.StackSlot).Order());
    }

    [Fact]
    public void KilledBodyDarkensToFortyPercentAndFallsOver()
    {
        var (_, vm, _, _) = Setup();
        var player = new EventPlayer(vm, new EventPlayerContext(), AnimationFactory.Create);
        var before = vm.Units[SoldierId].Rotation;
        player.Enqueue([new UnitKilled(SoldierId)]);

        player.Update(0.016f);
        Assert.True(vm.Units[SoldierId].Dead);
        RunToEnd(player);

        var soldier = vm.Units[SoldierId];
        Assert.Equal(CorpseKind.Body, soldier.Corpse);
        Assert.Equal(0.4f, soldier.Brightness, 3);
        Assert.Equal(before + MathF.PI / 2f, soldier.Rotation, 3);
    }

    [Fact]
    public void KilledRobotLeavesScrap()
    {
        var (_, vm, _, _) = Setup(robot: true);
        var player = new EventPlayer(vm, new EventPlayerContext(), AnimationFactory.Create);
        player.Enqueue([new UnitKilled(EnemyId)]);

        RunToEnd(player);

        Assert.Equal(CorpseKind.Scrap, vm.Units[EnemyId].Corpse);
    }

    [Fact]
    public void BleedingOutDropsTheSoldierOnceAndOnlyCountsDownAfter()
    {
        var (player, vm, _, _) = Setup();
        var before = vm.Units[SoldierId].Rotation;
        player.Enqueue([new UnitBleedingOut(SoldierId, Casualties.BleedOutTurns)]);
        RunToEnd(player);

        var soldier = vm.Units[SoldierId];
        Assert.Equal(before + MathF.PI / 2f, soldier.Rotation, 3);
        Assert.True(soldier.Statuses.IsBleedingOut);
        Assert.Equal("BLEEDING OUT", Assert.Single(vm.Floaters).Text);

        player.Enqueue([new UnitBleedingOut(SoldierId, 2)]);
        RunToEnd(player);

        Assert.Equal(2, soldier.Statuses.BleedingOutTurnsLeft);
        Assert.Equal(before + MathF.PI / 2f, soldier.Rotation, 3);
    }

    [Fact]
    public void ReactionFireIsAnnouncedBeforeTheShot()
    {
        var (player, vm, _, _) = Setup();
        player.Enqueue([
            new ReactionShotTriggered(SoldierId, EnemyId),
            new ShotResolved(SoldierId, EnemyId, 60, true, false, 3),
        ]);

        player.Update(0.05f);

        Assert.Equal("REACTION FIRE", Assert.Single(vm.Floaters).Text);
        Assert.Empty(vm.Effects);
    }

    [Fact]
    public void PodRevealShowsItsNameThenScampersAtOneAndAHalfSpeed()
    {
        var (player, vm, camera, seen) = Setup();
        seen.Add(new GridPos(8, 2));
        player.Enqueue([
            new PodActivated(new PodId(1), "Husk Swarm", new GridPos(8, 2), [EnemyId]),
            new UnitScampered(EnemyId, [new(8, 2), new(7, 2), new(6, 2), new(5, 2)]),
        ]);

        player.Update(0.1f);
        Assert.Equal("HUSK SWARM", vm.PodBanner);
        Assert.True(camera.Moves > 0);
        var t = 0.1f;
        while (vm.PodBanner is not null)
        {
            player.Update(0.01f);
            t += 0.01f;
        }
        Assert.InRange(t, 1.19f, 1.25f);
        var scamper = 0f;
        while (player.IsBusy)
        {
            player.Update(0.01f);
            scamper += 0.01f;
        }
        // 3 tiles at 0.12 s / 1.5 each = 0.24 s.
        Assert.InRange(scamper, 0.2f, 0.27f);
    }

    [Fact]
    public void EnemyPhaseBannerHoldsPlaybackButThePlayerPhaseDoesNot()
    {
        var (player, vm, _, _) = Setup();
        player.Enqueue([new PhaseStarted(TacticalPhase.Enemy, 1)]);
        player.Update(0.1f);

        Assert.True(player.IsBusy);
        Assert.Equal(TacticalPhase.Enemy, vm.Banner!.Phase);
        RunToEnd(player);

        player.Enqueue([new PhaseStarted(TacticalPhase.Player, 2)]);
        player.Update(0.016f);

        Assert.False(player.IsBusy);
        Assert.NotNull(vm.Banner);
    }

    [Fact]
    public void EnemyPhaseWithSixVisibleActorsFitsInTenSeconds()
    {
        var (_, vm, _, _) = Setup();
        var ctx = new EventPlayerContext { IsTileVisible = _ => true };
        var player = new EventPlayer(vm, ctx, AnimationFactory.Create);
        var events = new List<GameEvent> { new PhaseStarted(TacticalPhase.Enemy, 1) };
        for (var i = 0; i < 6; i++)
        {
            events.Add(
                new UnitMoved(
                    EnemyId,
                    [new(8, 2), new(7, 2), new(6, 2), new(5, 2), new(4, 2)],
                    false
                )
            );
            events.Add(new UnitFacing(EnemyId, new GridPos(2, 2)));
            events.Add(new ShotResolved(EnemyId, SoldierId, 60, true, false, 2));
            events.Add(new UnitDamaged(SoldierId, 2, 4, 0));
        }
        events.Add(new PhaseStarted(TacticalPhase.Player, 2));
        player.Enqueue(events);

        var seconds = 0f;
        while (player.IsBusy)
        {
            player.Update(0.01f);
            seconds += 0.01f;
        }

        Assert.True(seconds <= 10f, $"took {seconds:F1} s");
    }
}
