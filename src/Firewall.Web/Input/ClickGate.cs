using Yaeger.Input;
using Yaeger.Platform;

namespace Firewall.Web.Input;

/// <summary>
/// Hands this frame's left click to exactly one consumer. The UI asks first, so a click on a
/// button doesn't also click the tile beneath it. Call <see cref="BeginFrame"/> once per tick.
/// </summary>
public sealed class ClickGate(IInputState input)
{
    private bool _consumed;

    public void BeginFrame() => _consumed = false;

    /// <summary>A left click happened this frame and nobody has consumed it yet.</summary>
    public bool ClickAvailable => !_consumed && input.WasMouseButtonPressed(MouseButton.Left);

    /// <summary>Returns true and claims the click if one is available; false afterwards.</summary>
    public bool ConsumeClick()
    {
        if (!ClickAvailable)
            return false;
        _consumed = true;
        return true;
    }
}
