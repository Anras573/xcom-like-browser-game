using System.Numerics;
using Firewall.Rules.Data;
using Firewall.Rules.Tactical;
using Firewall.Web.Input;
using Firewall.Web.Rendering;
using Firewall.Web.Ui;
using Yaeger.Platform;

namespace Firewall.Web.Scenes;

/// <summary>
/// "Skirmish: squad builder" (#30): pick each soldier's class, rank and ability choices, plus their
/// weapon tier, armor and items, then start a Skirmish with them. The main way to try the classes.
/// Esc goes back to the debug menu.
/// </summary>
public sealed class SquadBuilderScene : SceneBase
{
    private const float ColumnWidth = 300f;
    private const float Gap = 10f;
    private const float RowHeight = 28f;
    private const float ArrowWidth = 28f;

    private SquadBuilderModel _model = null!;
    private TacticalState _scratch = null!;
    private ScreenCanvas _canvas;
    private int _forceLevel = 2;

    public override string Name => "SquadBuilder";

    protected override void OnEnter()
    {
        _model = SquadBuilderModel.Default(Ctx.Data);
        // The builder only reads stats; any one-tile map will do for the throwaway state.
        var map = MapParser.Parse(
            new MapFile
            {
                Id = "stub",
                Name = "Stub",
                Biome = Biome.None,
                Rows = ["."],
            },
            Ctx.Data.Tiles
        );
        _scratch = new TacticalState(map, new Rules.Rng.Pcg32Random(1));
    }

    protected override void OnUpdate(float dt)
    {
        if (Ctx.Bindings.WasPressed(GameAction.Cancel))
            Ctx.Scenes.Pop();
    }

    protected override void DrawScreen(ScreenCanvas canvas, UiContext ui)
    {
        _canvas = canvas;
        var total = SquadBuilderModel.Size * ColumnWidth + (SquadBuilderModel.Size - 1) * Gap;
        var left = (UiSpace.LogicalWidth - total) / 2f;
        ui.Label(
            new UiRect(left, 18f, total, 30f),
            "Squad builder",
            ui.Theme.Title,
            TextAlignment.Center
        );
        for (var i = 0; i < SquadBuilderModel.Size; i++)
            DrawSoldier(
                canvas,
                ui,
                i,
                new UiRect(left + i * (ColumnWidth + Gap), 60f, ColumnWidth, 560f)
            );

        var y = UiSpace.LogicalHeight - 70f;
        ui.Label(new UiRect(left, y, 110f, 40f), "Force Level", ui.Theme.Body);
        if (ui.Button(new UiRect(left + 114f, y + 6f, ArrowWidth, 28f), "<"))
            _forceLevel = Cycle(
                _forceLevel - 1,
                EncounterBuilder.MinForceLevel,
                EncounterBuilder.MaxForceLevel
            );
        ui.Label(
            new UiRect(left + 114f + ArrowWidth, y, 40f, 40f),
            _forceLevel.ToString(),
            ui.Theme.Body,
            TextAlignment.Center
        );
        if (ui.Button(new UiRect(left + 114f + ArrowWidth + 40f, y + 6f, ArrowWidth, 28f), ">"))
            _forceLevel = Cycle(
                _forceLevel + 1,
                EncounterBuilder.MinForceLevel,
                EncounterBuilder.MaxForceLevel
            );
        if (
            ui.Button(
                new UiRect(left + total - 220f, y, 220f, 40f),
                "Start Skirmish",
                ButtonStyle.Primary
            )
        )
            Ctx.Scenes.Replace(new SkirmishScene(_forceLevel, _model.Specs.ToList()));
        ui.Label(
            new UiRect(left + 220f, y, total - 460f, 40f),
            "Esc: back",
            ui.Theme.Small,
            TextAlignment.Center
        );
    }

