using Yaeger.Graphics;

namespace Firewall.Web.Rendering;

/// <summary>
/// <see cref="RenderLayer"/> values for every world entity, in draw order (lowest first).
/// Gaps leave room to slot a layer in later without renumbering.
/// </summary>
public static class RenderLayers
{
    /// <summary>Ground <see cref="Tilemap"/>.</summary>
    public const int Ground = 0;

    /// <summary>Decals on the ground (rubble, stains).</summary>
    public const int GroundDecal = 10;

    /// <summary>Props <see cref="Tilemap"/>: walls, crates, trees.</summary>
    public const int Props = 20;

    public const int Units = 30;

    /// <summary>Move-range and similar tile highlights; above props, below units and world text, so they tint neither.</summary>
    public const int Highlights = 25;

    /// <summary>HP pips and status icons.</summary>
    public const int UnitOverlay = 40;

    /// <summary>Particles and other effects.</summary>
    public const int Fx = 50;

    /// <summary>Drop shadow behind <see cref="WorldText"/>.</summary>
    public const int WorldTextShadow = 55;

    /// <summary>Floating damage numbers and similar world-space text.</summary>
    public const int WorldText = 60;

    /// <summary>All layers in draw order.</summary>
    public static readonly IReadOnlyList<int> InOrder =
    [
        Ground,
        GroundDecal,
        Props,
        Highlights,
        Units,
        UnitOverlay,
        Fx,
        WorldTextShadow,
        WorldText,
    ];

    public static RenderLayer Of(int layer) => new(layer);
}
