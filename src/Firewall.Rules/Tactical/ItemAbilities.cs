using Firewall.Rules.Data;
using Firewall.Rules.Tactical.Vision;

namespace Firewall.Rules.Tactical;

/// <summary>A unit the blast would hit: <paramref name="Friendly"/> ones are the thrower's own side.</summary>
public sealed record BlastVictim(TacticalUnit Unit, bool Friendly, int Damage, bool Kills);

/// <summary>
/// What throwing an item at a tile would do, for the targeting UI: the footprint, who is in it,
/// the damage and the cover that would break.
/// </summary>
public sealed record BlastPreview(
    GridPos Center,
    double Radius,
    IReadOnlyList<GridPos> Tiles,
    IReadOnlyList<BlastVictim> Victims,
    IReadOnlyList<TileChange> Breakage,
    int Damage,
    bool CanThrow,
    string? Reason
)
{
    public int Enemies => Victims.Count(v => !v.Friendly);
    public int Allies => Victims.Count(v => v.Friendly);
}

/// <summary>An ability aimed at a tile that can show its footprint (grenades, charges, smoke).</summary>
public interface ITilePreview
{
    BlastPreview Preview(AbilityContext ctx);
}

/// <summary>What the item abilities share: charges live on the unit's <see cref="ItemState"/>.</summary>
internal static class ItemUse
{
    public static ItemState? Find(TacticalUnit unit, string itemId) =>
        unit.Items.FirstOrDefault(i => i.ItemId == itemId);

    public static ValidationResult CheckCharges(AbilityContext ctx) =>
        Find(ctx.User, ctx.Def.Id) is { Charges: > 0 }
            ? ValidationResult.Ok
            : ValidationResult.Fail($"{ctx.Def.Name} has no charges left.");

    public static void Spend(AbilityContext ctx) => Find(ctx.User, ctx.Def.Id)!.Charges--;

    public static ItemDef Item(AbilityContext ctx) => ctx.Data.Items[ctx.Def.Id];

    public static bool Adjacent(GridPos a, GridPos b) =>
        a != b && Math.Abs(a.X - b.X) <= 1 && Math.Abs(a.Y - b.Y) <= 1;
}

/// <summary>
/// A tile-targeted item (GDD §5.5): throw range, line of sight to the <i>tile</i> (not to units),
/// then an effect on every tile of the blast. Subclasses say what the blast does.
/// </summary>
public abstract class ThrownItemAbility : IAbilityHandler, ITilePreview
{
    protected virtual double Radius(AbilityContext ctx) =>
        ItemUse.Item(ctx).Parameters.GetValueOrDefault("radius", 1)
        + SoldierPerks.ItemRadiusBonus(ctx.Data, ctx.User.Abilities, ItemUse.Item(ctx));

    protected static GridPos Center(AbilityContext ctx) => ((TileTarget)ctx.Target).Pos;

    public ValidationResult Validate(AbilityContext ctx)
    {
        var charges = ItemUse.CheckCharges(ctx);
        if (!charges.IsValid)
            return charges;
        var parameters = ItemUse.Item(ctx).Parameters;
        var from = ctx.User.Position;
        var to = Center(ctx);
        var min = parameters.GetValueOrDefault("minRange", 0);
        var range = parameters.GetValueOrDefault("range", 10);
        var distance = from.DistanceTo(to);
        // Range 1 means the neighbouring tiles, diagonals included (a Breach Charge is placed by hand).
        var tooFar = range <= 1 ? !ItemUse.Adjacent(from, to) && from != to : distance > range;
        if (tooFar)
            return ValidationResult.Fail($"Out of range ({range:0.#} tiles).");
        if (distance < min || (min >= 1 && from == to))
            return ValidationResult.Fail("Too close.");
        return LosTracer.HasLos(ctx.State.Map, from, to)
            ? ValidationResult.Ok
            : ValidationResult.Fail("No line of sight to that tile.");
    }

    public BlastPreview Preview(AbilityContext ctx)
    {
        var radius = Radius(ctx);
        var tiles = Blast.Tiles(ctx.State.Map, Center(ctx), radius);
        var check = Validate(ctx);
        var damage = Damage(ctx);
        var victims = Victims(ctx, tiles)
            .Select(u => new BlastVictim(
                u,
                u.Faction == ctx.User.Faction,
                damage,
                damage > 0 && (u.Statuses.IsDown || u.Hp <= damage)
            ))
            .ToList();
        return new BlastPreview(
            Center(ctx),
            radius,
            tiles,
            victims,
            Breakage(ctx, tiles).ToList(),
            damage,
            check.IsValid,
            check.IsValid ? null : check.Reason
        );
    }

