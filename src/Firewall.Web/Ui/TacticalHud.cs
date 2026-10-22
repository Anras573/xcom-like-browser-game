using System.Numerics;
using Firewall.Rules.Tactical;
using Firewall.Web.Rendering;
using Yaeger.Graphics;
using Yaeger.Platform;

namespace Firewall.Web.Ui;

/// <summary>An ability-bar button plus why it cannot be used right now (null when it can).</summary>
public sealed record HudAbility(AbilitySlot Slot, string? BlockedReason);

/// <summary>A visible hostile in the indicator column; <paramref name="HitChance"/> is set while Shoot is armed.</summary>
public sealed record HudEnemy(TacticalUnit Unit, string Name, int? HitChance, bool Targeted);

/// <summary>The boss bar at the top of the screen: name and HP while the boss is active.</summary>
public sealed record HudBoss(string Name, int Hp, int MaxHp);

/// <summary>Everything the HUD shows this frame; the scene fills it from the rules state.</summary>
public sealed class HudFrame
{
    public required IReadOnlyList<TacticalUnit> Squad { get; init; }
    public TacticalUnit? Selected { get; init; }
    public IReadOnlyList<HudAbility> Abilities { get; init; } = [];
    public string? ArmedAbility { get; init; }
    public IReadOnlyList<HudEnemy> Enemies { get; init; } = [];
    public HudBoss? Boss { get; init; }
    public string Objective { get; init; } = "";
    public string MissionName { get; init; } = "";
    public string? EndTurnBlocked { get; init; }
    public string? AbortBlocked { get; init; }

    /// <summary>Playback or the enemy phase is running: the bar and End Turn are disabled.</summary>
    public bool Busy { get; init; }

    public string? BannerText { get; init; }
    public float BannerTime { get; init; }
    public float BannerSeconds { get; init; }

    /// <summary>Keeps the banner up without animating it (the mission result).</summary>
    public bool BannerStatic { get; init; }

    public float Time { get; init; }
}

/// <summary>What the player clicked on the HUD this frame.</summary>
public sealed class HudResult
{
    public string? Ability { get; set; }
    public bool EndTurn { get; set; }
    public bool Abort { get; set; }
    public bool Settings { get; set; }
    public UnitId? SelectSoldier { get; set; }
    public bool CenterSelected { get; set; }
    public UnitId? ClickedEnemy { get; set; }
}

/// <summary>
/// The tactical HUD (issue #27): objective, phase banner, enemy indicators, soldier strip and
/// panel, ability bar and End Turn. Pure drawing: it reports clicks in a <see cref="HudResult"/>
/// and leaves every decision to the scene.
/// </summary>
public static class TacticalHud
{
    private const float Margin = 16f;
    private const float SlotSize = 56f;
    private const float SlotGap = 6f;
    private const float EnemySize = 44f;
    private const int MaxEnemyIcons = 10;

    private static readonly Color Red = new(255, 77, 77);
    private static readonly Color Amber = new(255, 176, 0);
    private static readonly Color Green = new(110, 224, 122);
    private static readonly Vector4 DimFill = new(0f, 0f, 0f, 0.55f);

    public static UiRect SoldierPanelRect { get; } = new(Margin, 584f, 340f, 120f);

    public static UiRect AbilityBarRect(int count)
    {
        var w = count * SlotSize + Math.Max(0, count - 1) * SlotGap;
        return new UiRect(
            (UiSpace.LogicalWidth - w) / 2f,
            UiSpace.LogicalHeight - Margin - SlotSize,
            w,
            SlotSize
        );
    }

    public static UiRect EndTurnRect { get; } =
        new(UiSpace.LogicalWidth - Margin - 160f, UiSpace.LogicalHeight - Margin - 56f, 160f, 56f);

    public static UiRect AbortRect { get; } =
        new(EndTurnRect.X - 12f - 170f, UiSpace.LogicalHeight - Margin - 40f, 170f, 40f);

    /// <summary>The bottom margin a panel sitting above the ability bar (the shot panel) must leave.</summary>
    public const float AboveBar = Margin + SlotSize + 30f;

