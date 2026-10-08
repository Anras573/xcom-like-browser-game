using System.Numerics;
using Firewall.Web.Assets;
using Firewall.Web.Input;
using Firewall.Web.Rendering;
using Firewall.Web.Ui;
using Yaeger.Browser;
using Yaeger.ECS;
using Yaeger.Graphics;
using Yaeger.Input;
using Yaeger.Platform;
using Yaeger.Systems;

namespace Firewall.Web;

/// <summary>Owns the Yaeger world and is ticked once per animation frame.</summary>
public sealed class GameApp
{
    private static readonly FontHandle Font = new(DebugSheetScene.FontFamily);

    private readonly World _world = new();
    private readonly BrowserRenderSurface _surface;
    private readonly RenderStatsSurface _stats;
    private readonly BrowserTimeSource _timeSource = new();
    private readonly IInputState _input = new BrowserInputState();
    private readonly FrameRenderer _frame;
    private readonly TacticalCamera _camera = new() { Center = new Vector2(10f, 10f) };
    private readonly Entity _cameraEntity;
    private readonly InputBindings _bindings;
    private readonly ClickGate _clicks;
    private readonly TacticalCameraController _cameraController;
    private (int X, int Y)? _hoverTile;
    private readonly DebugTextScene _textScene;
    private readonly UiContext _ui;
    private readonly UiGalleryScene _gallery = new();
    private bool _showGallery;

    public GameApp(BrowserRenderSurface surface, AssetRegistry registry)
    {
        _surface = surface;
        _stats = new RenderStatsSurface(surface);
        var atlas = new BrowserGlyphAtlas(() => surface.PixelRatio);
        var text = new BrowserTextRenderSurface(_stats, atlas);
        var entities = new UnifiedRenderSystem(_stats, text, _world, surface);
        _frame = new FrameRenderer(_stats, text, entities, surface)
        {
            WorldPass = DrawWorldOverlay,
            ScreenPass = DrawScreen,
            TextServices = new TextServices(
                atlas,
                (font, size, content) => atlas.Prepare(font, size, content),
                options => text.Options = options
            ),
        };

        // Tab, Space, Backspace and the arrows would otherwise move focus or scroll the page.
        BrowserInputState.SetPreventDefaultKeys(InputBindings.PreventDefaultKeys);
        _bindings = new InputBindings(_input);
        _clicks = new ClickGate(_input);
        _cameraController = new TacticalCameraController(_camera, _input, _bindings)
        {
            MapSize = new Vector2(DebugTacticalScene.MapSize),
        };

        _ui = new UiContext(_input, _clicks, _bindings, registry);
        DebugTacticalScene.Build(_world, registry);
        _textScene = new DebugTextScene(_world, registry);

        _cameraEntity = _world.CreateEntity("camera");
        _world.AddComponent(_cameraEntity, _camera.ToCamera2D());
    }

    public void Tick(double timestampMs)
    {
        _timeSource.Advance(timestampMs);
        _clicks.BeginFrame();
        if (_input.WasKeyPressed(Keys.F1))
            _showGallery = !_showGallery;
        UpdateCamera();
        _textScene.Update(_timeSource.DeltaTime, _camera.PixelsPerTile(_surface.Size.Y));
        _frame.Render();
    }

    private void UpdateCamera()
    {
        var canvas = _surface.Size;
        var dt = (float)_timeSource.DeltaTime;

        if (_bindings.WasPressed(GameAction.CenterCamera))
        {
            var (sx, sy) = DebugTacticalScene.Soldier;
            _cameraController.FocusOn(TacticalCamera.TileCenter(sx, sy), 0.4f);
        }
        _cameraController.Update(dt, canvas, _ui.PointerOverUi);

        _world.AddComponent(_cameraEntity, _camera.ToCamera2D());
        // Don't pick tiles (or later, click them) through UI drawn last frame.
        _hoverTile = _ui.PointerOverUi ? null : PickTile(canvas);
    }

    // Uses the same view-projection the renderer does, so the highlight matches the pixels.
    private (int X, int Y)? PickTile(Vector2 canvas)
    {
        var mouse = _input.MousePosition;
        if (
            !_input.IsMouseInside
            || mouse.X < 0f
            || mouse.Y < 0f
            || mouse.X >= canvas.X
            || mouse.Y >= canvas.Y
        )
            return null;
        var vp = _camera.ToCamera2D().ViewProjection(canvas.X / MathF.Max(canvas.Y, 1f));
        var world = Picking.ScreenToWorld(Picking.CanvasToNdc(mouse, canvas), vp);
        var tile = Picking.WorldToTile(world);
        return
            tile.X is >= 0 and < DebugTacticalScene.MapSize
            && tile.Y is >= 0 and < DebugTacticalScene.MapSize
            ? tile
            : null;
    }

    // Immediate-mode world drawing for things that may sit above world text: path/AoE preview.
    private void DrawWorldOverlay(WorldOverlay overlay)
    {
        if (_hoverTile is var (hx, hy))
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

    // Stays put in the top-left of the letterboxed 1280x720 UI while the world camera moves.
    private void DrawScreen(ScreenCanvas canvas)
    {
        _ui.Begin(canvas, new UiSpace(_surface.Size), (float)_timeSource.DeltaTime);
        if (_showGallery)
            _gallery.Draw(_ui);
        else
        {
            DrawPanel(canvas);
            DebugTextScene.DrawPanel(canvas);
            _ui.Label(new UiRect(16, 156, 300, 20), "F1: UI gallery", TextStyles.Small);
        }
        _ui.End();
    }

    private void DrawPanel(ScreenCanvas canvas)
    {
        canvas.FillRect(new Vector2(16, 16), new Vector2(300, 134), new Vector4(0f, 0f, 0f, 0.6f));
        canvas.Text("FIREWALL  debug", new Vector2(28, 24), Font, 20, new Color(255, 220, 90));
        canvas.Text(
            $"camera  {_camera.Center.X:0.0}, {_camera.Center.Y:0.0}",
            new Vector2(28, 54),
            Font,
            16,
            Color.White
        );
        canvas.Text(
            $"zoom  {_cameraController.ZoomFactor:0.00}x",
            new Vector2(28, 76),
            Font,
            16,
            Color.White
        );
        canvas.Text(
            $"batches {_stats.LastFrameDrawBatches}  quads {_stats.LastFrameQuads}",
            new Vector2(28, 98),
            Font,
            16,
            Color.White
        );
        canvas.Text(
            _hoverTile is var (tx, ty) ? $"tile  {tx}, {ty}" : "tile  -",
            new Vector2(28, 120),
            Font,
            16,
            Color.White
        );
    }
}
