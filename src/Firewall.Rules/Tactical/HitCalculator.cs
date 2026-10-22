using Firewall.Rules.Data;
using Firewall.Rules.Rng;
using Firewall.Rules.Tactical.Vision;

namespace Firewall.Rules.Tactical;

public enum RangeType
{
    Ranged,

    /// <summary>Needs an adjacent target (8-neighbour) and ignores cover and range.</summary>
    Melee,
}

/// <summary>How a shot is taken: everything the attacker and target do not say themselves.</summary>
public sealed record ShotContext
{
    public static ShotContext Default { get; } = new();

    public RangeType RangeType { get; init; } = RangeType.Ranged;

    /// <summary>An overwatch shot: the hit chance is multiplied by 0.7 last and it cannot crit.</summary>
    public bool Reaction { get; init; }

    /// <summary>Sentinel: a reaction shot can crit after all.</summary>
    public bool ReactionCrit { get; init; }

    /// <summary>Replaces the attacker's Aim (the Arc Stunner's 70 % base hit); null keeps it.</summary>
    public int? BaseAim { get; init; }

    /// <summary>Damage range for attackers without a weapon (enemies use their <see cref="EnemyDef"/>).</summary>
    public int DamageMin { get; init; }
    public int DamageMax { get; init; }

    /// <summary>Crit chance for attackers without a weapon.</summary>
    public int BaseCrit { get; init; }

    /// <summary>Rounds the shot uses (Suppression uses 2). Attackers without a weapon use none.</summary>
    public int AmmoCost { get; init; } = 1;

    /// <summary>The game data, for the numbers of passive abilities; null falls back to the GDD values.</summary>
    public GameData? Data { get; init; }

    /// <summary>The ability this shot belongs to (Rapid Fire, Headshot, ...); null for a plain shot.</summary>
    public string? AbilityId { get; init; }

    /// <summary>Flat damage added to the roll before a crit multiplies it (Headshot, Mayhem).</summary>
    public int BonusDamage { get; init; }

    /// <summary>A hit leaves the target <see cref="StatusFlags.Ruptured"/> (Rupture).</summary>
    public bool Ruptures { get; init; }

    private readonly IReadOnlyList<(string Label, int Value)>? _aimMods;

    /// <summary>Additive hit modifiers from abilities and items, applied before overwatch.</summary>
    public IReadOnlyList<(string Label, int Value)> AimMods
    {
        get => _aimMods ?? [];
        init => _aimMods = value;
    }

    private readonly IReadOnlyList<(string Label, int Value)>? _critMods;

    /// <summary>Additive crit modifiers from abilities and items.</summary>
    public IReadOnlyList<(string Label, int Value)> CritMods
    {
        get => _critMods ?? [];
        init => _critMods = value;
    }

    /// <summary>
    /// The context for <paramref name="attacker"/>'s plain attack: its weapon when it has one,
    /// otherwise its enemy definition's damage.
    /// </summary>
    public static ShotContext For(
        GameData data,
        TacticalUnit attacker,
        RangeType rangeType = RangeType.Ranged,
        AbilityDef? ability = null
    )
    {
        var context =
            attacker.Weapon is null && data.Enemies.TryGet(attacker.DefId, out var enemy)
                ? new ShotContext
                {
                    RangeType = rangeType,
                    DamageMin = enemy.DmgMin,
                    DamageMax = enemy.DmgMax,
                }
                : new ShotContext { RangeType = rangeType };
        context = context with { Data = data };
        return ability is null ? context : context.ForAbility(ability);
    }

