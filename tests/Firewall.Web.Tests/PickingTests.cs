using System.Numerics;
using Firewall.Web.Input;
using Firewall.Web.Rendering;
using Yaeger.Graphics;

namespace Firewall.Web.Tests;

public class PickingTests
{
    public static TheoryData<float, float, float, float, float> Views =>
        new()
        {
            // centreX, centreY, tilesVisible, width, height
            { 10f, 10f, 11f, 1280f, 720f },
            { 0f, 0f, 22f, 800f, 600f },
            { 17.5f, 3.25f, 5.5f, 1920f, 1080f },
            { -2f, 21f, 22f, 390f, 844f },
            { 7.3f, 12.9f, 11f, 3440f, 1440f },
        };

    [Theory]
    [MemberData(nameof(Views))]
    public void CanvasToWorldAndBack_RoundTrips(float cx, float cy, float tiles, float w, float h)
    {
        var camera = new TacticalCamera
        {
            Center = new Vector2(cx, cy),
            TilesVisibleVertically = tiles,
        };
        var size = new Vector2(w, h);
        var vp = camera.ToCamera2D().ViewProjection(w / h);

        foreach (
            var px in new[]
            {
                new Vector2(0, 0),
                new Vector2(w / 2, h / 2),
                new Vector2(w - 1, h - 1),
                new Vector2(123.5f, 77.25f),
            }
        )
        {
            var world = Picking.ScreenToWorld(Picking.CanvasToNdc(px, size), vp);
            var back = Picking.NdcToCanvas(Vector2.Transform(world, vp), size);
            Assert.InRange(Vector2.Distance(px, back), 0f, 0.05f);
        }
    }

    [Theory]
    [MemberData(nameof(Views))]
    public void ScreenToWorld_AgreesWithCameraCanvasToWorld(
        float cx,
        float cy,
        float tiles,
        float w,
        float h
    )
    {
        var camera = new TacticalCamera
        {
            Center = new Vector2(cx, cy),
            TilesVisibleVertically = tiles,
        };
        var size = new Vector2(w, h);
        var vp = camera.ToCamera2D().ViewProjection(w / h);
        var px = new Vector2(w * 0.31f, h * 0.77f);

        var world = Picking.ScreenToWorld(Picking.CanvasToNdc(px, size), vp);

        var expected = camera.CanvasToWorld(px, size);
        Assert.InRange(Vector2.Distance(world, expected), 0f, 1e-3f);
    }

    [Fact]
    public void ScreenCentre_IsCameraCentre()
    {
        var camera = new TacticalCamera { Center = new Vector2(4.5f, 6.5f) };
        var vp = camera.ToCamera2D().ViewProjection(16f / 9f);

        var world = Picking.ScreenToWorld(Vector2.Zero, vp);

        Assert.InRange(Vector2.Distance(world, camera.Center), 0f, 1e-4f);
    }

    [Theory]
    [InlineData(0.0f, 0.0f, 0, 0)]
    [InlineData(0.99f, 0.99f, 0, 0)]
    [InlineData(1.0f, 1.0f, 1, 1)]
    [InlineData(-0.01f, 2.5f, -1, 2)]
    [InlineData(19.999f, -1.5f, 19, -2)]
    public void WorldToTile_FloorsToTile(float x, float y, int tx, int ty)
    {
        Assert.Equal((tx, ty), Picking.WorldToTile(new Vector2(x, y)));
    }

    [Fact]
    public void PixelOnTileCentre_PicksThatTile()
    {
        var camera = new TacticalCamera
        {
            Center = new Vector2(10f, 10f),
            TilesVisibleVertically = 5.5f,
        };
        var size = new Vector2(1000f, 600f);
        var vp = camera.ToCamera2D().ViewProjection(size.X / size.Y);
        var centreNdc = Vector2.Transform(TacticalCamera.TileCenter(11, 9), vp);

        var world = Picking.ScreenToWorld(centreNdc, vp);

        Assert.Equal((11, 9), Picking.WorldToTile(world));
    }

    [Fact]
    public void ScreenToWorld_SingularMatrix_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Picking.ScreenToWorld(Vector2.Zero, default)
        );
    }
}