    private void DrawSoldier(ScreenCanvas canvas, UiContext ui, int i, UiRect column)
    {
        var spec = _model.Specs[i];
        var def = _model.ClassOf(i);
        var body = ui.Panel(column, $"Soldier {i + 1}");
        var y = body.Y;

        // Class
        y = Row(ui, body, y, "Class", def?.Name ?? "Rookie", step => _model.CycleClass(i, step));
        if (def is not null)
        {
            ui.Icon(def.Icon ?? "icon.star", new UiRect(body.X, y, 22f, 22f), ui.Theme.Accent);
            ui.Label(
                new UiRect(body.X + 28f, y, body.W - 28f, 40f),
                def.Description ?? def.Role,
                ui.Theme.Small,
                wrap: true
            );
            y += 46f;
        }
        y = Row(
            ui,
            body,
            y,
            "Rank",
            spec.Rank.ToString(),
            step => _model.CycleRank(i, step),
            def is not null
        );

        // Gear
        var weapon = Ctx.Data.Weapon(
            SoldierBuilder.WeaponClassOf(Ctx.Data, spec.ClassId),
            spec.WeaponTier
        );
        y = Row(ui, body, y, "Weapon", weapon.Name, step => _model.CycleWeaponTier(i, step));
        y = Row(
            ui,
            body,
            y,
            "Armor",
            Ctx.Data.Armor[spec.ArmorId].Name,
            step => _model.CycleArmor(i, step)
        );
        for (var slot = 0; slot < _model.ItemSlots(i); slot++)
        {
            var s = slot;
            var item = _model.ItemAt(i, s);
            y = Row(
                ui,
                body,
                y,
                $"Item {s + 1}",
                item is null ? "-" : Ctx.Data.Items[item].Name,
                step => _model.CycleItem(i, s, step)
            );
        }

        // Abilities: the automatic ones, then one row of choices per rank.
        y += 6f;
        ui.Label(new UiRect(body.X, y, body.W, 20f), "Abilities", ui.Theme.Title);
        y += 24f;
        if (def is null)
        {
            ui.Label(new UiRect(body.X, y, body.W, 20f), "A Rookie has none.", ui.Theme.Small);
            y += 24f;
        }
        else
        {
            foreach (var tier in def.AbilityTree.Where(t => t.Pick == 0 && t.Rank <= spec.Rank))
                y = AbilityRow(ui, body, y, tier.Rank, tier.Abilities, tier.Abilities, null);
            var ranks = SoldierBuilder.PickRanks(def, spec.Rank).ToList();
            for (var p = 0; p < ranks.Count; p++)
            {
                var pick = p;
                y = AbilityRow(
                    ui,
                    body,
                    y,
                    ranks[p].Rank,
                    ranks[p].Abilities,
                    [spec.Picks[p]],
                    id => _model.SetPick(i, pick, id)
                );
            }
        }

        // The result.
        var unit = SoldierBuilder.Build(_scratch, Ctx.Data, spec, "", "");
        ui.Label(
            new UiRect(body.X, body.Bottom - 22f, body.W, 22f),
            $"HP {unit.MaxHp}   Aim {unit.Aim}   Mobility {unit.Mobility}   Armor {unit.Armor}",
            ui.Theme.Small
        );
    }

    // "Label   [<] value [>]"; the arrows are greyed out when there is nothing to change.
    private float Row(
        UiContext ui,
        UiRect body,
        float y,
        string label,
        string value,
        Action<int> change,
        bool enabled = true
    )
    {
        ui.Label(new UiRect(body.X, y, 64f, RowHeight), label, ui.Theme.Small);
        var valueX = body.X + 64f + ArrowWidth + 4f;
        var valueW = body.W - 64f - 2f * ArrowWidth - 8f;
        if (
            ui.Button(
                new UiRect(body.X + 64f, y, ArrowWidth, RowHeight - 2f),
                "<",
                enabled: enabled
            )
        )
            change(-1);
        ui.Label(
            new UiRect(valueX, y, valueW, RowHeight),
            Fit(value, ui.Theme.Small, valueW),
            ui.Theme.Small,
            TextAlignment.Center
        );
        if (
            ui.Button(
                new UiRect(body.Right - ArrowWidth, y, ArrowWidth, RowHeight - 2f),
                ">",
                enabled: enabled
            )
        )
            change(1);
        return y + RowHeight + 2f;
    }

    // A rank's abilities as icon buttons; the chosen ones are lit. Fixed abilities cannot be clicked.
    private float AbilityRow(
        UiContext ui,
        UiRect body,
        float y,
        Rank rank,
        IReadOnlyList<string> offered,
        IReadOnlyList<string> chosen,
        Func<string, bool>? choose
    )
    {
        ui.Label(new UiRect(body.X, y, 76f, 36f), rank.ToString(), ui.Theme.Small);
        var x = body.X + 80f;
        foreach (var id in offered)
        {
            var def = Ctx.Data.Abilities[id];
            var rect = new UiRect(x, y, 36f, 36f);
            if (
                ui.IconButton(
                    rect,
                    def.Icon ?? "icon.star",
                    tooltip: HudModel.AbilityInfo(def),
                    selected: chosen.Contains(id),
                    enabled: choose is not null || chosen.Contains(id)
                ) && choose is not null
            )
                choose(id);
            x += 40f;
        }
        var shown =
            chosen.Count == 1
                ? Ctx.Data.Abilities[chosen[0]].Name
                : Ctx.Data.Abilities[offered[0]].Name;
        ui.Label(new UiRect(x + 6f, y, body.Right - x - 6f, 36f), shown, ui.Theme.Small);
        return y + 42f;
    }

    // Cuts the text with an ellipsis so it stays between the arrows.
    private string Fit(string text, TextStyle style, float width)
    {
        if (_canvas.Measure(text, style) <= width)
            return text;
        while (text.Length > 1 && _canvas.Measure(text + "...", style) > width)
            text = text[..^1];
        return text + "...";
    }

    private static int Cycle(int value, int min, int max) =>
        value < min ? max
        : value > max ? min
        : value;
}
