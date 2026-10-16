using System.Numerics;
using Firewall.Rules;
using Firewall.Web.Rendering;

namespace Firewall.Web.Tests;

public class MoveRangeTests
{
    private static IEnumerable<GridPos> Block(int w, int h) =>
        from y in Enumerable.Range(0, h)
        from x in Enumerable.Range(0, w)
        select new GridPos(x, y);

    [Fact]
    public void FillRunsMergeAdjacentTilesPerRow()
    {
        var rects = TileRegions.FillRuns([new(0, 0), new(1, 0), new(3, 0), new(0, 1)]);

        Assert.Equal(3, rects.Count);
        Assert.Contains(new TileRect(new Vector2(0, 0), new Vector2(2, 1)), rects);
        Assert.Contains(new TileRect(new Vector2(3, 0), new Vector2(1, 1)), rects);
        Assert.Contains(new TileRect(new Vector2(0, 1), new Vector2(1, 1)), rects);
    }

    [Fact]
    public void OutlineOfARectangleIsFourBars()
    {
        var bars = TileRegions.Outline(Block(4, 3), 0.1f);

        Assert.Equal(4, bars.Count);
        Assert.Contains(new TileRect(new Vector2(0, 0), new Vector2(4, 0.1f)), bars);
        Assert.Contains(new TileRect(new Vector2(0, 3 - 0.1f), new Vector2(4, 0.1f)), bars);
        Assert.Contains(new TileRect(new Vector2(0, 0), new Vector2(0.1f, 3)), bars);
        Assert.Contains(new TileRect(new Vector2(4 - 0.1f, 0), new Vector2(0.1f, 3)), bars);
    }

    [Fact]
    public void OutlineSkipsEdgesBetweenTilesOfTheRegion()
    {
        // An L shape has six border runs, none along the interior.
        var bars = TileRegions.Outline([new(0, 0), new(1, 0), new(0, 1)], 0.1f);

        Assert.Equal(6, bars.Count);
        Assert.DoesNotContain(bars, b => b.Min == new Vector2(1, 1) && b.Size.X > 1f);
    }

    [Fact]
    public void ATileWithAHoleIsOutlinedAroundTheHoleToo()
    {
        var ring = Block(3, 3).Where(p => p != new GridPos(1, 1));

        Assert.Equal(8, TileRegions.Outline(ring, 0.1f).Count);
    }

    [Fact]
    public void TweenWalksAtTwelveHundredthsPerTile()
    {
        var tween = new MoveTween([new(0, 0), new(1, 0), new(1, 1)]);

        Assert.Equal(0.24f, tween.Duration, 3);
        Assert.Equal(new Vector2(0.5f, 0.5f), tween.Position(0f, out _));
        var mid = tween.Position(0.06f, out var dir);
        Assert.Equal(new Vector2(1f, 0.5f), mid);
        Assert.Equal(Vector2.UnitX, dir);
        Assert.Equal(new Vector2(1.5f, 1.5f), tween.Position(5f, out _));
        Assert.True(tween.IsDone(0.24f));
        Assert.False(tween.IsDone(0.1f));
    }

    [Fact]
    public void LabelsNameTheApCost()
    {
        Assert.Equal("1 AP", MoveRangeOverlay.Label(1));
        Assert.Equal("2 AP (Dash)", MoveRangeOverlay.Label(2));
    }
}