    public abstract void Execute(AbilityContext ctx);

    protected virtual int Damage(AbilityContext ctx) => 0;

    protected virtual IEnumerable<TacticalUnit> Victims(
        AbilityContext ctx,
        IReadOnlyList<GridPos> tiles
    ) => [];

    protected virtual IEnumerable<TileChange> Breakage(
        AbilityContext ctx,
        IReadOnlyList<GridPos> tiles
    ) => [];

    /// <summary>Spends the charge and turns the thrower toward the tile.</summary>
    protected static void Begin(AbilityContext ctx)
    {
        ItemUse.Spend(ctx);
        ctx.Events.Add(new UnitFacing(ctx.User.Id, Center(ctx)));
    }
}

/// <summary>
/// Frag Grenade and Breach Charge: damage in a radius that ignores cover and never misses, armor
/// stripped from robots, and the tiles it breaks. A unit already down is killed outright.
/// Explosive damage is flat: armor does not soak it, it is shredded by it.
/// </summary>
public sealed class ExplosiveAbility : ThrownItemAbility
{
    protected override int Damage(AbilityContext ctx) =>
        (int)ItemUse.Item(ctx).Parameters.GetValueOrDefault("damage")
        + SoldierPerks.ExplosiveDamageBonus(ctx.Data, ctx.User.Abilities);

    protected override IEnumerable<TacticalUnit> Victims(
        AbilityContext ctx,
        IReadOnlyList<GridPos> tiles
    ) => Blast.Units(ctx.State, tiles);

    protected override IEnumerable<TileChange> Breakage(
        AbilityContext ctx,
        IReadOnlyList<GridPos> tiles
    )
    {
        var p = ItemUse.Item(ctx).Parameters;
        return Blast.Breakage(
            ctx.State.Map,
            tiles,
            p.GetValueOrDefault("destroysCover") > 0,
            p.GetValueOrDefault("destroysWalls") > 0
        );
    }

    public override void Execute(AbilityContext ctx)
    {
        Begin(ctx);
        var plan = Preview(ctx);
        var item = ItemUse.Item(ctx);
        var shred = (int)item.Parameters.GetValueOrDefault("armorShred");
        ctx.Events.Add(new ExplosionAt(plan.Center, (int)Math.Ceiling(plan.Radius)));
        foreach (var victim in plan.Victims)
            Hurt(ctx, victim.Unit, plan.Damage, shred);
        foreach (var change in plan.Breakage)
        {
            ctx.State.Map.SetTile(change.Pos, change.New);
            ctx.Events.Add(new TileChanged(change.Pos, change.Old, change.New));
        }
    }

    private static void Hurt(AbilityContext ctx, TacticalUnit unit, int damage, int shred)
    {
        var user = ctx.User;
        if (unit.Statuses.Ruptured)
            damage += SoldierPerks.IntParam(ctx.Data, SoldierPerks.Rupture, "damage", 3);
        var hostile = unit.Faction != user.Faction && unit.Faction != Faction.Neutral;
        var wasDown = unit.Statuses.IsDown;
        var hpBefore = unit.Hp;
        unit.Hp = Math.Max(0, unit.Hp - damage);
        if (shred > 0 && ctx.Data.Enemies.TryGet(unit.DefId, out var def) && def.Robot)
            unit.Armor = Math.Max(0, unit.Armor - shred);
        if (hostile)
            user.Stats.DamageDealt += hpBefore - unit.Hp;
        ctx.Events.Add(new UnitDamaged(unit.Id, damage, unit.Hp, unit.Armor));
        if (unit.Hp > 0)
            return;
        if (wasDown)
        {
            // Nothing left to bleed: a body in the blast is gone.
            unit.Statuses.BleedingOutTurnsLeft = 0;
            unit.Statuses.Set(StatusFlags.Stabilized, false);
            unit.IsAlive = false;
            ctx.Events.Add(new UnitKilled(unit.Id));
        }
        else if (!Casualties.Drop(unit, ctx.Rng, ctx.Events))
            return;
        if (hostile)
            user.Stats.Kills++;
    }
}

