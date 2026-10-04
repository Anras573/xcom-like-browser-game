using System.Numerics;
using Firewall.Web.Assets;
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
    private static readonly float[] ZoomLevels = [0.5f, 1f, 2f];
    private const float PanCellsPerSecond = 12f;

    private readonly World _world = new();
    private readonly BrowserRenderSurface _surface;
    private readonly BrowserTimeSource _timeSource = new();
    private readonly IInputState _input = new BrowserInputState();
    private readonly UnifiedRenderSystem _renderSystem;
    private readonly Entity _cameraEntity;

    private Vector2 _cameraCell = new(15.5f, -12f);
    private float _zoomMultiplier = 1f;

    public GameApp(BrowserRenderSurface surface, AssetRegistry registry)
    {
        _surface = surface;
        _renderSystem = new UnifiedRenderSystem(
            surface,
            new BrowserTextRenderSurface(surface),
            _world,
            surface
        );

        // Arrow keys would otherwise scroll the page.
        BrowserInputState.SetPreventDefaultKeys([Keys.Up, Keys.Down, Keys.Left, Keys.Right]);

        DebugSheetScene.Build(_world, registry);

        _cameraEntity = _world.CreateEntity("camera");
        _world.AddComponent(_cameraEntity, new Camera2D(_cameraCell));
    }

    public void Tick(double timestampMs)
    {
        _timeSource.Advance(timestampMs);
        BrowserInputState.BeginFrame();
        UpdateDebugCamera();
        _renderSystem.Render();
        BrowserInputState.EndFrame();
    }

    // Debug scene controls: 1/2/3 pick 0.5x/1x/2x (one cell = 32/64/128 px), WASD or arrows pan.
    private void UpdateDebugCamera()
    {
        for (var i = 0; i < ZoomLevels.Length; i++)
            if (_input.WasKeyPressed(Keys.Num1 + i))
                _zoomMultiplier = ZoomLevels[i];

        var dt = _timeSource.DeltaTime;
        var pan = Vector2.Zero;
        if (_input.IsKeyPressed(Keys.A) || _input.IsKeyPressed(Keys.Left))
            pan.X -= 1;
        if (_input.IsKeyPressed(Keys.D) || _input.IsKeyPressed(Keys.Right))
            pan.X += 1;
        if (_input.IsKeyPressed(Keys.W) || _input.IsKeyPressed(Keys.Up))
            pan.Y += 1;
        if (_input.IsKeyPressed(Keys.S) || _input.IsKeyPressed(Keys.Down))
            pan.Y -= 1;
        _cameraCell += pan * PanCellsPerSecond * dt / _zoomMultiplier;

        // The camera shows 2 / zoom world units vertically; 64 canvas px per cell at 1x.
        var zoom = 128f * _zoomMultiplier / MathF.Max(_surface.Size.Y, 1f);
        _world.AddComponent(_cameraEntity, new Camera2D(_cameraCell, zoom));
    }
}
