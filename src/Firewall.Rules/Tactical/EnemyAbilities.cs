using Firewall.Rules.Data;
using Firewall.Rules.Tactical.Vision;

namespace Firewall.Rules.Tactical;

/// <summary>
/// The Proxy's Overclock (GDD §5.6): 1 AP, does not end the turn. Gives an adjacent allied robot
/// that has already acted this turn +1 AP. The cooldown is the engine's.
/// </summary>
public sealed class OverclockAbility : IAbilityHandler
{
    public ValidationResult Validate(AbilityContext ctx)
    {
        var target = ctx.State.Unit(((UnitTarget)ctx.Target).Id);
        if (target.Id == ctx.User.Id)
            return ValidationResult.Fail("Overclock needs another unit.");
        if (!ctx.Data.Enemies.TryGet(target.DefId, out var def) || !def.Robot)
            return ValidationResult.Fail($"{target.DisplayName} is not a robot.");
        if (!target.IsStanding || target.Statuses.Stunned)
            return ValidationResult.Fail($"{target.DisplayName} cannot act.");
        if (!IsWithin(ctx.User.Position, target.Position, (int)ctx.Def.Parameters["range"]))
            return ValidationResult.Fail($"{target.DisplayName} is not adjacent.");
        return target.ActionPoints < TacticalEngine.ApPerTurn
            ? ValidationResult.Ok
            : ValidationResult.Fail($"{target.DisplayName} has not acted yet.");
    }

    public void Execute(AbilityContext ctx)
    {
        var target = ctx.State.Unit(((UnitTarget)ctx.Target).Id);
        target.ActionPoints += (int)ctx.Def.Parameters["grantAp"];
        ctx.Events.Add(new ApChanged(target.Id, target.ActionPoints));
    }

    private static bool IsWithin(GridPos a, GridPos b, int range) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)) <= range;
}

/// <summary>
/// Suppression (GDD §4.6, §5.6), shared by the Enforcer and the Gunner: 1 AP, ends the turn, uses
/// <c>ammo</c> rounds when the user has a weapon. The target gets <see cref="HitCalculator.SuppressedAim"/>
/// to hit, and the suppressor gets a <see cref="ReactionTrigger"/> aimed at it alone, so a shot
/// fires when it moves. It lasts until the suppressor's next turn or until the suppressor goes
/// down (<see cref="Suppression.Release"/>).
/// </summary>
public sealed class SuppressionAbility : IAbilityHandler
{
    public const string AbilityId = "suppression";

    public ValidationResult Validate(AbilityContext ctx)
    {
        var target = ctx.State.Unit(((UnitTarget)ctx.Target).Id);
        if (!target.IsStanding)
            return ValidationResult.Fail($"{target.DisplayName} is down.");
        var ammo = (int)ctx.Def.Parameters.GetValueOrDefault("ammo");
        if (ctx.User.Weapon is { } weapon && weapon.Ammo < ammo)
            return ValidationResult.Fail(weapon.Ammo == 0 ? "Out of ammo." : "Not enough ammo.");
        if (target.Statuses.SuppressedBy == ctx.User.Id)
            return ValidationResult.Fail($"{target.DisplayName} is already suppressed.");
        return LosTracer.CanSee(ctx.State, ctx.User, target)
            ? ValidationResult.Ok
            : ValidationResult.Fail("No line of sight to the target.");
    }

    public void Execute(AbilityContext ctx)
    {
        var user = ctx.User;
        var target = ctx.State.Unit(((UnitTarget)ctx.Target).Id);
        ctx.Events.Add(new UnitFacing(user.Id, target.Position));
        if (user.Weapon is { } weapon)
        {
            weapon.Ammo -= (int)ctx.Def.Parameters.GetValueOrDefault("ammo");
            ctx.Events.Add(new AmmoChanged(user.Id, weapon.Ammo));
        }

        target.Statuses.SuppressedBy = user.Id;
        target.Statuses.Set(StatusFlags.Suppressed);
        ctx.Events.Add(new StatusAdded(target.Id, StatusFlags.Suppressed));
        user.Reactions.Add(new ReactionTrigger(ctx.Def.Id, StatusFlags.None) { Only = target.Id });
    }
}

/// <summary>Ends suppression when its source stops being able to hold it.</summary>
public static class Suppression
{
    /// <summary>Clears the suppression <paramref name="suppressor"/> put on others.</summary>
    public static void Release(TacticalState state, UnitId suppressor, List<GameEvent> events)
    {
        foreach (var unit in state.Units)
        {
            if (unit.Statuses.SuppressedBy != suppressor)
                continue;
            unit.Statuses.SuppressedBy = null;
            if (!unit.Statuses.Suppressed)
                continue;
            unit.Statuses.Set(StatusFlags.Suppressed, false);
            events.Add(new StatusRemoved(unit.Id, StatusFlags.Suppressed));
        }
    }

    /// <summary>Releases everything held by suppressors that are dead, down or gone.</summary>
    public static void Sweep(TacticalState state, List<GameEvent> events)
    {
        var lost = state
            .Units.Where(u => u.Statuses.SuppressedBy is not null)
            .Select(u => u.Statuses.SuppressedBy!.Value)
            .Distinct()
            .Where(id => !state.Unit(id).IsStanding)
            .ToList();
        foreach (var id in lost)
            Release(state, id, events);
    }
}
