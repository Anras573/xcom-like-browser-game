using System.Numerics;
using Yaeger.Graphics;
using Yaeger.Platform;
using Yaeger.Systems;

namespace Firewall.Web.Rendering;

/// <summary>
/// One frame, in order: entities through <see cref="UnifiedRenderSystem"/> (which begins the
/// frame, clears, sets the world camera and flushes), then the world overlay pass under the
/// same camera, then the screen pass under the UI matrix, then a final flush.
/// </summary>
/// <remarks>
/// Don't call <c>BeginFrame</c> yourself: <c>UnifiedRenderSystem.Render</c> already does, and a
/// second call would clear what it drew. Overlays sit above every entity layer; anything that
/// must sit between layers has to be an entity with a <see cref="RenderLayer"/>.
/// </remarks>
public sealed class FrameRenderer(
    IRenderSurface surface,
    ITextRenderSurface text,
    UnifiedRenderSystem entities,
    IViewport viewport
)
{
    /// <summary>Hook for the particle system (#44); runs after entities, before overlays.</summary>
    public Action? Particles { get; set; }

    /// <summary>World-space immediate-mode drawing: move range, path/AoE preview, fog.</summary>
    public Action<WorldOverlay>? WorldPass { get; set; }

    /// <summary>Screen-space drawing in logical UI pixels.</summary>
    public Action<ScreenCanvas>? ScreenPass { get; set; }

    public void Render()
    {
        entities.Render();
        Particles?.Invoke();
        WorldPass?.Invoke(new WorldOverlay(surface));

        // Changing the matrix flushes the world quads first.
        var ui = new UiSpace(viewport.Size);
        surface.SetCamera(ui.ViewProjection());
        ScreenPass?.Invoke(new ScreenCanvas(surface, text));
        surface.FlushQueuedQuads();
    }
}

/// <summary>Flat-colour world-space drawing, in tile units (+Y up).</summary>
public readonly struct WorldOverlay(IRenderSurface surface)
{
    public void FillRect(Vector2 min, Vector2 size, Vector4 color) =>
        surface.SubmitQuad(RectTransform(min, size), color);

    public void FillTile(int x, int y, Vector4 color) =>
        FillRect(new Vector2(x, y), Vector2.One, color);

    /// <summary>Outline drawn inside the tile's edges.</summary>
    public void OutlineTile(int x, int y, float thickness, Vector4 color)
    {
        var t = thickness;
        FillRect(new Vector2(x, y), new Vector2(1f, t), color);
        FillRect(new Vector2(x, y + 1f - t), new Vector2(1f, t), color);
        FillRect(new Vector2(x, y + t), new Vector2(t, 1f - 2f * t), color);
        FillRect(new Vector2(x + 1f - t, y + t), new Vector2(t, 1f - 2f * t), color);
    }

    public void Line(Vector2 from, Vector2 to, float thickness, Vector4 color) =>
        surface.SubmitLine(from, to, thickness, color);

    internal static Matrix4x4 RectTransform(Vector2 min, Vector2 size) =>
        Matrix4x4.CreateScale(size.X, size.Y, 1f)
        * Matrix4x4.CreateTranslation(min.X + size.X / 2f, min.Y + size.Y / 2f, 0f);
}

/// <summary>Drawing in logical UI pixels: origin top-left, +Y down, 1280x720.</summary>
public readonly struct ScreenCanvas(IRenderSurface surface, ITextRenderSurface text)
{
    public void FillRect(Vector2 topLeft, Vector2 size, Vector4 color) =>
        surface.SubmitQuad(WorldOverlay.RectTransform(topLeft, size), color);

    /// <summary>Draws text with its top-left corner at <paramref name="topLeft"/>.</summary>
    public void Text(string content, Vector2 topLeft, FontHandle font, int fontSize, Color color)
    {
        // Glyph layout is Y-up from the baseline; flip into the Y-down UI space (the projection
        // flips it back, so the text reads upright) and drop the baseline below the top edge.
        var baseline = topLeft.Y + fontSize * 0.8f;
        text.DrawText(
            content,
            Matrix4x4.CreateScale(1f, -1f, 1f)
                * Matrix4x4.CreateTranslation(topLeft.X, baseline, 0f),
            font,
            fontSize,
            color
        );
    }
}
