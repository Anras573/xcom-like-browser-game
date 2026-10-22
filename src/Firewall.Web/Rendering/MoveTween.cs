using System.Numerics;
using Firewall.Rules;
using Yaeger.Graphics;

namespace Firewall.Web.Rendering;

/// <summary>
/// A unit walking along tile centres at a fixed time per tile step, optionally easing in and out
/// over the whole path.
/// </summary>
public sealed class MoveTween(
    IReadOnlyList<GridPos> path,
    float secondsPerTile = MoveTween.SecondsPerTile,
    bool ease = false
)
{
    public const float SecondsPerTile = 0.12f;

    /// <summary>Seconds per tile of a dash.</summary>
    public const float DashSecondsPerTile = 0.09f;

    public IReadOnlyList<GridPos> Path { get; } = path;

    public float Duration => Math.Max(Path.Count - 1, 0) * secondsPerTile;

    public bool IsDone(float elapsed) => elapsed >= Duration;

    /// <summary>World position after <paramref name="elapsed"/> seconds, clamped to the ends.</summary>
    public Vector2 Position(float elapsed, out Vector2 direction)
    {
        direction = Vector2.UnitX;
        if (Path.Count == 0)
            return Vector2.Zero;
        var steps = Path.Count - 1;
        var progress = ease
            ? Easing.Apply(
                EasingFunction.QuadInOut,
                Math.Clamp(elapsed / MathF.Max(Duration, 1e-6f), 0f, 1f)
            ) * steps
            : Math.Clamp(elapsed / secondsPerTile, 0f, steps);
        var i = Math.Min((int)progress, Path.Count - 2);
        if (i < 0)
            return Center(Path[0]);
        var from = Center(Path[i]);
        var to = Center(Path[i + 1]);
        if (to != from)
            direction = Vector2.Normalize(to - from);
        return Vector2.Lerp(from, to, progress - i);
    }

    private static Vector2 Center(GridPos p) => new(p.X + 0.5f, p.Y + 0.5f);
}