/// <summary>Smoke Grenade: a cloud on the blast tiles; units in it get +defense for <c>turns</c> turns. It does not block line of sight.</summary>
public sealed class SmokeAbility : ThrownItemAbility
{
    public override void Execute(AbilityContext ctx)
    {
        Begin(ctx);
        var p = ItemUse.Item(ctx).Parameters;
        var tiles = Blast.Tiles(ctx.State.Map, Center(ctx), Radius(ctx));
        var turns = (int)p.GetValueOrDefault("turns", 2);
        ctx.State.AddSmoke(tiles, turns, (int)p.GetValueOrDefault("defense", 20));
        ctx.Events.Add(new SmokeCreated(tiles, turns));
    }
}

/// <summary>Lace Jammer: stuns the Husks in the radius for a turn.</summary>
public sealed class LaceJammerAbility : ThrownItemAbility
{
    public const string HuskId = "husk";

    protected override IEnumerable<TacticalUnit> Victims(
        AbilityContext ctx,
        IReadOnlyList<GridPos> tiles
    ) =>
        Blast
            .Units(ctx.State, tiles)
            .Where(u =>
                u is { Faction: Faction.Meridian, IsStanding: true, ImmuneToStun: false }
                && u.DefId == HuskId
            );

    public override void Execute(AbilityContext ctx)
    {
        Begin(ctx);
        foreach (var husk in Victims(ctx, Blast.Tiles(ctx.State.Map, Center(ctx), Radius(ctx))))
        {
            if (husk.Statuses.Stunned)
                continue;
            husk.Statuses.Set(StatusFlags.Stunned);
            ctx.Events.Add(new StatusAdded(husk.Id, StatusFlags.Stunned));
            Suppression.Release(ctx.State, husk.Id, ctx.Events);
            AllyOverclockAbility.StandDown(husk, ctx.Events);
            ctx.Events.Add(new AbilityTriggered(husk.Id, ctx.Def.Id, "JAMMED"));
        }
    }
}

/// <summary>
/// Medkit (GDD §5.5): self or an adjacent ally (Field Medic adds the diagonals). Heals up to max
/// HP, or stabilizes a bleeding-out soldier; Revive stands them up at 1 HP instead.
/// </summary>
public sealed class MedkitAbility : IAbilityHandler
{
    public ValidationResult Validate(AbilityContext ctx)
    {
        var charges = ItemUse.CheckCharges(ctx);
        if (!charges.IsValid)
            return charges;
        var target = ctx.State.Unit(((UnitTarget)ctx.Target).Id);
        if (!SoldierPerks.MedkitReaches(ctx.User.Abilities, ctx.User.Position, target.Position))
            return ValidationResult.Fail($"{target.DisplayName} is not within reach.");
        if (target.Statuses.IsBleedingOut)
            return ValidationResult.Ok;
        if (target.Statuses.Stabilized)
            return ValidationResult.Fail($"{target.DisplayName} is already stabilized.");
        return target.Hp < target.MaxHp
            ? ValidationResult.Ok
            : ValidationResult.Fail($"{target.DisplayName} is at full health.");
    }

    public void Execute(AbilityContext ctx)
    {
        ItemUse.Spend(ctx);
        var user = ctx.User;
        var target = ctx.State.Unit(((UnitTarget)ctx.Target).Id);
        ctx.Events.Add(new UnitFacing(user.Id, target.Position));
        if (target.Statuses.IsBleedingOut)
        {
            target.Statuses.BleedingOutTurnsLeft = 0;
            if (SoldierPerks.MedkitReviveHp(ctx.Data, user.Abilities) is > 0 and var hp)
            {
                target.Hp = Math.Min(target.MaxHp, hp);
                ctx.Events.Add(new UnitRevived(target.Id, target.Hp));
            }
            else
            {
                target.Statuses.Set(StatusFlags.Stabilized);
                ctx.Events.Add(new UnitStabilized(target.Id));
            }
            return;
        }
        var item = ItemUse.Item(ctx);
        var heal = Math.Min(
            SoldierPerks.MedkitHeal(ctx.Data, user.Abilities, item),
            target.MaxHp - target.Hp
        );
        target.Hp += heal;
        ctx.Events.Add(new UnitHealed(target.Id, heal, target.Hp));
    }
}

