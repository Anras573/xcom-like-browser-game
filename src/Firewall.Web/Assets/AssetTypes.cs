using System.Numerics;
using Yaeger.Graphics;

namespace Firewall.Web.Assets;

/// <summary>A world-space sprite: one cell of a uniform-grid texture.</summary>
/// <param name="PivotOffset">Body centre relative to the cell centre, in pixels (usually zero).</param>
public readonly record struct SpriteFrame(
    string TexturePath,
    int Columns,
    int Rows,
    int FrameIndex,
    Vector2 PivotOffset = default
)
{
    /// <summary>The engine sheet to put on an entity next to <c>AnimationState(FrameIndex)</c>.</summary>
    public SpriteSheet ToSpriteSheet(Color? tint = null) =>
        new(TexturePath, Columns, Rows, tint: tint);
}

/// <summary>9-slice borders in pixels.</summary>
public readonly record struct UiInsets(int Left, int Top, int Right, int Bottom);

/// <summary>A screen-space UI region: texture plus UVs ready for <c>IRenderSurface.SubmitQuad</c>.</summary>
public readonly record struct UiRegion(
    string TexturePath,
    Vector2 UvMin,
    Vector2 UvMax,
    UiInsets Insets
);

public static class UiUv
{
    /// <summary>
    /// Converts a top-left-origin pixel rect to UVs. <c>yaeger-browser.js</c> uploads textures
    /// Y-flipped, so v runs bottom-up: <c>v0 = 1 - (y + h) / H</c>, <c>v1 = 1 - y / H</c>.
    /// </summary>
    public static (Vector2 UvMin, Vector2 UvMax) FromPixelRect(
        int x,
        int y,
        int width,
        int height,
        int atlasWidth,
        int atlasHeight
    ) =>
        (
            new Vector2((float)x / atlasWidth, 1f - (float)(y + height) / atlasHeight),
            new Vector2((float)(x + width) / atlasWidth, 1f - (float)y / atlasHeight)
        );
}
