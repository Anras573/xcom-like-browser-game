using Firewall.Web.Input;
using Yaeger.Input;

namespace Firewall.Web.Tests;

public class InputBindingsTests
{
    [Fact]
    public void WasPressed_IsTrueOnlyOnTheEdgeFrame()
    {
        var input = new FakeInput();
        var bindings = new InputBindings(input);

        input.Press(Keys.Backspace).NextFrame();
        Assert.True(bindings.WasPressed(GameAction.EndTurn));
        Assert.True(bindings.IsDown(GameAction.EndTurn));

        input.NextFrame();
        Assert.False(bindings.WasPressed(GameAction.EndTurn));
        Assert.True(bindings.IsDown(GameAction.EndTurn));

        input.Release(Keys.Backspace).NextFrame();
        Assert.False(bindings.IsDown(GameAction.EndTurn));
    }

    [Fact]
    public void SubFramePress_ReportsWasPressedButNotDown()
    {
        var input = new FakeInput();
        var bindings = new InputBindings(input);

        input.Press(Keys.Tab).Release(Keys.Tab).NextFrame();

        Assert.True(bindings.WasPressed(GameAction.NextUnit));
        Assert.False(bindings.IsDown(GameAction.NextUnit));
    }

    [Theory]
    [InlineData(Keys.W, GameAction.PanUp)]
    [InlineData(Keys.Up, GameAction.PanUp)]
    [InlineData(Keys.S, GameAction.PanDown)]
    [InlineData(Keys.Left, GameAction.PanLeft)]
    [InlineData(Keys.D, GameAction.PanRight)]
    [InlineData(Keys.Enter, GameAction.Confirm)]
    [InlineData(Keys.Space, GameAction.Confirm)]
    [InlineData(Keys.Escape, GameAction.Cancel)]
    [InlineData(Keys.F, GameAction.CenterCamera)]
    [InlineData(Keys.Num1, GameAction.Ability1)]
    [InlineData(Keys.Num9, GameAction.Ability9)]
    public void EachAlias_TriggersItsAction(Keys key, GameAction action)
    {
        var input = new FakeInput();
        var bindings = new InputBindings(input);

        input.Press(key).NextFrame();

        Assert.True(bindings.WasPressed(action));
        Assert.False(bindings.WasPressed(GameAction.EndTurn));
    }

    [Fact]
    public void EveryAction_HasAtLeastOneKey()
    {
        foreach (var action in Enum.GetValues<GameAction>())
            Assert.NotEmpty(InputBindings.Defaults[action]);
    }

    [Fact]
    public void PreventDefaultKeys_CoverBrowserDefaultKeysUsedByBindings()
    {
        foreach (
            var key in new[]
            {
                Keys.Tab,
                Keys.Space,
                Keys.Backspace,
                Keys.Up,
                Keys.Down,
                Keys.Left,
                Keys.Right,
                Keys.Enter,
            }
        )
            Assert.Contains(key, InputBindings.PreventDefaultKeys);
    }
}

public class ClickGateTests
{
    [Fact]
    public void ConsumeClick_ClaimsTheClickOnce()
    {
        var input = new FakeInput();
        var gate = new ClickGate(input);

        input.PressButton(Yaeger.Input.MouseButton.Left).NextFrame();
        gate.BeginFrame();

        Assert.True(gate.ClickAvailable);
        Assert.True(gate.ConsumeClick());
        Assert.False(gate.ConsumeClick());
        Assert.False(gate.ClickAvailable);
    }

    [Fact]
    public void NewFrame_ResetsConsumption()
    {
        var input = new FakeInput();
        var gate = new ClickGate(input);

        input.PressButton(Yaeger.Input.MouseButton.Left).NextFrame();
        gate.BeginFrame();
        gate.ConsumeClick();

        input.PressButton(Yaeger.Input.MouseButton.Left).NextFrame();
        gate.BeginFrame();
        Assert.True(gate.ConsumeClick());
    }

    [Fact]
    public void NoClick_NothingToConsume()
    {
        var input = new FakeInput();
        var gate = new ClickGate(input);

        input.NextFrame();
        gate.BeginFrame();

        Assert.False(gate.ConsumeClick());
    }
}
