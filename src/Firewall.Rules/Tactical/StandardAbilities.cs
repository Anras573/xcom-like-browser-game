using Firewall.Rules.Data;

namespace Firewall.Rules.Tactical;

/// <summary>
/// Overwatch (GDD §4.6): 1 AP, ends the turn, needs a round in the clip. Sets
/// <see cref="StatusFlags.Overwatch"/> and a <see cref="ReactionTrigger"/> until the unit's next
/// turn or until it fires.
/// </summary>
public sealed class OverwatchAbility : IAbilityHandler
{
    public ValidationResult Validate(AbilityContext ctx)
    {
        if (ctx.User.Weapon is not { } weapon)
            return ValidationResult.Ok;
        if (weapon.Ammo < 1)
            return ValidationResult.Fail("Out of ammo.");
        if (weapon.Def.Flags.Contains(WeaponFlag.NoFireAfterMove) && ctx.User.MovedThisTurn)
            return ValidationResult.Fail("Sniper rifles can't overwatch after moving");
        return ValidationResult.Ok;
    }

    public void Execute(AbilityContext ctx)
    {
        var unit = ctx.User;
        unit.Reactions.Add(new ReactionTrigger(ctx.Def.Id, StatusFlags.Overwatch));
        unit.Statuses.Set(StatusFlags.Overwatch);
        ctx.Events.Add(new StatusAdded(unit.Id, StatusFlags.Overwatch));
    }
}

/// <summary>Reload (GDD §4.5): 1 AP, does not end the turn, refills the clip. Emits <see cref="AmmoChanged"/>.</summary>
public sealed class ReloadAbility : IAbilityHandler
{
    public ValidationResult Validate(AbilityContext ctx) =>
        ctx.User.Weapon switch
        {
            null => ValidationResult.Fail($"{ctx.User.DisplayName} has no weapon to reload."),
            { } w when w.Ammo >= w.Def.Clip => ValidationResult.Fail("The clip is already full."),
            _ => ValidationResult.Ok,
        };

    public void Execute(AbilityContext ctx)
    {
        var weapon = ctx.User.Weapon!;
        weapon.Ammo = weapon.Def.Clip;
        ctx.Events.Add(new AmmoChanged(ctx.User.Id, weapon.Ammo));
    }
}

/// <summary>
/// Hunker Down (GDD §4.3): 1 AP, ends the turn, needs an adjacent cover object. Sets
/// <see cref="StatusFlags.Hunkered"/>, which doubles cover defense in <see cref="HitCalculator"/>,
/// until the unit's next turn.
/// </summary>
public sealed class HunkerDownAbility : IAbilityHandler
{
    public ValidationResult Validate(AbilityContext ctx) =>
        CoverCalculator.GetProfile(ctx.State.Map, ctx.User.Position).HasAny
            ? ValidationResult.Ok
            : ValidationResult.Fail("Hunker Down needs adjacent cover.");

    public void Execute(AbilityContext ctx)
    {
        ctx.User.Statuses.Set(StatusFlags.Hunkered);
        ctx.Events.Add(new StatusAdded(ctx.User.Id, StatusFlags.Hunkered));
    }
}
