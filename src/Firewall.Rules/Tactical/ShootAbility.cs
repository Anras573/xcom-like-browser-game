namespace Firewall.Rules.Tactical;

/// <summary>What a damage hook sees after a hit's armor has been subtracted.</summary>
public sealed record DamageContext(
    TacticalState State,
    TacticalUnit Attacker,
    TacticalUnit Target,
    ShotOutcome Outcome
);

/// <summary>
/// Adjusts the damage of a hit after armor (Shredder, Rupture: #30). A hook may also change the
/// target, such as its armor; the result is still at least 1.
/// </summary>
public interface IDamageHook
{
    int Modify(DamageContext ctx, int damage);
}

/// <summary>
/// Shoot and melee (GDD §4.5): 1 AP, ends the turn. Emits <see cref="UnitFacing"/>,
/// <see cref="ShotResolved"/>, <see cref="UnitDamaged"/> on a hit, <see cref="AmmoChanged"/> and
/// <see cref="UnitKilled"/> on a kill.
/// </summary>
public sealed class ShootAbility(RangeType rangeType = RangeType.Ranged) : IAbilityHandler
{
    /// <summary>Run in order on every hit.</summary>
    public List<IDamageHook> DamageHooks { get; } = [];

    public ShotPreview Preview(AbilityContext ctx, TacticalUnit target) =>
        HitCalculator.Preview(
            ctx.State,
            ctx.User,
            target,
            ShotContext.For(ctx.Data, ctx.User, rangeType)
        );

    public ValidationResult Validate(AbilityContext ctx)
    {
        var target = ctx.State.Unit(((UnitTarget)ctx.Target).Id);
        var preview = Preview(ctx, target);
        return preview.CanFire ? ValidationResult.Ok : ValidationResult.Fail(preview.Reason!);
    }

    public void Execute(AbilityContext ctx)
    {
        var attacker = ctx.User;
        var target = ctx.State.Unit(((UnitTarget)ctx.Target).Id);
        var shot = ShotContext.For(ctx.Data, attacker, rangeType);
        var preview = HitCalculator.Preview(ctx.State, attacker, target, shot);
        ctx.Events.Add(new UnitFacing(attacker.Id, target.Position));

        var outcome = HitCalculator.Roll(preview, target.Armor, ctx.Rng);
        var damage = outcome.Damage;
        if (outcome.Hit)
        {
            var hook = new DamageContext(ctx.State, attacker, target, outcome);
            foreach (var h in DamageHooks)
                damage = Math.Max(1, h.Modify(hook, damage));
        }
        ctx.Events.Add(
            new ShotResolved(
                attacker.Id,
                target.Id,
                preview.HitChance,
                outcome.Hit,
                outcome.Crit,
                damage
            )
        );
        if (outcome.Hit)
        {
            target.Hp = Math.Max(0, target.Hp - damage);
            ctx.Events.Add(new UnitDamaged(target.Id, damage, target.Hp, target.Armor));
        }

        if (rangeType == RangeType.Ranged && attacker.Weapon is { } weapon)
        {
            weapon.Ammo -= shot.AmmoCost;
            ctx.Events.Add(new AmmoChanged(attacker.Id, weapon.Ammo));
        }

        if (outcome.Hit && target.Hp == 0)
        {
            // Soldiers roll for bleed-out instead with #24.
            target.IsAlive = false;
            ctx.Events.Add(new UnitKilled(target.Id));
        }
    }
}
