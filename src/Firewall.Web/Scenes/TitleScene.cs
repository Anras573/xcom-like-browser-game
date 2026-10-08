using System.Numerics;
using Firewall.Web.Assets;
using Firewall.Web.Rendering;
using Firewall.Web.Ui;
using Yaeger.Graphics;
using Yaeger.Platform;

namespace Firewall.Web.Scenes;

/// <summary>Placeholder title: logo text, three buttons, a slowly panning dimmed map behind.</summary>
public sealed class TitleScene : SceneBase
{
    private readonly TacticalCamera _camera = new()
    {
        Center = new Vector2(10f, 10f),
        TilesVisibleVertically = 8f,
    };
    private float _time;

    public override string Name => "Title";

    protected override Camera2D Camera => _camera.ToCamera2D();

    protected override void OnEnter() => DebugTacticalScene.Build(World, Ctx.Assets);

    protected override void OnUpdate(float dt)
    {
        _time += dt;
        _camera.Center = new Vector2(
            10f + 2.5f * MathF.Sin(_time * 0.15f),
            10f + 1.5f * MathF.Cos(_time * 0.11f)
        );
    }

    protected override void DrawScreen(ScreenCanvas canvas, UiContext ui)
    {
        canvas.FillRect(Vector2.Zero, UiSpace.LogicalSize, new Vector4(0f, 0f, 0f, 0.6f));

        var logo = new TextStyle(TextStyles.Future, 72, new Color(255, 220, 90), Shadow: true);
        var lw = canvas.Measure("FIREWALL", logo);
        canvas.Text("FIREWALL", new Vector2((UiSpace.LogicalWidth - lw) / 2f, 130), logo);

        const float w = 320f;
        var x = (UiSpace.LogicalWidth - w) / 2f;
        var y = 300f;
        ui.Button(
            new UiRect(x, y, w, 48),
            "New Campaign",
            ButtonStyle.Primary,
            enabled: false,
            tooltip: "Coming soon"
        );
        if (ui.Button(new UiRect(x, y + 64, w, 48), "Skirmish (debug)"))
            Ctx.Scenes.Push(new PickingTestScene());
        if (ui.Button(new UiRect(x, y + 128, w, 48), "Debug Menu"))
            Ctx.Scenes.Push(new DebugMenuScene());
    }
}
