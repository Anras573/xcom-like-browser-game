using System.Numerics;
using Firewall.Rules;

namespace Firewall.Web.Rendering;

/// <summary>An axis-aligned rectangle in tile units (+Y up).</summary>
public readonly record struct TileRect(Vector2 Min, Vector2 Size);

/// <summary>
/// Turns a set of tiles into few rectangles: fills as horizontal runs, and a crisp outline as thin
/// bars along the region's border (the edges whose neighbour is not in the set). The result is
/// computed once per set and drawn every frame.
/// </summary>
public static class TileRegions
{
    /// <summary>One rectangle per horizontal run of adjacent tiles.</summary>
    public static IReadOnlyList<TileRect> FillRuns(IEnumerable<GridPos> tiles)
    {
        var rects = new List<TileRect>();
        foreach (var row in tiles.GroupBy(t => t.Y).OrderBy(g => g.Key))
        {
            var xs = row.Select(t => t.X).Distinct().Order().ToList();
            var start = xs[0];
            for (var i = 1; i <= xs.Count; i++)
            {
                if (i < xs.Count && xs[i] == xs[i - 1] + 1)
                    continue;
                var end = xs[i - 1];
                rects.Add(
                    new TileRect(new Vector2(start, row.Key), new Vector2(end - start + 1, 1))
                );
                if (i < xs.Count)
                    start = xs[i];
            }
        }
        return rects;
    }

    /// <summary>Bars of the given thickness drawn just inside the region's border, merged into runs.</summary>
    public static IReadOnlyList<TileRect> Outline(IEnumerable<GridPos> tiles, float thickness)
    {
        var set = tiles.ToHashSet();
        var bottom = new List<(int Line, int Pos)>();
        var top = new List<(int Line, int Pos)>();
        var left = new List<(int Line, int Pos)>();
        var right = new List<(int Line, int Pos)>();
        foreach (var t in set)
        {
            if (!set.Contains(new GridPos(t.X, t.Y - 1)))
                bottom.Add((t.Y, t.X));
            if (!set.Contains(new GridPos(t.X, t.Y + 1)))
                top.Add((t.Y, t.X));
            if (!set.Contains(new GridPos(t.X - 1, t.Y)))
                left.Add((t.X, t.Y));
            if (!set.Contains(new GridPos(t.X + 1, t.Y)))
                right.Add((t.X, t.Y));
        }

        var rects = new List<TileRect>();
        foreach (var (line, from, to) in Runs(bottom))
            rects.Add(new TileRect(new Vector2(from, line), new Vector2(to - from, thickness)));
        foreach (var (line, from, to) in Runs(top))
            rects.Add(
                new TileRect(
                    new Vector2(from, line + 1 - thickness),
                    new Vector2(to - from, thickness)
                )
            );
        foreach (var (line, from, to) in Runs(left))
            rects.Add(new TileRect(new Vector2(line, from), new Vector2(thickness, to - from)));
        foreach (var (line, from, to) in Runs(right))
            rects.Add(
                new TileRect(
                    new Vector2(line + 1 - thickness, from),
                    new Vector2(thickness, to - from)
                )
            );
        return rects;
    }

    // Merges unit edges on the same line into [from, to) runs.
    private static IEnumerable<(int Line, int From, int To)> Runs(List<(int Line, int Pos)> edges)
    {
        edges.Sort();
        for (var i = 0; i < edges.Count; )
        {
            var (line, from) = edges[i];
            var to = from + 1;
            i++;
            while (i < edges.Count && edges[i].Line == line && edges[i].Pos == to)
            {
                to++;
                i++;
            }
            yield return (line, from, to);
        }
    }
}
