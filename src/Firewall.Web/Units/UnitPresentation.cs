using System.Globalization;
using System.Numerics;
using Firewall.Rules;
using Firewall.Rules.Data;
using Firewall.Rules.Tactical;
using Yaeger.Graphics;

namespace Firewall.Web.Units;

public enum PipKind
{
    Filled,
    Empty,
    Armor,
}

/// <summary>The pure decisions behind <see cref="UnitView"/>: pose, rotation, tint, facing, pips.</summary>
public static class UnitPresentation
{
    /// <summary>Frame name of the body sprite, e.g. <c>unit.soldier1.machine</c>.</summary>
    public static string BodyFrame(TacticalUnit unit, EnemyDef? enemy, bool reloading) =>
        $"unit.{unit.AppearanceId}.{PoseFor(unit, enemy, reloading)}";

    /// <summary>Frame name for a unit as the view model shows it.</summary>
    public static string BodyFrame(Playback.UnitVm unit, EnemyDef? enemy, bool reloading) =>
        $"unit.{unit.AppearanceId}.{PoseFor(unit.Statuses, unit.WeaponClass, enemy, reloading)}";

    /// <summary>
    /// GDD §3 sprite mapping. Soldiers: rifle, shotgun and LMG hold <c>machine</c>, the sniper rifle
    /// <c>silencer</c>, a reload <c>reload</c>, unarmed <c>hold</c>. Stunned and downed (bleeding out
    /// or stabilized) units <c>hold</c>. Enemies use their definition's pose.
    /// </summary>
    public static string PoseFor(TacticalUnit unit, EnemyDef? enemy, bool reloading) =>
        PoseFor(unit.Statuses, unit.Weapon?.Def.Class, enemy, reloading);

    public static string PoseFor(
        StatusSet statuses,
        WeaponClass? weapon,
        EnemyDef? enemy,
        bool reloading
    )
    {
        if (statuses.Stunned || statuses.IsDown)
            return "hold";
        if (enemy is not null)
            return enemy.Sprite.Pose;
        if (reloading)
            return "reload";
        return weapon switch
        {
            WeaponClass.Sniper => "silencer",
            WeaponClass.Rifle or WeaponClass.Shotgun or WeaponClass.Lmg => "machine",
            _ => "hold",
        };
    }

    /// <summary>Source sprites face +X, so this is the rotation that makes them face <paramref name="dir"/>.</summary>
    public static float RotationFor(Vector2 dir) =>
        dir == Vector2.Zero ? 0f : MathF.Atan2(dir.Y, dir.X);

    /// <summary><c>#rrggbb</c> to a colour; null for none. Throws on a malformed string.</summary>
    public static Color? ParseTint(string? tint)
    {
        if (string.IsNullOrEmpty(tint))
            return null;
        if (tint.Length != 7 || tint[0] != '#')
            throw new FormatException($"Tint '{tint}' is not #rrggbb.");
        byte Channel(int i) =>
            byte.Parse(tint.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new Color(Channel(1), Channel(3), Channel(5));
    }

    /// <summary>
    /// Where a unit first looks: at the nearest living enemy within its sight, otherwise at the
    /// center of the map. The result is a direction from the unit, never zero.
    /// </summary>
    public static Vector2 InitialFacing(TacticalUnit unit, TacticalState state)
    {
        TacticalUnit? nearest = null;
        var best = double.MaxValue;
        foreach (var other in state.Units)
        {
            if (!other.IsOnMap || !IsOpponent(unit.Faction, other.Faction))
                continue;
            var d = unit.Position.DistanceTo(other.Position);
            if (d <= unit.Sight && d < best)
            {
                best = d;
                nearest = other;
            }
        }
        var target = nearest is { } n
            ? new Vector2(n.Position.X, n.Position.Y)
            : new Vector2((state.Map.Width - 1) / 2f, (state.Map.Height - 1) / 2f);
        var dir = target - new Vector2(unit.Position.X, unit.Position.Y);
        return dir == Vector2.Zero ? Vector2.UnitX : dir;
    }

    private static bool IsOpponent(Faction a, Faction b) =>
        a != Faction.Neutral && b != Faction.Neutral && a != b;

    /// <summary>
    /// One pip per max HP (filled up to current HP, then empty), then one grey pip per armor point.
    /// </summary>
    public static IReadOnlyList<PipKind> Pips(int hp, int maxHp, int armor)
    {
        var pips = new List<PipKind>();
        for (var i = 0; i < maxHp; i++)
            pips.Add(i < hp ? PipKind.Filled : PipKind.Empty);
        for (var i = 0; i < armor; i++)
            pips.Add(PipKind.Armor);
        return pips;
    }

    /// <summary>Status icon frames in the order they are drawn.</summary>
    public static IReadOnlyList<string> StatusIcons(StatusSet statuses)
    {
        var icons = new List<string>();
        if (statuses.Overwatch)
            icons.Add("status.overwatch");
        if (statuses.Hunkered)
            icons.Add("status.hunker");
        if (statuses.Suppressed)
            icons.Add("status.suppressed");
        if (statuses.IsBleedingOut)
            icons.Add("status.bleeding");
        return icons;
    }

    /// <summary>The bleeding-out icon's red pulse: alpha between 0.35 and 1, about twice a second.</summary>
    public static float BleedPulseAlpha(float seconds) =>
        0.675f + 0.325f * MathF.Sin(seconds * 2f * MathF.PI / 0.6f);

    /// <summary>The Relay Node's cyan pulse: a brightness factor between 0.55 and 1, about once a second.</summary>
    public static float RelayPulse(float seconds) =>
        0.775f + 0.225f * MathF.Sin(seconds * 2f * MathF.PI / 1.0f);

    /// <summary>Opacity of the suppression line: a pulse between 0.35 and 0.9.</summary>
    public static float SuppressionPulse(float seconds) =>
        0.625f + 0.275f * MathF.Sin(seconds * 2f * MathF.PI / 0.8f);

    /// <summary>Selection-ring opacity: a slow sine between 0.45 and 1.</summary>
    public static float RingAlpha(float seconds) =>
        0.725f + 0.275f * MathF.Sin(seconds * 2f * MathF.PI / 1.4f);

    /// <summary>Tooltip lines: name, HP, armor if any, and the type name for enemies.</summary>
    public static IReadOnlyList<string> TooltipLines(TacticalUnit unit, EnemyDef? enemy)
    {
        var lines = new List<string> { unit.DisplayName };
        if (enemy is not null && enemy.Name != unit.DisplayName)
            lines.Add(enemy.Name);
        lines.Add($"HP {unit.Hp}/{unit.MaxHp}");
        if (unit.Armor > 0)
            lines.Add($"Armor {unit.Armor}");
        if (unit.Statuses.Capturable)
            lines.Add("Stunned: can be captured");
        else if (unit.Statuses.Stunned)
            lines.Add("Stunned");
        if (unit.Carrying is { } carried)
            lines.Add($"Carrying {carried.DisplayName}");
        return lines;
    }
}
