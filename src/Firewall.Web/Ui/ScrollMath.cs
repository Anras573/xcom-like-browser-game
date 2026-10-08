namespace Firewall.Web.Ui;

public static class ScrollMath
{
    public static float MaxOffset(int itemCount, float itemHeight, float viewHeight) =>
        MathF.Max(0f, itemCount * itemHeight - viewHeight);

    public static float Clamp(float offset, int itemCount, float itemHeight, float viewHeight) =>
        Math.Clamp(offset, 0f, MaxOffset(itemCount, itemHeight, viewHeight));

    /// <summary>
    /// Items wholly inside the viewport: <c>[First, End)</c>. Partly visible items are left out
    /// because the browser runtime has no scissor, so they would paint outside the list.
    /// </summary>
    public static (int First, int End) Visible(
        float offset,
        int itemCount,
        float itemHeight,
        float viewHeight
    )
    {
        if (itemCount <= 0 || itemHeight <= 0f)
            return (0, 0);
        var first = Math.Max(0, (int)MathF.Ceiling(offset / itemHeight - 1e-4f));
        var end = Math.Min(itemCount, (int)MathF.Floor((offset + viewHeight) / itemHeight + 1e-4f));
        return (first, Math.Max(first, end));
    }
}
