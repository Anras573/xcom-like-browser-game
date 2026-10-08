using System.Numerics;
using Firewall.Web.Rendering;
using Yaeger.ECS;
using Yaeger.Graphics;

namespace Firewall.Web.Assets;

/// <summary>
/// Debug content for text: a world label that follows a moving sprite and looping outlined
/// damage numbers (both <see cref="WorldText"/>), and a screen-space panel with a heading, a
/// wrapped paragraph and inline-coloured text (drawn by <c>GameApp</c>).
/// </summary>
public sealed class DebugTextScene
{
    private const string Paragraph =
        "Squad tactics in the browser. This paragraph wraps inside a fixed width using Kenney Future Narrow, "
        + "and stays sharp at every zoom level and on HiDPI screens.";

    public const string RichLine =
        "Hit chance [c=#7CFC00]72%[/c]  Damage [c=#FF6347]4-6[/c] [c=#ffd700]crit[/c]";

    private readonly World _world;
    private readonly Entity _mover;
    private readonly WorldText _label;
    private readonly WorldText[] _numbers;
    private float _time;

    public DebugTextScene(World world, AssetRegistry registry)
    {
        _world = world;
        var frame = registry.Frame("unit.zombie1.stand");
        _mover = world.CreateEntity();
        world.AddComponent(_mover, new Transform2D(Vector2.Zero));
        world.AddComponent(_mover, frame.ToSpriteSheet());
        world.AddComponent(_mover, new AnimationState(frame.FrameIndex));
        world.AddComponent(_mover, RenderLayers.Of(RenderLayers.Units));

        _label = WorldText.Create(
            world,
            "Zombie  HP 12/12",
            TextStyles.Small.With(Color.White) with
            {
                Shadow = true,
            },
            Vector2.Zero,
            32f
        );
        _numbers =
        [
            WorldText.Create(world, "-5", TextStyles.Damage, Vector2.Zero, 32f),
            WorldText.Create(world, "-11!", TextStyles.Crit, Vector2.Zero, 32f),
        ];
    }

    /// <summary>Moves the sprite around its circuit and re-places the text at the current zoom.</summary>
    public void Update(double deltaSeconds, float pixelsPerTile)
    {
        _time += (float)deltaSeconds;
        var pos = new Vector2(
            14f + 2f * MathF.Cos(_time * 0.6f),
            13f + 1.5f * MathF.Sin(_time * 0.6f)
        );
        _world.AddComponent(_mover, new Transform2D(pos, _time * 0.6f + MathF.PI));

        _label.Place(_world, pos + new Vector2(-0.8f, 0.9f), pixelsPerTile);

        // Damage numbers rise from the soldier's tile and restart every two seconds.
        var (sx, sy) = DebugTacticalScene.Soldier;
        for (var i = 0; i < _numbers.Length; i++)
        {
            var t = (_time + i) % 2f;
            _numbers[i]
                .Place(
                    _world,
                    new Vector2(sx + 0.1f + i * 1.2f, sy + 1.2f + t * 0.6f),
                    pixelsPerTile
                );
        }
    }

    // Stays in the top-right of the letterboxed 1280x720 UI.
    public static void DrawPanel(ScreenCanvas canvas)
    {
        var x = UiSpace.LogicalWidth - 396f;
        canvas.FillRect(
            new Vector2(x - 12, 16),
            new Vector2(392, 300),
            new Vector4(0f, 0f, 0f, 0.6f)
        );
        canvas.Text("Heading style", new Vector2(x, 24), TextStyles.Heading);
        canvas.WrappedText(Paragraph, new Vector2(x, 66), TextStyles.Body, 368f);
        canvas.RichText(RichLine, new Vector2(x, 176), TextStyles.Body);
        canvas.Text("Small: HP 12/12  AP 2/2", new Vector2(x, 206), TextStyles.Small);
        canvas.Text("-5", new Vector2(x, 236), TextStyles.Damage);
        canvas.Text("-11!", new Vector2(x + 60, 232), TextStyles.Crit);
        canvas.Text("Tooltip: Overwatch", new Vector2(x, 280), TextStyles.Tooltip);
    }
}
