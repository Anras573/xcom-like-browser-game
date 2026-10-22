using System.Numerics;
using Firewall.Rules;
using Firewall.Rules.Data;
using Firewall.Rules.Tactical;
using Firewall.Web.Rendering;
using Firewall.Web.Units;
using Yaeger.Graphics;

namespace Firewall.Web.Playback;

/// <summary>
/// An animation that runs for a fixed number of playback seconds. Zero duration makes it instant:
/// <see cref="Finish"/> still runs, so the end state is applied either way.
/// </summary>
public abstract class TimedAnimation : IEventAnimation
{
    private float _elapsed;

    protected ViewModel Vm { get; private set; } = null!;
    protected EventPlayerContext Ctx { get; private set; } = null!;

    /// <summary>Set by <see cref="Begin"/> once it knows whether anyone is watching.</summary>
    protected float Duration { get; set; }

    public void Start(ViewModel vm, EventPlayerContext ctx)
    {
        Vm = vm;
        Ctx = ctx;
        Begin();
    }

    public bool Update(float dt)
    {
        _elapsed += dt;
        if (_elapsed < Duration)
        {
            Tick(_elapsed);
            return false;
        }
        if (Duration > 0f)
            Tick(Duration);
        Finish();
        return true;
    }

    protected virtual void Begin() { }

    protected virtual void Tick(float t) { }

    protected virtual void Finish() { }

    protected UnitVm? Unit(UnitId id) => Vm.Units.GetValueOrDefault(id);

    /// <summary>Soldiers are always watched; an enemy only while the squad sees its tile.</summary>
    protected bool Watched(UnitVm unit) => Ctx.IsWatched(unit, unit.Tile, unit.Tile);

    protected static float Lerp(float a, float b, float t) => a + (b - a) * t;

    protected static float ShortestAngleDelta(float from, float to)
    {
        var d = (to - from) % MathF.Tau;
        if (d > MathF.PI)
            d -= MathF.Tau;
        else if (d < -MathF.PI)
            d += MathF.Tau;
        return d;
    }
}

/// <summary><see cref="UnitMoved"/> and <see cref="UnitScampered"/>: walk the path, facing each step.</summary>
public sealed class MoveAnimation(
    UnitId unit,
    IReadOnlyList<GridPos> path,
    bool dashed,
    float speedFactor = 1f
) : TimedAnimation
{
    private UnitVm? _unit;
    private MoveTween _tween = null!;
    private bool _watched;

    protected override void Begin()
    {
        _unit = Unit(unit);
        if (_unit is null || path.Count < 2)
            return;
        // Hidden enemies move without a trace: no camera, no walk.
        _watched = Ctx.IsWatched(_unit, path[0], path[^1]);
        if (!_watched)
            return;
        var perTile =
            (dashed ? MoveTween.DashSecondsPerTile : MoveTween.SecondsPerTile) / speedFactor;
        _tween = new MoveTween(path, perTile, ease: true);
        Duration = _tween.Duration;
        _unit.ForceVisible = true;
        _unit.SnapTo(path[0]);
    }

    protected override void Tick(float t)
    {
        if (_unit is null || !_watched)
            return;
        var pos = _tween.Position(t, out var dir);
        _unit.Center = pos;
        _unit.Rotation = UnitPresentation.RotationFor(dir);
        Ctx.Camera.Focus(pos, 0.2f);
    }

    protected override void Finish()
    {
        if (_unit is null || path.Count == 0)
            return;
        if (path.Count >= 2)
        {
            var from = UnitVm.TileCenter(path[^2]);
            _unit.FaceToward(from, UnitVm.TileCenter(path[^1]));
        }
        _unit.SnapTo(path[^1]);
        if (_unit.IsHostile)
            _unit.ForceVisible = false;
    }
}

/// <summary><see cref="UnitFacing"/>: turn in place.</summary>
public sealed class FacingAnimation(UnitFacing facing) : TimedAnimation
{
    public const float Seconds = 0.15f;

    private UnitVm? _unit;
    private float _from;
    private float _delta;

    protected override void Begin()
    {
        _unit = Unit(facing.Unit);
        if (_unit is null)
            return;
        var dir = UnitVm.TileCenter(facing.LookAt) - _unit.Center;
        if (dir == Vector2.Zero)
            return;
        _from = _unit.Rotation;
        _delta = ShortestAngleDelta(_from, UnitPresentation.RotationFor(dir));
        Duration = Watched(_unit) ? Seconds : 0f;
    }