    /// <summary>
    /// The numbers an active shot ability adds (GDD §5.2): Rapid Fire's aim penalty, Headshot's
    /// damage, Rupture's guaranteed crit.
    /// </summary>
    private ShotContext ForAbility(AbilityDef ability)
    {
        var context = this with { AbilityId = ability.Id };
        if (ability.Parameters.TryGetValue("aim", out var aim) && aim != 0)
            context = context with { AimMods = [.. AimMods, (ability.Name, (int)aim)] };
        switch (ability.Id)
        {
            case SoldierPerks.Headshot:
                return context with { BonusDamage = (int)ability.Parameters["damage"] };
            case SoldierPerks.Rupture:
                return context with
                {
                    CritMods = [.. CritMods, (ability.Name, 100)],
                    Ruptures = true,
                };
            default:
                return context;
        }
    }
}

/// <summary>The odds of one shot, as shown in the shot panel.</summary>
/// <param name="Breakdown">The hit modifiers; they add up to the hit chance before clamping.</param>
/// <param name="CritBreakdown">The crit modifiers; they add up to the crit chance before clamping.</param>
public sealed record ShotPreview(
    int HitChance,
    int CritChance,
    int DamageMin,
    int DamageMax,
    IReadOnlyList<(string Label, int Value)> Breakdown,
    IReadOnlyList<(string Label, int Value)> CritBreakdown,
    bool Flanked,
    bool CanFire,
    string? Reason
)
{
    /// <summary>The ability that makes this shot miss for certain (Untouchable, Lightning Reflexes), if any.</summary>
    public string? AutoMissBy { get; init; }

    /// <summary>The breakdown's sum: the hit chance before clamping.</summary>
    public int RawHitChance => Breakdown.Sum(b => b.Value);
}

/// <summary>The rolled result of one shot, before damage hooks.</summary>
/// <param name="Damage">After the crit multiplier and armor; 0 on a miss.</param>
public readonly record struct ShotOutcome(bool Hit, bool Crit, int Rolled, int Damage);

/// <summary>Hit, crit and damage (GDD §4.5). Pure: the preview never touches the RNG.</summary>
public static class HitCalculator
{
    public const int FlankedCrit = 50;
    public const int SuppressedAim = -30;

    /// <summary>Overwatch multiplies the hit chance by 70 %, as an integer to avoid 0.7's binary error.</summary>
    public const int ReactionAimPercent = 70;

    public const int HunkerCoverMultiplier = 2;

