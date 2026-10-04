using System.Numerics;
using Firewall.Web.Rendering;
using Yaeger.ECS;
using Yaeger.Graphics;

namespace Firewall.Web.Assets;

/// <summary>
/// Exercises the rendering setup: a 20x20 ground <see cref="Tilemap"/>, a props tilemap above
/// it, rotated <see cref="SpriteSheet"/> characters, and (drawn by <c>GameApp</c>) a world
/// overlay and a screen-space panel.
/// </summary>
public static class DebugTacticalScene
{
    public const int MapSize = 20;

    /// <summary>Tile of the soldier whose move range the world overlay highlights.</summary>
    public static readonly (int X, int Y) Soldier = (9, 9);

    private static readonly string[] Floors =
    [
        "ground.concrete.0",
        "ground.concrete.1",
        "ground.concrete.2",
    ];

    public static void Build(World world, AssetRegistry registry)
    {
        var tileset = new Tileset("assets/tiles.png", 27, 20);

        var ground = new int[MapSize * MapSize];
        var decals = new int[MapSize * MapSize];
        var props = new int[MapSize * MapSize];
        Array.Fill(decals, Tilemap.EmptyTile);
        Array.Fill(props, Tilemap.EmptyTile);

        for (var row = 0; row < MapSize; row++)
        for (var col = 0; col < MapSize; col++)
        {
            var i = row * MapSize + col;
            ground[i] = registry.Frame(Floors[(col * 7 + row * 3) % Floors.Length]).FrameIndex;
            if ((col + row * 5) % 11 == 0)
                decals[i] = registry.Frame("decal.rubble.0").FrameIndex;
        }

        // Row 0 is the top of the map, so world y = MapSize - 1 - row.
        for (var col = 3; col < 17; col++)
        {
            props[3 * MapSize + col] = registry.Frame("wall.grey.straight_h").FrameIndex;
            props[16 * MapSize + col] = registry.Frame("wall.grey.straight_h").FrameIndex;
        }
        props[6 * MapSize + 6] = registry.Frame("prop.crate.large").FrameIndex;
        props[7 * MapSize + 12] = registry.Frame("prop.crate.small").FrameIndex;
        props[12 * MapSize + 7] = registry.Frame("prop.rock.0").FrameIndex;
        props[11 * MapSize + 13] = registry.Frame("prop.bush.green").FrameIndex;

        AddTilemap(world, new Tilemap(tileset, MapSize, MapSize, ground), RenderLayers.Ground);
        AddTilemap(world, new Tilemap(tileset, MapSize, MapSize, decals), RenderLayers.GroundDecal);
        AddTilemap(world, new Tilemap(tileset, MapSize, MapSize, props), RenderLayers.Props);

        AddUnit(world, registry, "unit.soldier1.machine", Soldier.X, Soldier.Y, MathF.PI / 4f);
        AddUnit(world, registry, "unit.manBlue.gun", 8, 11, MathF.PI);
        AddUnit(world, registry, "unit.zombie1.stand", 13, 8, -MathF.PI / 2f);
        AddUnit(world, registry, "unit.robot1.gun", 11, 13, 0f);
    }

    private static void AddTilemap(World world, Tilemap map, int layer)
    {
        var e = world.CreateEntity();
        world.AddComponent(e, new Transform2D(Vector2.Zero)); // bottom-left corner of the map
        world.AddComponent(e, map);
        world.AddComponent(e, RenderLayers.Of(layer));
    }

    private static void AddUnit(
        World world,
        AssetRegistry registry,
        string frameName,
        int tileX,
        int tileY,
        float rotation
    )
    {
        var frame = registry.Frame(frameName);
        var e = world.CreateEntity();
        world.AddComponent(e, new Transform2D(TacticalCamera.TileCenter(tileX, tileY), rotation));
        world.AddComponent(e, frame.ToSpriteSheet());
        world.AddComponent(e, new AnimationState(frame.FrameIndex));
        world.AddComponent(e, RenderLayers.Of(RenderLayers.Units));
    }
}