    public static HudResult Draw(ScreenCanvas canvas, UiContext ui, HudFrame f)
    {
        var result = new HudResult();
        DrawObjective(canvas, ui, f);
        DrawTopRight(canvas, ui, f, result);
        DrawBoss(canvas, f);
        DrawEnemies(canvas, ui, f, result);
        DrawStrip(canvas, ui, f, result);
        DrawSoldierPanel(canvas, ui, f, result);
        DrawAbilityBar(canvas, ui, f, result);
        DrawTurnButtons(canvas, ui, f, result);
        DrawBanner(canvas, ui, f);
        return result;
    }

    // ------------------------------------------------------------------ top

    private static void DrawObjective(ScreenCanvas canvas, UiContext ui, HudFrame f)
    {
        var w = Math.Clamp(canvas.Measure(f.Objective, ui.Theme.Body) + 28f, 240f, 560f);
        var rect = new UiRect(Margin, Margin, w, 40f);
        var content = ui.Panel(rect);
        ui.Tooltip(rect, "Objective");
        ui.Label(content with { H = rect.H - 2f * ui.Theme.Padding }, f.Objective, ui.Theme.Body);
    }

    private static void DrawTopRight(ScreenCanvas canvas, UiContext ui, HudFrame f, HudResult r)
    {
        var cog = new UiRect(UiSpace.LogicalWidth - Margin - 44f, Margin, 44f, 44f);
        if (ui.IconButton(cog, "icon.gear", tooltip: "Pause menu  [Esc]"))
            r.Settings = true;
        var w = canvas.Measure(f.MissionName, ui.Theme.Title) + 28f;
        var name = new UiRect(cog.X - 8f - w, Margin, w, 40f);
        var content = ui.Panel(name);
        ui.Label(
            content with
            {
                H = name.H - 2f * ui.Theme.Padding,
            },
            f.MissionName,
            ui.Theme.Title
        );
    }

    // Top centre, under the objective row: the boss's name over a red HP bar.
    private static void DrawBoss(ScreenCanvas canvas, HudFrame f)
    {
        if (f.Boss is not { } boss)
            return;
        var rect = new UiRect((UiSpace.LogicalWidth - 480f) / 2f, 64f, 480f, 34f);
        canvas.FillRect(new Vector2(rect.X, rect.Y), new Vector2(rect.W, rect.H), DimFill);
        var share = boss.MaxHp <= 0 ? 0f : Math.Clamp(boss.Hp / (float)boss.MaxHp, 0f, 1f);
        var inner = new Vector2(rect.W - 8f, 10f);
        var origin = new Vector2(rect.X + 4f, rect.Y + rect.H - 14f);
        canvas.FillRect(origin, inner, new Vector4(0.2f, 0.05f, 0.05f, 0.9f));
        canvas.FillRect(
            origin,
            new Vector2(inner.X * share, inner.Y),
            new Vector4(0.9f, 0.2f, 0.18f, 1f)
        );
        var label = $"{boss.Name.ToUpperInvariant()}   {boss.Hp}/{boss.MaxHp}";
        canvas.Text(label, new Vector2(rect.X + 10f, rect.Y + 2f), TextStyles.Small);
    }

    // ------------------------------------------------------------------ banner

    private static void DrawBanner(ScreenCanvas canvas, UiContext ui, HudFrame f)
    {
        if (f.BannerText is not { } text)
            return;
        var (offset, alpha) = f.BannerStatic
            ? (0f, 1f)
            : HudModel.BannerAnimation(f.BannerTime, f.BannerSeconds);
        if (alpha <= 0f)
            return;
        var style = TextStyles.Heading;
        var w = canvas.Measure(text, style) + 80f;
        var rect = new UiRect((UiSpace.LogicalWidth - w) / 2f, 76f + offset * 90f, w, 52f);
        canvas.FillRect(rect.Position, rect.Size, new Vector4(0.04f, 0.06f, 0.1f, 0.85f * alpha));
        canvas.FillRect(
            new Vector2(rect.X, rect.Bottom - 3f),
            new Vector2(rect.W, 3f),
            ui.Theme.Accent with
            {
                W = alpha,
            }
        );
        canvas.Text(
            text,
            new Vector2(rect.X + 40f, rect.Y + 10f),
            style.With(new Color(255, 220, 90, (byte)(255 * alpha)))
        );
    }

