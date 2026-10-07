using System.Numerics;
using Yaeger.ECS;
using Yaeger.Graphics;
using Yaeger.Platform;

namespace Firewall.Web.Assets;

/// <summary>
/// Draws every frame of every sheet in a labelled grid with engine <see cref="SpriteSheet"/>
/// entities, for checking the pipeline output and texture bleeding. One world unit = one 64px
/// cell; cells are contiguous, exactly as a tilemap would draw them.
/// </summary>
public static class DebugSheetScene
{
    public const string FontFamily = Rendering.TextStyles.Future;

    private const float LabelScale = 1f / 64f; // text lays out in pixels; 1 cell = 64 px
    private static readonly Color LabelColor = new(255, 220, 90);
    private static readonly Color BackdropColor = new(46, 50, 62);

    /// <summary>Where each sheet sits, in cells; y grows upwards, rows go down.</summary>
    private static readonly (string Texture, Vector2 Origin)[] Layout =
    [
        ("assets/tiles.png", new Vector2(2, -2)),
        ("assets/characters.png", new Vector2(33, -2)),
        ("assets/overlays.png", new Vector2(33, -14)),
        ("assets/fx.png", new Vector2(42, -2)),
    ];

    public static void Build(World world, AssetRegistry registry)
    {
        foreach (var grid in registry.Grids)
        {
            var origin = Array.Find(Layout, l => l.Texture == grid.Texture).Origin;
            AddGrid(world, grid, origin);
        }

        AddUiAtlas(world, registry.UiAtlas, new Vector2(2, -25));
    }

    private static void AddGrid(World world, GridManifest grid, Vector2 origin)
    {
        var name = Path.GetFileNameWithoutExtension(grid.Texture);
        AddLabel(
            world,
            $"{name}  ({grid.Columns}x{grid.Rows}, {grid.Frames.Count} named)",
            origin + new Vector2(0, 1.2f),
            18
        );

        AddBackdrop(world, origin, grid.Columns, grid.Rows);

        for (var row = 0; row < grid.Rows; row++)
        {
            var rowLabelPos = origin + new Vector2(-0.6f, -row - 0.5f);
            AddLabel(world, (row * grid.Columns).ToString(), rowLabelPos, 11, rightAlign: true);
        }

        for (var col = 0; col < grid.Columns; col++)
            AddLabel(
                world,
                col.ToString(),
                origin + new Vector2(col + 0.5f, 0.45f),
                11,
                centre: true
            );

        var sheet = new SpriteSheet(grid.Texture, grid.Columns, grid.Rows);
        for (var frame = 0; frame < grid.Columns * grid.Rows; frame++)
        {
            var e = world.CreateEntity();
            var cell =
                origin + new Vector2(frame % grid.Columns + 0.5f, -(frame / grid.Columns) - 0.5f);
            world.AddComponent(e, new Transform2D(cell));
            world.AddComponent(e, sheet);
            world.AddComponent(e, new AnimationState(frame));
        }
    }

    private static void AddUiAtlas(World world, UiManifest ui, Vector2 origin)
    {
        AddLabel(
            world,
            $"ui  ({ui.Width}x{ui.Height}, {ui.Regions.Count} regions)",
            origin + new Vector2(0, 1.2f),
            18
        );
        var size = new Vector2(ui.Width, ui.Height) / 64f;
        AddBackdrop(world, origin, size.X, size.Y);

        var e = world.CreateEntity();
        world.AddComponent(
            e,
            new Transform2D(origin + new Vector2(size.X, -size.Y) / 2f, scale: size)
        );
        world.AddComponent(e, new Sprite(ui.Texture));
    }

    private static void AddBackdrop(World world, Vector2 origin, float width, float height)
    {
        var e = world.CreateEntity();
        world.AddComponent(
            e,
            new Transform2D(
                origin + new Vector2(width, -height) / 2f,
                scale: new Vector2(width, height)
            )
        );
        world.AddComponent(e, new Sprite("", BackdropColor));
        world.AddComponent(e, new RenderLayer(-1));
    }

    private static void AddLabel(
        World world,
        string content,
        Vector2 position,
        int fontSize,
        bool centre = false,
        bool rightAlign = false
    )
    {
        // Glyph quads are positioned from their top-left, so nudge for alignment using a rough
        // per-character advance; good enough for a debug overlay.
        var width = content.Length * fontSize * 0.6f * LabelScale;
        if (centre)
            position.X -= width / 2f;
        else if (rightAlign)
            position.X -= width;
        position.Y += fontSize * LabelScale / 2f;

        var e = world.CreateEntity();
        world.AddComponent(
            e,
            new Transform2D(position, scale: new Vector2(LabelScale, LabelScale))
        );
        world.AddComponent(e, new Text(content, new FontHandle(FontFamily), fontSize, LabelColor));
        world.AddComponent(e, new RenderLayer(1));
    }
}
