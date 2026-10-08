using Firewall.Web.Input;
using Firewall.Web.Rendering;
using Firewall.Web.Scenes;
using Yaeger.Browser;
using Yaeger.Input;
using Yaeger.Platform;

namespace Firewall.Web;

/// <summary>Owns the scene manager and is ticked once per animation frame.</summary>
public sealed class GameApp
{
    private readonly BrowserTimeSource _timeSource = new();
    private readonly ClickGate _clicks;
    private readonly SceneManager _scenes;

    public GameApp(BrowserRenderSurface surface, HttpClient http)
    {
        IInputState input = new BrowserInputState();
        var stats = new RenderStatsSurface(surface);
        var atlas = new BrowserGlyphAtlas(() => surface.PixelRatio);
        var text = new BrowserTextRenderSurface(stats, atlas);
        var render = new RenderServices(
            surface,
            stats,
            text,
            new TextServices(
                atlas,
                (font, size, content) => atlas.Prepare(font, size, content),
                options => text.Options = options
            )
        );

        // Tab, Space, Backspace and the arrows would otherwise move focus or scroll the page.
        BrowserInputState.SetPreventDefaultKeys(InputBindings.PreventDefaultKeys);
        _clicks = new ClickGate(input);
        var ctx = new SceneContext(input, new InputBindings(input), _clicks, render)
        {
            Seeds = new SeedSource((ulong)DateTime.UtcNow.Ticks),
        };

        DebugScenes.RegisterBuiltIn();
        _scenes = new SceneManager(ctx);
        _scenes.Push(new BootScene(BootSteps.Create(ctx, http, surface), () => new TitleScene()));
        _scenes.Flush();
    }

    public void Tick(double timestampMs)
    {
        _timeSource.Advance(timestampMs);
        _clicks.BeginFrame();
        _scenes.Update(_timeSource.DeltaTime);
        _scenes.Render();
    }
}
