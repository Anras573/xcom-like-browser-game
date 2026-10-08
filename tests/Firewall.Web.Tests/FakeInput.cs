using System.Numerics;
using Yaeger.Input;
using Yaeger.Platform;

namespace Firewall.Web.Tests;

/// <summary>
/// Scripted <see cref="IInputState"/>: stage presses/releases/moves, then <see cref="NextFrame"/>
/// publishes them as that frame's edges and held state.
/// </summary>
internal sealed class FakeInput : IInputState
{
    private readonly HashSet<Keys> _held = [];
    private readonly HashSet<Keys> _down = [];
    private readonly HashSet<Keys> _up = [];
    private readonly HashSet<MouseButton> _buttonsHeld = [];
    private readonly HashSet<MouseButton> _buttonsDown = [];
    private readonly HashSet<MouseButton> _buttonsUp = [];
    private readonly HashSet<Keys> _stagedDown = [];
    private readonly HashSet<Keys> _stagedUp = [];
    private readonly HashSet<MouseButton> _stagedButtonsDown = [];
    private readonly HashSet<MouseButton> _stagedButtonsUp = [];
    private float _stagedScroll;

    public Vector2 MousePosition { get; set; }
    public Vector2 MousePositionNdc => Vector2.Zero;
    public float ScrollDelta { get; private set; }

    public FakeInput Press(Keys key)
    {
        _stagedDown.Add(key);
        return this;
    }

    public FakeInput Release(Keys key)
    {
        _stagedUp.Add(key);
        return this;
    }

    public FakeInput PressButton(MouseButton button)
    {
        _stagedButtonsDown.Add(button);
        return this;
    }

    public FakeInput ReleaseButton(MouseButton button)
    {
        _stagedButtonsUp.Add(button);
        return this;
    }

    public FakeInput Scroll(float delta)
    {
        _stagedScroll += delta;
        return this;
    }

    public FakeInput NextFrame()
    {
        _down.Clear();
        _up.Clear();
        _buttonsDown.Clear();
        _buttonsUp.Clear();
        _down.UnionWith(_stagedDown);
        _up.UnionWith(_stagedUp);
        _buttonsDown.UnionWith(_stagedButtonsDown);
        _buttonsUp.UnionWith(_stagedButtonsUp);
        _held.UnionWith(_stagedDown);
        _buttonsHeld.UnionWith(_stagedButtonsDown);
        // A press and release inside one frame still reports both edges but is not held.
        _held.ExceptWith(_stagedUp);
        _buttonsHeld.ExceptWith(_stagedButtonsUp);
        ScrollDelta = _stagedScroll;
        _stagedDown.Clear();
        _stagedUp.Clear();
        _stagedButtonsDown.Clear();
        _stagedButtonsUp.Clear();
        _stagedScroll = 0f;
        return this;
    }

    public bool IsKeyPressed(Keys key) => _held.Contains(key);

    public bool IsMouseButtonPressed(MouseButton button) => _buttonsHeld.Contains(button);

    public bool WasKeyPressed(Keys key) => _down.Contains(key);

    public bool WasKeyReleased(Keys key) => _up.Contains(key);

    public bool WasMouseButtonPressed(MouseButton button) => _buttonsDown.Contains(button);

    public bool WasMouseButtonReleased(MouseButton button) => _buttonsUp.Contains(button);
}