/// <summary>
/// Arc Stunner (GDD §5.5): melee against robots and capturable enemies (Proxies), 70 % base hit
/// through <see cref="HitCalculator"/> with a melee shot. A Proxy at or under
/// <c>captureHpPercent</c> of its HP is stunned and <see cref="StatusFlags.Capturable"/> for the rest
/// of the mission; anything else takes <c>robotDamage</c> and a one-turn stun.
/// </summary>
public sealed class ArcStunnerAbility : IAbilityHandler, IAbilityPreview
{
    private static ShotContext Shot(AbilityContext ctx) =>
        ShotContext.For(ctx.Data, ctx.User, RangeType.Melee) with
        {
            BaseAim = (int)ItemUse.Item(ctx).Parameters.GetValueOrDefault("baseHit", 70),
        };

    public ShotPreview? Preview(AbilityContext ctx, TacticalUnit target)
    {
        var preview = HitCalculator.Preview(ctx.State, ctx.User, target, Shot(ctx));
        var check = Validate(ctx with { Target = Target.Unit(target.Id) });
        var damage = Captures(ctx, target) ? 0 : RobotDamage(ctx);
        return preview with
        {
            DamageMin = damage,
            DamageMax = damage,
            CritChance = 0,
            CanFire = check.IsValid,
            Reason = check.IsValid ? null : check.Reason,
        };
    }

    public ValidationResult Validate(AbilityContext ctx)
    {
        var charges = ItemUse.CheckCharges(ctx);
        if (!charges.IsValid)
            return charges;
        var target = ctx.State.Unit(((UnitTarget)ctx.Target).Id);
        if (
            !ctx.Data.Enemies.TryGet(target.DefId, out var enemy)
            || enemy.Kind != EnemyKind.Unit
            || !(enemy.Robot || enemy.Capturable)
        )
            return ValidationResult.Fail($"{target.DisplayName} is not a robot or Proxy.");
        if (!target.IsStanding)
            return ValidationResult.Fail($"{target.DisplayName} is down.");
        if (target.Statuses.Capturable && target.Statuses.Stunned)
            return ValidationResult.Fail($"{target.DisplayName} is already stunned.");
        return
            HitCalculator.Preview(ctx.State, ctx.User, target, Shot(ctx))
                is { CanFire: false } blocked
            ? ValidationResult.Fail(blocked.Reason!)
            : ValidationResult.Ok;
    }

    private static int RobotDamage(AbilityContext ctx) =>
        (int)ItemUse.Item(ctx).Parameters.GetValueOrDefault("robotDamage", 2);

    /// <summary>The Proxy is hurt enough to be taken alive.</summary>
    private static bool Captures(AbilityContext ctx, TacticalUnit target) =>
        ctx.Data.Enemies.TryGet(target.DefId, out var enemy)
        && enemy.Capturable
        && target.Hp * 100
            <= ItemUse.Item(ctx).Parameters.GetValueOrDefault("captureHpPercent", 60)
                * target.MaxHp;

    public void Execute(AbilityContext ctx)
    {
        ItemUse.Spend(ctx);
        var user = ctx.User;
        var target = ctx.State.Unit(((UnitTarget)ctx.Target).Id);
        var preview = HitCalculator.Preview(ctx.State, user, target, Shot(ctx));
        ctx.Events.Add(new UnitFacing(user.Id, target.Position));
        user.Stats.ShotsFired++;
        if (!ctx.Rng.Roll(preview.HitChance))
        {
            ctx.Events.Add(
                new ShotResolved(user.Id, target.Id, preview.HitChance, false, false, 0)
            );
            return;
        }
        user.Stats.Hits++;
        var capture = Captures(ctx, target);
        var damage = capture ? 0 : RobotDamage(ctx);
        ctx.Events.Add(
            new ShotResolved(user.Id, target.Id, preview.HitChance, true, false, damage)
        );
        if (damage > 0)
        {
            var hpBefore = target.Hp;
            target.Hp = Math.Max(0, target.Hp - damage);
            user.Stats.DamageDealt += hpBefore - target.Hp;
            ctx.Events.Add(new UnitDamaged(target.Id, damage, target.Hp, target.Armor));
            if (target.Hp == 0 && Casualties.Drop(target, ctx.Rng, ctx.Events))
            {
                user.Stats.Kills++;
                return;
            }
        }
        if (target.ImmuneToStun)
            return;
        target.Statuses.Set(StatusFlags.Stunned);
        ctx.Events.Add(new StatusAdded(target.Id, StatusFlags.Stunned));
        Suppression.Release(ctx.State, target.Id, ctx.Events);
        AllyOverclockAbility.StandDown(target, ctx.Events);
        if (capture)
        {
            target.Statuses.Set(StatusFlags.Capturable);
            ctx.Events.Add(new AbilityTriggered(target.Id, ctx.Def.Id, "CAPTURABLE"));
        }
    }
}

