using Firewall.Rules.Data;
using Firewall.Web.Assets;
using Firewall.Web.Input;
using Firewall.Web.Rendering;
using Firewall.Web.Ui;
using Yaeger.Platform;

namespace Firewall.Web.Scenes;

/// <summary>Audio service stub; wired to <c>IAudioOutput</c> later.</summary>
public sealed class AudioService
{
    public void Play(UiSound sound) { }
}

/// <summary>Save service stub; the real one arrives with the campaign (Epic 4).</summary>
public sealed class SaveService
{
    public bool HasSave => false;
}

/// <summary>Hands out deterministic seeds (SplitMix64) for the rules' <c>IRandom</c>.</summary>
public sealed class SeedSource(ulong root)
{
    private ulong _state = root;

    public ulong Next()
    {
        var z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}

/// <summary>
/// Services shared by every scene. <see cref="Assets"/>, <see cref="Ui"/> and <see cref="Data"/>
/// exist only once the boot scene has loaded them (see <see cref="Install"/>).
/// </summary>
public sealed class SceneContext(
    IInputState input,
    InputBindings bindings,
    ClickGate clicks,
    RenderServices? render = null
)
{
    private AssetRegistry? _assets;
    private UiContext? _ui;

    public IInputState Input { get; } = input;
    public InputBindings Bindings { get; } = bindings;
    public ClickGate Clicks { get; } = clicks;

    /// <summary>Null in tests that never render.</summary>
    public RenderServices Render { get; } = render!;

    /// <summary>Set by the <see cref="SceneManager"/> that owns this context.</summary>
    public SceneManager Scenes { get; internal set; } = null!;

    public AudioService Audio { get; } = new();
    public SaveService Saves { get; } = new();
    public SeedSource Seeds { get; set; } = new(0);
    public GameData Data { get; private set; } = GameData.Empty;

    public AssetRegistry Assets =>
        _assets ?? throw new InvalidOperationException("Not loaded yet.");
    public UiContext Ui => _ui ?? throw new InvalidOperationException("Not loaded yet.");
    public bool IsLoaded => _assets is not null;

    /// <summary>Called by the boot scene once the manifests and data are loaded.</summary>
    public void Install(AssetRegistry assets, GameData data)
    {
        _assets = assets;
        Data = data;
        _ui = new UiContext(Input, Clicks, Bindings, assets);
        _ui.Sound = Audio.Play;
    }

    /// <summary>Test seam: installs a prebuilt UI context.</summary>
    internal void Install(AssetRegistry assets, UiContext ui)
    {
        _assets = assets;
        _ui = ui;
    }
}
