using System.Numerics;
using Yaeger.ECS;
using Yaeger.Graphics;

namespace Firewall.Web.Rendering;

/// <summary>
/// World-space text as <see cref="Text"/> entities. Glyph layout is in pixels, so the transform
/// scale converts pixels to tiles: <c>1 / pixelsPerTile</c>. For text that is sharp, pass the
/// on-screen pixels per tile (<see cref="TacticalCamera.PixelsPerTile"/>) and refresh it with
/// <see cref="Place"/> when the zoom changes.
/// </summary>
public readonly record struct WorldText(Entity Main, Entity? Shadow)
{
    /// <summary>Shadow offset in pixels, down and to the right.</summary>
    public static readonly Vector2 ShadowOffset = new(1f, -1f);

    public static readonly Color ShadowColor = new(0, 0, 0, 153); // black at 60 %

    /// <summary>Creates the text with its baseline starting at <paramref name="position"/>.</summary>
    public static WorldText Create(
        World world,
        string content,
        TextStyle style,
        Vector2 position,
        float pixelsPerTile
    )
    {
        var main = world.CreateEntity();
        world.AddComponent(main, new Text(content, style.Font, style.Size, style.Color));
        world.AddComponent(main, RenderLayers.Of(RenderLayers.WorldText));

        Entity? shadow = null;
        if (style.Shadow)
        {
            var s = world.CreateEntity();
            world.AddComponent(s, new Text(content, style.Font, style.Size, ShadowColor));
            world.AddComponent(s, RenderLayers.Of(RenderLayers.WorldTextShadow));
            shadow = s;
        }

        var text = new WorldText(main, shadow);
        text.Place(world, position, pixelsPerTile);
        return text;
    }

    public void Place(World world, Vector2 position, float pixelsPerTile)
    {
        var scale = 1f / MathF.Max(pixelsPerTile, 1e-3f);
        world.AddComponent(Main, new Transform2D(position, scale: new Vector2(scale, scale)));
        if (Shadow is { } shadow)
            world.AddComponent(
                shadow,
                new Transform2D(position + ShadowOffset * scale, scale: new Vector2(scale, scale))
            );
    }
}
