using System.Numerics;
using Firewall.Web.Rendering;
using Yaeger.ECS;
using Yaeger.Graphics;
using Yaeger.Platform;
using Yaeger.Systems;

namespace Firewall.Web.Tests;

public class RenderingTests
{
    private sealed class RecordingSurface : IRenderSurface
    {
        public List<string> Textures { get; } = [];
        public List<Matrix4x4> Cameras { get; } = [];

        public void BeginFrame() { }

        public void EndFrame() { }

        public void FlushQueuedQuads() { }

        public void SetCamera(Matrix4x4 viewProjection) => Cameras.Add(viewProjection);

        public void SubmitQuad(Matrix4x4 transform, string texturePath, Vector4 color) =>
            Textures.Add(texturePath);

        public void SubmitQuad(
            Matrix4x4 transform,
            string texturePath,
            Vector2 uvMin,
            Vector2 uvMax,
            Vector4 color
        ) => Textures.Add(texturePath);

        public Vector2 GetTextureSize(string path) =>
            path == "tiles.png" ? new Vector2(1728, 1280) : Vector2.Zero;
    }

    private sealed record Viewport(Vector2 Size) : IViewport
    {
        public float PixelRatio => 1f;
    }

    private static Vector2 ToNdc(Matrix4x4 m, Vector2 p)
    {
        var v = Vector4.Transform(new Vector4(p, 0f, 1f), m);
        return new Vector2(v.X / v.W, v.Y / v.W);
    }

    [Fact]
    public void LayersAreStrictlyAscending()
    {
        var layers = RenderLayers.InOrder;
        for (var i = 1; i < layers.Count; i++)
            Assert.True(layers[i - 1] < layers[i]);
    }

    [Fact]
    public void LowerLayersAreSubmittedBeforeHigherOnes()
    {
        var world = new World();
        var surface = new RecordingSurface();
        // Create entities in reverse layer order so Entity.Id can't explain the result.
        foreach (var layer in RenderLayers.InOrder.Reverse())
        {
            var e = world.CreateEntity();
            world.AddComponent(e, new Transform2D(Vector2.Zero));
            world.AddComponent(e, new Sprite($"layer{layer}"));
            world.AddComponent(e, RenderLayers.Of(layer));
        }

        new UnifiedRenderSystem(surface, null, world, new Viewport(new(800, 600))).Render();

        Assert.Equal(RenderLayers.InOrder.Select(l => $"layer{l}"), surface.Textures);
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(2560, 1440)]
    [InlineData(1024, 768)]
    [InlineData(800, 1200)]
    public void UiMatrixMapsLogicalCornersInsideTheLetterbox(float w, float h)
    {
        var ui = new UiSpace(new Vector2(w, h));
        var m = ui.ViewProjection();
        var topLeft = ToNdc(m, Vector2.Zero);
        var bottomRight = ToNdc(m, UiSpace.LogicalSize);

        // Symmetric margins, uniform scale, and the UI is exactly as wide/tall as scale says.
        Assert.Equal(-bottomRight.X, topLeft.X, 1e-4f);
        Assert.Equal(-bottomRight.Y, topLeft.Y, 1e-4f);
        Assert.Equal(1f, MathF.Max(MathF.Abs(topLeft.X), MathF.Abs(topLeft.Y)), 1e-4f);
        Assert.True(topLeft.X <= -1f + 1e-4f || topLeft.Y >= 1f - 1e-4f);
        Assert.True(topLeft.Y > 0f && bottomRight.Y < 0f); // +Y down
    }

    [Fact]
    public void UiMatrixFillsViewportAtSixteenByNine()
    {
        var m = new UiSpace(new Vector2(1920, 1080)).ViewProjection();
        Assert.Equal(new Vector2(-1f, 1f), ToNdc(m, Vector2.Zero));
        var br = ToNdc(m, UiSpace.LogicalSize);
        Assert.Equal(1f, br.X, 1e-5f);
        Assert.Equal(-1f, br.Y, 1e-5f);
    }

    [Fact]
    public void UiMatrixLetterboxesFourByThree()
    {
        // 1024x768: scale 0.8, UI is 1024x576, with 96 px bars above and below.
        var ui = new UiSpace(new Vector2(1024, 768));
        Assert.Equal(0.8f, ui.Scale, 1e-5f);
        Assert.Equal(new Vector2(0, 96), ui.Offset);

        var m = ui.ViewProjection();
        var tl = ToNdc(m, Vector2.Zero);
        var br = ToNdc(m, UiSpace.LogicalSize);
        Assert.Equal(-1f, tl.X, 1e-5f);
        Assert.Equal(0.75f, tl.Y, 1e-5f);
        Assert.Equal(1f, br.X, 1e-5f);
        Assert.Equal(-0.75f, br.Y, 1e-5f);
    }

