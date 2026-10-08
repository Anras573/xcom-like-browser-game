using System.Numerics;
using Firewall.Web.Assets;
using Firewall.Web.Input;
using Firewall.Web.Rendering;
using Firewall.Web.Ui;
using Yaeger.Graphics;
using Yaeger.Input;

namespace Firewall.Web.Scenes;

/// <summary>
/// The debug tactical map (tiles, props, units) with the camera controller, shared by the sprite
/// gallery, text test and picking test. Esc goes back; P opens a test pause overlay.
/// </summary>
public abstract class DebugMapScene : SceneBase
{
    private readonly TacticalCamera _camera = new() { Center = new Vector2(10f, 10f) };
    private TacticalCameraController _controller = null!;

    protected (int X, int Y)? HoverTile { get; private set; }
    protected TacticalCamera TacticalCam => _camera;

    protected override Camera2D Camera => _camera.ToCamera2D();

    protected override void OnEnter()
    {
        _controller = new TacticalCameraController(_camera, Ctx.Input, Ctx.Bindings)
        {
            MapSize = new Vector2(DebugTacticalScene.MapSize),
        };
        DebugTacticalScene.Build(World, Ctx.Assets);
    }

    protected override void OnUpdate(float dt)
    {
        if (Ctx.Bindings.WasPressed(GameAction.Cancel))
        {
            Ctx.Scenes.Pop();
            return;
        }
        if (Ctx.Input.WasKeyPressed(Keys.P))
            Ctx.Scenes.Push(new PauseOverlayScene());

        var canvas = Ctx.Render.CanvasSize;
        if (Ctx.Bindings.WasPressed(GameAction.CenterCamera))
        {
            var (sx, sy) = DebugTacticalScene.Soldier;
            _controller.FocusOn(TacticalCamera.TileCenter(sx, sy), 0.4f);
        }
        _controller.Update(dt, canvas, Ctx.Ui.PointerOverUi);
        HoverTile = Ctx.Ui.PointerOverUi ? null : PickTile(canvas);
    }

    // Uses the same view-projection the renderer does, so the highlight matches the pixels.
    private (int X, int Y)? PickTile(Vector2 canvas)
    {
        var mouse = Ctx.Input.MousePosition;
        if (
            !Ctx.Input.IsMouseInside
            || mouse.X < 0f
            || mouse.Y < 0f
            || mouse.X >= canvas.X
            || mouse.Y >= canvas.Y
        )
            return null;
        var vp = _camera.ToCamera2D().ViewProjection(canvas.X / MathF.Max(canvas.Y, 1f));
        var tile = Picking.WorldToTile(
            Picking.ScreenToWorld(Picking.CanvasToNdc(mouse, canvas), vp)
        );
        return
            tile.X is >= 0 and < DebugTacticalScene.MapSize
            && tile.Y is >= 0 and < DebugTacticalScene.MapSize
            ? tile
            : null;
    }

    protected override void DrawScreen(ScreenCanvas canvas, UiContext ui)
    {
        canvas.FillRect(new Vector2(16, 16), new Vector2(300, 134), new Vector4(0f, 0f, 0f, 0.6f));
        canvas.Text(
            $"{Name}",
            new Vector2(28, 24),
            new TextStyle(TextStyles.FutureNarrow, 20, new Color(255, 220, 90))
        );
        var white = TextStyles.Small.With(Color.White);
        canvas.Text(
            $"camera  {_camera.Center.X:0.0}, {_camera.Center.Y:0.0}",
            new Vector2(28, 54),
            white
        );
        canvas.Text($"zoom  {_controller.ZoomFactor:0.00}x", new Vector2(28, 76), white);
        canvas.Text(
            $"batches {Ctx.Render.Stats.LastFrameDrawBatches}  quads {Ctx.Render.Stats.LastFrameQuads}",
            new Vector2(28, 98),
            white
        );
        canvas.Text(
            HoverTile is var (tx, ty) ? $"tile  {tx}, {ty}" : "tile  -",
            new Vector2(28, 120),
            white
        );
        canvas.Text("Esc: back   P: pause overlay", new Vector2(16, 156), TextStyles.Small);
        DrawExtraScreen(canvas, ui);
    }

    protected virtual void DrawExtraScreen(ScreenCanvas canvas, UiContext ui) { }
}

/// <summary>Sprites, tilemaps and animation frames on the debug map.</summary>
public sealed class SpriteGalleryScene : DebugMapScene
{
    public override string Name => "Sprite gallery";
}

/// <summary>Tile picking: hover highlight and a line preview in the world overlay.</summary>
public sealed class PickingTestScene : DebugMapScene
{
    public override string Name => "Picking test";

    protected override void DrawWorld(WorldOverlay overlay)
    {
        if (HoverTile is var (hx, hy))
        {
            overlay.FillTile(hx, hy, new Vector4(1f, 1f, 1f, 0.18f));
            overlay.OutlineTile(hx, hy, 0.05f, new Vector4(1f, 0.9f, 0.3f, 1f));
        }
        var (sx, sy) = DebugTacticalScene.Soldier;
        overlay.Line(
            TacticalCamera.TileCenter(sx, sy),
            TacticalCamera.TileCenter(sx + 3, sy + 1),
            0.08f,
            new Vector4(1f, 1f, 0.4f, 1f)
        );
    }
}

/// <summary>World text that follows sprites, plus the screen-space text panel.</summary>
public sealed class TextTestScene : DebugMapScene
{
    private DebugTextScene _text = null!;

    public override string Name => "Text test";

    protected override void OnEnter()
    {
        base.OnEnter();
        _text = new DebugTextScene(World, Ctx.Assets);
    }

    protected override void OnUpdate(float dt)
    {
        base.OnUpdate(dt);
        _text.Update(dt, TacticalCam.PixelsPerTile(Ctx.Render.CanvasSize.Y));
    }

    protected override void DrawExtraScreen(ScreenCanvas canvas, UiContext ui) =>
        DebugTextScene.DrawPanel(canvas);
}
