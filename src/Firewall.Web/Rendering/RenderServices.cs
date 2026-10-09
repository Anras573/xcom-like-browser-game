using System.Numerics;
using Yaeger.ECS;
using Yaeger.Graphics;
using Yaeger.Platform;
using Yaeger.Systems;

namespace Firewall.Web.Rendering;

/// <summary>
/// The shared render stack (surface, text, stats) from which each scene builds a
/// <see cref="FrameRenderer"/> over its own <see cref="World"/>.
/// </summary>
public sealed class RenderServices(
    IViewport viewport,
    RenderStatsSurface stats,
    ITextRenderSurface text,
    TextServices textServices
)
{
    public RenderStatsSurface Stats => stats;

    /// <summary>Canvas size in CSS pixels.</summary>
    public Vector2 CanvasSize => viewport.Size;

    public FrameRenderer CreateFrame(World world) =>
        new(stats, text, new UnifiedRenderSystem(stats, text, world, viewport), viewport)
        {
            TextServices = textServices,
        };

    /// <summary>Full-screen black quad (the scene-transition fade), drawn over everything.</summary>
    public void DrawFade(float alpha)
    {
        var ui = new UiSpace(CanvasSize);
        stats.SetCamera(ui.ViewProjection());
        var all = ui.CanvasRect;
        new ScreenCanvas(stats, text, textServices).FillRect(
            all.Position,
            all.Size,
            new Vector4(0f, 0f, 0f, alpha)
        );
        stats.FlushQueuedQuads();
    }
}