    [Fact]
    public void CanvasPixelsRoundTripThroughUiSpace()
    {
        var ui = new UiSpace(new Vector2(1024, 768));
        Assert.Equal(Vector2.Zero, ui.FromCanvasPixels(new Vector2(0, 96)));
        var p = new Vector2(317f, 402f);
        var back = ui.ToCanvasPixels(ui.FromCanvasPixels(p));
        Assert.Equal(p.X, back.X, 1e-3f);
        Assert.Equal(p.Y, back.Y, 1e-3f);
    }

    [Fact]
    public void ZoomShowsTheRequestedTilesVerticallyAtAnyAspect()
    {
        var camera = new TacticalCamera { Center = new Vector2(10f, 10f) };
        Assert.Equal(11f, camera.TilesVisibleVertically);
        Assert.Equal(2f / 11f, camera.Zoom, 1e-6f);

        foreach (var aspect in new[] { 16f / 9f, 4f / 3f })
        {
            var vp = camera.ToCamera2D().ViewProjection(aspect);
            // Half the visible height above the centre is the top edge of the screen.
            Assert.Equal(1f, ToNdc(vp, new Vector2(10f, 15.5f)).Y, 1e-5f);
            Assert.Equal(-1f, ToNdc(vp, new Vector2(10f, 4.5f)).Y, 1e-5f);
            // One tile is the same size in both axes: no stretching.
            var dx = ToNdc(vp, new Vector2(11f, 10f)).X - ToNdc(vp, new Vector2(10f, 10f)).X;
            var dy = ToNdc(vp, new Vector2(10f, 11f)).Y - ToNdc(vp, new Vector2(10f, 10f)).Y;
            Assert.Equal(dy, dx * aspect, 1e-5f);
        }
    }

    [Fact]
    public void CanvasToWorldInvertsTheCamera()
    {
        var camera = new TacticalCamera { Center = new Vector2(3f, 4f) };
        var size = new Vector2(1600, 800);
        Assert.Equal(camera.Center, camera.CanvasToWorld(size / 2f, size));
        var topLeft = camera.CanvasToWorld(Vector2.Zero, size);
        Assert.Equal(3f - 5.5f * 2f, topLeft.X, 1e-4f);
        Assert.Equal(4f + 5.5f, topLeft.Y, 1e-4f);
    }

    [Fact]
    public void FrameRendererDrawsWorldOverlayBeforeScreenPassUnderTheUiCamera()
    {
        var world = new World();
        var cam = world.CreateEntity();
        world.AddComponent(cam, new Camera2D(Vector2.Zero));
        var surface = new RecordingSurface();
        var viewport = new Viewport(new(1280, 720));
        var frame = new FrameRenderer(
            surface,
            new NoText(),
            new UnifiedRenderSystem(surface, null, world, viewport),
            viewport
        )
        {
            WorldPass = o => o.FillTile(0, 0, Vector4.One),
            ScreenPass = c => c.FillRect(Vector2.Zero, Vector2.One, Vector4.One),
        };

        frame.Render();

        Assert.Equal(2, surface.Textures.Count);
        Assert.Equal(2, surface.Cameras.Count); // world camera, then UI camera
        Assert.Equal(new UiSpace(viewport.Size).ViewProjection(), surface.Cameras[1]);
    }

    private sealed class NoText : ITextRenderSurface
    {
        public void DrawText(
            string text,
            Matrix4x4 transform,
            FontHandle font,
            int fontSize,
            Color color
        ) { }

        public void DrawText(
            string text,
            Matrix4x4 transform,
            IFontHandle font,
            int fontSize,
            Color color
        ) { }
    }

    [Fact]
    public void StatsSurfaceCountsBatchesByTextureRunsAndFlushes()
    {
        var stats = new RenderStatsSurface(new RecordingSurface());
        stats.BeginFrame();
        foreach (var t in new[] { "a", "a", "b", "b", "a" })
            stats.SubmitQuad(Matrix4x4.Identity, t, Vector4.One);
        stats.FlushQueuedQuads();
        stats.SubmitQuad(Matrix4x4.Identity, "a", Vector4.One);
        stats.EndFrame();
        stats.BeginFrame();

        Assert.Equal(6, stats.LastFrameQuads);
        Assert.Equal(4, stats.LastFrameDrawBatches);
    }

    [Fact]
    public void StatsSurfaceForwardsTextureSize()
    {
        IRenderSurface stats = new RenderStatsSurface(new RecordingSurface());

        Assert.Equal(new Vector2(1728, 1280), stats.GetTextureSize("tiles.png"));
        Assert.Equal(Vector2.Zero, stats.GetTextureSize("unknown.png"));
    }
}
