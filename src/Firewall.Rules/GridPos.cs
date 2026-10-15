namespace Firewall.Rules;

/// <summary>
/// A tile position on the tactical grid. <b>+Y is up</b>, matching world space, so
/// <c>(0, 0)</c> is the bottom-left tile.
/// </summary>
public readonly record struct GridPos(int X, int Y)
{
    /// <summary>Euclidean distance between tile centers, in tiles.</summary>
    public double DistanceTo(GridPos other)
    {
        var dx = other.X - X;
        var dy = other.Y - Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