    public static ShotPreview Preview(
        TacticalState state,
        TacticalUnit attacker,
        TacticalUnit target,
        ShotContext ctx
    )
    {
        var melee = ctx.RangeType == RangeType.Melee;
        var weapon = attacker.Weapon;
        var breakdown = new List<(string, int)> { ("Aim", ctx.BaseAim ?? attacker.Aim) };
        if (!melee && weapon is not null)
            breakdown.Add(
                ("Range", weapon.Def.RangeMod(attacker.Position.DistanceTo(target.Position)))
            );
        AddIfNonZero(breakdown, "Target Defense", -target.Defense);
        AddIfNonZero(breakdown, "Smoke", -state.SmokeDefenseAt(target.Position));

        var flanked = false;
        if (!melee)
        {
            var cover = CoverCalculator.Evaluate(state.Map, target.Position, attacker.Position);
            flanked = cover.Flanked;
            if (cover.Level != CoverLevel.None)
            {
                var level = cover.Level;
                var bonus = cover.DefenseBonus;
                var lowProfile =
                    level == CoverLevel.Half && target.Abilities.Contains(SoldierPerks.LowProfile);
                if (lowProfile)
                {
                    level = CoverLevel.Full;
                    bonus = CoverCalculator.FullDefense;
                }
                breakdown.Add(
                    (
                        lowProfile ? "Full Cover (Low Profile)"
                        : level == CoverLevel.Full ? "Full Cover"
                        : "Half Cover",
                        -bonus
                    )
                );
                if (target.Statuses.Hunkered)
                    breakdown.Add(("Hunkered", -bonus * (HunkerCoverMultiplier - 1)));
            }
        }
        if (attacker.Statuses.Suppressed)
            breakdown.Add(("Suppressed", SuppressedAim));
        if (attacker.Abilities.Contains(SoldierPerks.SteadyAim) && attacker.Stats.ShotsFired == 0)
            AddIfNonZero(
                breakdown,
                "Steady Aim",
                SoldierPerks.IntParam(ctx.Data, SoldierPerks.SteadyAim, "aim", 15)
            );
        if (!melee && FiresAfterMoving(attacker))
            AddIfNonZero(
                breakdown,
                "Snap Shot",
                SoldierPerks.IntParam(ctx.Data, SoldierPerks.SnapShot, "aim", -20)
            );
        foreach (var (label, value) in ctx.AimMods)
            AddIfNonZero(breakdown, label, value);
        if (ctx.Reaction)
        {
            var sum = breakdown.Sum(b => b.Item2);
            breakdown.Add(("Overwatch x0.7", RoundPercent(sum, ReactionAimPercent) - sum));
        }
        var autoMiss = AutoMissBy(target, ctx);
        if (autoMiss is not null)
            breakdown.Add((AbilityName(ctx, autoMiss), -breakdown.Sum(b => b.Item2)));
        var hit = Math.Clamp(breakdown.Sum(b => b.Item2), 0, 100);

        var critBreakdown = new List<(string, int)>();
        if (!ctx.Reaction || ctx.ReactionCrit)
        {
            AddIfNonZero(critBreakdown, "Weapon", weapon?.Def.Crit ?? ctx.BaseCrit);
            if (flanked)
                critBreakdown.Add(("Flanked", FlankedCrit));
            foreach (var (label, value) in ctx.CritMods)
                AddIfNonZero(critBreakdown, label, value);
            if (
                attacker.Abilities.Contains(SoldierPerks.CloseCombat)
                && attacker.Position.DistanceTo(target.Position)
                    <= SoldierPerks.Param(ctx.Data, SoldierPerks.CloseCombat, "maxDist", 4)
            )
                AddIfNonZero(
                    critBreakdown,
                    "Close Combat",
                    SoldierPerks.IntParam(ctx.Data, SoldierPerks.CloseCombat, "crit", 20)
                );
        }
        var bonusDamage =
            ctx.BonusDamage
            + (
                target.Statuses.Ruptured
                    ? SoldierPerks.IntParam(ctx.Data, SoldierPerks.Rupture, "damage", 3)
                    : 0
            );
        var crit = Math.Clamp(critBreakdown.Sum(b => b.Item2), 0, 100);

        var reason = WhyNot(state, attacker, target, ctx);
        return new ShotPreview(
            hit,
            crit,
            (weapon?.Def.DmgMin ?? ctx.DamageMin) + bonusDamage,
            (weapon?.Def.DmgMax ?? ctx.DamageMax) + bonusDamage,
            breakdown,
            critBreakdown,
            flanked,
            reason is null,
            reason
        )
        {
            AutoMissBy = autoMiss,
        };
    }

    /// <summary>
    /// The ability that turns this attack into a sure miss: Lightning Reflexes against the first
    /// reaction shot of a phase, or an armed Untouchable against any attack.
    /// </summary>
    private static string? AutoMissBy(TacticalUnit target, ShotContext ctx) =>
        ctx.Reaction
        && target.Abilities.Contains(SoldierPerks.LightningReflexes)
        && !target.Perks.LightningReflexesSpent
            ? SoldierPerks.LightningReflexes
        : target.Abilities.Contains(SoldierPerks.Untouchable) && target.Perks.UntouchableArmed
            ? SoldierPerks.Untouchable
        : null;

    private static string AbilityName(ShotContext ctx, string abilityId) =>
        ctx.Data is not null && ctx.Data.Abilities.TryGet(abilityId, out var def)
            ? def.Name
            : abilityId;

    /// <summary>The shooter moved this turn and its weapon would not allow a shot: Snap Shot is in use.</summary>
    private static bool FiresAfterMoving(TacticalUnit attacker) =>
        attacker.MovedThisTurn
        && attacker.Weapon is { } weapon
        && weapon.Def.Flags.Contains(WeaponFlag.NoFireAfterMove)
        && SoldierPerks.CanFireAfterMoving(attacker);

