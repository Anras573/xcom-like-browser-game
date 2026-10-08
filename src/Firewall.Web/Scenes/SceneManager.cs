using Firewall.Web.Rendering;

namespace Firewall.Web.Scenes;

/// <summary>
/// Stack of scenes with <see cref="Push"/>, <see cref="Pop"/> and <see cref="Replace"/>. Changes
/// that swap a full scene fade through black (<see cref="FadeSeconds"/> out, then in); overlays
/// switch instantly. Requests are queued and applied at the next safe moment, so a scene may ask
/// for a change from inside its own <c>Update</c>.
/// </summary>
public sealed class SceneManager
{
    public const float FadeSeconds = 0.25f;

    /// <summary>A background tab can deliver huge deltas; never simulate more than this.</summary>
    public const float MaxDeltaSeconds = 0.1f;

    private enum Phase
    {
        None,
        FadeOut,
        FadeIn,
    }

    private readonly SceneContext ctx;
    private readonly List<IScene> _stack = [];
    private readonly Queue<Action> _pending = new();
    private Phase _phase;
    private float _fade;
    private Action? _swap;

    public SceneManager(SceneContext ctx)
    {
        this.ctx = ctx;
        ctx.Scenes = this;
    }

    /// <summary>Log sink; the entity count per scene goes here.</summary>
    public Action<string> Log { get; set; } = Console.WriteLine;

    public IReadOnlyList<IScene> Stack => _stack;
    public IScene? Top => _stack.Count > 0 ? _stack[^1] : null;
    public bool IsTransitioning => _phase != Phase.None;

    /// <summary>0 = clear, 1 = fully black.</summary>
    public float FadeAlpha =>
        _phase switch
        {
            Phase.FadeOut => Math.Clamp(_fade / FadeSeconds, 0f, 1f),
            Phase.FadeIn => 1f - Math.Clamp(_fade / FadeSeconds, 0f, 1f),
            _ => 0f,
        };

    public void Push(IScene scene) =>
        _pending.Enqueue(() => Change(scene.IsOverlay, () => Enter(scene)));

    public void Pop() =>
        _pending.Enqueue(() =>
        {
            if (_stack.Count == 0)
                return;
            Change(_stack[^1].IsOverlay, ExitTop);
        });

    public void Replace(IScene scene) =>
        _pending.Enqueue(() =>
            Change(
                scene.IsOverlay && (Top?.IsOverlay ?? false),
                () =>
                {
                    ExitTop();
                    Enter(scene);
                }
            )
        );

    /// <summary>Applies queued changes immediately, without fading (startup and tests).</summary>
    public void Flush()
    {
        while (_pending.Count > 0)
        {
            var next = _pending.Dequeue();
            next();
            if (_swap is { } swap)
            {
                _swap = null;
                _phase = Phase.None;
                swap();
            }
        }
    }

    public void Update(float dt)
    {
        dt = Math.Clamp(dt, 0f, MaxDeltaSeconds);

        switch (_phase)
        {
            case Phase.FadeOut:
                _fade += dt;
                if (_fade >= FadeSeconds)
                {
                    _swap?.Invoke();
                    _swap = null;
                    _phase = Phase.FadeIn;
                    _fade = 0f;
                }
                return; // The outgoing scene is frozen under the fade and takes no input.
            case Phase.FadeIn:
                _fade += dt;
                if (_fade >= FadeSeconds)
                    _phase = Phase.None;
                break;
        }

        Top?.Update(dt);

        // Applied after the update, so a key that closes an overlay can't also reach the scene
        // below it in the same frame.
        if (_phase == Phase.None && _pending.Count > 0)
            _pending.Dequeue()();
    }

    /// <summary>Draws the stack bottom-up (from the topmost non-overlay scene) and the fade on top.</summary>
    public void Render()
    {
        var first = _stack.Count - 1;
        while (first > 0 && _stack[first].IsOverlay)
            first--;

        for (var i = Math.Max(first, 0); i < _stack.Count; i++)
        {
            // Everything below the top, and everything during a fade, is shown but inert.
            if (ctx.IsLoaded)
                ctx.Ui.InputSuspended = i < _stack.Count - 1 || _phase == Phase.FadeOut;
            _stack[i].Render();
        }

        if (ctx.IsLoaded)
            ctx.Ui.InputSuspended = false;
        if (FadeAlpha > 0f)
            ctx.Render?.DrawFade(FadeAlpha);
    }

    private void Change(bool instant, Action swap)
    {
        if (instant || _stack.Count == 0)
        {
            swap();
            return;
        }
        _swap = swap;
        _phase = Phase.FadeOut;
        _fade = 0f;
    }

    private void Enter(IScene scene)
    {
        scene.Enter(ctx);
        _stack.Add(scene);
        Log($"[scene] enter {scene.Name}: {scene.EntityCount} entities (stack {_stack.Count})");
    }

    private void ExitTop()
    {
        if (_stack.Count == 0)
            return;
        var scene = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        var before = scene.EntityCount;
        scene.Exit();
        Log($"[scene] exit {scene.Name}: {before} -> {scene.EntityCount} entities");
    }
}
