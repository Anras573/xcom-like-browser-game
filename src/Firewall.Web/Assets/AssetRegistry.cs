using System.Net.Http.Json;
using System.Numerics;

namespace Firewall.Web.Assets;

/// <summary>Maps semantic names (e.g. <c>unit.soldier1.machine</c>) to sheet frames and UI regions.</summary>
public sealed class AssetRegistry
{
    private static readonly string[] GridManifestPaths =
    [
        "assets/tiles.json",
        "assets/characters.json",
        "assets/overlays.json",
        "assets/fx.json",
    ];

    private const string UiManifestPath = "assets/ui.json";

    private readonly Dictionary<string, SpriteFrame> _frames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FlipbookEntry> _flipbooks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GridManifest> _flipbookSheets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UiRegion> _ui = new(StringComparer.Ordinal);

    public AssetRegistry(IEnumerable<GridManifest> grids, UiManifest ui)
    {
        var gridList = grids.ToList();
        foreach (var grid in gridList)
        {
            foreach (var (name, index) in grid.Frames)
            {
                if (index < 0 || index >= grid.Columns * grid.Rows)
                    throw new InvalidDataException(
                        $"Frame '{name}' ({index}) is outside {grid.Texture} ({grid.Columns}x{grid.Rows})."
                    );

                var pivot = grid.PivotOffsets?.GetValueOrDefault(name) is { Length: 2 } p
                    ? new Vector2(p[0], p[1])
                    : Vector2.Zero;
                if (!_frames.TryAdd(name, new(grid.Texture, grid.Columns, grid.Rows, index, pivot)))
                    throw new InvalidDataException($"Frame name '{name}' is defined twice.");
            }

            foreach (var (name, flipbook) in grid.Flipbooks ?? [])
            {
                _flipbooks[name] = flipbook;
                _flipbookSheets[name] = grid;
            }
        }
        Grids = gridList;

        foreach (var (name, r) in ui.Regions)
        {
            var (uvMin, uvMax) = UiUv.FromPixelRect(r.X, r.Y, r.W, r.H, ui.Width, ui.Height);
            var i = r.Insets;
            var insets = i is { Length: 4 } ? new UiInsets(i[0], i[1], i[2], i[3]) : default;
            _ui.Add(name, new UiRegion(ui.Texture, uvMin, uvMax, insets));
        }
        UiAtlas = ui;
    }

    public IReadOnlyList<GridManifest> Grids { get; }

    public UiManifest UiAtlas { get; }

    /// <summary>Every texture the manifests reference, for <c>PreloadAsync</c>.</summary>
    public IEnumerable<string> TexturePaths =>
        Grids.Select(g => g.Texture).Append(UiAtlas.Texture).Distinct();

    public static async Task<AssetRegistry> LoadAsync(
        HttpClient http,
        CancellationToken cancellationToken = default
    )
    {
        var grids = new List<GridManifest>();
        foreach (var path in GridManifestPaths)
            grids.Add(
                await http.GetFromJsonAsync(
                    path,
                    AssetJsonContext.Default.GridManifest,
                    cancellationToken
                ) ?? throw new InvalidDataException($"{path} is empty.")
            );

        var ui =
            await http.GetFromJsonAsync(
                UiManifestPath,
                AssetJsonContext.Default.UiManifest,
                cancellationToken
            ) ?? throw new InvalidDataException($"{UiManifestPath} is empty.");
        return new AssetRegistry(grids, ui);
    }

    public SpriteFrame Frame(string name) =>
        _frames.TryGetValue(name, out var frame)
            ? frame
            : throw new KeyNotFoundException($"Unknown sprite frame '{name}'.");

    public bool TryFrame(string name, out SpriteFrame frame) =>
        _frames.TryGetValue(name, out frame);

    /// <summary>First frame of a flipbook (e.g. <c>fx.explosion</c>) and its frame count.</summary>
    public (SpriteFrame First, int Count) Flipbook(string name)
    {
        if (!_flipbooks.TryGetValue(name, out var flipbook))
            throw new KeyNotFoundException($"Unknown flipbook '{name}'.");
        var grid = _flipbookSheets[name];
        return (new(grid.Texture, grid.Columns, grid.Rows, flipbook.Start), flipbook.Count);
    }

    public UiRegion Ui(string name) =>
        _ui.TryGetValue(name, out var region)
            ? region
            : throw new KeyNotFoundException($"Unknown UI region '{name}'.");
}
