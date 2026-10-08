using System.Numerics;
using Firewall.Web.Input;
using Firewall.Web.Rendering;
using Yaeger.Input;

namespace Firewall.Web.Tests;

public class TacticalCameraControllerTests
{
    private static readonly Vector2 Canvas = new(1280f, 720f);
    private static readonly Vector2 Middle = Canvas / 2f;

    private readonly FakeInput _input = new();
    private readonly TacticalCamera _camera = new() { Center = new Vector2(10f, 10f) };
    private readonly TacticalCameraController _controller;

    public TacticalCameraControllerTests()
    {
        _controller = new TacticalCameraController(_camera, _input, new InputBindings(_input))
        {
            MapSize = new Vector2(20f),
        };
        _input.MousePosition = Middle;
    }

    private void Step(float dt = 0.1f)
    {
        _input.NextFrame();
        _controller.Update(dt, Canvas);
    }

    [Fact]
    public void PointerOverUi_IgnoresWheelAndEdgeScroll()
    {
        _input.Scroll(-500f);
        _input.NextFrame();
        _controller.Update(0.5f, Canvas, pointerOverUi: true);
        Assert.Equal(1f, _controller.ZoomFactor, 3);

        _input.MousePosition = new Vector2(2f, Middle.Y);
        _input.NextFrame();
        _controller.Update(0.5f, Canvas, pointerOverUi: true);
        Assert.Equal(10f, _camera.Center.X, 3);
    }

    [Fact]
    public void WasdPansAndSpeedScalesWithZoom()
    {
        _input.Press(Keys.D);
        Step();
        var wide = _camera.Center.X - 10f;

        _controller.SetZoom(2f);
        _camera.Center = new Vector2(10f, 10f);
        Step();
        var close = _camera.Center.X - 10f;

        Assert.True(wide > 0f);
        Assert.InRange(close, wide / 2f - 1e-3f, wide / 2f + 1e-3f);
    }

    [Fact]
    public void EdgeScroll_PansWithinTwelvePixels_AndCanBeDisabled()
    {
        _input.MousePosition = new Vector2(Canvas.X - 5f, Middle.Y);
        Step();
        Assert.True(_camera.Center.X > 10f);

        _camera.Center = new Vector2(10f, 10f);
        _controller.EdgeScrollEnabled = false;
        Step();
        Assert.Equal(10f, _camera.Center.X);
    }

    [Fact]
    public void EdgeScroll_OffWhenMouseOutsideCanvas()
    {
        _input.MousePosition = new Vector2(Canvas.X + 40f, Middle.Y);
        Step();
        Assert.Equal(10f, _camera.Center.X);
    }

    [Fact]
    public void EdgeScroll_OffWhenFlaggedOutside_EvenIfPositionIsInBand()
    {
        _input.MousePosition = new Vector2(Canvas.X - 5f, 2f);
        _input.IsMouseInside = false;
        Step();
        Assert.Equal(new Vector2(10f, 10f), _camera.Center);
    }

    [Fact]
    public void Wheel_WhenOutside_DoesNotZoom_AndKeysZoomAroundScreenCentre()
    {
        _input.MousePosition = new Vector2(1200f, 100f);
        _input.IsMouseInside = false;
        _input.Scroll(-500f);
        Step();
        Assert.Equal(1f, _controller.ZoomFactor, 3);

        var before = _camera.CanvasToWorld(Middle, Canvas);
        _input.Press(Keys.Plus);
        for (var i = 0; i < 10; i++)
            Step();
        Assert.True(_controller.ZoomFactor > 1f);
        Assert.InRange(Vector2.Distance(_camera.CanvasToWorld(Middle, Canvas), before), 0f, 0.01f);
    }

    [Fact]
    public void Wheel_ZoomsWithinLimits()
    {
        for (var i = 0; i < 100; i++)
        {
            _input.Scroll(-300f);
            Step();
        }
        Assert.InRange(_controller.ZoomFactor, 1.99f, 2.0001f);

        for (var i = 0; i < 100; i++)
        {
            _input.Scroll(300f);
            Step();
        }
        Assert.InRange(_controller.ZoomFactor, 0.4999f, 0.51f);
    }

