using System.Numerics;
using Firewall.Web.Assets;
using Firewall.Web.Playback;
using Yaeger.ECS;
using Yaeger.Graphics;

namespace Firewall.Web.Rendering;

/// <summary>Draws tracers, muzzle flashes and flash quads from the view model on the effects layer.</summary>
public sealed class EffectsView(World world, AssetRegistry assets)
{
    private static readonly Color Hidden = new(255, 255, 255, 0);

    private readonly List<Entity> _quads = [];
    private readonly List<Entity> _flashes = [];

    public void Sync(IReadOnlyList<EffectVm> effects)
    {
        var quad = 0;
        var flash = 0;
        foreach (var e in effects)
        {
            if (e.Kind == EffectKind.MuzzleFlash)
                DrawMuzzle(Take(_flashes, flash++), e);
            else
                DrawQuad(Take(_quads, quad++), e);
        }
        for (var i = quad; i < _quads.Count; i++)
        {
            world.AddComponent(_quads[i], new Sprite("", Hidden));
            world.AddComponent(_quads[i], new Transform2D(Vector2.Zero, 0f, Vector2.Zero));
        }
        for (var i = flash; i < _flashes.Count; i++)
        {
            world.AddComponent(
                _flashes[i],
                assets.Flipbook("fx.flash").First.ToSpriteSheet(Hidden)
            );
            world.AddComponent(_flashes[i], new Transform2D(Vector2.Zero, 0f, Vector2.Zero));
        }
    }

    private Entity Take(List<Entity> pool, int i)
    {
        if (i < pool.Count)
            return pool[i];
        var entity = world.CreateEntity();
        world.AddComponent(entity, RenderLayers.Of(RenderLayers.Fx));
        pool.Add(entity);
        return entity;
    }

    private void DrawQuad(Entity entity, EffectVm e)
    {
        var fade = 1f - e.Progress;
        var color = new Color(e.Color.R, e.Color.G, e.Color.B, (byte)(e.Color.A * fade));
        world.AddComponent(entity, new Sprite("", color));
        if (e.Kind == EffectKind.Tracer)
        {
            var delta = e.To - e.From;
            world.AddComponent(
                entity,
                new Transform2D(
                    (e.From + e.To) / 2f,
                    MathF.Atan2(delta.Y, delta.X),
                    new Vector2(delta.Length(), e.Size)
                )
            );
        }
        else
            world.AddComponent(
                entity,
                new Transform2D((e.From + e.To) / 2f, 0f, new Vector2(e.Size, e.Size))
            );
    }

    private void DrawMuzzle(Entity entity, EffectVm e)
    {
        var (first, count) = assets.Flipbook("fx.flash");
        var index = first.FrameIndex + Math.Min((int)(e.Progress * count), count - 1);
        var dir = e.To - e.From;
        world.AddComponent(entity, first.ToSpriteSheet(new Color(255, 240, 200)));
        world.AddComponent(entity, new AnimationState(index));
        world.AddComponent(
            entity,
            new Transform2D(e.From, MathF.Atan2(dir.Y, dir.X), new Vector2(e.Size, e.Size))
        );
    }
}
