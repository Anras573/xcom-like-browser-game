using System.Numerics;
using Firewall.Web.Assets;
using Firewall.Web.Rendering;
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
    private static readonly float[] TilesVisibleLevels = [22f, 11f, 5.5f];
    private const float PanTilesPerSecond = 8f;

    private static readonly FontHandle Font = new(DebugSheetScene.FontFamily);
    private static readonly Vector4 RangeFill = new(0.2f, 0.6f, 1f, 0.28f);
    private static readonly Vector4 RangeOutline = new(0.4f, 0.8f, 1f, 0.9f);

    private readonly World _world = new();
    private readonly BrowserRenderSurface _surface;
    private readonly RenderStatsSurface _stats;
    private readonly BrowserTimeSource _timeSource = new();
    private readonly IInputState _input = new BrowserInputState();
    private readonly FrameRenderer _frame;
    private readonly TacticalCamera _camera = new() { Center = new Vector2(10f, 10f) };
    private readonly Entity _cameraEntity;

    public GameApp(BrowserRenderSurface surface, AssetRegistry registry)
    {
        _surface = surface;
        _stats = new RenderStatsSurface(surface);
        var text = new BrowserTextRenderSurface(
            _stats,
            new BrowserGlyphAtlas(() => surface.PixelRatio)
        );
        var entities = new UnifiedRenderSystem(_stats, text, _world, surface);
        _frame = new FrameRenderer(_stats, text, entities, surface)
        {
            WorldPass = DrawWorldOverlay,
            ScreenPass = DrawPanel,
        };

        // Arrow keys would otherwise scroll the page.
        BrowserInputState.SetPreventDefaultKeys([Keys.Up, Keys.Down, Keys.Left, Keys.Right]);

        DebugTacticalScene.Build(_world, registry);

        _cameraEntity = _world.CreateEntity("camera");
        _world.AddComponent(_cameraEntity, _camera.ToCamera2D());
    }

    public void Tick(double timestampMs)
    {
        _timeSource.Advance(timestampMs);
        BrowserInputState.BeginFrame();
        UpdateDebugCamera();
        _frame.Render();
        BrowserInputState.EndFrame();
    }

    // Debug controls: 1/2/3 show 22/11/5.5 tiles vertically, WASD or arrows pan.
    private void UpdateDebugCamera()
    {
        for (var i = 0; i < TilesVisibleLevels.Length; i++)
            if (_input.WasKeyPressed(Keys.Num1 + i))
                _camera.TilesVisibleVertically = TilesVisibleLevels[i];

        var pan = Vector2.Zero;
        if (_input.IsKeyPressed(Keys.A) || _input.IsKeyPressed(Keys.Left))
            pan.X -= 1;
        if (_input.IsKeyPressed(Keys.D) || _input.IsKeyPressed(Keys.Right))
            pan.X += 1;
        if (_input.IsKeyPressed(Keys.W) || _input.IsKeyPressed(Keys.Up))
            pan.Y += 1;
        if (_input.IsKeyPressed(Keys.S) || _input.IsKeyPressed(Keys.Down))
            pan.Y -= 1;
        _camera.Center += pan * PanTilesPerSecond * (float)_timeSource.DeltaTime;

        _world.AddComponent(_cameraEntity, _camera.ToCamera2D());
    }

    // Move-range preview around the soldier; changes freely per frame, so it isn't in the ECS.
    private static void DrawWorldOverlay(WorldOverlay overlay)
    {
        var (sx, sy) = DebugTacticalScene.Soldier;
        for (var dy = -3; dy <= 3; dy++)
        for (var dx = -3; dx <= 3; dx++)
        {
            if (dx * dx + dy * dy > 9)
                continue;
            overlay.FillTile(sx + dx, sy + dy, RangeFill);
            overlay.OutlineTile(sx + dx, sy + dy, 0.04f, RangeOutline);
        }
        overlay.Line(
            TacticalCamera.TileCenter(sx, sy),
            TacticalCamera.TileCenter(sx + 3, sy + 1),
            0.08f,
            new Vector4(1f, 1f, 0.4f, 1f)
        );
    }

    // Stays put in the top-left of the letterboxed 1280x720 UI while the world camera moves.
    private void DrawPanel(ScreenCanvas canvas)
    {
        canvas.FillRect(new Vector2(16, 16), new Vector2(300, 112), new Vector4(0f, 0f, 0f, 0.6f));
        canvas.Text("FIREWALL  debug", new Vector2(28, 24), Font, 20, new Color(255, 220, 90));
        canvas.Text(
            $"camera  {_camera.Center.X:0.0}, {_camera.Center.Y:0.0}",
            new Vector2(28, 54),
            Font,
            16,
            Color.White
        );
        canvas.Text(
            $"tiles visible  {_camera.TilesVisibleVertically:0.#}",
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
    }
}
