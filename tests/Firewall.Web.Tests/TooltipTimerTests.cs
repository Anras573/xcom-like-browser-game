using Firewall.Web.Ui;

namespace Firewall.Web.Tests;

public class TooltipTimerTests
{
    [Fact]
    public void AppearsOnlyAfterTheDelay()
    {
        var t = new TooltipTimer();
        t.Update(0.1f, 7); // first frame over the widget just starts the clock
        Assert.False(t.Visible);
        t.Update(0.3f, 7);
        Assert.False(t.Visible); // 0.3 s
        t.Update(0.04f, 7);
        Assert.False(t.Visible); // 0.34 s
        t.Update(0.02f, 7);
        Assert.True(t.Visible); // 0.36 s >= 0.35
    }

    [Fact]
    public void MovingToAnotherWidgetRestartsTheClock()
    {
        var t = new TooltipTimer();
        t.Update(0f, 1);
        t.Update(0.5f, 1);
        Assert.True(t.Visible);
        t.Update(0.016f, 2);
        Assert.False(t.Visible);
        t.Update(0.2f, 2);
        Assert.False(t.Visible);
    }

    [Fact]
    public void LeavingHidesAndResets()
    {
        var t = new TooltipTimer();
        t.Update(0f, 1);
        t.Update(1f, 1);
        t.Update(0.016f, 0);
        Assert.False(t.Visible);
        t.Update(0f, 1);
        t.Update(0.2f, 1);
        Assert.False(t.Visible);
    }

    [Fact]
    public void ResetHidesUntilHoverRestarts()
    {
        var t = new TooltipTimer();
        t.Update(0f, 1);
        t.Update(1f, 1);
        t.Reset();
        Assert.False(t.Visible);
    }

    [Fact]
    public void DelayIsConfigurable()
    {
        var t = new TooltipTimer { Delay = 1f };
        t.Update(0f, 1);
        t.Update(0.9f, 1);
        Assert.False(t.Visible);
        t.Update(0.2f, 1);
        Assert.True(t.Visible);
    }
}
