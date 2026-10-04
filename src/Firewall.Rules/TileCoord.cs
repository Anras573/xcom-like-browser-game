namespace Firewall.Rules;

/// <summary>A tile position on the tactical grid.</summary>
public readonly record struct TileCoord(int X, int Y)
{
    /// <summary>Euclidean distance between tile centers, in tiles.</summary>
    public double DistanceTo(TileCoord other)
    {
        var dx = other.X - X;
        var dy = other.Y - Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