    protected override void Tick(float t) =>
        _unit!.Rotation = _from + _delta * Math.Clamp(t / Seconds, 0f, 1f);

    protected override void Finish()
    {
        if (_unit is not null && _delta != 0f)
            _unit.Rotation = _from + _delta;
    }
}

/// <summary>
/// <see cref="ShotResolved"/>: the shooter turns and winds up, a muzzle flash and a tracer fire,
/// and a miss shows "MISSED". The damage that follows is its own event.
/// </summary>
public sealed class ShotAnimation(ShotResolved shot) : TimedAnimation
{
    public const float WindUp = 0.1f;
    public const float TracerSeconds = 0.12f;
    public const float MuzzleSeconds = 0.08f;
    private const float MuzzleOffset = 0.55f;

    private static readonly Color SoldierTracer = new(255, 232, 150);
    private static readonly Color EnemyTracer = new(255, 110, 70);

    private UnitVm? _shooter;
    private UnitVm? _target;
    private bool _animate;
    private bool _fired;
    private float _from;
    private float _delta;

    protected override void Begin()
    {
        Ctx.LastShot = shot;
        _shooter = Unit(shot.Attacker);
        _target = Unit(shot.Target);
        if (_shooter is null || _target is null)
            return;
        // A hidden enemy shooting a hidden target shows nothing; shooting a soldier is always shown.
        _animate = !_shooter.IsHostile || _target.Faction == Faction.Firewall || Watched(_shooter);
        _from = _shooter.Rotation;
        var dir = _target.Center - _shooter.Center;
        _delta =
            dir == Vector2.Zero ? 0f : ShortestAngleDelta(_from, UnitPresentation.RotationFor(dir));
        if (!_animate)
            return;
        _shooter.ForceVisible = true;
        Duration = WindUp + TracerSeconds;
        Ctx.Camera.Frame(_shooter.Center, _target.Center);
    }

    protected override void Tick(float t)
    {
        if (_shooter is null)
            return;
        _shooter.Rotation = _from + _delta * Math.Clamp(t / WindUp, 0f, 1f);
        if (_fired || t < WindUp)
            return;
        _fired = true;
        Fire();
    }

    private void Fire()
    {
        var shooter = _shooter!;
        var dir = new Vector2(MathF.Cos(shooter.Rotation), MathF.Sin(shooter.Rotation));
        var muzzle = shooter.Center + dir * MuzzleOffset;
        Vm.AddEffect(
            new EffectVm(EffectKind.MuzzleFlash, muzzle, muzzle + dir, MuzzleSeconds, Color.White)
            {
                Size = 0.5f,
            }
        );
        Vm.AddEffect(
            new EffectVm(
                EffectKind.Tracer,
                muzzle,
                ImpactPoint(muzzle),
                TracerSeconds,
                shooter.IsHostile ? EnemyTracer : SoldierTracer
            )
            {
                Size = 0.06f,
            }
        );
    }

    // A hit lands on the target; a miss whistles past to one side of it.
    private Vector2 ImpactPoint(Vector2 muzzle)
    {
        var target = _target!.Center;
        if (shot.Hit)
            return target;
        var along = Vector2.Normalize(target - muzzle);
        var side = new Vector2(-along.Y, along.X);
        var sign = (shot.Attacker.Value + shot.Target.Value) % 2 == 0 ? 1f : -1f;
        return target + side * sign * (0.45f + 0.1f * (shot.HitChance % 3));
    }

    protected override void Finish()
    {
        if (_shooter is null)
            return;
        _shooter.Rotation = _from + _delta;
        _shooter.ForceVisible = false;
        if (_animate && !shot.Hit)
            Vm.AddFloater("MISSED", Color.White, 22, _target!.Center + new Vector2(0f, 0.9f));
    }
}

/// <summary><see cref="UnitDamaged"/>: flash, drain the pips, float the numbers.</summary>
public sealed class DamagedAnimation(UnitDamaged damage) : TimedAnimation
{
    public const float Seconds = 0.3f;
    private const float FlashRise = 0.06f;

    private static readonly Color Red = new(255, 80, 70);
    private static readonly Color Yellow = new(255, 214, 92);
    private static readonly Color Grey = new(180, 186, 196);

    private UnitVm? _unit;
    private int _hpBefore;

