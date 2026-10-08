using System.Numerics;
using Firewall.Web.Rendering;
using Yaeger.Graphics;
using Yaeger.Input;
using Yaeger.Platform;

namespace Firewall.Web.Input;

/// <summary>
/// XCOM-style camera: WASD/arrows, edge scroll, RMB-drag, smoothed wheel zoom toward the
/// cursor, map bounds and eased <see cref="FocusOn"/>. Call <see cref="Update"/> once per tick.
/// </summary>
public sealed class TacticalCameraController(
    TacticalCamera camera,
    IInputState input,
    InputBindings bindings
)
{
    public const float MinZoom = 0.5f;
    public const float MaxZoom = 2f;
    public const float EdgeScrollPixels = 12f;
    public const float DragThresholdPixels = 6f;
    public const float BoundsMarginTiles = 2f;

    /// <summary>Pan speed in tiles per second at the default zoom; scales with the visible span.</summary>
    public const float PanTilesPerSecond = 8f;

    private const float ZoomPerScrollPixel = 0.0015f;
    private const float ZoomSmoothingRate = 14f;

    private float _targetZoom = 1f;
    private bool _rmbDown;
    private bool _dragging;
    private Vector2 _rmbStart;
    private Vector2 _dragAnchorWorld;
    private bool _focusing;
    private Vector2 _focusFrom;
    private Vector2 _focusTo;
    private float _focusDuration;
    private float _focusElapsed;

    /// <summary>Map size in tiles; the camera is clamped to it plus <see cref="BoundsMarginTiles"/>.</summary>
    public Vector2 MapSize { get; set; }

    public bool EdgeScrollEnabled { get; set; } = true;

    /// <summary>Zoom factor relative to the default view: 0.5 shows twice the tiles, 2 half.</summary>
    public float ZoomFactor =>
        TacticalCamera.DefaultTilesVisibleVertically / camera.TilesVisibleVertically;

    public bool IsFocusing => _focusing;

    public bool IsDragging => _dragging;

    /// <summary>True for the frame an RMB press was released without dragging (a cancel click).</summary>
    public bool RightClicked { get; private set; }

    /// <summary>Sets the zoom target (and snaps to it); clamped to the allowed range.</summary>
    public void SetZoom(float factor)
    {
        _targetZoom = Math.Clamp(factor, MinZoom, MaxZoom);
        camera.TilesVisibleVertically = TacticalCamera.DefaultTilesVisibleVertically / _targetZoom;
    }

    /// <summary>Eases the camera centre to <paramref name="world"/>. Player panning cancels it.</summary>
    public void FocusOn(Vector2 world, float duration)
    {
        if (!(duration > 0f))
        {
            _focusing = false;
            camera.Center = world;
            return;
        }
        _focusing = true;
        _focusFrom = camera.Center;
        _focusTo = world;
        _focusDuration = duration;
        _focusElapsed = 0f;
    }

    public void Update(float deltaTime, Vector2 canvasSize)
    {
        RightClicked = false;
        var mouse = input.MousePosition;
        // MousePosition keeps its last value after the pointer leaves, so check the flag first.
        var inCanvas =
            input.IsMouseInside
            && mouse.X >= 0f
            && mouse.Y >= 0f
            && mouse.X < canvasSize.X
            && mouse.Y < canvasSize.Y;

        UpdateRightButton(mouse, canvasSize, inCanvas);
        var panned = _dragging;

        var pan = Vector2.Zero;
        if (bindings.IsDown(GameAction.PanLeft))
            pan.X -= 1f;
        if (bindings.IsDown(GameAction.PanRight))
            pan.X += 1f;
        if (bindings.IsDown(GameAction.PanUp))
            pan.Y += 1f;
        if (bindings.IsDown(GameAction.PanDown))
            pan.Y -= 1f;

        if (EdgeScrollEnabled && inCanvas && !_rmbDown)
        {
            if (mouse.X <= EdgeScrollPixels)
                pan.X -= 1f;
            if (mouse.X >= canvasSize.X - EdgeScrollPixels)
                pan.X += 1f;
            if (mouse.Y <= EdgeScrollPixels)
                pan.Y += 1f;
            if (mouse.Y >= canvasSize.Y - EdgeScrollPixels)
                pan.Y -= 1f;
        }

        if (pan != Vector2.Zero)
        {
            panned = true;
            _focusing = false;
            var speed =
                PanTilesPerSecond
                * camera.TilesVisibleVertically
                / TacticalCamera.DefaultTilesVisibleVertically;
            camera.Center += Vector2.Normalize(pan) * speed * deltaTime;
        }

        UpdateZoom(deltaTime, mouse, canvasSize, inCanvas);

        if (_focusing && !panned)
        {
            _focusElapsed += deltaTime;
            var t = Math.Clamp(_focusElapsed / _focusDuration, 0f, 1f);
            camera.Center = Vector2.Lerp(
                _focusFrom,
                _focusTo,
                Easing.Apply(EasingFunction.QuadInOut, t)
            );
            if (t >= 1f)
                _focusing = false;
        }

        ClampToBounds(canvasSize);
    }

    private void UpdateRightButton(Vector2 mouse, Vector2 canvasSize, bool inCanvas)
    {
        if (input.WasMouseButtonPressed(MouseButton.Right) && inCanvas)
        {
            _rmbDown = true;
            _dragging = false;
            _rmbStart = mouse;
            _dragAnchorWorld = camera.CanvasToWorld(mouse, canvasSize);
        }

        if (_rmbDown && !_dragging && Vector2.Distance(mouse, _rmbStart) > DragThresholdPixels)
        {
            _dragging = true;
            _focusing = false;
        }

        if (_dragging)
        {
            // Keep the grabbed world point under the cursor.
            camera.Center += _dragAnchorWorld - camera.CanvasToWorld(mouse, canvasSize);
        }

        if (_rmbDown && input.WasMouseButtonReleased(MouseButton.Right))
        {
            RightClicked = !_dragging;
            _rmbDown = false;
            _dragging = false;
        }
        else if (_rmbDown && !input.IsMouseButtonPressed(MouseButton.Right))
        {
            _rmbDown = false;
            _dragging = false;
        }
    }

    private void UpdateZoom(float deltaTime, Vector2 mouse, Vector2 canvasSize, bool inCanvas)
    {
        // Browser wheel: positive delta = scrolled down = zoom out.
        if (inCanvas && input.ScrollDelta != 0f)
            _targetZoom = Math.Clamp(
                _targetZoom * MathF.Exp(-input.ScrollDelta * ZoomPerScrollPixel),
                MinZoom,
                MaxZoom
            );
        if (bindings.IsDown(GameAction.ZoomIn))
            _targetZoom = Math.Clamp(_targetZoom * (1f + 1.5f * deltaTime), MinZoom, MaxZoom);
        if (bindings.IsDown(GameAction.ZoomOut))
            _targetZoom = Math.Clamp(_targetZoom / (1f + 1.5f * deltaTime), MinZoom, MaxZoom);

        var current = ZoomFactor;
        if (MathF.Abs(current - _targetZoom) < 1e-4f)
            return;

        // Exponential approach, framerate independent; snap once close.
        var next =
            _targetZoom + (current - _targetZoom) * MathF.Exp(-ZoomSmoothingRate * deltaTime);
        if (MathF.Abs(next - _targetZoom) < 1e-3f)
            next = _targetZoom;

        // Zoom toward the cursor (or the screen centre if it is outside the canvas).
        var anchor = inCanvas ? mouse : canvasSize / 2f;
        var before = camera.CanvasToWorld(anchor, canvasSize);
        camera.TilesVisibleVertically = TacticalCamera.DefaultTilesVisibleVertically / next;
        camera.Center += before - camera.CanvasToWorld(anchor, canvasSize);
    }

    private void ClampToBounds(Vector2 canvasSize)
    {
        var halfHeight = camera.TilesVisibleVertically / 2f;
        var halfWidth = halfHeight * canvasSize.X / MathF.Max(canvasSize.Y, 1f);
        camera.Center = new Vector2(
            ClampAxis(camera.Center.X, halfWidth, MapSize.X),
            ClampAxis(camera.Center.Y, halfHeight, MapSize.Y)
        );
    }

    // Keep the view inside [-margin, size + margin]; centre it when the view is wider than that.
    private static float ClampAxis(float center, float half, float mapSize)
    {
        var min = -BoundsMarginTiles + half;
        var max = mapSize + BoundsMarginTiles - half;
        return min > max ? mapSize / 2f : Math.Clamp(center, min, max);
    }
}