    [Fact]
    public void Wheel_ZoomsTowardCursor()
    {
        var cursor = new Vector2(1000f, 200f);
        _input.MousePosition = cursor;
        var before = _camera.CanvasToWorld(cursor, Canvas);

        for (var i = 0; i < 30; i++)
        {
            _input.Scroll(i == 0 ? -200f : 0f);
            Step();
        }

        Assert.True(_controller.ZoomFactor > 1.2f);
        Assert.InRange(Vector2.Distance(_camera.CanvasToWorld(cursor, Canvas), before), 0f, 0.01f);
    }

    [Fact]
    public void RightDrag_PansAfterThreshold_AndIsNotAClick()
    {
        _input.PressButton(MouseButton.Right);
        Step();
        _input.MousePosition = Middle + new Vector2(3f, 0f);
        Step();
        Assert.False(_controller.IsDragging);

        _input.MousePosition = Middle + new Vector2(120f, 0f);
        Step();
        Assert.True(_controller.IsDragging);
        Assert.True(_camera.Center.X < 10f); // grabbing the map and pulling right moves the view left

        _input.ReleaseButton(MouseButton.Right);
        Step();
        Assert.False(_controller.RightClicked);
        Assert.False(_controller.IsDragging);
    }

    [Fact]
    public void RightPressAndReleaseUnderThreshold_IsAClick()
    {
        _input.PressButton(MouseButton.Right);
        Step();
        _input.MousePosition = Middle + new Vector2(4f, 3f);
        _input.ReleaseButton(MouseButton.Right);
        Step();

        Assert.True(_controller.RightClicked);
        Assert.Equal(10f, _camera.Center.X);

        Step();
        Assert.False(_controller.RightClicked);
    }

    [Fact]
    public void SubFrameRightClick_IsAClick()
    {
        _input.PressButton(MouseButton.Right).ReleaseButton(MouseButton.Right);
        Step();
        Assert.True(_controller.RightClicked);
    }

    [Fact]
    public void Bounds_ClampToMapPlusMargin()
    {
        _controller.SetZoom(2f); // 5.5 tiles tall: small enough to hit the edge
        _input.Press(Keys.D);
        for (var i = 0; i < 100; i++)
            Step();

        var halfWidth = _camera.TilesVisibleVertically / 2f * Canvas.X / Canvas.Y;
        Assert.InRange(
            _camera.Center.X + halfWidth,
            0f,
            20f + TacticalCameraController.BoundsMarginTiles + 1e-3f
        );
    }

    [Fact]
    public void Bounds_CentreViewLargerThanMap()
    {
        _controller.SetZoom(0.5f); // 22 tiles tall > 20 + 4 wide? width is larger still
        _camera.Center = new Vector2(3f, 3f);
        Step();
        Assert.Equal(10f, _camera.Center.X, 3);
    }

    [Fact]
    public void FocusOn_EasesToTarget()
    {
        _controller.SetZoom(2f);
        _controller.FocusOn(new Vector2(14f, 6f), 1f);
        Assert.True(_controller.IsFocusing);

        Step(0.5f);
        var mid = _camera.Center;
        Assert.True(mid.X > 10f && mid.X < 14f);

        Step(0.6f);
        Assert.False(_controller.IsFocusing);
        Assert.Equal(14f, _camera.Center.X, 3);
        Assert.Equal(6f, _camera.Center.Y, 3);
    }

    [Fact]
    public void FocusOn_IsCancelledByPanInput()
    {
        _controller.SetZoom(2f);
        _controller.FocusOn(new Vector2(14f, 6f), 1f);
        _input.Press(Keys.W);
        Step(0.1f);

        Assert.False(_controller.IsFocusing);
        var x = _camera.Center.X;
        Step(0.5f);
        Assert.Equal(x, _camera.Center.X, 3);
    }
}
