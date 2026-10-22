using System.Numerics;
using Firewall.Rules.Tactical;
using Firewall.Web.Rendering;
using Firewall.Web.Units;
using Yaeger.Graphics;

namespace Firewall.Web.Ui;

/// <summary>
/// The bottom-centre panel of Shoot targeting: big hit %, crit %, damage, ammo, a FLANKED badge
/// and the hit breakdown (+green / -red).
/// </summary>
public static class ShotPanel
{
    public const float Width = 480f;
    private const float RowHeight = 20f;
    private const float Margin = 16f;

    public static readonly TextStyle HitStyle = new(
        TextStyles.Future,
        52,
        new Color(255, 255, 255)
    );
    private static readonly Color Plus = new(110, 224, 122);
    private static readonly Color Minus = new(255, 96, 96);
    private static readonly Color Neutral = new(200, 205, 215);

    /// <summary>"+10", "-20" or "0".</summary>
    public static string Signed(int value) => value > 0 ? $"+{value}" : value.ToString();

    public static Color ValueColor(int value) =>
        value > 0 ? Plus
        : value < 0 ? Minus
        : Neutral;

    /// <summary>The panel rectangle for a breakdown of <paramref name="rows"/> lines.</summary>
    public static UiRect Rect(int rows, float bottomMargin = Margin)
    {
        // Title bar, then the taller of the two columns, then the key hint.
        var height = 40f + MathF.Max(150f, rows * RowHeight + 4f) + 40f;
        return new UiRect(
            (UiSpace.LogicalWidth - Width) / 2f,
            UiSpace.LogicalHeight - height - bottomMargin,
            Width,
            height
        );
    }

    public static void Draw(
        ScreenCanvas canvas,
        UiContext ui,
        TargetCandidate target,
        WeaponState? weapon,
        int candidateCount,
        float bottomMargin = Margin,
        string? abilityId = null,
        string? detail = null
    )
    {
        if (target.Preview is not { } p)
        {
            DrawPlain(ui, target, candidateCount, bottomMargin, detail);
            return;
        }
        // A hack has a chance but no damage; the Arc Stunner has damage, or a capture instead.
        var stunner = abilityId == AbilityRegistry.ArcStunner;
        var attack = p.DamageMax > 0 || stunner;
        var rows = p.Breakdown.Count + (p.CritBreakdown.Count > 0 ? 1 + p.CritBreakdown.Count : 0);
        var rect = Rect(rows, bottomMargin);
        var title =
            candidateCount > 1
                ? $"{target.Unit.DisplayName}   ({candidateCount} targets, Tab to cycle)"
                : target.Unit.DisplayName;
        var content = ui.Panel(rect, title);

        if (p.Flanked)
        {
            const string badge = "FLANKED";
            var w = canvas.Measure(badge, TextStyles.Small) + 16f;
            var at = new Vector2(rect.Right - w - 10f, rect.Y + 8f);
            canvas.FillRect(at, new Vector2(w, 22f), ui.Theme.Warning);
            canvas.Text(
                badge,
                at + new Vector2(8f, 4f),
                TextStyles.Small.With(new Color(20, 20, 20))
            );
        }

        // Left: the odds.
        var left = new Vector2(content.X + 4f, content.Y);
        canvas.Text($"{p.HitChance}%", left, HitStyle);
        canvas.Text(attack ? "TO HIT" : "TO HACK", left + new Vector2(4f, 58f), TextStyles.Small);
        var y = left.Y + 84f;
        if (attack)
        {
            canvas.Text($"Crit {p.CritChance}%", new Vector2(left.X, y), TextStyles.Body);
            canvas.Text(
                stunner && p.DamageMax == 0 ? "Capture: stunned for good"
                    : p.DamageMin == p.DamageMax ? $"Damage {p.DamageMax}"
                    : $"Damage {p.DamageMin}-{p.DamageMax}",
                new Vector2(left.X, y + 22f),
                TextStyles.Body
            );
        }
        if (attack && !stunner && weapon is not null)
            canvas.Text(
                $"Ammo {weapon.Ammo}/{weapon.Def.Clip}",
                new Vector2(left.X, y + 44f),
                TextStyles.Body
            );

        // Right: the breakdown, which adds up to the hit % before clamping.
        var colX = content.X + 190f;
        var valueRight = content.Right - 4f;
        var rowY = content.Y + 2f;
        foreach (var (label, value) in p.Breakdown)
            rowY = Row(canvas, label, value, colX, valueRight, rowY);
        if (p.CritBreakdown.Count > 0)
        {
            canvas.Text("Crit", new Vector2(colX, rowY + 2f), ui.Theme.Title);
            rowY += RowHeight;
            foreach (var (label, value) in p.CritBreakdown)
                rowY = Row(canvas, label, value, colX, valueRight, rowY);
        }

        canvas.Text(
            stunner ? "Enter / Space / click target: stun    Tab: next target    Esc / RMB: cancel"
                : attack
                    ? "Enter / Space / click target: fire    Tab: next target    Esc / RMB: cancel"
                : "Enter / Space / click target: hack    Tab: next target    Esc / RMB: cancel",
            new Vector2(content.X, rect.Bottom - 26f),
            TextStyles.Small
        );
    }

    // Abilities with no odds to show (Suppression, Overclock): just who, and the keys.
    private static void DrawPlain(
        UiContext ui,
        TargetCandidate target,
        int candidateCount,
        float bottomMargin,
        string? detail
    )
    {
        var height = detail is null ? 100f : 124f;
        var rect = new UiRect(
            (UiSpace.LogicalWidth - Width) / 2f,
            UiSpace.LogicalHeight - height - bottomMargin,
            Width,
            height
        );
        var title =
            candidateCount > 1
                ? $"{target.Unit.DisplayName}   ({candidateCount} targets, Tab to cycle)"
                : target.Unit.DisplayName;
        var content = ui.Panel(rect, title);
        var y = content.Y + 8f;
        if (detail is not null)
        {
            ui.Label(new UiRect(content.X, y, content.W, 24f), detail, ui.Theme.Body);
            y += 24f;
        }
        ui.Label(
            new UiRect(content.X, y, content.W, 24f),
            "Enter / Space / click target: use    Tab: next target    Esc / RMB: cancel",
            ui.Theme.Small
        );
    }

    private static float Row(
        ScreenCanvas canvas,
        string label,
        int value,
        float x,
        float right,
        float y
    )
    {
        canvas.Text(label, new Vector2(x, y), TextStyles.Tooltip);
        var text = Signed(value);
        var style = TextStyles.Tooltip.With(ValueColor(value));
        canvas.Text(text, new Vector2(right - canvas.Measure(text, style), y), style);
        return y + RowHeight;
    }
}
