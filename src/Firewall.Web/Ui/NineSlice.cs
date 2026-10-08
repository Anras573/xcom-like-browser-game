using System.Numerics;
using Firewall.Web.Assets;

namespace Firewall.Web.Ui;

/// <summary>One cell of a 9-slice: where it lands on screen and which part of the region it samples.</summary>
public readonly record struct SlicePart(UiRect Rect, Vector2 UvMin, Vector2 UvMax);

public static class NineSlice
{
    /// <summary>
    /// Splits <paramref name="target"/> into up to nine parts. Corners keep their pixel size (shrunk
    /// proportionally if the target is smaller than the borders), edges and centre stretch. Zero-sized
    /// parts are omitted. UV v runs bottom-up, so pixel row 0 is at <c>UvMax.Y</c>.
    /// </summary>
    public static List<SlicePart> Compute(UiRegion region, UiRect target)
    {
        var size = region.PixelSize;
        var ins = region.Insets;
        var parts = new List<SlicePart>(9);
        if (size.X <= 0f || size.Y <= 0f || target.W <= 0f || target.H <= 0f)
            return parts;

        float l = ins.Left,
            r = ins.Right,
            t = ins.Top,
            b = ins.Bottom;
        var kx = l + r > target.W ? target.W / (l + r) : 1f;
        var ky = t + b > target.H ? target.H / (t + b) : 1f;

        // Source pixel edges and destination edges along each axis.
        float[] sx = [0f, l, size.X - r, size.X];
        float[] sy = [0f, t, size.Y - b, size.Y];
        float[] dx = [target.X, target.X + l * kx, target.Right - r * kx, target.Right];
        float[] dy = [target.Y, target.Y + t * ky, target.Bottom - b * ky, target.Bottom];

        var uvSpan = region.UvMax - region.UvMin;
        for (var row = 0; row < 3; row++)
        for (var col = 0; col < 3; col++)
        {
            var rect = new UiRect(dx[col], dy[row], dx[col + 1] - dx[col], dy[row + 1] - dy[row]);
            if (rect.W <= 0f || rect.H <= 0f || sx[col + 1] <= sx[col] || sy[row + 1] <= sy[row])
                continue;
            var uvMin = new Vector2(
                region.UvMin.X + sx[col] / size.X * uvSpan.X,
                region.UvMax.Y - sy[row + 1] / size.Y * uvSpan.Y
            );
            var uvMax = new Vector2(
                region.UvMin.X + sx[col + 1] / size.X * uvSpan.X,
                region.UvMax.Y - sy[row] / size.Y * uvSpan.Y
            );
            parts.Add(new SlicePart(rect, uvMin, uvMax));
        }
        return parts;
    }
}
