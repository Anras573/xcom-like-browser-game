using System.Numerics;

namespace Firewall.Web.Rendering;

/// <summary>
/// The logical 1280x720 UI resolution fitted into a canvas: uniformly scaled by
/// <c>min(w / 1280, h / 720)</c> and centred (letterboxed). Origin top-left, +Y down.
/// </summary>
/// <param name="CanvasSize">Canvas size in CSS pixels (<c>IViewport.Size</c>).</param>
public readonly record struct UiSpace(Vector2 CanvasSize)
{
    public const float LogicalWidth = 1280f;
    public const float LogicalHeight = 720f;

    public static Vector2 LogicalSize => new(LogicalWidth, LogicalHeight);

    /// <summary>Canvas pixels per logical UI pixel.</summary>
    public float Scale =>
        MathF.Min(CanvasSize.X / LogicalWidth, CanvasSize.Y / LogicalHeight) is var s and > 0f
            ? s
            : 1f;

    /// <summary>Canvas position of the logical (0, 0): the letterbox margin.</summary>
    public Vector2 Offset => (CanvasSize - LogicalSize * Scale) / 2f;

    /// <summary>Mouse position (CSS pixels) to logical UI pixels.</summary>
    public Vector2 FromCanvasPixels(Vector2 canvas) => (canvas - Offset) / Scale;

    /// <summary>Logical UI pixels to canvas pixels (CSS pixels).</summary>
    public Vector2 ToCanvasPixels(Vector2 logical) => logical * Scale + Offset;

    /// <summary>View-projection mapping logical UI pixels to NDC (row-vector convention).</summary>
    public Matrix4x4 ViewProjection()
    {
        var w = MathF.Max(CanvasSize.X, 1f);
        var h = MathF.Max(CanvasSize.Y, 1f);
        var s = Scale;
        var o = Offset;
        return Matrix4x4.CreateScale(2f * s / w, -2f * s / h, 1f)
            * Matrix4x4.CreateTranslation(2f * o.X / w - 1f, 1f - 2f * o.Y / h, 0f);
    }
}
