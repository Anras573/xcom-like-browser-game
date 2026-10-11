using Firewall.Rules.Data;
using Firewall.Web.Assets;
using Firewall.Web.Rendering;
using Yaeger.Browser;
using Yaeger.Platform;

namespace Firewall.Web.Scenes;

/// <summary>The loading sequence the boot scene runs: fonts, atlas manifests, textures, game data.</summary>
public static class BootSteps
{
    public static IReadOnlyList<BootStep> Create(
        SceneContext ctx,
        HttpClient http,
        BrowserRenderSurface surface
    )
    {
        AssetRegistry? registry = null;
        return
        [
            // First, so the progress label can be drawn from the second step on.
            new("Loading fonts", _ => TextStyles.LoadAsync()),
            new(
                "Loading atlas manifests",
                async _ => registry = await AssetRegistry.LoadAsync(http)
            ),
            new(
                "Loading textures",
                async progress =>
                {
                    // tiles.png has no spacing between tiles, so Linear bleeds neighbours at
                    // fractional camera positions. Must be set before the texture loads.
                    surface.SetSampling("assets/tiles.png", TextureSampling.Pixelated);
                    await surface.PreloadAsync(registry!.TexturePaths, progress);
                }
            ),
            new(
                "Loading game data",
                async _ =>
                {
                    var files = new Dictionary<string, string>();
                    foreach (var file in GameDataFiles.All)
                        files[file] = await http.GetStringAsync($"data/{file}");
                    var data = GameData.Load(f => files[f]);
                    var errors = data.Validate();
                    if (errors.Count > 0)
                        throw new InvalidDataException(
                            "Game data is invalid:\n" + string.Join('\n', errors)
                        );
                    ctx.Install(registry!, data);
                }
            ),
        ];
    }
}
