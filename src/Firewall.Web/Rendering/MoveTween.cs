using System.Numerics;
using Firewall.Rules;

namespace Firewall.Web.Rendering;

/// <summary>A unit walking along tile centres at a fixed time per tile step.</summary>
public sealed class MoveTween(IReadOnlyList<GridPos> path)
{
    public const float SecondsPerTile = 0.12f;

    public IReadOnlyList<GridPos> Path { get; } = path;

    public float Duration => Math.Max(Path.Count - 1, 0) * SecondsPerTile;

    public bool IsDone(float elapsed) => elapsed >= Duration;

    /// <summary>World position after <paramref name="elapsed"/> seconds, clamped to the ends.</summary>
    public Vector2 Position(float elapsed, out Vector2 direction)
    {
        direction = Vector2.UnitX;
        if (Path.Count == 0)
            return Vector2.Zero;
        var progress = Math.Clamp(elapsed / SecondsPerTile, 0f, Path.Count - 1);
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
