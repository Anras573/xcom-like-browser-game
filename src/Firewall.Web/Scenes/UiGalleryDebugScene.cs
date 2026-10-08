using Firewall.Web.Input;
using Firewall.Web.Rendering;
using Firewall.Web.Ui;

namespace Firewall.Web.Scenes;

/// <summary>Hosts the widget gallery as a scene; Esc goes back unless its modal is open.</summary>
public sealed class UiGalleryDebugScene : SceneBase
{
    private readonly UiGalleryScene _gallery = new();

    public override string Name => "UI gallery";

    protected override void OnUpdate(float dt)
    {
        if (!_gallery.ModalOpen && Ctx.Bindings.WasPressed(GameAction.Cancel))
            Ctx.Scenes.Pop();
    }

    protected override void DrawScreen(ScreenCanvas canvas, UiContext ui) => _gallery.Draw(ui);
}
