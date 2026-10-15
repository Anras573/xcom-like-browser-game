using System.Numerics;
using Firewall.Rules;
using Firewall.Rules.Tactical;

namespace Firewall.Web.Rendering;

/// <summary>
/// The blue (1 AP) and yellow (2 AP, dash) move-range regions for one unit, plus the hover path
/// preview. The geometry is built once when the range changes; drawing only submits quads.
/// </summary>
public sealed class MoveRangeOverlay
{
    public static readonly Vector4 BlueFill = new(0x3f / 255f, 0xa9 / 255f, 1f, 0.22f);
    public static readonly Vector4 BlueLine = new(0x3f / 255f, 0xa9 / 255f, 1f, 1f);
    public static readonly Vector4 YellowFill = new(1f, 0xd2 / 255f, 0x3f / 255f, 0.22f);
    public static readonly Vector4 YellowLine = new(1f, 0xd2 / 255f, 0x3f / 255f, 1f);

    private const float OutlineThickness = 0.05f;

    private readonly IReadOnlyList<TileRect> _blueFill;
    private readonly IReadOnlyList<TileRect> _blueOutline;
    private readonly IReadOnlyList<TileRect> _yellowFill;
    private readonly IReadOnlyList<TileRect> _yellowOutline;
    private GridPos? _previewTile;
    private IReadOnlyList<GridPos>? _previewPath;
    private int _previewAp;

    public MoveRangeOverlay(MoveZones zones)
    {
        Zones = zones;
        _blueFill = TileRegions.FillRuns(zones.Blue);
        _blueOutline = TileRegions.Outline(zones.Blue, OutlineThickness);
        _yellowFill = TileRegions.FillRuns(zones.Yellow);
        _yellowOutline = TileRegions.Outline(zones.Yellow, OutlineThickness);
    }

    public MoveZones Zones { get; }

    /// <summary>The previewed path and its AP cost, or null if the tile is out of range.</summary>
    public (IReadOnlyList<GridPos> Path, int Ap)? PreviewTo(GridPos? tile)
    {
        // Only rebuilt when the hovered tile changes.
        if (tile != _previewTile)
        {
            _previewTile = tile;
            _previewAp = tile is { } t && Zones.ApCostTo(t) is { } ap ? ap : 0;
            _previewPath = _previewAp > 0 ? Zones.Reachable.PathTo(tile!.Value) : null;
        }
        return _previewPath is null ? null : (_previewPath, _previewAp);
    }

    public void Draw(WorldOverlay overlay)
    {
        foreach (var r in _blueFill)
            overlay.FillRect(r.Min, r.Size, BlueFill);
        foreach (var r in _yellowFill)
            overlay.FillRect(r.Min, r.Size, YellowFill);
        foreach (var r in _blueOutline)
            overlay.FillRect(r.Min, r.Size, BlueLine);
        foreach (var r in _yellowOutline)
            overlay.FillRect(r.Min, r.Size, YellowLine);
    }

    /// <summary>Path segments and dots, a destination marker and cover shields.</summary>
    public static void DrawPreview(WorldOverlay overlay, IReadOnlyList<GridPos> path, int ap)
    {
        var color = ap == 1 ? BlueLine : YellowLine;
        var faint = color with { W = 0.6f };
        for (var i = 1; i < path.Count; i++)
            overlay.Line(Center(path[i - 1]), Center(path[i]), 0.06f, faint);
        for (var i = 1; i < path.Count - 1; i++)
            DrawDot(overlay, Center(path[i]), 0.16f, color);

        var dest = path[^1];
        overlay.OutlineTile(dest.X, dest.Y, 0.06f, color);
        DrawDot(overlay, Center(dest), 0.28f, color with { W = 0.85f });
        DrawCoverShields(overlay, dest);
    }

    /// <summary>"1 AP" or "2 AP (Dash)".</summary>
    public static string Label(int ap) => ap == 1 ? "1 AP" : "2 AP (Dash)";

    // Stub: the cover system (#20) draws the shields for the tile's neighbours here.
    private static void DrawCoverShields(WorldOverlay overlay, GridPos dest) { }

    private static Vector2 Center(GridPos p) => new(p.X + 0.5f, p.Y + 0.5f);

    private static void DrawDot(WorldOverlay overlay, Vector2 center, float size, Vector4 color) =>
        overlay.FillRect(center - new Vector2(size / 2f), new Vector2(size), color);
}
