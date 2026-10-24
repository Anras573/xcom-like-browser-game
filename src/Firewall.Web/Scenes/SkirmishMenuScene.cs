using Firewall.Rules.Data;
using Firewall.Rules.Tactical;
using Firewall.Web.Input;
using Firewall.Web.Rendering;
using Firewall.Web.Ui;
using Yaeger.Platform;

namespace Firewall.Web.Scenes;

/// <summary>
/// "Skirmish (debug)": a form for the mission type, biome, Force Level, difficulty, seed and squad
/// (#33). Launch builds the mission through <see cref="MissionSetup"/>, the way the campaign will.
/// Esc goes back.
/// </summary>
public sealed class SkirmishMenuScene : SceneBase
{
    private const float RowHeight = 40f;
    private const float ArrowWidth = 36f;

    private readonly SkirmishMenuModel _model = new();
    private SquadBuilderModel? _squad;
    private bool _customSquad;

    public override string Name => "Skirmish";

    protected override void OnUpdate(float dt)
    {
        if (Ctx.Bindings.WasPressed(GameAction.Cancel))
            Ctx.Scenes.Pop();
    }

    protected override void DrawScreen(ScreenCanvas canvas, UiContext ui)
    {
        const float w = 640f;
        const float h = 600f;
        var body = ui.Panel(
            new UiRect((UiSpace.LogicalWidth - w) / 2f, (UiSpace.LogicalHeight - h) / 2f, w, h),
            "Skirmish (debug)"
        );
        var y = body.Y;

        y = Row(ui, body, y, "Mission", _model.Type.ToString(), s => _model.CycleType(s));
        var def = Ctx.Data.Missions[_model.Type.ToString().ToLowerInvariant()];
        ui.Label(new UiRect(body.X, y, body.W, 36f), def.Goal, ui.Theme.Small, wrap: true);
        y += 40f;
        y = Row(ui, body, y, "Biome", _model.Biome.ToString(), s => _model.CycleBiome(s));
        y = Row(
            ui,
            body,
            y,
            "Force Level",
            _model.ForceLevel.ToString(),
            s => _model.CycleForceLevel(s)
        );
        y = Row(
            ui,
            body,
            y,
            "Difficulty",
            _model.Difficulty.ToString(),
            s => _model.CycleDifficulty(s)
        );

        // Seed: random by default; "New" fixes one so a map can be replayed.
        ui.Label(new UiRect(body.X, y, 130f, RowHeight), "Seed", ui.Theme.Body);
        ui.Label(
            new UiRect(body.X + 140f, y, 220f, RowHeight),
            _model.Seed?.ToString() ?? "Random",
            ui.Theme.Body
        );
        if (ui.Button(new UiRect(body.X + 370f, y, 100f, RowHeight - 4f), "New"))
            _model.Seed = Ctx.Seeds.Next() % 100_000;
        if (
            ui.Button(
                new UiRect(body.X + 478f, y, 100f, RowHeight - 4f),
                "Random",
                enabled: _model.Seed is not null
            )
        )
            _model.Seed = null;
        y += RowHeight;

        // Squad: four default rookies, or the squad builder's soldiers.
        ui.Label(new UiRect(body.X, y, 130f, RowHeight), "Squad", ui.Theme.Body);
        ui.Label(
            new UiRect(body.X + 140f, y, 220f, RowHeight),
            _customSquad ? "Custom" : "Default rookies",
            ui.Theme.Body
        );
        if (ui.Button(new UiRect(body.X + 370f, y, 100f, RowHeight - 4f), "Edit"))
        {
            _squad ??= SquadBuilderModel.Default(Ctx.Data);
            _customSquad = true;
            Ctx.Scenes.Push(new SquadBuilderScene(_squad));
        }
        if (
            ui.Button(
                new UiRect(body.X + 478f, y, 100f, RowHeight - 4f),
                "Default",
                enabled: _customSquad
            )
        )
            _customSquad = false;
        y += RowHeight + 10f;

        if (_model.Type == MissionType.Capture && !CarriesStunner())
            ui.Label(
                new UiRect(body.X, y, body.W, 36f),
                "No soldier carries an Arc Stunner: the Proxy bonus can't be earned. The campaign wouldn't offer this mission.",
                ui.Theme.Small,
                wrap: true
            );

        if (
            ui.Button(
                new UiRect(body.Right - 220f, body.Bottom - 44f, 220f, 44f),
                "Launch",
                ButtonStyle.Primary
            )
        )
            Ctx.Scenes.Replace(
                new SkirmishScene(
                    _model.ToSpec(Ctx.Seeds.Next() % 100_000),
                    _customSquad ? _squad?.Specs.ToList() : null
                )
            );
        if (ui.Button(new UiRect(body.X, body.Bottom - 44f, 120f, 44f), "Back"))
            Ctx.Scenes.Pop();
    }

    // The default rookies carry an Arc Stunner; a custom squad may not.
    private bool CarriesStunner() =>
        !_customSquad
        || (_squad?.Specs.Any(s => s.ItemIds.Contains(AbilityRegistry.ArcStunner)) ?? false);

    private float Row(
        UiContext ui,
        UiRect body,
        float y,
        string label,
        string value,
        Action<int> change
    )
    {
        ui.Label(new UiRect(body.X, y, 130f, RowHeight), label, ui.Theme.Body);
        if (ui.Button(new UiRect(body.X + 140f, y, ArrowWidth, RowHeight - 4f), "<"))
            change(-1);
        ui.Label(
            new UiRect(body.X + 140f + ArrowWidth, y, 220f - ArrowWidth, RowHeight),
            value,
            ui.Theme.Body,
            TextAlignment.Center
        );
        if (ui.Button(new UiRect(body.X + 360f, y, ArrowWidth, RowHeight - 4f), ">"))
            change(1);
        return y + RowHeight;
    }
}
