using System.Numerics;
using Firewall.Web.Input;
using Firewall.Web.Rendering;
using Firewall.Web.Ui;
using Yaeger.Platform;

namespace Firewall.Web.Scenes;

/// <summary>Test overlay: dims the scene below and takes all of its input. Esc resumes.</summary>
public sealed class PauseOverlayScene : SceneBase
{
    public override string Name => "PauseOverlay";
    public override bool IsOverlay => true;

    protected override void OnUpdate(float dt)
    {
        if (Ctx.Bindings.WasPressed(GameAction.Cancel))
            Ctx.Scenes.Pop();
    }

    protected override void DrawScreen(ScreenCanvas canvas, UiContext ui)
    {
        canvas.FillRect(ui.Space.CanvasRect.Position, ui.Space.CanvasRect.Size, ui.Theme.Dim);
        var body = ui.Panel(
            new UiRect(
                (UiSpace.LogicalWidth - 360) / 2f,
                (UiSpace.LogicalHeight - 252) / 2f,
                360,
                252
            ),
            "Paused"
        );
        if (ui.Button(new UiRect(body.X, body.Y, body.W, 40), "Resume", ButtonStyle.Primary))
            Ctx.Scenes.Pop();
        if (
            ui.Button(
                new UiRect(body.X, body.Y + 52, body.W, 40),
                $"Playback speed: {Ctx.Playback.Label}"
            )
        )
            Ctx.Playback.Cycle();
        if (ui.Button(new UiRect(body.X, body.Y + 104, body.W, 40), "Leave scene"))
        {
            Ctx.Scenes.Pop();
            Ctx.Scenes.Pop();
        }
        ui.Label(
            new UiRect(body.X, body.Bottom - 20, body.W, 20),
            "Esc: resume. Hold Space in combat for 4x.",
            ui.Theme.Small,
            TextAlignment.Center
        );
    }
}