    protected override void Begin()
    {
        _unit = Unit(damage.Unit);
        if (_unit is null)
            return;
        _hpBefore = _unit.Hp;
        if (!Watched(_unit))
            return;
        Duration = Seconds;
        var hpLoss = _hpBefore - damage.HpAfter;
        _unit.FlashColor = hpLoss > 0 ? Red : Color.White;
        var crit = Ctx.LastShot is { Crit: true } last && last.Target == damage.Unit;
        var above = _unit.Center + new Vector2(0f, 0.9f);
        if (damage.Amount > 0)
            Vm.AddFloater(
                crit ? $"CRIT -{damage.Amount}" : $"-{damage.Amount}",
                crit ? Yellow : Red,
                crit ? 30 : 22,
                above
            );
        if (_unit.Armor > damage.ArmorAfter)
            Vm.AddFloater($"-{_unit.Armor - damage.ArmorAfter} ARMOR", Grey, 18, above);
    }

    protected override void Tick(float t)
    {
        var unit = _unit!;
        unit.FlashAmount =
            t < FlashRise ? t / FlashRise : 1f - (t - FlashRise) / (Seconds - FlashRise);
        unit.Hp = (int)MathF.Round(Lerp(_hpBefore, damage.HpAfter, t / Seconds));
    }

    protected override void Finish()
    {
        if (_unit is null)
            return;
        _unit.FlashAmount = 0f;
        _unit.Hp = damage.HpAfter;
        _unit.Armor = damage.ArmorAfter;
    }
}

/// <summary>
/// <see cref="UnitKilled"/>: the body darkens to 40 % and falls over, staying on the ground layer;
/// a robot fades into a scrap decal instead.
/// </summary>
public sealed class KilledAnimation(UnitKilled killed) : TimedAnimation
{
    public const float Seconds = 0.4f;
    public const float CorpseBrightness = 0.4f;

    private UnitVm? _unit;
    private float _rotation;

    protected override void Begin()
    {
        _unit = Unit(killed.Unit);
        if (_unit is null)
            return;
        _unit.Dead = true;
        _rotation = _unit.Rotation;
        Duration = Watched(_unit) ? Seconds : 0f;
    }

    protected override void Tick(float t)
    {
        var k = t / Seconds;
        if (_unit!.Robot)
            _unit.Alpha = 1f - k;
        else
        {
            _unit.Brightness = Lerp(1f, CorpseBrightness, k);
            _unit.Rotation = _rotation + k * MathF.PI / 2f;
        }
    }

    protected override void Finish()
    {
        if (_unit is null)
            return;
        _unit.Alpha = 1f;
        if (_unit.Robot)
            _unit.Corpse = CorpseKind.Scrap;
        else
        {
            _unit.Corpse = CorpseKind.Body;
            _unit.Brightness = CorpseBrightness;
            _unit.Rotation = _rotation + MathF.PI / 2f;
        }
    }
}

/// <summary><see cref="UnitBleedingOut"/>: a soldier drops the first time; later ticks only count down.</summary>
public sealed class BleedingOutAnimation(UnitBleedingOut bleeding) : TimedAnimation
{
    public const float Seconds = 0.3f;

    private UnitVm? _unit;
    private float _rotation;
    private bool _drops;

    protected override void Begin()
    {
        _unit = Unit(bleeding.Unit);
        if (_unit is null)
            return;
        _unit.Statuses.BleedingOutTurnsLeft = bleeding.Turns;
        _drops = bleeding.Turns == Casualties.BleedOutTurns;
        if (!_drops)
            return;
        _rotation = _unit.Rotation;
        Duration = Seconds;
        Ctx.Camera.Focus(_unit.Center);
        Ctx.SceneEvent(bleeding);
        Vm.AddFloater(
            "BLEEDING OUT",
            new Color(255, 70, 70),
            22,
            _unit.Center + new Vector2(0f, 0.9f)
        );
    }

    protected override void Tick(float t) =>
        _unit!.Rotation = _rotation + t / Seconds * MathF.PI / 2f;

    protected override void Finish()
    {
        if (_unit is not null && _drops)
            _unit.Rotation = _rotation + MathF.PI / 2f;
    }
}

/// <summary><see cref="ReactionShotTriggered"/>: "REACTION FIRE" over the shooter before the shot.</summary>
public sealed class ReactionAnimation(ReactionShotTriggered reaction) : TimedAnimation
{
    public const float Seconds = 0.35f;

    protected override void Begin()
    {
        if (Unit(reaction.Shooter) is not { } shooter || !Watched(shooter))
            return;
        Duration = Seconds;
        Ctx.Camera.Focus(shooter.Center);
        Vm.AddFloater(
            "REACTION FIRE",
            new Color(255, 214, 92),
            22,
            shooter.Center + new Vector2(0f, 0.9f)
        );
    }
}

