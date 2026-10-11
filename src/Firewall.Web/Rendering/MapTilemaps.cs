using System.Numerics;
using Firewall.Rules;
using Firewall.Rules.Tactical;
using Firewall.Web.Assets;
using Yaeger.ECS;
using Yaeger.Graphics;

namespace Firewall.Web.Rendering;

/// <summary>
/// A <see cref="TacticalMap"/> drawn as four engine <see cref="Tilemap"/>s at the world origin
/// (tile size 1, so grid <c>(x, y)</c> is world <c>(x, y)</c>): ground, rubble decals, props
/// (walls, crates, trees, ...) and a details layer for window glass. Follows
/// <see cref="TacticalMap.TileChanged"/> by touching only the cells that changed.
/// </summary>
public sealed class MapTilemaps : IDisposable
{
    private readonly TacticalMap _map;
    private readonly AssetRegistry _registry;
    private readonly Tilemap _decals;
    private readonly Tilemap _props;
    private readonly Tilemap _details;
    private readonly Entity[] _entities;
    private readonly World _world;

    private MapTilemaps(World world, TacticalMap map, AssetRegistry registry)
    {
        _world = world;
        _map = map;
        _registry = registry;
        var tileset = new Tileset("assets/tiles.png", 27, 20);
        var ground = new Tilemap(tileset, map.Width, map.Height);
        _decals = new Tilemap(tileset, map.Width, map.Height);
        _props = new Tilemap(tileset, map.Width, map.Height);
        _details = new Tilemap(tileset, map.Width, map.Height);
        _entities =
        [
            Add(ground, RenderLayers.Ground),
            Add(_decals, RenderLayers.GroundDecal),
            Add(_props, RenderLayers.Props),
            Add(_details, RenderLayers.PropsDetail),
        ];

        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            ground.SetTile(
                x,
                MapTiling.RowOf(y, map.Height),
                Index(MapTiling.GroundFrame(map.Biome, map[new GridPos(x, y)], x, y))
            );
            RefreshProps(x, y);
        }
        map.TileChanged += OnTileChanged;
    }

    public static MapTilemaps Build(World world, TacticalMap map, AssetRegistry registry) =>
        new(world, map, registry);

    public Tilemap Props => _props;

    public void Dispose()
    {
        _map.TileChanged -= OnTileChanged;
        foreach (var e in _entities)
            _world.DestroyEntity(e);
    }

    private Entity Add(Tilemap tilemap, int layer)
    {
        var e = _world.CreateEntity();
        _world.AddComponent(e, new Transform2D(Vector2.Zero)); // bottom-left corner of the map
        _world.AddComponent(e, tilemap);
        _world.AddComponent(e, RenderLayers.Of(layer));
        return e;
    }

    private int Index(string frame) => _registry.Frame(frame).FrameIndex;

    private int IndexOrEmpty(string? frame) => frame is null ? Tilemap.EmptyTile : Index(frame);

    private void OnTileChanged(TileChange change)
    {
        var (x, y) = (change.Pos.X, change.Pos.Y);
        var row = MapTiling.RowOf(y, _map.Height);
        if (MapTiling.RubbleFrame(change.Old, change.New, x, y) is { } rubble)
            _decals.SetTile(x, row, Index(rubble));
        // The wall mask looks at the 8 neighbours, so only those can change look with it.
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
            if (_map.InBounds(new GridPos(x + dx, y + dy)))
                RefreshProps(x + dx, y + dy);
    }

    private void RefreshProps(int x, int y)
    {
        var type = _map[new GridPos(x, y)];
        var row = MapTiling.RowOf(y, _map.Height);
        var prop = MapTiling.PropFrame(
            _map.Biome,
            type,
            x,
            y,
            (cx, cy) => MapTiling.CountsAsWall(_map, cx, cy)
        );
        _props.SetTile(x, row, IndexOrEmpty(prop));
        _details.SetTile(x, row, IndexOrEmpty(MapTiling.DetailFrame(type)));
    }
}
