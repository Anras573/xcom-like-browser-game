using System.Numerics;
using Firewall.Rules;
using Firewall.Rules.Tactical;
using Firewall.Web.Assets;
using Yaeger.ECS;
using Yaeger.Graphics;

namespace Firewall.Web.Rendering;

/// <summary>
/// Half and full shield icons on the sides of a hovered destination tile that have cover. They
/// turn red when a visible enemy would flank the tile. Call <see cref="Sync"/> every frame.
/// </summary>
public sealed class CoverShieldsView
{
    private const float Inset = 0.34f; // icon centre, from the tile centre
    private const float IconSize = 0.44f;

    public static readonly Color Normal = new(255, 255, 255, 235);
    public static readonly Color Flanked = new(255, 70, 60, 255);
    private static readonly Color Hidden = new(255, 255, 255, 0);

    private static readonly Vector2[] Offsets =
    [
        new(0f, Inset), // N
        new(Inset, 0f), // E
        new(0f, -Inset), // S
        new(-Inset, 0f), // W
    ];

    private readonly World _world;
    private readonly AssetRegistry _assets;
    private readonly Entity[] _icons = new Entity[4];

    public CoverShieldsView(World world, AssetRegistry assets)
    {
        _world = world;
        _assets = assets;
        for (var i = 0; i < _icons.Length; i++)
        {
            _icons[i] = world.CreateEntity();
            world.AddComponent(_icons[i], RenderLayers.Of(RenderLayers.UnitOverlay));
            Hide(i);
        }
    }

    /// <summary>The frame for a cover level, or null for none.</summary>
    public static string? FrameName(CoverLevel level) =>
        level switch
        {
            CoverLevel.Half => "overlay.shield_half",
            CoverLevel.Full => "overlay.shield_full",
            _ => null,
        };

    /// <summary>Shows the shields for <paramref name="tile"/>, or hides them when it is null.</summary>
    public void Sync(TacticalMap map, GridPos? tile, bool flanked)
    {
        var profile = tile is { } t ? CoverCalculator.GetProfile(map, t) : CoverProfile.None;
        Span<CoverLevel> levels = [profile.North, profile.East, profile.South, profile.West];
        for (var i = 0; i < _icons.Length; i++)
        {
            if (tile is not { } pos || FrameName(levels[i]) is not { } name)
            {
                Hide(i);
                continue;
            }
            var frame = _assets.Frame(name);
            var center = new Vector2(pos.X + 0.5f, pos.Y + 0.5f) + Offsets[i];
            _world.AddComponent(_icons[i], frame.ToSpriteSheet(flanked ? Flanked : Normal));
            _world.AddComponent(_icons[i], new AnimationState(frame.FrameIndex));
            _world.AddComponent(
                _icons[i],
                new Transform2D(center, 0f, new Vector2(IconSize, IconSize))
            );
        }
    }

    private void Hide(int i)
    {
        var frame = _assets.Frame("overlay.shield_half");
        _world.AddComponent(_icons[i], frame.ToSpriteSheet(Hidden));
        _world.AddComponent(_icons[i], new AnimationState(frame.FrameIndex));
        _world.AddComponent(_icons[i], new Transform2D(Vector2.Zero));
    }
}
