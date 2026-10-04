using System.Numerics;
using Yaeger.Platform;

namespace Firewall.Web.Rendering;

/// <summary>
/// Forwards to another <see cref="IRenderSurface"/> and counts quads and draw batches per frame.
/// The engine doesn't expose its draw-call count, so batches are derived with the same rule the
/// browser surface uses: consecutive quads with one texture share a batch, up to
/// <see cref="MaxQuadsPerBatch"/>, and a flush or camera change ends the run.
/// </summary>
public sealed class RenderStatsSurface(IRenderSurface inner) : IRenderSurface
{
    /// <summary>Mirrors <c>BrowserRenderSurface</c>'s batch capacity.</summary>
    public const int MaxQuadsPerBatch = 1000;

    private string? _runTexture;
    private int _runLength;
    private Matrix4x4? _camera;

    public int Quads { get; private set; }
    public int DrawBatches { get; private set; }

    /// <summary>Totals of the last completed frame (safe to display while drawing the next).</summary>
    public int LastFrameQuads { get; private set; }
    public int LastFrameDrawBatches { get; private set; }

    public void BeginFrame()
    {
        LastFrameQuads = Quads;
        LastFrameDrawBatches = DrawBatches;
        Quads = 0;
        DrawBatches = 0;
        EndRun();
        inner.BeginFrame();
    }

    public void EndFrame()
    {
        EndRun();
        inner.EndFrame();
    }

    public void FlushQueuedQuads()
    {
        EndRun();
        inner.FlushQueuedQuads();
    }

    public void SetCamera(Matrix4x4 viewProjection)
    {
        if (_camera != viewProjection)
            EndRun();
        _camera = viewProjection;
        inner.SetCamera(viewProjection);
    }

    public void SubmitQuad(Matrix4x4 transform, string texturePath, Vector4 color)
    {
        Count(texturePath);
        inner.SubmitQuad(transform, texturePath, color);
    }

    public void SubmitQuad(
        Matrix4x4 transform,
        string texturePath,
        Vector2 uvMin,
        Vector2 uvMax,
        Vector4 color
    )
    {
        Count(texturePath);
        inner.SubmitQuad(transform, texturePath, uvMin, uvMax, color);
    }

    private void Count(string texturePath)
    {
        Quads++;
        if (_runLength == 0 || _runTexture != texturePath || _runLength == MaxQuadsPerBatch)
        {
            DrawBatches++;
            _runTexture = texturePath;
            _runLength = 0;
        }
        _runLength++;
    }

    private void EndRun() => _runLength = 0;
}
