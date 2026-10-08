using Yaeger.Input;
using Yaeger.Platform;

namespace Firewall.Web.Input;

public enum GameAction
{
    PanUp,
    PanDown,
    PanLeft,
    PanRight,
    ZoomIn,
    ZoomOut,
    EndTurn,

    /// <summary>Cycles forward; consumers treat <c>Shift</c> held with this action as cycling backwards.</summary>
    NextUnit,
    Ability1,
    Ability2,
    Ability3,
    Ability4,
    Ability5,
    Ability6,
    Ability7,
    Ability8,
    Ability9,
    Confirm,
    Cancel,
    CenterCamera,
}

/// <summary>
/// Action to key map (defaults from GDD §4.11) on top of <see cref="IInputState"/>. Mouse
/// buttons and the wheel are handled by their consumers, not bound here.
/// </summary>
public sealed class InputBindings(IInputState input)
{
    /// <summary>The one place the default keys live.</summary>
    public static readonly IReadOnlyDictionary<GameAction, Keys[]> Defaults = new Dictionary<
        GameAction,
        Keys[]
    >
    {
        [GameAction.PanUp] = [Keys.W, Keys.Up],
        [GameAction.PanDown] = [Keys.S, Keys.Down],
        [GameAction.PanLeft] = [Keys.A, Keys.Left],
        [GameAction.PanRight] = [Keys.D, Keys.Right],
        [GameAction.ZoomIn] = [Keys.Plus],
        [GameAction.ZoomOut] = [Keys.Minus],
        [GameAction.EndTurn] = [Keys.Backspace],
        [GameAction.NextUnit] = [Keys.Tab],
        [GameAction.Ability1] = [Keys.Num1],
        [GameAction.Ability2] = [Keys.Num2],
        [GameAction.Ability3] = [Keys.Num3],
        [GameAction.Ability4] = [Keys.Num4],
        [GameAction.Ability5] = [Keys.Num5],
        [GameAction.Ability6] = [Keys.Num6],
        [GameAction.Ability7] = [Keys.Num7],
        [GameAction.Ability8] = [Keys.Num8],
        [GameAction.Ability9] = [Keys.Num9],
        [GameAction.Confirm] = [Keys.Enter, Keys.Space],
        [GameAction.Cancel] = [Keys.Escape],
        [GameAction.CenterCamera] = [Keys.F],
    };

    /// <summary>Keys whose browser default (scroll, focus move) must be suppressed.</summary>
    public static IReadOnlyList<Keys> PreventDefaultKeys { get; } =
    [
        Keys.Space,
        Keys.Tab,
        Keys.Backspace,
        Keys.Up,
        Keys.Down,
        Keys.Left,
        Keys.Right,
        Keys.Enter,
        Keys.F1,
    ];

    public IReadOnlyList<Keys> KeysFor(GameAction action) => Defaults[action];

    /// <summary>True while any key bound to <paramref name="action"/> is held.</summary>
    public bool IsDown(GameAction action)
    {
        foreach (var key in Defaults[action])
            if (input.IsKeyPressed(key))
                return true;
        return false;
    }

    /// <summary>True on the frame any key bound to <paramref name="action"/> went down.</summary>
    public bool WasPressed(GameAction action)
    {
        foreach (var key in Defaults[action])
            if (input.WasKeyPressed(key))
                return true;
        return false;
    }
}
