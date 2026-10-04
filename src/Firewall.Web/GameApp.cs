using System.Numerics;
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
    private const string TilePath = "assets/tile.png";

    /// <summary>Textures to preload before the first <see cref="Tick"/>.</summary>
    public static readonly string[] TexturePaths = [TilePath];

    private readonly World _world = new();
    private readonly BrowserTimeSource _timeSource = new();
    private readonly IInputState _input = new BrowserInputState();
    private readonly UnifiedRenderSystem _renderSystem;

    public GameApp(BrowserRenderSurface surface)
    {
        _renderSystem = new UnifiedRenderSystem(
            surface,
            new BrowserTextRenderSurface(surface),
            _world,
            surface
        );

        var solid = _world.CreateEntity("solid");
        _world.AddComponent(
            solid,
            new Transform2D(new Vector2(-0.4f, 0f), scale: new Vector2(0.4f, 0.4f))
        );
        _world.AddComponent(solid, new Sprite("", new Color(230, 160, 40)));

        var textured = _world.CreateEntity("textured");
        _world.AddComponent(
            textured,
            new Transform2D(new Vector2(0.4f, 0f), scale: new Vector2(0.4f, 0.4f))
        );
        _world.AddComponent(textured, new Sprite(TilePath, new Color(255, 255, 255)));
    }

    public void Tick(double timestampMs)
    {
        _timeSource.Advance(timestampMs);
        BrowserInputState.BeginFrame();
        _renderSystem.Render();
    }
}
