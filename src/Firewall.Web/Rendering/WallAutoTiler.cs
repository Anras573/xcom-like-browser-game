namespace Firewall.Web.Rendering;

[Flags]
public enum Sides : byte
{
    None = 0,
    N = 1,
    E = 2,
    S = 4,
    W = 8,
}

[Flags]
public enum Corners : byte
{
    None = 0,
    NE = 1,
    SE = 2,
    SW = 4,
    NW = 8,
}

/// <summary>
/// What a wall cell looks like: <see cref="Edges"/> are the sides whose neighbour is not a wall
/// (the tile draws a rim there), <see cref="Knobs"/> the diagonals that are not a wall while both
/// adjacent sides are (a concave corner, drawn as a small knob).
/// </summary>
public readonly record struct WallMask(Sides Edges, Corners Knobs);

/// <summary>
/// Blob auto-tiling for walls, using the grey wall frames of <c>tiles.png</c>
/// (see <c>tools/tile_map.yml</c>). +Y is up, so north is <c>y + 1</c>.
/// </summary>
/// <remarks>
/// The sheet is incomplete: it has no tile for a diagonal-only knob pair (NE+SW, NW+SE), three
/// knobs, or an edge tile with a single knob (the <c>t_*</c> tiles carry both). <see cref="FrameName"/>
/// then drops knobs until a tile exists, so those junctions lose a small corner knob.
/// </remarks>
public static class WallAutoTiler
{
    private static readonly (string Name, Sides Edges, Corners Knobs)[] Table =
    [
        ("interior", Sides.None, Corners.None),
        ("cross", Sides.None, Corners.NE | Corners.SE | Corners.SW | Corners.NW),
        ("knob_ne", Sides.None, Corners.NE),
        ("knob_se", Sides.None, Corners.SE),
        ("knob_sw", Sides.None, Corners.SW),
        ("knob_nw", Sides.None, Corners.NW),
        ("knob_ne_se", Sides.None, Corners.NE | Corners.SE),
        ("knob_sw_se", Sides.None, Corners.SW | Corners.SE),
        ("knob_nw_sw", Sides.None, Corners.NW | Corners.SW),
        ("knob_nw_ne", Sides.None, Corners.NW | Corners.NE),
        ("n", Sides.N, Corners.None),
        ("e", Sides.E, Corners.None),
        ("s", Sides.S, Corners.None),
        ("w", Sides.W, Corners.None),
        ("t_n", Sides.N, Corners.SE | Corners.SW),
        ("t_e", Sides.E, Corners.NW | Corners.SW),
        ("t_s", Sides.S, Corners.NE | Corners.NW),
        ("t_w", Sides.W, Corners.NE | Corners.SE),
        ("straight_h", Sides.N | Sides.S, Corners.None),
        ("straight_v", Sides.E | Sides.W, Corners.None),
        ("corner_ne", Sides.N | Sides.E, Corners.SW),
        ("corner_ne.plain", Sides.N | Sides.E, Corners.None),
        ("corner_se", Sides.S | Sides.E, Corners.NW),
        ("corner_se.plain", Sides.S | Sides.E, Corners.None),
        ("corner_sw", Sides.S | Sides.W, Corners.NE),
        ("corner_sw.plain", Sides.S | Sides.W, Corners.None),
        ("corner_nw", Sides.N | Sides.W, Corners.SE),
        ("corner_nw.plain", Sides.N | Sides.W, Corners.None),
        ("end_e", Sides.N | Sides.S | Sides.W, Corners.None), // open to the east
        ("end_n", Sides.E | Sides.S | Sides.W, Corners.None),
        ("end_s", Sides.N | Sides.E | Sides.W, Corners.None),
        ("end_w", Sides.N | Sides.E | Sides.S, Corners.None),
        ("enclosed", Sides.N | Sides.E | Sides.S | Sides.W, Corners.None),
    ];

    /// <summary>Every frame name (<c>wall.grey.*</c>) the auto-tiler can return.</summary>
    public static IEnumerable<string> FrameNames => Table.Select(t => Prefix + t.Name);

    private const string Prefix = "wall.grey.";

    /// <summary>The mask of the cell <c>(x, y)</c>; <paramref name="isWall"/> must accept any coordinate.</summary>
    public static WallMask Compute(Func<int, int, bool> isWall, int x, int y)
    {
        var n = isWall(x, y + 1);
        var e = isWall(x + 1, y);
        var s = isWall(x, y - 1);
        var w = isWall(x - 1, y);

        var edges = Sides.None;
        if (!n)
            edges |= Sides.N;
        if (!e)
            edges |= Sides.E;
        if (!s)
            edges |= Sides.S;
        if (!w)
            edges |= Sides.W;

        var knobs = Corners.None;
        if (n && e && !isWall(x + 1, y + 1))
            knobs |= Corners.NE;
        if (s && e && !isWall(x + 1, y - 1))
            knobs |= Corners.SE;
        if (s && w && !isWall(x - 1, y - 1))
            knobs |= Corners.SW;
        if (n && w && !isWall(x - 1, y + 1))
            knobs |= Corners.NW;
        return new WallMask(edges, knobs);
    }

    /// <summary>The frame for a mask, falling back to the closest tile the sheet has.</summary>
    public static string FrameName(WallMask mask)
    {
        string? best = null;
        var bestKnobs = -1;
        foreach (var (name, edges, knobs) in Table)
        {
            if (edges != mask.Edges || (knobs & ~mask.Knobs) != 0)
                continue;
            var count = System.Numerics.BitOperations.PopCount((uint)knobs);
            if (count > bestKnobs)
            {
                best = name;
                bestKnobs = count;
            }
        }
        // Every edge set has a knob-free tile, so this cannot be null.
        return Prefix + best!;
    }
}
