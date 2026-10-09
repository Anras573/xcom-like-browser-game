using System.Numerics;
using Firewall.Web.Assets;
using Firewall.Web.Ui;

namespace Firewall.Web.Tests;

public class UiLayoutTests
{
    private static readonly UiRegion Panel = new(
        "ui.png",
        new Vector2(0f, 0.5f),
        new Vector2(0.25f, 1f),
        new UiInsets(16, 16, 16, 16),
        new Vector2(64, 64)
    );

    [Fact]
    public void NineSliceHasNineParts()
    {
        Assert.Equal(9, NineSlice.Compute(Panel, new UiRect(10, 20, 200, 100)).Count);
    }

    [Fact]
    public void CornersKeepPixelSizeAndCentreStretches()
    {
        var parts = NineSlice.Compute(Panel, new UiRect(10, 20, 200, 100));
        Assert.Equal(new UiRect(10, 20, 16, 16), parts[0].Rect);
        Assert.Equal(new UiRect(194, 104, 16, 16), parts[8].Rect);
        Assert.Equal(new UiRect(26, 36, 168, 68), parts[4].Rect);
    }

    [Fact]
    public void PartsTileTheTargetExactly()
    {
        var target = new UiRect(0, 0, 123, 77);
        var parts = NineSlice.Compute(Panel, target);
        Assert.Equal(target.W * target.H, parts.Sum(p => p.Rect.W * p.Rect.H), 3);
    }

    [Fact]
    public void TopLeftCornerSamplesTopLeftOfRegion()
    {
        var corner = NineSlice.Compute(Panel, new UiRect(0, 0, 200, 100))[0];
        // Region UV: u 0..0.25, v 0.5..1 (top is v = 1). A 16 px corner of 64 covers a quarter.
        Assert.Equal(new Vector2(0f, 0.875f), corner.UvMin);
        Assert.Equal(new Vector2(0.0625f, 1f), corner.UvMax);
    }

    [Fact]
    public void TinyTargetShrinksBordersInsteadOfOverlapping()
    {
        var target = new UiRect(0, 0, 20, 20);
        var parts = NineSlice.Compute(Panel, target);
        Assert.Equal(target.W * target.H, parts.Sum(p => p.Rect.W * p.Rect.H), 3);
    }

    [Fact]
    public void VisibleRangeCullsToWholeItemsInView()
    {
        // 50 items of 36 px in a 360 px view: 10 items at a time.
        Assert.Equal((0, 10), ScrollMath.Visible(0, 50, 36, 360));
        Assert.Equal((10, 20), ScrollMath.Visible(360, 50, 36, 360));
        // Mid-item offset: the two partly visible edge items are skipped.
        var (first, end) = ScrollMath.Visible(18, 50, 36, 360);
        Assert.Equal((1, 10), (first, end));
    }

    [Fact]
    public void ScrollOffsetClamps()
    {
        Assert.Equal(0f, ScrollMath.Clamp(-50, 50, 36, 360));
        Assert.Equal(50 * 36 - 360, ScrollMath.Clamp(99999, 50, 36, 360));
        Assert.Equal(0f, ScrollMath.Clamp(40, 5, 36, 360)); // everything fits
    }

    [Fact]
    public void EmptyListShowsNothing() => Assert.Equal((0, 0), ScrollMath.Visible(0, 0, 36, 360));
}

public class UiSpaceCanvasRectTests
{
    [Fact]
    public void CanvasRectCoversLetterboxBars()
    {
        var space = new Firewall.Web.Rendering.UiSpace(new Vector2(1000, 700));
        var r = space.CanvasRect;
        Assert.Equal(
            new Vector2(0, 0),
            space.ToCanvasPixels(r.Position) - new Vector2(0, 0),
            new Vector2Comparer()
        );
        Assert.Equal(1000f, space.ToCanvasPixels(new Vector2(r.Right, r.Bottom)).X, 2);
        Assert.Equal(700f, space.ToCanvasPixels(new Vector2(r.Right, r.Bottom)).Y, 2);
        Assert.True(r.Y < 0f); // the bars sit above and below the logical area
    }

    private sealed class Vector2Comparer : IEqualityComparer<Vector2>
    {
        public bool Equals(Vector2 a, Vector2 b) => Vector2.Distance(a, b) < 0.01f;

        public int GetHashCode(Vector2 v) => v.GetHashCode();
    }
}
