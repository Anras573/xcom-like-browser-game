using System.Numerics;

namespace Firewall.Web.Input;

/// <summary>Pure mouse to world to tile maths. 1 tile = 1 world unit, +Y up.</summary>
public static class Picking
{
    /// <summary>Canvas pixels (CSS, origin top-left) to NDC (+Y up).</summary>
    public static Vector2 CanvasToNdc(Vector2 canvasPixels, Vector2 canvasSize) =>
        new(
            canvasPixels.X / MathF.Max(canvasSize.X, 1f) * 2f - 1f,
            1f - canvasPixels.Y / MathF.Max(canvasSize.Y, 1f) * 2f
        );

    /// <summary>NDC to canvas pixels; the inverse of <see cref="CanvasToNdc"/>.</summary>
    public static Vector2 NdcToCanvas(Vector2 ndc, Vector2 canvasSize) =>
        new((ndc.X + 1f) / 2f * canvasSize.X, (1f - ndc.Y) / 2f * canvasSize.Y);

    /// <summary>World position under an NDC point, given the row-vector view-projection.</summary>
    /// <exception cref="InvalidOperationException">The matrix is singular.</exception>
    public static Vector2 ScreenToWorld(Vector2 ndc, Matrix4x4 viewProjection) =>
        Matrix4x4.Invert(viewProjection, out var inverse)
            ? Vector2.Transform(ndc, inverse)
            : throw new InvalidOperationException("View-projection is not invertible.");

    /// <summary>The tile containing a world position; tile <c>(x, y)</c> spans <c>[x, x+1) × [y, y+1)</c>.</summary>
    public static (int X, int Y) WorldToTile(Vector2 world) =>
        ((int)MathF.Floor(world.X), (int)MathF.Floor(world.Y));
}
