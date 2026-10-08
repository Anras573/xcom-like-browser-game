using System.Numerics;
using Firewall.Web.Input;
using Firewall.Web.Rendering;
using Firewall.Web.Ui;
using Yaeger.Input;
using Yaeger.Platform;

namespace Firewall.Web.Tests;

public class UiInputTests
{
    private static readonly UiRect Rect = new(100, 100, 200, 50);
    private static readonly Vector2 Inside = new(150, 120);
    private static readonly Vector2 Outside = new(600, 600);

    // Canvas exactly 1280x720 so logical == canvas pixels.
    private readonly FakeInput _input = new();
    private readonly ClickGate _gate;
    private readonly UiInput _ui;

    public UiInputTests()
    {
        _gate = new ClickGate(_input);
        _ui = new UiInput(_input, _gate);
    }

    private WidgetState Frame(Vector2 mouse, bool enabled = true, bool blocked = false)
    {
        _input.MousePosition = mouse;
        _input.NextFrame();
        _gate.BeginFrame();
        _ui.Begin(new UiSpace(UiSpace.LogicalSize));
        var state = _ui.Interact(1, Rect, enabled, blocked);
        _ui.End();
        return state;
    }

    [Fact]
    public void PressAndReleaseInsideClicks()
    {
        _input.PressButton(MouseButton.Left);
        Assert.False(Frame(Inside).Clicked);
        _input.ReleaseButton(MouseButton.Left);
        Assert.True(Frame(Inside).Clicked);
    }

    [Fact]
    public void PressInsideReleaseOutsideDoesNotClick()
    {
        _input.PressButton(MouseButton.Left);
        Frame(Inside);
        _input.ReleaseButton(MouseButton.Left);
        Assert.False(Frame(Outside).Clicked);
        // And the armed state is gone: a later release inside is not a click either.
        _input.ReleaseButton(MouseButton.Left);
        Assert.False(Frame(Inside).Clicked);
    }

    [Fact]
    public void PressOutsideDragInsideReleaseDoesNotClick()
    {
        _input.PressButton(MouseButton.Left);
        Frame(Outside);
        _input.ReleaseButton(MouseButton.Left);
        Assert.False(Frame(Inside).Clicked);
    }

    [Fact]
    public void PressAndReleaseInOneFrameClicks()
    {
        _input.PressButton(MouseButton.Left).ReleaseButton(MouseButton.Left);
        Assert.True(Frame(Inside).Clicked);
    }

    [Fact]
    public void DownWhileHeldInsideAndNotWhenDraggedOut()
    {
        _input.PressButton(MouseButton.Left);
        Assert.True(Frame(Inside).Down);
        Assert.False(Frame(Outside).Down);
        Assert.True(Frame(Inside).Down);
    }

    [Fact]
    public void DisabledNeverClicksButStillSwallowsTheClick()
    {
        _input.PressButton(MouseButton.Left);
        Frame(Inside, enabled: false);
        Assert.False(_gate.ClickAvailable);
        _input.ReleaseButton(MouseButton.Left);
        Assert.False(Frame(Inside, enabled: false).Clicked);
    }

    [Fact]
    public void ClickOnUiDoesNotFallThroughToTheWorld()
    {
        _input.PressButton(MouseButton.Left);
        Frame(Inside);
        Assert.False(_gate.ConsumeClick());
    }

    [Fact]
    public void ClickOutsideUiStillReachesTheWorld()
    {
        _input.PressButton(MouseButton.Left);
        Frame(Outside);
        Assert.True(_gate.ConsumeClick());
    }

    [Fact]
    public void BlockedWidgetIsInert()
    {
        _input.PressButton(MouseButton.Left).ReleaseButton(MouseButton.Left);
        var state = Frame(Inside, blocked: true);
        Assert.False(state.Hover);
        Assert.False(state.Clicked);
    }

    [Fact]
    public void MouseIsMappedThroughLetterboxing()
    {
        // 2560x720 canvas: scale 1, logical origin is centred at x = 640.
        var space = new UiSpace(new Vector2(2560, 720));
        _input.MousePosition = new Vector2(640 + 150, 120);
        _input.NextFrame();
        _ui.Begin(space);
        Assert.Equal(new Vector2(150, 120), _ui.Mouse);
    }

    [Fact]
    public void MouseOutsideCanvasHoversNothing()
    {
        _input.MousePosition = Inside;
        _input.IsMouseInside = false;
        _input.NextFrame();
        _ui.Begin(new UiSpace(UiSpace.LogicalSize));
        Assert.False(_ui.IsHover(Rect));
    }
}
