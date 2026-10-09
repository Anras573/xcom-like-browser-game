using Firewall.Web.Rendering;
using Firewall.Web.Ui;
using Yaeger.Platform;

namespace Firewall.Web.Scenes;

/// <summary>Lists every scene in <see cref="DebugScenes"/>; Esc goes back.</summary>
public sealed class DebugMenuScene : SceneBase
{
    public override string Name => "DebugMenu";

    protected override void OnUpdate(float dt)
    {
        if (Ctx.Bindings.WasPressed(Input.GameAction.Cancel))
            Ctx.Scenes.Pop();
    }

    protected override void DrawScreen(ScreenCanvas canvas, UiContext ui)
    {
        var entries = DebugScenes.All;
        const float w = 420f;
        const float row = 48f;
        var h = 70f + entries.Count * row + 40f;
        var body = ui.Panel(
            new UiRect((UiSpace.LogicalWidth - w) / 2f, (UiSpace.LogicalHeight - h) / 2f, w, h),
            "Debug menu"
        );
        var y = body.Y;
        foreach (var (name, create) in entries)
        {
            if (ui.Button(new UiRect(body.X, y, body.W, 40), name))
                Ctx.Scenes.Push(create());
            y += row;
        }
        ui.Label(
            new UiRect(body.X, body.Bottom - 20, body.W, 20),
            "Esc: back",
            ui.Theme.Small,
            TextAlignment.Center
        );
    }
}
