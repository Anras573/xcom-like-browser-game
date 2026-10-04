using System.Numerics;
using Firewall.Web.Assets;

namespace Firewall.Web.Tests;

public class UiUvTests
{
    [Fact]
    public void FullAtlasMapsToUnitSquare()
    {
        var (min, max) = UiUv.FromPixelRect(0, 0, 1024, 512, 1024, 512);
        Assert.Equal(Vector2.Zero, min);
        Assert.Equal(Vector2.One, max);
    }

    [Fact]
    public void TopLeftRectFlipsY()
    {
        // Top-left origin: a 64x32 rect at the top of a 256x128 atlas touches v = 1 and ends at 0.75.
        var (min, max) = UiUv.FromPixelRect(0, 0, 64, 32, 256, 128);
        Assert.Equal(new Vector2(0f, 0.75f), min);
        Assert.Equal(new Vector2(0.25f, 1f), max);
    }

    [Fact]
    public void BottomRightRectFlipsY()
    {
        var (min, max) = UiUv.FromPixelRect(192, 96, 64, 32, 256, 128);
        Assert.Equal(new Vector2(0.75f, 0f), min);
        Assert.Equal(new Vector2(1f, 0.25f), max);
    }

    [Fact]
    public void InteriorRectUsesBothOffsets()
    {
        var (min, max) = UiUv.FromPixelRect(100, 40, 50, 20, 400, 200);
        Assert.Equal(0.25f, min.X, 6);
        Assert.Equal(1f - 60f / 200f, min.Y, 6);
        Assert.Equal(150f / 400f, max.X, 6);
        Assert.Equal(1f - 40f / 200f, max.Y, 6);
    }
}