    /// <summary>
    /// Rolls a previewed shot in the fixed order: hit, then crit (only on a hit), then damage
    /// (only on a hit). Replays depend on this order.
    /// </summary>
    public static ShotOutcome Roll(ShotPreview preview, int targetArmor, IRandom rng)
    {
        if (!rng.Roll(preview.HitChance))
            return new ShotOutcome(false, false, 0, 0);
        var crit = rng.Roll(preview.CritChance);
        var rolled = rng.Next(preview.DamageMin, preview.DamageMax + 1);
        return new ShotOutcome(true, crit, rolled, Damage(rolled, crit, targetArmor));
    }

    /// <summary>Damage of a hit: a crit gives ceil(× 1.5), then armor is subtracted, minimum 1.</summary>
    public static int Damage(int rolled, bool crit, int armor)
    {
        var damage = crit ? (rolled * 3 + 1) / 2 : rolled;
        return Math.Max(1, damage - armor);
    }

    /// <summary><paramref name="value"/> × <paramref name="percent"/> / 100, rounded half away from zero.</summary>
    public static int RoundPercent(int value, int percent)
    {
        var scaled = value * percent;
        var rounded = (Math.Abs(scaled) + 50) / 100;
        return scaled < 0 ? -rounded : rounded;
    }

    private static string? WhyNot(
        TacticalState state,
        TacticalUnit attacker,
        TacticalUnit target,
        ShotContext ctx
    )
    {
        if (!attacker.IsOnMap)
            return $"{attacker.DisplayName} is not on the map.";
        if (attacker.Statuses.Stunned)
            return $"{attacker.DisplayName} is stunned.";
        if (!target.IsOnMap)
            return "That target is not valid.";
        // Downed soldiers are out of the fight: nobody shoots at them on purpose.
        if (target.Statuses.IsDown)
            return $"{target.DisplayName} is down.";
        if (ctx.RangeType == RangeType.Melee)
        {
            if (!IsAdjacent(attacker.Position, target.Position))
                return "The target is not adjacent.";
            return null;
        }
        if (attacker.Carrying is not null)
            return $"{attacker.DisplayName} can't shoot while carrying.";
        if (attacker.Weapon is { } weapon)
        {
            if (weapon.Ammo < ctx.AmmoCost)
                return weapon.Ammo == 0 ? "Out of ammo." : "Not enough ammo.";
            if (
                weapon.Def.Flags.Contains(WeaponFlag.NoFireAfterMove)
                && attacker.MovedThisTurn
                && !SoldierPerks.CanFireAfterMoving(attacker)
            )
                return "Sniper rifles can't fire after moving";
        }
        if (!LosTracer.CanSee(state, attacker, target) && !SquadSees(state, attacker, target, ctx))
            return "No line of sight to the target.";
        return null;
    }

    /// <summary>
    /// Squadsight: the target is in the squad's view and there is a clear line from the shooter's
    /// own tile, however far. Reaction shots still need the shooter's own sight.
    /// </summary>
    private static bool SquadSees(
        TacticalState state,
        TacticalUnit attacker,
        TacticalUnit target,
        ShotContext ctx
    ) =>
        !ctx.Reaction
        && attacker.Faction == Faction.Firewall
        && attacker.Abilities.Contains(SoldierPerks.Squadsight)
        && state.PlayerVision.IsUnitVisible(target)
        && LosTracer.HasLos(state.Map, attacker.Position, target.Position);

    private static bool IsAdjacent(GridPos a, GridPos b) =>
        a != b && Math.Abs(a.X - b.X) <= 1 && Math.Abs(a.Y - b.Y) <= 1;

    private static void AddIfNonZero(List<(string, int)> list, string label, int value)
    {
        if (value != 0)
            list.Add((label, value));
    }
}
