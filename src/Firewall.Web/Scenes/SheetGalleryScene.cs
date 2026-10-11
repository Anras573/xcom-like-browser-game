using System.Numerics;
using Firewall.Web.Assets;
using Firewall.Web.Input;
using Firewall.Web.Rendering;
using Firewall.Web.Ui;
using Yaeger.Graphics;

namespace Firewall.Web.Scenes;

/// <summary>
/// Every frame of every sheet, plus the UI atlas, in labelled grids (<see cref="DebugSheetScene"/>),
/// for checking the asset pipeline output and texture bleeding. Pan and zoom like the map; Esc goes back.
/// </summary>
public sealed class SheetGalleryScene : SceneBase
{
    private static readonly Vector2 Offset = new(0f, DebugSheetScene.Extent.Y);

    private readonly TacticalCamera _camera = new();
    private TacticalCameraController _controller = null!;

    public override string Name => "Sheet gallery";

    protected override Camera2D Camera => _camera.ToCamera2D();

    protected override void OnEnter()
    {
        _controller = new TacticalCameraController(_camera, Ctx.Input, Ctx.Bindings)
        {
            MapSize = DebugSheetScene.Extent,
        };
        _controller.SetZoom(TacticalCameraController.MinZoom);
        // Start on the tile sheet, the largest grid.
        _camera.Center = new Vector2(16f, Offset.Y - 11f);
        DebugSheetScene.Build(World, Ctx.Assets, Offset);
    }

    protected override void OnUpdate(float dt)
    {
        if (Ctx.Bindings.WasPressed(GameAction.Cancel))
        {
            Ctx.Scenes.Pop();
            return;
        }
        _controller.Update(dt, Ctx.Render.CanvasSize, Ctx.Ui.PointerOverUi);
    }

    // Bottom right: the start view has empty space there, and the sheet titles stay readable.
    protected override void DrawScreen(ScreenCanvas canvas, UiContext ui)
    {
        var origin = new Vector2(UiSpace.LogicalWidth - 252f, UiSpace.LogicalHeight - 120f);
        canvas.FillRect(origin, new Vector2(236, 104), new Vector4(0f, 0f, 0f, 0.6f));
        canvas.Text(
            Name,
            origin + new Vector2(12, 8),
            new TextStyle(TextStyles.FutureNarrow, 20, new Color(255, 220, 90))
        );
        var white = TextStyles.Small.With(Color.White);
        canvas.Text($"zoom  {_controller.ZoomFactor:0.00}x", origin + new Vector2(12, 38), white);
        canvas.Text(
            $"batches {Ctx.Render.Stats.LastFrameDrawBatches}  quads {Ctx.Render.Stats.LastFrameQuads}",
            origin + new Vector2(12, 58),
            white
        );
        canvas.Text("Esc: back", origin + new Vector2(12, 80), TextStyles.Small);
    }
}