/// <summary><see cref="PodActivated"/>: look at the pod and name it.</summary>
public sealed class PodAnimation(PodActivated pod) : TimedAnimation
{
    public const float Seconds = 1.2f;

    protected override void Begin()
    {
        // A pod waking out of sight (shot at from the fog) gives nothing away: no banner, no pan.
        if (!Ctx.IsTileVisible(pod.At))
            return;
        Duration = Seconds;
        Vm.PodBanner = pod.Name.ToUpperInvariant();
        Ctx.Camera.Focus(UnitVm.TileCenter(pod.At));
    }

    protected override void Finish() => Vm.PodBanner = null;
}

/// <summary><see cref="PhaseStarted"/>: the banner. The player's own phase does not hold input.</summary>
public sealed class PhaseAnimation(PhaseStarted phase) : TimedAnimation
{
    protected override void Begin()
    {
        Vm.Banner = new BannerVm(phase.Phase, phase.Turn);
        Duration = phase.Phase == TacticalPhase.Player ? 0f : ViewModel.BannerSeconds;
    }
}

/// <summary>Placeholder blast and tile-change flash until the real effects (#44).</summary>
public sealed class FlashAnimation(GridPos pos, float size, Color color, float seconds)
    : TimedAnimation
{
    protected override void Begin()
    {
        if (!Ctx.IsTileVisible(pos))
            return;
        var center = UnitVm.TileCenter(pos);
        Vm.AddEffect(
            new EffectVm(EffectKind.FlashQuad, center, center, seconds, color) { Size = size }
        );
        Duration = seconds;
    }
}

/// <summary>A status flag changing, with a floating word for overwatch and hunker.</summary>
public sealed class StatusAnimation(UnitId unit, StatusFlags status, bool added) : TimedAnimation
{
    protected override void Begin()
    {
        if (Unit(unit) is not { } u)
            return;
        u.Statuses.Set(status, added);
        if (!added && status == StatusFlags.Suppressed)
            u.SuppressedBy = null;
        if (!added || !Watched(u))
            return;
        var word = status switch
        {
            StatusFlags.Overwatch => "OVERWATCH",
            StatusFlags.Hunkered => "HUNKERED",
            StatusFlags.Suppressed => "SUPPRESSED",
            StatusFlags.Stunned => "STUNNED",
            StatusFlags.Ruptured => "RUPTURED",
            _ => null,
        };
        if (word is not null)
            Vm.AddFloater(word, new Color(255, 214, 92), 20, u.Center + new Vector2(0f, 0.9f));
    }
}

/// <summary>
/// <see cref="AbilityUsed"/> of Suppression: the target's yellow line to the suppressor starts, and
/// the camera frames the pair.
/// </summary>
public sealed class SuppressionAnimation(AbilityUsed used) : TimedAnimation
{
    public const float Seconds = 0.5f;

    protected override void Begin()
    {
        if (
            used.Target is not UnitTarget(var targetId)
            || Unit(used.Unit) is not { } suppressor
            || Unit(targetId) is not { } target
        )
            return;
        target.SuppressedBy = suppressor.Id;
        if (!Watched(suppressor) && !Watched(target))
            return;
        Duration = Seconds;
        Ctx.Camera.Frame(suppressor.Center, target.Center);
        Vm.AddFloater(
            "SUPPRESSING",
            new Color(255, 214, 92),
            20,
            suppressor.Center + new Vector2(0f, 0.9f)
        );
    }
}

/// <summary>
/// <see cref="UnitSpawned"/>: a summoned unit fades in on its tile with a cyan flash. Its view is
/// made by the scene once it is in the view model.
/// </summary>
public sealed class SpawnAnimation(UnitSpawned spawn) : TimedAnimation
{
    public const float Seconds = 0.5f;

    private UnitVm? _unit;

    protected override void Begin()
    {
        _unit = new UnitVm(spawn);
        Vm.Units[spawn.Unit] = _unit;
        if (!Ctx.IsTileVisible(spawn.At))
        {
            _unit.Alpha = 1f;
            return;
        }
        Duration = Seconds;
        _unit.ForceVisible = true;
        Ctx.Camera.Focus(_unit.Center);
        Vm.AddEffect(
            new EffectVm(
                EffectKind.FlashQuad,
                _unit.Center,
                _unit.Center,
                Seconds,
                new Color(90, 230, 255, 200)
            )
            {
                Size = 1.4f,
            }
        );
        Vm.AddFloater(
            "SUMMONED",
            new Color(90, 230, 255),
            20,
            _unit.Center + new Vector2(0f, 0.9f)
        );
    }

