using System.Numerics;
using Firewall.Web.Playback;
using Yaeger.ECS;
using Yaeger.Graphics;

namespace Firewall.Web.Rendering;

/// <summary>
/// Draws the view model's floating texts as pooled world-space <see cref="Text"/> entities with a
/// drop shadow. Entities are reused; unused ones are made fully transparent.
/// </summary>
public sealed class FloatersView(World world)
{
    // Glyph width as a fraction of the font size, to centre text without measuring it.
    private const float GlyphWidth = 0.6f;

    private readonly List<WorldText> _pool = [];

    public int PoolSize => _pool.Count;

    public void Sync(IReadOnlyList<FloaterVm> floaters, float pixelsPerTile)
    {
        var font = TextStyles.Damage.Font;
        while (_pool.Count < floaters.Count)
            _pool.Add(
                WorldText.Create(
                    world,
                    "",
                    TextStyles.Damage,
                    Vector2.Zero,
                    Math.Max(pixelsPerTile, 1f)
                )
            );

        for (var i = 0; i < _pool.Count; i++)
        {
            var text = _pool[i];
            if (i >= floaters.Count)
            {
                world.AddComponent(text.Main, new Text(" ", font, 1, new Color(0, 0, 0, 0)));
                if (text.Shadow is { } hidden)
                    world.AddComponent(hidden, new Text(" ", font, 1, new Color(0, 0, 0, 0)));
                continue;
            }
            var f = floaters[i];
            var alpha = (byte)(255 * f.Alpha);
            world.AddComponent(
                text.Main,
                new Text(f.Text, font, f.Size, new Color(f.Color.R, f.Color.G, f.Color.B, alpha))
            );
            if (text.Shadow is { } shadow)
                world.AddComponent(
                    shadow,
                    new Text(
                        f.Text,
                        font,
                        f.Size,
                        new Color(0, 0, 0, (byte)(WorldText.ShadowColor.A * f.Alpha))
                    )
                );
            var width = f.Text.Length * f.Size * GlyphWidth / pixelsPerTile;
            text.Place(world, f.Position - new Vector2(width / 2f, 0f), pixelsPerTile);
        }
    }
}
