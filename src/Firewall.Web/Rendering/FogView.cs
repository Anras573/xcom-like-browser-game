using System.Numerics;
using Firewall.Rules;
using Firewall.Rules.Tactical.Vision;
using Yaeger.ECS;
using Yaeger.Graphics;

namespace Firewall.Web.Rendering;

/// <summary>
/// Fog of war (GDD §4.4): one quad per tile on <see cref="RenderLayers.Fog"/>. Unexplored tiles are
/// opaque black, explored-but-unseen tiles a 55 % veil, visible tiles nothing. Call <see cref="Sync"/>
/// every frame; quads are only touched when their tile's state changes.
/// </summary>
public sealed class FogView
{
    public static readonly Color UnexploredColor = new(11, 13, 16, 255);
    public static readonly Color ExploredColor = new(11, 13, 16, 140); // 55 % of 255
    private static readonly Color Clear = new(11, 13, 16, 0);

    private readonly World _world;
    private readonly int _width;
    private readonly int _height;
    private readonly Entity[] _quads;
    private readonly TileVisibility?[] _shown;

    public FogView(World world, int width, int height)
    {
        _world = world;
        _width = width;
        _height = height;
        _quads = new Entity[width * height];
        _shown = new TileVisibility?[width * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var e = world.CreateEntity();
            world.AddComponent(e, RenderLayers.Of(RenderLayers.Fog));
            world.AddComponent(
                e,
                new Transform2D(new Vector2(x + 0.5f, y + 0.5f), 0f, Vector2.One)
            );
            world.AddComponent(e, new Sprite("", Clear));
            _quads[y * width + x] = e;
        }
    }

    public static Color ColorFor(TileVisibility visibility) =>
        visibility switch
        {
            TileVisibility.Unexplored => UnexploredColor,
            TileVisibility.Explored => ExploredColor,
            _ => Clear,
        };

    /// <param name="vision">The squad's visibility, or null to show no fog (the F2 debug toggle).</param>
    public void Sync(VisibilityMap? vision)
    {
        for (var y = 0; y < _height; y++)
        for (var x = 0; x < _width; x++)
        {
            var i = y * _width + x;
            var state = vision?[new GridPos(x, y)] ?? TileVisibility.Visible;
            if (_shown[i] == state)
                continue;
            _shown[i] = state;
            _world.AddComponent(_quads[i], new Sprite("", ColorFor(state)));
        }
    }
}