    protected override void Tick(float t) => _unit!.Alpha = t / Seconds;

    protected override void Finish()
    {
        if (_unit is null)
            return;
        _unit.Alpha = 1f;
        _unit.ForceVisible = false;
    }
}

/// <summary><see cref="UnitStabilized"/>: out of danger, still down.</summary>
public sealed class StabilizedAnimation(UnitStabilized stabilized) : TimedAnimation
{
    protected override void Begin()
    {
        if (Unit(stabilized.Unit) is not { } u)
            return;
        u.Statuses.BleedingOutTurnsLeft = 0;
        u.Statuses.Set(StatusFlags.Stabilized);
        Vm.AddFloater("STABILIZED", new Color(120, 230, 140), 20, u.Center + new Vector2(0f, 0.9f));
    }
}

/// <summary>
/// <see cref="AbilityTriggered"/>: the ability's name as floating text over the unit ("RUN &amp; GUN",
/// "IN THE ZONE", "HACKED"). Hostile units only show it while the squad watches them.
/// </summary>
public sealed class CalloutAnimation(AbilityTriggered callout) : TimedAnimation
{
    private static readonly Color Gold = new(255, 214, 92);

    protected override void Begin()
    {
        if (Unit(callout.Unit) is not { } u || !Watched(u))
            return;
        Vm.AddFloater(callout.Text, Gold, 22, u.Center + new Vector2(0f, 0.9f));
    }
}

/// <summary>An instant change to one unit's view; the callback may also tell the scene.</summary>
public sealed class UnitEditAnimation(UnitId unit, Action<UnitVm, EventPlayerContext> edit)
    : TimedAnimation
{
    protected override void Begin()
    {
        if (Unit(unit) is { } u)
            edit(u, Ctx);
    }
}

/// <summary>An event the scene reacts to itself (toasts, mission end, ...); takes no time.</summary>
public sealed class SceneEventAnimation(GameEvent e) : TimedAnimation
{
    protected override void Begin() => Ctx.SceneEvent(e);
}

/// <summary>Maps each <see cref="GameEvent"/> to its animation; events with nothing to show map to null.</summary>
public static class AnimationFactory
{
    public const float ScamperSpeed = 1.5f;

    public static IEventAnimation? Create(GameEvent e) =>
        e switch
        {
            UnitMoved m => new MoveAnimation(m.Unit, m.Path, m.Dashed),
            UnitScampered s => new MoveAnimation(s.Unit, s.Path, false, ScamperSpeed),
            UnitFacing f => new FacingAnimation(f),
            ShotResolved s => new ShotAnimation(s),
            UnitDamaged d => new DamagedAnimation(d),
            UnitKilled k => new KilledAnimation(k),
            UnitBleedingOut b => new BleedingOutAnimation(b),
            UnitStabilized s => new StabilizedAnimation(s),
            ReactionShotTriggered r => new ReactionAnimation(r),
            UnitSpawned s => new SpawnAnimation(s),
            AbilityUsed { AbilityId: SuppressionAbility.AbilityId } a => new SuppressionAnimation(
                a
            ),
            PodActivated p => new PodAnimation(p),
            PhaseStarted p => new PhaseAnimation(p),
            ExplosionAt x => new FlashAnimation(
                x.Pos,
                2f * x.Radius + 1f,
                new Color(255, 170, 60, 200),
                0.3f
            ),
            TileChanged t => new FlashAnimation(t.Pos, 1f, new Color(255, 255, 255, 160), 0.15f),
            AbilityTriggered t => new CalloutAnimation(t),
            UnitSuppressed s => new UnitEditAnimation(s.Unit, (u, _) => u.SuppressedBy = s.By),
            StatusAdded a => new StatusAnimation(a.Unit, a.Status, true),
            StatusRemoved r => new StatusAnimation(r.Unit, r.Status, false),
            AbilityUsed { AbilityId: AbilityRegistry.Reload } a => new UnitEditAnimation(
                a.Unit,
                (u, _) => u.ReloadSeconds = 0.6f
            ),
            UnitEvacuated v => new UnitEditAnimation(
                v.Unit,
                (u, ctx) =>
                {
                    u.Removed = true;
                    ctx.SceneEvent(v);
                }
            ),
            MoveBlocked or MissionEnded or AllSoldiersExhausted => new SceneEventAnimation(e),
            _ => null,
        };
}