    // ------------------------------------------------------------------ enemies

    private static void DrawEnemies(ScreenCanvas canvas, UiContext ui, HudFrame f, HudResult r)
    {
        var x = UiSpace.LogicalWidth - Margin - EnemySize;
        var y = 76f;
        foreach (var enemy in f.Enemies.Take(MaxEnemyIcons))
        {
            var rect = new UiRect(x, y, EnemySize, EnemySize);
            var unit = enemy.Unit;
            var tip =
                $"[c=#ff6b6b]{enemy.Name}[/c]\nHP {unit.Hp}/{unit.MaxHp}"
                + (enemy.HitChance is { } hit ? $"\nHit chance [c=#6ee07a]{hit}%[/c]" : "")
                + (
                    f.Busy ? ""
                    : enemy.HitChance is null ? "\nClick to centre the camera"
                    : "\nClick to target"
                );
            if (ui.IconButton(rect, "icon.singleplayer", tooltip: tip, selected: enemy.Targeted))
                r.ClickedEnemy = unit.Id;
            ui.Icon(
                "icon.singleplayer",
                rect.Inset(MathF.Round(rect.W * 0.18f)),
                Vector4.One with
                {
                    Y = 0.3f,
                    Z = 0.3f,
                }
            );
            if (enemy.HitChance is { } pct)
            {
                var text = $"{pct}%";
                var tw = canvas.Measure(text, TextStyles.Small) + 6f;
                var pill = new UiRect(rect.Center.X - tw / 2f, rect.Bottom - 15f, tw, 15f);
                canvas.FillRect(pill.Position, pill.Size, new Vector4(0f, 0f, 0f, 0.8f));
                canvas.Text(
                    text,
                    pill.Position + new Vector2(3f, 1f),
                    TextStyles.Small with
                    {
                        Color =
                            pct >= 50 ? Green
                            : pct >= 25 ? Amber
                            : Red,
                    }
                );
            }
            y += EnemySize + 8f;
        }
        if (f.Enemies.Count > MaxEnemyIcons)
            canvas.Text(
                $"+{f.Enemies.Count - MaxEnemyIcons}",
                new Vector2(x + 8f, y),
                TextStyles.Small
            );
    }

    // ------------------------------------------------------------------ strip

    private static void DrawStrip(ScreenCanvas canvas, UiContext ui, HudFrame f, HudResult r)
    {
        const float w = 72f;
        const float h = 38f;
        var y = SoldierPanelRect.Y - 6f - h;
        var x = Margin;
        foreach (var unit in f.Squad)
        {
            var state = HudModel.StripStateOf(unit);
            var rect = new UiRect(x, y, w, h);
            x += w + 4f;
            var selected = f.Selected?.Id == unit.Id;
            var tint = state switch
            {
                StripState.BleedingOut => new Vector4(0.55f, 0.1f, 0.1f, 0.92f),
                StripState.Dead => new Vector4(0.12f, 0.06f, 0.06f, 0.92f),
                StripState.Evacuated => new Vector4(0.1f, 0.14f, 0.12f, 0.8f),
                _ => ui.Theme.PanelTint,
            };
            ui.Fill(rect, tint);
            if (selected)
                Outline(ui, rect, ui.Theme.Accent);
            var selectable = unit.IsStanding;
            if (
                ui.HitArea(
                    rect,
                    "strip" + unit.Id.Value,
                    $"{unit.DisplayName}\n{StripTip(unit, state)}"
                ) && selectable
            )
                r.SelectSoldier = unit.Id;

            canvas.Text(
                Initials(unit.DisplayName),
                rect.Position + new Vector2(5f, 3f),
                TextStyles.Body
            );
            switch (state)
            {
                case StripState.Dead:
                    canvas.Text(
                        "X",
                        rect.Position + new Vector2(50f, 4f),
                        TextStyles.Heading.With(Red)
                    );
                    break;
                case StripState.Evacuated:
                    canvas.Text("EVAC", rect.Position + new Vector2(32f, 5f), TextStyles.Small);
                    break;
                case StripState.BleedingOut:
                    canvas.Text(
                        unit.Statuses.BleedingOutTurnsLeft.ToString(),
                        rect.Position + new Vector2(54f, 4f),
                        TextStyles.Body.With(Red)
                    );
                    break;
                default:
                    DrawApPips(
                        ui,
                        new Vector2(rect.Right - 22f, rect.Y + 7f),
                        unit.ActionPoints,
                        4f
                    );
                    break;
            }
            if (state is StripState.Ready or StripState.Spent or StripState.Stabilized)
            {
                var bar = new UiRect(rect.X + 4f, rect.Bottom - 9f, rect.W - 8f, 5f);
                ui.Fill(bar, DimFill);
                var frac = unit.MaxHp <= 0 ? 0f : Math.Clamp(unit.Hp / (float)unit.MaxHp, 0f, 1f);
                ui.Fill(
                    bar with
                    {
                        W = bar.W * frac,
                    },
                    frac > 0.34f ? ui.Theme.Success : ui.Theme.Danger
                );
            }
        }
    }

