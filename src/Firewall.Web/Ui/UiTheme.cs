using System.Numerics;
using Firewall.Web.Rendering;
using Yaeger.Graphics;

namespace Firewall.Web.Ui;

public enum ButtonStyle
{
    Normal,
    Primary,
    Danger,
}

public enum ButtonVisual
{
    Normal,
    Hover,
    Pressed,
    Disabled,
}

public enum BarColor
{
    Blue,
    Green,
    Red,
    Yellow,
}

/// <summary>Colours, padding and text styles for every widget, in one place.</summary>
public sealed class UiTheme
{
    public static Vector4 Hex(int rgb, float alpha = 1f) =>
        new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, alpha);

    // Palette (issue #13).
    public Vector4 PanelTint { get; init; } = Hex(0x1b2330, 0.92f);
    public Vector4 Accent { get; init; } = Hex(0x3fd0ff);
    public Vector4 Warning { get; init; } = Hex(0xffb000);
    public Vector4 Danger { get; init; } = Hex(0xff4d4d);
    public Vector4 Success { get; init; } = Hex(0x6ee07a);

    // The atlas panels are light grey, so a tint multiplies them down to the wanted shade.
    public Vector4 ButtonNormal { get; init; } = Hex(0x3a4a63);
    public Vector4 ButtonHover { get; init; } = Hex(0x4f6a8f);
    public Vector4 ButtonPressed { get; init; } = Hex(0x233044);
    public Vector4 ButtonDisabled { get; init; } = Hex(0x2a303a, 0.8f);
    public Vector4 Dim { get; init; } = new(0f, 0f, 0f, 0.55f);
    public Vector4 TooltipTint { get; init; } = Hex(0x10151d, 0.96f);

    public float Padding { get; init; } = 10f;
    public float TitleBarHeight { get; init; } = 30f;
    public float TooltipDelay { get; init; } = TooltipTimer.DefaultDelay;

    public TextStyle Title { get; init; } =
        TextStyles.Body with
        {
            Family = TextStyles.Future,
            Size = 16,
            Color = new Color(63, 208, 255),
        };
    public TextStyle Body { get; init; } = TextStyles.Body;
    public TextStyle Small { get; init; } = TextStyles.Small;
    public TextStyle ButtonText { get; init; } = TextStyles.Body with { Color = Color.White };
    public TextStyle DisabledText { get; init; } =
        TextStyles.Body with
        {
            Color = new Color(120, 128, 140),
        };
    public TextStyle TooltipText { get; init; } = TextStyles.Tooltip;

    public Vector4 ButtonTint(ButtonVisual v, ButtonStyle style)
    {
        var accent = style switch
        {
            ButtonStyle.Primary => Accent,
            ButtonStyle.Danger => Danger,
            _ => Vector4.Zero,
        };
        var baseTint = v switch
        {
            ButtonVisual.Hover => ButtonHover,
            ButtonVisual.Pressed => ButtonPressed,
            ButtonVisual.Disabled => ButtonDisabled,
            _ => ButtonNormal,
        };
        if (accent == Vector4.Zero || v == ButtonVisual.Disabled)
            return baseTint;
        // Accent buttons lean towards their colour, darker when pressed.
        var k =
            v == ButtonVisual.Pressed ? 0.35f
            : v == ButtonVisual.Hover ? 0.75f
            : 0.55f;
        var mixed = Vector4.Lerp(baseTint, accent, k);
        return new Vector4(mixed.X, mixed.Y, mixed.Z, 1f);
    }

    public string BarRegion(BarColor c) =>
        c switch
        {
            BarColor.Green => "ui.bar.green",
            BarColor.Red => "ui.bar.red",
            BarColor.Yellow => "ui.bar.yellow",
            _ => "ui.bar.blue",
        };
}