/// <summary>
/// Carry and Drop (GDD §4.7, §5.5): pick up an adjacent downed squadmate or a capturable Proxy. The
/// carrier loses <c>mobilityPenalty</c> mobility and cannot shoot; the carried unit rides along and
/// leaves with them through evac.
/// </summary>
public sealed class CarryAbility : IAbilityHandler
{
    public const int DefaultMobilityPenalty = 2;

    public ValidationResult Validate(AbilityContext ctx)
    {
        var user = ctx.User;
        if (user.Carrying is { } held)
            return ValidationResult.Fail($"Already carrying {held.DisplayName}.");
        var target = ctx.State.Unit(((UnitTarget)ctx.Target).Id);
        if (!target.IsOnMap)
            return ValidationResult.Fail("That target is not valid.");
        if (!ItemUse.Adjacent(user.Position, target.Position))
            return ValidationResult.Fail($"{target.DisplayName} is not adjacent.");
        return CanBeCarried(user, target)
            ? ValidationResult.Ok
            : ValidationResult.Fail($"{target.DisplayName} cannot be carried.");
    }

    /// <summary>A downed squadmate (bleeding out or stabilized) or a stunned, capturable enemy.</summary>
    public static bool CanBeCarried(TacticalUnit carrier, TacticalUnit target) =>
        target.Id != carrier.Id
        && (
            target.Faction == carrier.Faction && target.Statuses.IsDown
            || target.Faction == Faction.Meridian
                && target.Statuses.Capturable
                && target.Statuses.Stunned
        );

    public static int Penalty(AbilityDef def) =>
        (int)def.Parameters.GetValueOrDefault("mobilityPenalty", DefaultMobilityPenalty);

    public void Execute(AbilityContext ctx)
    {
        var user = ctx.User;
        var target = ctx.State.Unit(((UnitTarget)ctx.Target).Id);
        ctx.Events.Add(new UnitFacing(user.Id, target.Position));
        user.Carrying = target;
        target.CarriedBy = user;
        target.Position = user.Position;
        user.Mobility -= Penalty(ctx.Def);
        ctx.Events.Add(new UnitCarried(user.Id, target.Id));
    }

    /// <summary>
    /// Puts down whoever <paramref name="carrier"/> carries on <paramref name="at"/> and gives the
    /// mobility back. Does nothing for a carrier with nothing to put down.
    /// </summary>
    public static void Release(
        TacticalUnit carrier,
        GridPos at,
        int mobilityPenalty,
        List<GameEvent> events
    )
    {
        if (carrier.Carrying is not { } carried)
            return;
        carrier.Carrying = null;
        carried.CarriedBy = null;
        carried.Position = at;
        carrier.Mobility += mobilityPenalty;
        events.Add(new UnitDropped(carrier.Id, carried.Id, at));
    }
}

/// <summary>Drop: puts the carried unit down on a free tile next to the carrier.</summary>
public sealed class DropAbility : IAbilityHandler
{
    public ValidationResult Validate(AbilityContext ctx) =>
        ctx.User.Carrying is null
            ? ValidationResult.Fail($"{ctx.User.DisplayName} is not carrying anyone.")
        : DropTile(ctx.State, ctx.User.Position) is null
            ? ValidationResult.Fail("No free tile to put them down.")
        : ValidationResult.Ok;

    /// <summary>The free walkable tile next to <paramref name="from"/>, orthogonal ones first, then by Y and X.</summary>
    public static GridPos? DropTile(TacticalState state, GridPos from)
    {
        foreach (var pos in state.NearestFreeTiles(from))
        {
            if (ItemUse.Adjacent(from, pos))
                return pos;
            if (pos.DistanceTo(from) > 1.5)
                break;
        }
        return null;
    }

    public void Execute(AbilityContext ctx)
    {
        var at = DropTile(ctx.State, ctx.User.Position)!.Value;
        CarryAbility.Release(
            ctx.User,
            at,
            CarryAbility.Penalty(ctx.Data.Abilities[AbilityRegistry.Carry]),
            ctx.Events
        );
    }
}
