using System.Numerics;
using Firewall.Web.Input;
using Firewall.Web.Rendering;
using Firewall.Web.Ui;

namespace Firewall.Web.Playback;

/// <summary>
/// Moves the camera for playback, but only when the subject is not already well inside the view,
/// so an action near the middle of the screen never makes the camera twitch.
/// </summary>
public sealed class CameraDirector(
    TacticalCamera camera,
    TacticalCameraController controller,
    Func<Vector2> canvasSize
) : ICameraDirector
{
    /// <summary>A point is "well inside" within this fraction of the half-extent of the view.</summary>
    public const float InnerFraction = 0.7f;

    /// <summary>True once playback has moved the camera, so the scene knows to bring it back.</summary>
    public bool Moved { get; private set; }

    public void Focus(Vector2 world, float seconds = 0.35f)
    {
        if (IsWellInside(world))
            return;
        MoveTo(world, seconds);
    }

    public void Frame(Vector2 a, Vector2 b, float seconds = 0.35f)
    {
        if (IsWellInside(a) && IsWellInside(b))
            return;
        MoveTo((a + b) / 2f, seconds);
    }

    /// <summary>Forget that playback moved the camera, e.g. because the scene has just re-aimed it.</summary>
    public void Cancel() => Moved = false;

    /// <summary>Eases back to <paramref name="world"/> after playback, if playback moved the camera and it is off-view.</summary>
    public void Return(Vector2 world, float seconds = 0.35f)
    {
        if (!Moved)
            return;
        Moved = false;
        if (!IsWellInside(world))
            controller.FocusOn(FocusPoint(world), seconds);
    }

    public bool IsWellInside(Vector2 world)
    {
        var size = canvasSize();
        return IsWellInside(
            camera.Center,
            camera.TilesVisibleVertically,
            size.X / MathF.Max(size.Y, 1f),
            world,
            InnerFraction
        );
    }

    /// <summary>Is <paramref name="point"/> within <paramref name="fraction"/> of the view's half-size around <paramref name="center"/>?</summary>
    public static bool IsWellInside(
        Vector2 center,
        float tilesVisibleVertically,
        float aspect,
        Vector2 point,
        float fraction
    )
    {
        var halfHeight = tilesVisibleVertically / 2f * fraction;
        var halfWidth = halfHeight * aspect;
        return MathF.Abs(point.X - center.X) <= halfWidth
            && MathF.Abs(point.Y - center.Y) <= halfHeight;
    }

    private void MoveTo(Vector2 world, float seconds)
    {
        Moved = true;
        controller.FocusOn(FocusPoint(world), seconds);
    }

    private Vector2 FocusPoint(Vector2 world) =>
        HudModel.FocusPoint(world, camera.TilesVisibleVertically);
}
