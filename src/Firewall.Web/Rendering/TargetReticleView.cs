using System.Numerics;
using Firewall.Rules;
using Firewall.Web.Assets;
using Yaeger.ECS;
using Yaeger.Graphics;

namespace Firewall.Web.Rendering;

/// <summary>A slowly turning, pulsing crosshair on the current shot target. Call <see cref="Sync"/> every frame.</summary>
public sealed class TargetReticleView
{
    private const string FrameName = "overlay.crosshair";
    private const float Size = 1.15f;
    private const float TurnsPerSecond = 0.12f;

    private static readonly Color Tint = new(255, 90, 70, 255);
    private static readonly Color Hidden = new(255, 255, 255, 0);

    private readonly World _world;
    private readonly AssetRegistry _assets;
    private readonly Entity _entity;

    public TargetReticleView(World world, AssetRegistry assets)
    {
        _world = world;
        _assets = assets;
        _entity = world.CreateEntity();
        world.AddComponent(_entity, RenderLayers.Of(RenderLayers.UnitOverlay));
        Sync(null, 0f);
    }

    /// <summary>Shows the reticle on <paramref name="tile"/>, or hides it when null.</summary>
    public void Sync(GridPos? tile, float time)
    {
        var frame = _assets.Frame(FrameName);
        _world.AddComponent(_entity, frame.ToSpriteSheet(tile is null ? Hidden : Tint));
        _world.AddComponent(_entity, new AnimationState(frame.FrameIndex));
        if (tile is not { } t)
        {
            _world.AddComponent(_entity, new Transform2D(Vector2.Zero));
            return;
        }
        var pulse = Size * (1f + 0.06f * MathF.Sin(time * 5f));
        _world.AddComponent(
            _entity,
            new Transform2D(
                TacticalCamera.TileCenter(t.X, t.Y),
                time * TurnsPerSecond * MathF.Tau,
                new Vector2(pulse, pulse)
            )
        );
    }
}
