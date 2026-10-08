namespace Firewall.Web.Ui;

/// <summary>
/// Hover dwell timer: a tooltip appears once the same widget has been hovered for
/// <see cref="Delay"/> seconds, and resets when the pointer moves to another widget or leaves.
/// </summary>
public sealed class TooltipTimer
{
    public const float DefaultDelay = 0.35f;

    private int _id;
    private float _time;

    public float Delay { get; init; } = DefaultDelay;

    public bool Visible => _id != 0 && _time >= Delay;

    public int HoveredId => _id;

    /// <summary>Advance one frame. <paramref name="hoveredId"/> is 0 when nothing with a tooltip is hovered.</summary>
    public void Update(float deltaSeconds, int hoveredId)
    {
        if (hoveredId != _id)
        {
            _id = hoveredId;
            _time = 0f;
            return;
        }
        if (_id != 0)
            _time += deltaSeconds;
    }

    /// <summary>Hide until the pointer re-enters (e.g. after a click).</summary>
    public void Reset()
    {
        _id = 0;
        _time = 0f;
    }
}
