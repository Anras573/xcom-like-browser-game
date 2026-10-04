using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using Firewall.Web.Assets;

namespace Firewall.Web.Tests;

public class AssetRegistryTests
{
    private static readonly string AssetsDir = FindAssetsDir();

    private static readonly string[] GridNames = ["tiles", "characters", "overlays", "fx"];

    private static string FindAssetsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (
            dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Firewall.Web"))
        )
            dir = dir.Parent;
        return Path.Combine(
            dir?.FullName ?? throw new DirectoryNotFoundException("repo root not found"),
            "src",
            "Firewall.Web",
            "wwwroot",
            "assets"
        );
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static GridManifest LoadGrid(string name) =>
        JsonSerializer.Deserialize<GridManifest>(
            File.ReadAllText(Path.Combine(AssetsDir, name + ".json")),
            Json
        )!;

    private static UiManifest LoadUi() =>
        JsonSerializer.Deserialize<UiManifest>(
            File.ReadAllText(Path.Combine(AssetsDir, "ui.json")),
            Json
        )!;

    private static AssetRegistry LoadRegistry() => new(GridNames.Select(LoadGrid), LoadUi());

    private static (int Width, int Height) PngSize(string texturePath)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AssetsDir, "..", texturePath));
        return (
            BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16)),
            BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20))
        );
    }

    [Fact]
    public void FrameResolvesSheetAndIndex()
    {
        var registry = LoadRegistry();

        var stand = registry.Frame("unit.soldier1.stand");
        Assert.Equal("assets/characters.png", stand.TexturePath);
        Assert.Equal(6, stand.Columns);
        Assert.Equal(9, stand.Rows);
        Assert.Equal(5 * 6 + 5, stand.FrameIndex); // soldier1 is the 6th character, stand the 6th pose

        var sheet = stand.ToSpriteSheet();
        Assert.Equal(stand.TexturePath, sheet.TexturePath);
        Assert.Equal(54, sheet.FrameCount);
    }

    [Fact]
    public void TileNamesUseRowTimesColumnsPlusColumn()
    {
        var registry = LoadRegistry();
        Assert.Equal(10 * 27 + 11, registry.Frame("wall.grey.straight_h").FrameIndex);
        Assert.Equal(27, registry.Frame("wall.grey.straight_h").Columns);
    }

    [Fact]
    public void UnknownNamesThrow()
    {
        var registry = LoadRegistry();
        Assert.Throws<KeyNotFoundException>(() => registry.Frame("nope"));
        Assert.Throws<KeyNotFoundException>(() => registry.Ui("nope"));
    }

    [Fact]
    public void UiRegionConvertsRectToFlippedUvAndKeepsInsets()
    {
        var ui = new UiManifest(
            "assets/ui.png",
            256,
            128,
            2,
            new() { ["panel"] = new(0, 0, 64, 32, [8, 8, 8, 12]) }
        );
        var registry = new AssetRegistry([], ui);

        var region = registry.Ui("panel");
        Assert.Equal("assets/ui.png", region.TexturePath);
        Assert.Equal(new Vector2(0f, 0.75f), region.UvMin);
        Assert.Equal(new Vector2(0.25f, 1f), region.UvMax);
        Assert.Equal(new UiInsets(8, 8, 8, 12), region.Insets);
    }

    [Fact]
    public void DuplicateFrameNamesAreRejected()
    {
        var a = new GridManifest("a.png", 1, 1, new() { ["x"] = 0 });
        var b = new GridManifest("b.png", 1, 1, new() { ["x"] = 0 });
        var ui = new UiManifest("ui.png", 1, 1, 0, []);
        Assert.Throws<InvalidDataException>(() => new AssetRegistry([a, b], ui));
    }

    [Fact]
    public void GridManifestsMatchTheirTextures()
    {
        foreach (var name in GridNames)
        {
            var grid = LoadGrid(name);
            Assert.Equal((grid.Columns * 64, grid.Rows * 64), PngSize(grid.Texture));
            Assert.All(
                grid.Frames,
                kv => Assert.InRange(kv.Value, 0, grid.Columns * grid.Rows - 1)
            );
        }
    }

    [Fact]
    public void UiRegionsLieInsideTheAtlas()
    {
        var ui = LoadUi();
        Assert.Equal((ui.Width, ui.Height), PngSize(ui.Texture));
        foreach (var (name, r) in ui.Regions)
        {
            Assert.True(r.X >= ui.Extrude && r.Y >= ui.Extrude, name);
            Assert.True(r.X + r.W + ui.Extrude <= ui.Width, name);
            Assert.True(r.Y + r.H + ui.Extrude <= ui.Height, name);
        }
    }

    [Fact]
    public void ManifestsCoverTheExpectedContent()
    {
        var registry = LoadRegistry();
        foreach (
            var name in new[]
            {
                "wall.grey.straight_v",
                "wall.grey.corner_nw",
                "wall.grey.t_n",
                "wall.grey.cross",
                "wall.grey.end_w",
            }
        )
            registry.Frame(name);

        foreach (var pose in new[] { "gun", "hold", "machine", "reload", "silencer", "stand" })
            registry.Frame($"unit.zombie1.{pose}");

        foreach (
            var name in new[]
            {
                "overlay.move_range",
                "overlay.path_dot",
                "overlay.shield_half",
                "overlay.shield_full",
                "overlay.crosshair",
                "overlay.selection_ring",
                "status.overwatch",
                "status.hunker",
                "status.bleeding",
                "decal.corpse_scrap",
            }
        )
            registry.Frame(name);

        var (first, count) = registry.Flipbook("fx.explosion");
        Assert.Equal(9, count);
        Assert.Equal("assets/fx.png", first.TexturePath);
        Assert.Equal(first.FrameIndex, registry.Frame("fx.explosion.00").FrameIndex);
    }
}
