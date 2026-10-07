using Yaeger.Browser;
using Yaeger.Graphics;

namespace Firewall.Web.Rendering;

/// <summary>Font, size (px), colour and shadow for one kind of text.</summary>
public readonly record struct TextStyle(string Family, int Size, Color Color, bool Shadow = false)
{
    public FontHandle Font => new(Family);

    public TextStyle With(Color color) => this with { Color = color };
}

/// <summary>
/// One place defining how each role of text looks. Screen sizes are logical UI pixels; world
/// text sizes are CSS pixels (see <see cref="WorldText"/>). Call <see cref="LoadAsync"/> before
/// first use.
/// </summary>
public static class TextStyles
{
    public const string Future = "KenneyFuture";
    public const string FutureNarrow = "KenneyFutureNarrow";
    public const string MiniSquare = "KenneyMiniSquare";

    /// <summary>Family and URL (base-relative) of every shipped font.</summary>
    public static readonly IReadOnlyList<(string Family, string Url)> Fonts =
    [
        (Future, "assets/fonts/KenneyFuture.ttf"),
        (FutureNarrow, "assets/fonts/KenneyFutureNarrow.ttf"),
        (MiniSquare, "assets/fonts/KenneyMiniSquare.ttf"),
    ];

    public static readonly TextStyle Heading = new(Future, 28, new Color(255, 220, 90));
    public static readonly TextStyle Body = new(FutureNarrow, 18, new Color(225, 230, 240));
    public static readonly TextStyle Small = new(MiniSquare, 14, new Color(190, 200, 215));
    public static readonly TextStyle Damage = new(MiniSquare, 22, Color.White, Shadow: true);
    public static readonly TextStyle Crit = new(
        MiniSquare,
        30,
        new Color(255, 90, 60),
        Shadow: true
    );
    public static readonly TextStyle Tooltip = new(FutureNarrow, 15, new Color(240, 240, 240));

    /// <summary>Loads every font; fonts must be loaded before first use.</summary>
    public static Task LoadAsync() =>
        Task.WhenAll(Fonts.Select(f => BrowserTextRenderSurface.LoadFontAsync(f.Family, f.Url)));
}
