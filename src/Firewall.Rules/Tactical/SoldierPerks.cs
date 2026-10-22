using Firewall.Rules.Data;

namespace Firewall.Rules.Tactical;

/// <summary>
/// The soldier abilities of GDD §5.2 (#30): their ids, the numbers they read from
/// <c>abilities.json</c>, and the small rules that need no handler. Passive abilities act through
/// <see cref="HitCalculator"/>, the AP rules and reaction fire in the engine; the item passives
/// (Grenadier, Field Medic, ...) are the numbers the item handlers (#31) ask for here.
/// </summary>
public static class SoldierPerks
{
    // Breacher
    public const string RunAndGun = "run_and_gun";
    public const string CloseCombat = "close_combat";
    public const string LightningReflexes = "lightning_reflexes";
    public const string RapidFire = "rapid_fire";
    public const string HitAndRun = "hit_and_run";
    public const string Untouchable = "untouchable";
    public const string KillingZone = "killing_zone";

    // Marksman
    public const string Squadsight = "squadsight";
    public const string SnapShot = "snap_shot";
    public const string SteadyAim = "steady_aim";
    public const string Sentinel = AbilityRegistry.Sentinel;
    public const string LowProfile = "low_profile";
    public const string InTheZone = "in_the_zone";
    public const string Headshot = "headshot";

    // Gunner
    public const string Suppression = SuppressionAbility.AbilityId;
    public const string Shredder = "shredder";
    public const string Bulletstorm = "bulletstorm";
    public const string Grenadier = "grenadier";
    public const string DangerZone = "danger_zone";
    public const string Mayhem = "mayhem";
    public const string Rupture = "rupture";

    // Tech
    public const string FieldMedic = "field_medic";
    public const string SmokeScreen = "smoke_screen";
    public const string Revive = "revive";
    public const string Hack = "hack";
    public const string Sprinter = "sprinter";
    public const string Savior = "savior";
    public const string Overclock = "overclock";

    /// <summary>
    /// A number of <paramref name="abilityId"/> from the data. The fallback is the GDD value, used
    /// when no data is at hand (some tests shoot with <see cref="ShotContext.Default"/>).
    /// </summary>
    public static double Param(GameData? data, string abilityId, string key, double fallback) =>
        data is not null
        && data.Abilities.TryGet(abilityId, out var def)
        && def.Parameters.TryGetValue(key, out var value)
            ? value
            : fallback;

    public static int IntParam(GameData? data, string abilityId, string key, int fallback) =>
        (int)Param(data, abilityId, key, fallback);

    /// <summary>
    /// Whether a weapon that cannot fire after moving may do so anyway: only with Snap Shot, which
    /// costs aim (<see cref="HitCalculator"/>).
    /// </summary>
    public static bool CanFireAfterMoving(TacticalUnit unit) =>
        unit.Abilities.Contains(SnapShot);

    /// <summary>Run &amp; Gun is ready: the unit dashed this turn and the ability is off cooldown.</summary>
    public static bool CanRunAndGun(TacticalUnit unit) =>
        unit.Abilities.Contains(RunAndGun)
        && unit.Perks.DashedThisTurn
        && unit.Cooldowns.GetValueOrDefault(RunAndGun) <= 0;

    // ------------------------------------------------------------------ stats

    /// <summary>Extra mobility from abilities (Sprinter).</summary>
    public static int MobilityBonus(GameData data, IReadOnlySet<string> abilities) =>
        abilities.Contains(Sprinter) ? IntParam(data, Sprinter, "mobility", 3) : 0;

    // ------------------------------------------------------------------ items (used by #31)

    /// <summary>Grenades that hurt: Frag Grenade and Breach Charge. Smoke and the Lace Jammer do not.</summary>
    public static bool IsExplosive(ItemDef item) =>
        item.Kind == ItemKind.Grenade && item.Parameters.ContainsKey("damage");

    public static bool IsSmoke(ItemDef item) =>
        item.Kind == ItemKind.Grenade && item.Parameters.ContainsKey("defense");

    /// <summary>Charges a soldier with <paramref name="abilities"/> gets for <paramref name="item"/>.</summary>
    public static int ItemCharges(GameData data, IReadOnlySet<string> abilities, ItemDef item)
    {
        var charges = item.Charges;
        if (item.Kind == ItemKind.Medkit && abilities.Contains(FieldMedic))
            charges += IntParam(data, FieldMedic, "charges", 1);
        if (IsSmoke(item) && abilities.Contains(SmokeScreen))
            charges += IntParam(data, SmokeScreen, "charges", 1);
        if (IsExplosive(item) && abilities.Contains(Grenadier))
            charges += IntParam(data, Grenadier, "charges", 1);
        return charges;
    }

    /// <summary>Damage added to an explosive thrown by a soldier with <paramref name="abilities"/>.</summary>
    public static int ExplosiveDamageBonus(GameData data, IReadOnlySet<string> abilities) =>
        (abilities.Contains(Grenadier) ? IntParam(data, Grenadier, "damage", 1) : 0)
        + (abilities.Contains(Mayhem) ? IntParam(data, Mayhem, "damage", 2) : 0);

    /// <summary>Radius added to <paramref name="item"/> (Danger Zone for explosives, Smoke Screen for smoke).</summary>
    public static int ItemRadiusBonus(
        GameData data,
        IReadOnlySet<string> abilities,
        ItemDef item
    ) =>
        (IsExplosive(item) && abilities.Contains(DangerZone) ? IntParam(data, DangerZone, "radius", 1) : 0)
        + (IsSmoke(item) && abilities.Contains(SmokeScreen) ? IntParam(data, SmokeScreen, "radius", 1) : 0);

    /// <summary>HP a medkit heals: the item's, plus Revive and Savior.</summary>
    public static int MedkitHeal(GameData data, IReadOnlySet<string> abilities, ItemDef medkit) =>
        (int)medkit.Parameters.GetValueOrDefault("heal")
        + (abilities.Contains(Revive) ? IntParam(data, Revive, "heal", 2) : 0)
        + (abilities.Contains(Savior) ? IntParam(data, Savior, "heal", 4) : 0);

    /// <summary>Revive brings a bleeding-out soldier straight back up at this HP; 0 without it.</summary>
    public static int MedkitReviveHp(GameData data, IReadOnlySet<string> abilities) =>
        abilities.Contains(Revive) ? IntParam(data, Revive, "reviveHp", 1) : 0;

    /// <summary>
    /// Whether a medkit used from <paramref name="from"/> reaches <paramref name="to"/>: the same
    /// tile, a side-by-side tile, or with Field Medic the diagonals too.
    /// </summary>
    public static bool MedkitReaches(IReadOnlySet<string> abilities, GridPos from, GridPos to)
    {
        var dx = Math.Abs(from.X - to.X);
        var dy = Math.Abs(from.Y - to.Y);
        return abilities.Contains(FieldMedic) ? Math.Max(dx, dy) <= 1 : dx + dy <= 1;
    }
}
