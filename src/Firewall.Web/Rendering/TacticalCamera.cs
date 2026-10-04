using System.Numerics;
using Yaeger.Graphics;

namespace Firewall.Web.Rendering;

/// <summary>
/// Camera in tile units: 1 tile = 1 world unit, tile <c>(x, y)</c> is centred on
/// <c>(x + 0.5, y + 0.5)</c> and +Y is up. Wraps <see cref="Camera2D"/>, whose zoom 1 shows
/// 2 world units vertically, so the zoom is derived from how many tiles should fit on screen.
/// </summary>
public sealed class TacticalCamera
{
    public const float DefaultTilesVisibleVertically = 11f;

    private float _tilesVisibleVertically = DefaultTilesVisibleVertically;

    /// <summary>World position at the centre of the screen.</summary>
    public Vector2 Center { get; set; }

    /// <summary>How many tiles fit top to bottom. Must be positive.</summary>
    public float TilesVisibleVertically
    {
        get => _tilesVisibleVertically;
        set
        {
            if (!(value > 0f) || !float.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "Must be positive.");
            _tilesVisibleVertically = value;
        }
    }

    public float Zoom => ZoomFor(TilesVisibleVertically);

    public Camera2D ToCamera2D() => new(Center, Zoom);

    /// <summary><see cref="Camera2D.Zoom"/> that shows <paramref name="tilesVisibleVertically"/> tiles.</summary>
    public static float ZoomFor(float tilesVisibleVertically) => 2f / tilesVisibleVertically;

    /// <summary>World position of the centre of tile <c>(x, y)</c>.</summary>
    public static Vector2 TileCenter(int x, int y) => new(x + 0.5f, y + 0.5f);

    /// <summary>The tile containing a world position.</summary>
    public static (int X, int Y) WorldToTile(Vector2 world) =>
        ((int)MathF.Floor(world.X), (int)MathF.Floor(world.Y));

    /// <summary>World position under a canvas pixel (CSS pixels, origin top-left).</summary>
    public Vector2 CanvasToWorld(Vector2 canvasPixels, Vector2 canvasSize)
    {
        var halfHeight = TilesVisibleVertically / 2f;
        var halfWidth = halfHeight * canvasSize.X / MathF.Max(canvasSize.Y, 1f);
        var ndcX = canvasPixels.X / MathF.Max(canvasSize.X, 1f) * 2f - 1f;
        var ndcY = 1f - canvasPixels.Y / MathF.Max(canvasSize.Y, 1f) * 2f;
        return Center + new Vector2(ndcX * halfWidth, ndcY * halfHeight);
    }
}
