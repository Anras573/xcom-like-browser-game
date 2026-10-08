using System.Numerics;

namespace Firewall.Web.Ui;

/// <summary>A rectangle in logical UI pixels (origin top-left, +Y down).</summary>
public readonly record struct UiRect(float X, float Y, float W, float H)
{
    public float Right => X + W;
    public float Bottom => Y + H;
    public Vector2 Position => new(X, Y);
    public Vector2 Size => new(W, H);
    public Vector2 Center => new(X + W / 2f, Y + H / 2f);

    public bool Contains(Vector2 p) => p.X >= X && p.X < Right && p.Y >= Y && p.Y < Bottom;

    public UiRect Inset(float all) => Inset(all, all);

    public UiRect Inset(float horizontal, float vertical) =>
        new(
            X + horizontal,
            Y + vertical,
            MathF.Max(0f, W - 2f * horizontal),
            MathF.Max(0f, H - 2f * vertical)
        );

    public bool Intersects(UiRect o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;
}