    private static string StripTip(TacticalUnit unit, StripState state) =>
        state switch
        {
            StripState.Dead => "[c=#ff4d4d]Killed in action[/c]",
            StripState.Evacuated => "Evacuated",
            StripState.BleedingOut =>
                $"[c=#ff4d4d]Bleeding out[/c]: {unit.Statuses.BleedingOutTurnsLeft} turns left",
            StripState.Stabilized => "Stabilized",
            _ => $"HP {unit.Hp}/{unit.MaxHp}   AP {unit.ActionPoints}\nClick to select",
        };

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0
            ? "?"
            : string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
    }

    // ------------------------------------------------------------------ soldier panel

    private static void DrawSoldierPanel(ScreenCanvas canvas, UiContext ui, HudFrame f, HudResult r)
    {
        var rect = SoldierPanelRect;
        var content = ui.Panel(rect);
        if (f.Selected is not { } unit)
        {
            ui.Label(content, "No soldier selected", ui.Theme.Small);
            return;
        }
        if (ui.HitArea(rect, "soldier-panel", "Click to centre the camera  [F]"))
            r.CenterSelected = true;

        var x = content.X;
        var y = content.Y - 2f;
        canvas.Text(unit.DisplayName, new Vector2(x, y), ui.Theme.Title);
        var rank = unit.Rank.ToString().ToUpperInvariant();
        canvas.Text(
            rank,
            new Vector2(content.Right - canvas.Measure(rank, TextStyles.Small), y + 2f),
            TextStyles.Small
        );
        ui.Tooltip(new UiRect(x, y, content.W, 20f), $"{unit.DisplayName}\nRank: {unit.Rank}");

        const float labelW = 44f;
        const float pipsMaxW = 190f;
        y += 26f;
        canvas.Text("HP", new Vector2(x, y), TextStyles.Small);
        var hpRect = new UiRect(x + labelW, y + 1f, PipsWidth(unit.MaxHp, pipsMaxW), 12f);
        ui.ProgressBar(hpRect, unit.Hp, unit.MaxHp, BarColor.Green, Math.Max(1, unit.MaxHp));
        canvas.Text($"{unit.Hp}/{unit.MaxHp}", new Vector2(hpRect.Right + 8f, y), TextStyles.Small);
        ui.Tooltip(
            new UiRect(x, y, labelW + pipsMaxW, 14f),
            $"Health: [c=#6ee07a]{unit.Hp}[/c]/{unit.MaxHp}"
                + (unit.Armor > 0 ? $"\nArmor: {unit.Armor}" : "")
        );

        y += 22f;
        canvas.Text("AMMO", new Vector2(x, y), TextStyles.Small);
        if (unit.Weapon is { } weapon)
        {
            var clip = Math.Max(1, weapon.Def.Clip);
            var ammoRect = new UiRect(x + labelW, y + 1f, PipsWidth(clip, pipsMaxW), 12f);
            ui.ProgressBar(ammoRect, weapon.Ammo, clip, BarColor.Yellow, clip);
            canvas.Text(
                $"{weapon.Ammo}/{weapon.Def.Clip}",
                new Vector2(ammoRect.Right + 8f, y),
                TextStyles.Small
            );
            ui.Tooltip(
                new UiRect(x, y, labelW + pipsMaxW, 14f),
                $"Ammo: [c=#ffb000]{weapon.Ammo}[/c]/{weapon.Def.Clip}\n{weapon.Def.Name}"
            );
        }
        else
            canvas.Text("-", new Vector2(x + labelW, y), TextStyles.Small);

        y += 22f;
        canvas.Text("AP", new Vector2(x, y), TextStyles.Small);
        DrawApPips(ui, new Vector2(x + labelW + 6f, y + 2f), unit.ActionPoints, 7f);
        ui.Tooltip(
            new UiRect(x, y, labelW + 40f, 14f),
            $"Action points: [c=#3fd0ff]{unit.ActionPoints}[/c]/{TacticalEngine.ApPerTurn}"
        );

        DrawStatusChips(canvas, ui, unit, new Vector2(content.Right, y - 1f));
    }

    private static float PipsWidth(int count, float max) => Math.Min(max, count * 16f);

    // Right-aligned status tags (the status icons of the unit overlay have no UI atlas region).
    private static void DrawStatusChips(
        ScreenCanvas canvas,
        UiContext ui,
        TacticalUnit unit,
        Vector2 right
    )
    {
        var chips = new List<(string Text, Color Color, string Tip)>();
        if (unit.Statuses.Overwatch)
            chips.Add(("OW", Amber, "Overwatch\nShoots the first enemy that moves in sight."));
        if (unit.Statuses.Hunkered)
            chips.Add(("HD", Green, "Hunkered down\nBetter defense until the next turn."));
        if (unit.Statuses.Suppressed)
            chips.Add(("SUP", Red, "Suppressed\nAim reduced."));
        if (unit.Statuses.Stunned)
            chips.Add(("STUN", Red, "Stunned\nCannot act."));
        if (unit.Statuses.Stabilized)
            chips.Add(("STAB", Green, "Stabilized\nOut of the fight, no longer bleeding."));
        if (unit.Statuses.IsBleedingOut)
            chips.Add(
                ("BLEED", Red, $"Bleeding out\n{unit.Statuses.BleedingOutTurnsLeft} turns left.")
            );
        var x = right.X;
        foreach (var (text, color, tip) in chips)
        {
            var w = canvas.Measure(text, TextStyles.Small) + 10f;
            x -= w;
            var chip = new UiRect(x, right.Y, w, 18f);
            ui.Fill(chip, new Vector4(0f, 0f, 0f, 0.6f));
            Outline(ui, chip, new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, 1f));
            canvas.Text(text, chip.Position + new Vector2(5f, 2f), TextStyles.Small.With(color));
            ui.Tooltip(chip, tip);
            x -= 4f;
        }
    }

    // ------------------------------------------------------------------ ability bar

    private static void DrawAbilityBar(ScreenCanvas canvas, UiContext ui, HudFrame f, HudResult r)
    {
        var bar = AbilityBarRect(f.Abilities.Count);
        HudAbility? hovered = null;
        for (var i = 0; i < f.Abilities.Count; i++)
        {
            var a = f.Abilities[i];
            var rect = new UiRect(bar.X + i * (SlotSize + SlotGap), bar.Y, SlotSize, SlotSize);
            var reason = f.Busy ? "Wait for the current action to finish." : a.BlockedReason;
            if (
                ui.IconButton(
                    rect,
                    a.Slot.Icon,
                    a.Slot.HotkeyLabel,
                    HudModel.AbilityTooltip(a.Slot, reason),
                    cooldown: a.Slot.Cooldown,
                    enabled: reason is null,
                    selected: f.ArmedAbility == a.Slot.AbilityId
                )
            )
                r.Ability = a.Slot.AbilityId;
            DrawCostPips(ui, rect, a.Slot.ApCost);
            if (ui.IsHovering(rect))
                hovered = a;
        }
        // The tooltip repeats this text, so the line steps aside once the tooltip shows.
        if (hovered is not null && !ui.TooltipVisible)
        {
            var text = $"[c=#3fd0ff]{hovered.Slot.Name}[/c]  {hovered.Slot.Description}";
            var style = ui.Theme.Body;
            var w = Math.Min(560f, canvas.Measure(text, style) + 24f);
            var rect = new UiRect((UiSpace.LogicalWidth - w) / 2f, bar.Y - 32f, w, 26f);
            ui.Fill(rect, new Vector4(0.04f, 0.06f, 0.1f, 0.88f));
            canvas.RichText(text, rect.Position + new Vector2(12f, 4f), style);
        }
    }

    // One small diamond per AP the ability costs, in the bottom-right of its button.
    private static void DrawCostPips(UiContext ui, UiRect button, int cost)
    {
        for (var i = 0; i < cost; i++)
            DrawDiamond(
                ui,
                new Vector2(button.Right - 9f - i * 9f, button.Bottom - 9f),
                3.5f,
                ui.Theme.Accent
            );
    }

    // ------------------------------------------------------------------ end turn

    private static void DrawTurnButtons(ScreenCanvas canvas, UiContext ui, HudFrame f, HudResult r)
    {
        var endBlocked = f.Busy ? "Wait for the current action to finish." : f.EndTurnBlocked;
        if (
            ui.Button(
                EndTurnRect,
                "End Turn",
                ButtonStyle.Primary,
                enabled: endBlocked is null,
                tooltip: endBlocked ?? "End the player turn  [Backspace]"
            )
        )
            r.EndTurn = true;
        var abortBlocked = f.Busy ? "Wait for the current action to finish." : f.AbortBlocked;
        if (
            ui.Button(
                AbortRect,
                "Abort Mission",
                ButtonStyle.Normal,
                enabled: abortBlocked is null,
                tooltip: abortBlocked ?? "Leave the mission"
            )
        )
            r.Abort = true;
        if (f.Busy)
        {
            var dots = new string('.', 1 + (int)(f.Time * 3f) % 3);
            canvas.Text(
                dots,
                new Vector2(EndTurnRect.X + 6f, EndTurnRect.Y - 22f),
                TextStyles.Heading.With(new Color(190, 200, 215, 200))
            );
        }
    }

    // ------------------------------------------------------------------ shapes

    private static void DrawApPips(UiContext ui, Vector2 at, int ap, float radius)
    {
        var pips = HudModel.ApPips(ap);
        for (var i = 0; i < pips.Count; i++)
        {
            var center = at + new Vector2(radius + i * (radius * 2f + 4f), radius);
            DrawDiamond(ui, center, radius, ui.Theme.Accent, pips[i] == ApPip.Full);
        }
    }

    // A diamond built from horizontal strips (no rotated quads in the UI canvas).
    private static void DrawDiamond(
        UiContext ui,
        Vector2 center,
        float radius,
        Vector4 color,
        bool filled = true
    )
    {
        var rows = Math.Max(3, (int)MathF.Round(radius * 2f));
        var step = radius * 2f / rows;
        for (var i = 0; i < rows; i++)
        {
            var dy = (i + 0.5f) * step - radius;
            var half = radius - MathF.Abs(dy);
            var strip = new UiRect(center.X - half, center.Y + dy - step / 2f, half * 2f, step);
            if (filled)
                ui.Fill(strip, color);
            else
            {
                var edge = MathF.Min(half, 1.5f);
                ui.Fill(new UiRect(strip.X, strip.Y, edge, step), color with { W = 0.7f });
                ui.Fill(
                    new UiRect(strip.Right - edge, strip.Y, edge, step),
                    color with
                    {
                        W = 0.7f,
                    }
                );
            }
        }
    }

    private static void Outline(UiContext ui, UiRect r, Vector4 color)
    {
        ui.Fill(new UiRect(r.X, r.Y, r.W, 2f), color);
        ui.Fill(new UiRect(r.X, r.Bottom - 2f, r.W, 2f), color);
        ui.Fill(new UiRect(r.X, r.Y, 2f, r.H), color);
        ui.Fill(new UiRect(r.Right - 2f, r.Y, 2f, r.H), color);
    }
}
