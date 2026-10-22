using Firewall.Rules.Rng;

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
/// <see cref="UnitKilled"/> or <see cref="UnitBleedingOut"/> at 0 HP (<see cref="Casualties"/>).
/// The shot abilities of the classes use it too (#30): Rapid Fire fires <c>shots</c> times, Headshot
/// and Rupture change the shot (<see cref="ShotContext.For"/>); the AP rules around them are the engine's.
/// </summary>
public sealed class ShootAbility(RangeType rangeType = RangeType.Ranged)
    : IAbilityHandler,
        IAbilityPreview
{
    /// <summary>Run in order on every hit.</summary>
    public List<IDamageHook> DamageHooks { get; } = [];

    public ShotPreview Preview(AbilityContext ctx, TacticalUnit target) =>
        HitCalculator.Preview(
            ctx.State,
            ctx.User,
            target,
            ShotContext.For(ctx.Data, ctx.User, rangeType, ctx.Def)
        );

    public ValidationResult Validate(AbilityContext ctx)
    {
        var target = ctx.State.Unit(((UnitTarget)ctx.Target).Id);
        var preview = Preview(ctx, target);
        return preview.CanFire ? ValidationResult.Ok : ValidationResult.Fail(preview.Reason!);
    }

    public void Execute(AbilityContext ctx)
    {
        var target = ctx.State.Unit(((UnitTarget)ctx.Target).Id);
        var shot = ShotContext.For(ctx.Data, ctx.User, rangeType, ctx.Def);
        var shots = (int)ctx.Def.Parameters.GetValueOrDefault("shots", 1);
        for (var i = 0; i < shots; i++)
        {
            // Later shots only go off at a target still standing and while the clip lasts.
            if (i > 0 && (!target.IsStanding || !Preview(ctx, target).CanFire))
                break;
            Fire(ctx.State, ctx.User, target, shot, ctx.Rng, ctx.Events);
        }
    }

    /// <summary>
    /// Resolves one shot the caller has already validated, with this ability's damage hooks.
    /// Reaction fire uses it too.
    /// </summary>
    public void Fire(
        TacticalState state,
        TacticalUnit attacker,
        TacticalUnit target,
        ShotContext shot,
        IRandom rng,
        List<GameEvent> events
    )
    {
        var preview = HitCalculator.Preview(state, attacker, target, shot);
        events.Add(new UnitFacing(attacker.Id, target.Position));
        if (preview.AutoMissBy is { } saver)
            SpendAutoMiss(target, saver, shot, events);

        var outcome = HitCalculator.Roll(preview, target.Armor, rng);
        var damage = outcome.Damage;
        if (outcome.Hit)
        {
            var hook = new DamageContext(state, attacker, target, outcome);
            foreach (var h in DamageHooks)
                damage = Math.Max(1, h.Modify(hook, damage));
        }
        events.Add(
            new ShotResolved(
                attacker.Id,
                target.Id,
                preview.HitChance,
                outcome.Hit,
                outcome.Crit,
                damage
            )
        );
        attacker.Stats.ShotsFired++;
        if (outcome.Hit)
        {
            var hpBefore = target.Hp;
            target.Hp = Math.Max(0, target.Hp - damage);
            attacker.Stats.Hits++;
            attacker.Stats.DamageDealt += hpBefore - target.Hp;
            // Shredder: the hit strips armor for good (the damage above already used it).
            if (attacker.Abilities.Contains(SoldierPerks.Shredder) && target.Armor > 0)
                target.Armor = Math.Max(
                    0,
                    target.Armor
                        - SoldierPerks.IntParam(shot.Data, SoldierPerks.Shredder, "armorShred", 1)
                );
            events.Add(new UnitDamaged(target.Id, damage, target.Hp, target.Armor));
            if (shot.Ruptures && !target.Statuses.Ruptured)
            {
                target.Statuses.Set(StatusFlags.Ruptured);
                events.Add(new StatusAdded(target.Id, StatusFlags.Ruptured));
            }
        }

        if (shot.RangeType == RangeType.Ranged && attacker.Weapon is { } weapon)
        {
            weapon.Ammo -= shot.AmmoCost;
            events.Add(new AmmoChanged(attacker.Id, weapon.Ammo));
        }

        if (outcome.Hit && target.Hp == 0 && Casualties.Drop(target, rng, events))
        {
            attacker.Stats.Kills++;
            // Untouchable: a kill covers the killer against the next attack.
            if (attacker.Abilities.Contains(SoldierPerks.Untouchable))
                attacker.Perks.UntouchableArmed = true;
        }
    }

    // The saving ability is used up and says so.
    private static void SpendAutoMiss(
        TacticalUnit target,
        string abilityId,
        ShotContext shot,
        List<GameEvent> events
    )
    {
        if (abilityId == SoldierPerks.LightningReflexes)
            target.Perks.LightningReflexesSpent = true;
        else
            target.Perks.UntouchableArmed = false;
        var name =
            shot.Data is not null && shot.Data.Abilities.TryGet(abilityId, out var def)
                ? def.Name
                : abilityId;
        events.Add(new AbilityTriggered(target.Id, abilityId, name.ToUpperInvariant()));
    }
}
