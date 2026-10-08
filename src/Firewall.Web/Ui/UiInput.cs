using System.Numerics;
using Firewall.Web.Input;
using Firewall.Web.Rendering;
using Yaeger.Input;
using Yaeger.Platform;

namespace Firewall.Web.Ui;

/// <summary>What a widget reports back for the frame.</summary>
/// <param name="Hover">Pointer is over it (and nothing blocks it).</param>
/// <param name="Down">Pressed on it and still held over it.</param>
/// <param name="Clicked">Pressed inside, then released inside, this frame.</param>
public readonly record struct WidgetState(bool Hover, bool Down, bool Clicked);

/// <summary>
/// Pointer interaction in logical UI pixels. Hit-testing maps the mouse through
/// <see cref="UiSpace"/> (the same transform the screen pass draws with), so widgets line up with
/// what the player sees under letterboxing. A click is "pressed inside, released inside": the press
/// arms the widget, the release fires it only if the pointer is still over it.
/// </summary>
public sealed class UiInput(IInputState input, ClickGate clicks)
{
    private const float Offscreen = -1e6f;
    private UiSpace _space = new(UiSpace.LogicalSize);
    private int _active;

    /// <summary>Pointer in logical UI pixels; far off-screen while it is outside the canvas.</summary>
    public Vector2 Mouse { get; private set; } = new(Offscreen, Offscreen);

    public float ScrollDelta => input.ScrollDelta;

    public bool LeftPressed => input.WasMouseButtonPressed(MouseButton.Left);

    public void Begin(UiSpace space)
    {
        _space = space;
        Mouse = input.IsMouseInside
            ? space.FromCanvasPixels(input.MousePosition)
            : new Vector2(Offscreen, Offscreen);
    }

    /// <summary>Call once at the end of the frame: a release anywhere disarms whatever was pressed.</summary>
    public void End()
    {
        if (input.WasMouseButtonReleased(MouseButton.Left))
            _active = 0;
    }

    public bool IsHover(UiRect rect) => rect.Contains(Mouse);

    /// <summary>Claims this frame's click if the pointer is over <paramref name="rect"/>, so the world doesn't get it.</summary>
    public void Swallow(UiRect rect)
    {
        if (LeftPressed && IsHover(rect))
            clicks.ConsumeClick();
    }

    /// <param name="id">Stable widget id; widgets drawn at the same place with the same label share one.</param>
    /// <param name="enabled">Disabled widgets still swallow the click but never fire.</param>
    /// <param name="blocked">Something above (a modal) takes the pointer; the widget is inert.</param>
    public WidgetState Interact(int id, UiRect rect, bool enabled = true, bool blocked = false)
    {
        if (blocked)
        {
            if (_active == id)
                _active = 0;
            return default;
        }

        var hover = IsHover(rect);
        if (hover && LeftPressed)
        {
            clicks.ConsumeClick();
            if (enabled)
                _active = id;
        }

        var clicked = false;
        if (_active == id && input.WasMouseButtonReleased(MouseButton.Left))
        {
            clicked = hover && enabled;
            _active = 0;
        }

        var down = _active == id && hover && input.IsMouseButtonPressed(MouseButton.Left);
        return new WidgetState(hover, down, clicked);
    }
}
