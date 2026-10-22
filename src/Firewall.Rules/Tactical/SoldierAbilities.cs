using Firewall.Rules.Data;
using Firewall.Rules.Tactical.Vision;

namespace Firewall.Rules.Tactical;

/// <summary>
/// Run &amp; Gun (GDD §5.2): after a dash this turn, grants <c>grantAp</c> AP that may only be spent
/// on a shot (<see cref="PerkState.ShootOnlyAp"/>). Costs no AP itself; the cooldown is the engine's.
/// </summary>
public sealed class RunAndGunAbility : IAbilityHandler
{
    public ValidationResult Validate(AbilityContext ctx)
    {
        if (!ctx.User.Perks.DashedThisTurn)
            return ValidationResult.Fail("Run & Gun needs a dash first.");
        return ctx.User.Weapon is null
            ? ValidationResult.Fail($"{ctx.User.DisplayName} has no weapon.")
            : ValidationResult.Ok;
    }

    public void Execute(AbilityContext ctx)
    {
        var grant = (int)ctx.Def.Parameters.GetValueOrDefault("grantAp", 1);
        ctx.User.Perks.DashedThisTurn = false;
        ctx.User.Perks.ShootOnlyAp += grant;
        ctx.User.ActionPoints += grant;
        ctx.Events.Add(new ApChanged(ctx.User.Id, ctx.User.ActionPoints));
    }
}

/// <summary>
/// The Tech's Hack (GDD §5.2): 1 AP, a robot within <c>range</c> tiles in line of sight. It
/// succeeds with <c>baseChance + perRank × rank index + perTier × robot tier</c> percent and then
/// stuns the robot for a turn. A robot flagged <see cref="TacticalUnit.ImmuneToStun"/> (the Guardian) cannot be hacked.
/// </summary>
public sealed class HackAbility : IAbilityHandler, IAbilityPreview
{
    public ValidationResult Validate(AbilityContext ctx)
    {
        var target = ctx.State.Unit(((UnitTarget)ctx.Target).Id);
        if (!ctx.Data.Enemies.TryGet(target.DefId, out var enemy) || !enemy.Robot)
            return ValidationResult.Fail($"{target.DisplayName} is not a robot.");
        if (target.ImmuneToStun || enemy.ImmuneToStun)
            return ValidationResult.Fail($"{target.DisplayName} is immune to hacking.");
        if (!target.IsStanding)
            return ValidationResult.Fail($"{target.DisplayName} is down.");
        if (target.Statuses.Stunned)
            return ValidationResult.Fail($"{target.DisplayName} is already stunned.");
        var range = ctx.Def.Parameters.GetValueOrDefault("range", 6);
        if (ctx.User.Position.DistanceTo(target.Position) > range)
            return ValidationResult.Fail($"{target.DisplayName} is out of range ({range} tiles).");
        return LosTracer.CanSee(ctx.State, ctx.User, target)
            ? ValidationResult.Ok
            : ValidationResult.Fail("No line of sight to the target.");
    }

    /// <summary>The success chance in percent, clamped to 0 to 100.</summary>
    public static int Chance(AbilityDef def, TacticalUnit user, int robotTier)
    {
        var p = def.Parameters;
        var chance =
            p.GetValueOrDefault("baseChance", 50)
            + p.GetValueOrDefault("perRank", 5) * (int)user.Rank
            + p.GetValueOrDefault("perTier", -15) * robotTier;
        return Math.Clamp((int)chance, 0, 100);
    }

    public ShotPreview? Preview(AbilityContext ctx, TacticalUnit target)
    {
        var tier = ctx.Data.Enemies.TryGet(target.DefId, out var enemy) ? enemy.Tier : 0;
        var p = ctx.Def.Parameters;
        var breakdown = new List<(string, int)>
        {
            ("Hack", (int)p.GetValueOrDefault("baseChance", 50)),
        };
        var rank = (int)(p.GetValueOrDefault("perRank", 5) * (int)ctx.User.Rank);
        if (rank != 0)
            breakdown.Add(("Rank", rank));
        var robot = (int)(p.GetValueOrDefault("perTier", -15) * tier);
        if (robot != 0)
            breakdown.Add(("Robot tier", robot));
        var check = Validate(ctx);
        return new ShotPreview(
            Chance(ctx.Def, ctx.User, tier),
            0,
            0,
            0,
            breakdown,
            [],
            false,
            check.IsValid,
            check.IsValid ? null : check.Reason
        );
    }

    public void Execute(AbilityContext ctx)
    {
        var user = ctx.User;
        var target = ctx.State.Unit(((UnitTarget)ctx.Target).Id);
        var tier = ctx.Data.Enemies[target.DefId].Tier;
        ctx.Events.Add(new UnitFacing(user.Id, target.Position));
        if (!ctx.Rng.Roll(Chance(ctx.Def, user, tier)))
        {
            ctx.Events.Add(new AbilityTriggered(target.Id, ctx.Def.Id, "HACK FAILED"));
            return;
        }
        target.Statuses.Set(StatusFlags.Stunned);
        target.Reactions.Clear();
        ctx.Events.Add(new StatusAdded(target.Id, StatusFlags.Stunned));
        ctx.Events.Add(new AbilityTriggered(target.Id, ctx.Def.Id, "HACKED"));
    }
}

/// <summary>
/// The Tech's Overclock (GDD §5.2): 1 AP, gives another standing squadmate <c>grantAp</c> AP this
/// turn. (The Proxy's own <see cref="OverclockAbility"/> is a different ability.)
/// </summary>
public sealed class AllyOverclockAbility : IAbilityHandler
{
    public ValidationResult Validate(AbilityContext ctx)
    {
        var target = ctx.State.Unit(((UnitTarget)ctx.Target).Id);
        if (target.Id == ctx.User.Id)
            return ValidationResult.Fail("Overclock needs another soldier.");
        return target.IsStanding && !target.Statuses.Stunned
            ? ValidationResult.Ok
            : ValidationResult.Fail($"{target.DisplayName} cannot act.");
    }

    public void Execute(AbilityContext ctx)
    {
        var target = ctx.State.Unit(((UnitTarget)ctx.Target).Id);
        target.ActionPoints += (int)ctx.Def.Parameters.GetValueOrDefault("grantAp", 1);
        ctx.Events.Add(new ApChanged(target.Id, target.ActionPoints));
        ctx.Events.Add(new AbilityTriggered(target.Id, ctx.Def.Id, "OVERCLOCKED"));
    }
}
