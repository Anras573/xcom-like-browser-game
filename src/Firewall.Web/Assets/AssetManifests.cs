using System.Text.Json.Serialization;

namespace Firewall.Web.Assets;

/// <summary>A uniform grid of 64x64 cells (assets/{tiles,characters,overlays,fx}.json).</summary>
public sealed record GridManifest(
    string Texture,
    int Columns,
    int Rows,
    Dictionary<string, int> Frames,
    Dictionary<string, int[]>? PivotOffsets = null,
    Dictionary<string, FlipbookEntry>? Flipbooks = null
);

/// <summary>Contiguous run of frames in a grid sheet, played in order.</summary>
public sealed record FlipbookEntry(int Start, int Count);

/// <summary>The free-form packed UI atlas (assets/ui.json); rects are pixels, top-left origin.</summary>
public sealed record UiManifest(
    string Texture,
    int Width,
    int Height,
    int Extrude,
    Dictionary<string, UiRegionEntry> Regions
);

public sealed record UiRegionEntry(int X, int Y, int W, int H, int[]? Insets = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GridManifest))]
[JsonSerializable(typeof(UiManifest))]
internal sealed partial class AssetJsonContext : JsonSerializerContext;
