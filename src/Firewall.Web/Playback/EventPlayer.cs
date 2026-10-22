using Firewall.Rules.Tactical;

namespace Firewall.Web.Playback;

/// <summary>One step of playback. Changes the <see cref="ViewModel"/> only; never reads rules state.</summary>
public interface IEventAnimation
{
    void Start(ViewModel vm, EventPlayerContext ctx);

    /// <summary>Advances by <paramref name="dt"/> playback seconds (speed already applied); true when done.</summary>
    bool Update(float dt);
}

/// <summary>
/// Plays a list of <see cref="GameEvent"/>s one after another through <see cref="IEventAnimation"/>s.
/// Speed multiplies the time each animation sees, so it never changes what happens, only how fast.
/// </summary>
public sealed class EventPlayer(
    ViewModel vm,
    EventPlayerContext ctx,
    Func<GameEvent, IEventAnimation?> factory
)
{
    private readonly Queue<GameEvent> _queue = new();
    private IEventAnimation? _current;

    /// <summary>1 plays in real time; 2 twice as fast.</summary>
    public float Speed { get; set; } = 1f;

    /// <summary>Events are playing or waiting; the player's input is locked meanwhile.</summary>
    public bool IsBusy => _current is not null || _queue.Count > 0;

    public ViewModel ViewModel => vm;

    public void Enqueue(IEnumerable<GameEvent> events)
    {
        foreach (var e in events)
            _queue.Enqueue(e);
    }

    public void Update(float dt)
    {
        var scaled = dt * Speed;
        var step = scaled;
        while (true)
        {
            if (_current is null)
            {
                if (!_queue.TryDequeue(out var next))
                    break;
                if (factory(next) is not { } animation)
                    continue;
                animation.Start(vm, ctx);
                _current = animation;
            }
            if (!_current.Update(step))
                break;
            _current = null;
            // Time went into the animation that just finished; the next only gets a chance to be instant.
            step = 0f;
        }
        vm.Update(scaled);
    }
}
