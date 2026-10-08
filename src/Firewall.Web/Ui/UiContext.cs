using System.Numerics;
using Firewall.Web.Assets;
using Firewall.Web.Input;
using Firewall.Web.Rendering;
using Yaeger.Input;
using Yaeger.Platform;

namespace Firewall.Web.Ui;

public enum UiSound
{
    Click,
}

/// <summary>
/// Immediate-mode UI drawn in the screen pass, in logical 1280x720 pixels. Call
/// <see cref="Begin"/> at the top of the screen pass, then widgets in back-to-front order, then
/// <see cref="End"/> (which draws tooltips and toasts on top). A widget draws and reports input
/// in the same call: <c>if (ui.Button(rect, "Recruit")) {...}</c>.
/// </summary>
/// <remarks>
/// Hit-testing maps the mouse through <see cref="UiSpace"/>, so it matches the letterboxed
/// drawing exactly. Panels, buttons and modals swallow the click via <see cref="ClickGate"/>;
/// the world should also skip hover/picking while <see cref="PointerOverUi"/> is true (it
/// reports last frame's layout, since the world updates before the screen pass).
/// </remarks>
public sealed class UiContext(
    IInputState input,
    ClickGate clicks,
    InputBindings bindings,
    AssetRegistry assets,
    UiTheme? theme = null
)
{
    public const int ModalPending = -1;
    public const int ModalDismissed = -2;

    private const float ToastSeconds = 0.4f;
    private const float WheelScale = 0.5f;

    private readonly UiInput _input = new(input, clicks);
    private readonly TooltipTimer _tooltip = new()
    {
        Delay = (theme ?? new UiTheme()).TooltipDelay,
    };
    private readonly Dictionary<int, float> _scroll = [];
    private readonly List<UiRect> _blockers = [];
    private readonly List<UiRect> _prevBlockers = [];
    private readonly List<(string Text, float Left, float Total)> _toasts = [];
    private ScreenCanvas _canvas;
    private float _dt;
    private int _tooltipCandidate;
    private string _tooltipText = "";
    private bool _modalThisFrame;
    private bool _modalPrev;
    private bool _modalWasOpenBeforeThisFrame;
    private bool _inModal;
    private bool _begun;

    public UiTheme Theme { get; } = theme ?? new UiTheme();

    /// <summary>Raised for UI sounds (button click); wire to audio later.</summary>
    public Action<UiSound>? Sound { get; set; }

    public Vector2 Mouse => _input.Mouse;

    public bool PointerOverUi => _prevBlockers.Exists(r => r.Contains(_input.Mouse));

    public bool TooltipVisible => _tooltip.Visible;

    /// <summary>Items actually submitted by the last <see cref="ScrollList"/>; for the gallery and tests.</summary>
    public int LastListItemsDrawn { get; private set; }

    public void Begin(ScreenCanvas canvas, UiSpace space, float deltaSeconds)
    {
        _canvas = canvas;
        _dt = deltaSeconds;
        _input.Begin(space);
        _tooltipCandidate = 0;
        _modalWasOpenBeforeThisFrame = _modalPrev;
        _modalThisFrame = false;
        _begun = true;
    }

    public void End()
    {
        if (!_begun)
            return;
        _begun = false;

        _tooltip.Update(_dt, _tooltipCandidate);
        if (_tooltip.Visible)
            DrawTooltip();
        DrawToasts();
        _input.End();

        _modalPrev = _modalThisFrame;
        _prevBlockers.Clear();
        _prevBlockers.AddRange(_blockers);
        _blockers.Clear();
    }

    /// <summary>
    /// Set by the scene manager for scenes that are shown but must not react: those under an
    /// overlay or a fade. Widgets still draw, but report no hover, clicks, scrolling or hotkeys.
    /// </summary>
    public bool InputSuspended { get; set; }

    private bool Blocked => InputSuspended || (_modalWasOpenBeforeThisFrame && !_inModal);

    private static int IdOf(string key, UiRect r) => HashCode.Combine(key, (int)r.X, (int)r.Y);

    // ---------------------------------------------------------------- drawing helpers

    private void Slice(string region, UiRect rect, Vector4 tint)
    {
        var r = assets.Ui(region);
        foreach (var part in NineSlice.Compute(r, rect))
            _canvas.Image(
                r.TexturePath,
                part.Rect.Position,
                part.Rect.Size,
                part.UvMin,
                part.UvMax,
                tint
            );
    }

    private void Icon(string region, UiRect rect, Vector4 tint)
    {
        var r = assets.Ui(region);
        _canvas.Image(r.TexturePath, rect.Position, rect.Size, r.UvMin, r.UvMax, tint);
    }

    private void Fill(UiRect r, Vector4 c) => _canvas.FillRect(r.Position, r.Size, c);

    private void CenteredText(UiRect rect, string text, TextStyle style)
    {
        var w = _canvas.Measure(text, style);
        _canvas.RichText(
            text,
            new Vector2(rect.X + (rect.W - w) / 2f, rect.Y + (rect.H - style.Size) / 2f),
            style
        );
    }

    private void RequestTooltip(int id, string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        _tooltipCandidate = id;
        _tooltipText = text;
    }

    // ---------------------------------------------------------------- panel

    /// <summary>9-slice panel with an optional title bar. Returns the content area inside it.</summary>
    public UiRect Panel(UiRect rect, string? title = null)
    {
        Slice("ui.panel_rectangle", rect, Theme.PanelTint);
        _blockers.Add(rect);
        if (!Blocked)
            _input.Swallow(rect);

        var content = rect.Inset(Theme.Padding);
        if (string.IsNullOrEmpty(title))
            return content;

        var bar = new UiRect(rect.X + 4, rect.Y + 4, rect.W - 8, Theme.TitleBarHeight);
        Fill(bar, new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0.18f));
        Fill(new UiRect(bar.X, bar.Bottom - 2, bar.W, 2), Theme.Accent);
        _canvas.Text(
            title,
            new Vector2(bar.X + Theme.Padding, bar.Y + (bar.H - Theme.Title.Size) / 2f),
            Theme.Title
        );
        return new UiRect(
            content.X,
            bar.Bottom + 6,
            content.W,
            MathF.Max(0f, rect.Bottom - Theme.Padding - bar.Bottom - 6)
        );
    }

    // ---------------------------------------------------------------- label

    public void Label(
        UiRect rect,
        string text,
        TextStyle? style = null,
        TextAlignment align = TextAlignment.Left,
        bool wrap = false
    )
    {
        var s = style ?? Theme.Body;
        if (wrap)
        {
            _canvas.WrappedText(text, rect.Position, s, rect.W, align);
            return;
        }
        var w = _canvas.Measure(text, s);
        var x = align switch
        {
            TextAlignment.Center => rect.X + (rect.W - w) / 2f,
            TextAlignment.Right => rect.Right - w,
            _ => rect.X,
        };
        _canvas.RichText(text, new Vector2(x, rect.Y + MathF.Max(0f, (rect.H - s.Size) / 2f)), s);
    }

    // ---------------------------------------------------------------- buttons

    /// <summary>Draws a button in a given visual state; <see cref="Button"/> picks the state from input.</summary>
    public void ButtonVisualAt(
        UiRect rect,
        string text,
        ButtonVisual visual,
        ButtonStyle style = ButtonStyle.Normal
    )
    {
        Slice(
            visual == ButtonVisual.Pressed ? "ui.button_rectangle" : "ui.button_rectangle_depth",
            visual == ButtonVisual.Pressed ? rect with { Y = rect.Y + 2, H = rect.H - 2 } : rect,
            Theme.ButtonTint(visual, style)
        );
        var textRect = visual == ButtonVisual.Pressed ? rect with { Y = rect.Y + 1 } : rect;
        CenteredText(
            textRect,
            text,
            visual == ButtonVisual.Disabled ? Theme.DisabledText : Theme.ButtonText
        );
    }

    /// <returns>True on the frame the button is clicked (pressed and released inside it).</returns>
    public bool Button(
        UiRect rect,
        string text,
        ButtonStyle style = ButtonStyle.Normal,
        bool enabled = true,
        string? tooltip = null
    )
    {
        var id = IdOf(text, rect);
        _blockers.Add(rect);
        var state = _input.Interact(id, rect, enabled, Blocked);
        var visual =
            !enabled ? ButtonVisual.Disabled
            : state.Down ? ButtonVisual.Pressed
            : state.Hover ? ButtonVisual.Hover
            : ButtonVisual.Normal;
        ButtonVisualAt(rect, text, visual, style);

        if (state.Hover)
            RequestTooltip(id, tooltip);
        if (state.Clicked)
        {
            _tooltip.Reset();
            Sound?.Invoke(UiSound.Click);
        }
        return state.Clicked;
    }

    public void IconButtonVisualAt(
        UiRect rect,
        string icon,
        ButtonVisual visual,
        string? hotkeyLabel = null,
        int cooldown = 0,
        bool selected = false
    )
    {
        var tint = selected
            ? Theme.ButtonTint(ButtonVisual.Hover, ButtonStyle.Primary)
            : Theme.ButtonTint(visual, ButtonStyle.Normal);
        Slice("ui.button_square", rect, tint);
        var inner = rect.Inset(MathF.Round(rect.W * 0.18f));
        Icon(
            icon,
            inner,
            visual == ButtonVisual.Disabled ? new Vector4(1f, 1f, 1f, 0.35f) : Vector4.One
        );

        if (cooldown > 0)
        {
            Fill(rect.Inset(3), new Vector4(0f, 0f, 0f, 0.65f));
            CenteredText(
                rect,
                cooldown.ToString(),
                Theme.Title with
                {
                    Size = 22,
                    Color = new Yaeger.Graphics.Color(255, 176, 0),
                }
            );
        }
        if (!string.IsNullOrEmpty(hotkeyLabel))
            _canvas.Text(
                hotkeyLabel,
                new Vector2(rect.X + 5, rect.Y + 3),
                Theme.Small with
                {
                    Shadow = true,
                }
            );
    }

    /// <summary>Ability-bar button: an icon, optional hotkey number and a cooldown (turns) overlay that disables it.</summary>
    public bool IconButton(
        UiRect rect,
        string icon,
        string? hotkeyLabel = null,
        string? tooltip = null,
        int cooldown = 0,
        bool enabled = true,
        bool selected = false
    )
    {
        var usable = enabled && cooldown <= 0;
        var id = IdOf(icon, rect);
        _blockers.Add(rect);
        var state = _input.Interact(id, rect, usable, Blocked);
        var visual =
            !usable ? ButtonVisual.Disabled
            : state.Down ? ButtonVisual.Pressed
            : state.Hover ? ButtonVisual.Hover
            : ButtonVisual.Normal;
        IconButtonVisualAt(rect, icon, visual, hotkeyLabel, cooldown, selected);

        if (state.Hover)
            RequestTooltip(id, tooltip);
        if (state.Clicked)
        {
            _tooltip.Reset();
            Sound?.Invoke(UiSound.Click);
        }
        return state.Clicked;
    }

    // ---------------------------------------------------------------- progress bar

    /// <summary>
    /// Filled bar. With <paramref name="segments"/> &gt; 0 it is drawn as that many pips (HP pips),
    /// each lit when <c>value</c> covers it.
    /// </summary>
    public void ProgressBar(
        UiRect rect,
        float value,
        float max,
        BarColor color = BarColor.Green,
        int segments = 0
    )
    {
        var frac = max <= 0f ? 0f : Math.Clamp(value / max, 0f, 1f);
        if (segments > 0)
        {
            const float gap = 2f;
            var w = (rect.W - gap * (segments - 1)) / segments;
            var lit = (int)MathF.Ceiling(frac * segments - 1e-4f);
            var fill = color switch
            {
                BarColor.Red => Theme.Danger,
                BarColor.Yellow => Theme.Warning,
                BarColor.Blue => Theme.Accent,
                _ => Theme.Success,
            };
            for (var i = 0; i < segments; i++)
            {
                var pip = new UiRect(rect.X + i * (w + gap), rect.Y, w, rect.H);
                Fill(pip, i < lit ? fill : new Vector4(0f, 0f, 0f, 0.55f));
            }
            return;
        }

        ThreePart("ui.bar_shadow", rect);
        if (frac > 0f)
            ThreePart(
                Theme.BarRegion(color),
                rect with
                {
                    W = MathF.Max(rect.H * 0.5f, rect.W * frac),
                }
            );
    }

    // l / m / r strips: caps keep their aspect at the bar height, the middle stretches.
    private void ThreePart(string prefix, UiRect rect)
    {
        var l = assets.Ui(prefix + ".l");
        var m = assets.Ui(prefix + ".m");
        var r = assets.Ui(prefix + ".r");
        var cap = MathF.Min(rect.H * l.PixelSize.X / MathF.Max(l.PixelSize.Y, 1f), rect.W / 2f);
        _canvas.Image(
            l.TexturePath,
            rect.Position,
            new Vector2(cap, rect.H),
            l.UvMin,
            l.UvMax,
            Vector4.One
        );
        _canvas.Image(
            m.TexturePath,
            new Vector2(rect.X + cap, rect.Y),
            new Vector2(rect.W - 2f * cap, rect.H),
            m.UvMin,
            m.UvMax,
            Vector4.One
        );
        _canvas.Image(
            r.TexturePath,
            new Vector2(rect.Right - cap, rect.Y),
            new Vector2(cap, rect.H),
            r.UvMin,
            r.UvMax,
            Vector4.One
        );
    }

    // ---------------------------------------------------------------- tooltip

    /// <summary>Shows <paramref name="text"/> (colour tags, <c>\n</c> for lines) after hovering <paramref name="rect"/> for the tooltip delay.</summary>
    public void Tooltip(UiRect rect, string text)
    {
        if (!Blocked && _input.IsHover(rect))
            RequestTooltip(IdOf("tip:" + text, rect), text);
    }

    private void DrawTooltip()
    {
        var style = Theme.TooltipText;
        var lines = _tooltipText.Split('\n');
        var lineH = style.Size * 1.35f;
        var pad = 8f;
        var width = lines.Max(l => _canvas.Measure(l, style)) + pad * 2f;
        var height = lines.Length * lineH + pad * 2f - (lineH - style.Size);

        var p = _input.Mouse + new Vector2(14f, 18f);
        // Keep it on screen; flip to the other side of the cursor near the right/bottom edges.
        if (p.X + width > UiSpace.LogicalWidth)
            p.X = MathF.Max(0f, _input.Mouse.X - 14f - width);
        if (p.Y + height > UiSpace.LogicalHeight)
            p.Y = MathF.Max(0f, _input.Mouse.Y - 10f - height);
        p = new Vector2(
            Math.Clamp(p.X, 0f, MathF.Max(0f, UiSpace.LogicalWidth - width)),
            Math.Clamp(p.Y, 0f, MathF.Max(0f, UiSpace.LogicalHeight - height))
        );

        var box = new UiRect(p.X, p.Y, width, height);
        Slice("ui.panel_rectangle", box, Theme.TooltipTint);
        Fill(new UiRect(box.X + 3, box.Y + 3, 2, box.H - 6), Theme.Accent);
        for (var i = 0; i < lines.Length; i++)
            _canvas.RichText(
                lines[i],
                new Vector2(box.X + pad + 2, box.Y + pad - 2 + i * lineH),
                style
            );
    }

    // ---------------------------------------------------------------- scroll list

    /// <summary>
    /// Wheel-scrollable list. Only items wholly inside <paramref name="rect"/> are drawn (no
    /// scissor in the browser runtime), so 50 items cost a screenful of widgets. Scroll offset is
    /// kept per <paramref name="id"/>. <paramref name="drawItem"/> gets the item index and its rect.
    /// </summary>
    public void ScrollList(
        string id,
        UiRect rect,
        int itemCount,
        float itemHeight,
        Action<int, UiRect> drawItem
    )
    {
        var key = IdOf(id, rect);
        _blockers.Add(rect);
        var offset = _scroll.GetValueOrDefault(key);
        if (!Blocked && _input.IsHover(rect) && _input.ScrollDelta != 0f)
            offset += _input.ScrollDelta * WheelScale;
        offset = ScrollMath.Clamp(offset, itemCount, itemHeight, rect.H);
        _scroll[key] = offset;

        var scrollable = ScrollMath.MaxOffset(itemCount, itemHeight, rect.H) > 0f;
        var itemW = scrollable ? rect.W - 10f : rect.W;
        var (first, end) = ScrollMath.Visible(offset, itemCount, itemHeight, rect.H);
        LastListItemsDrawn = end - first;
        for (var i = first; i < end; i++)
            drawItem(i, new UiRect(rect.X, rect.Y + i * itemHeight - offset, itemW, itemHeight));

        if (!scrollable)
            return;
        var total = itemCount * itemHeight;
        var thumbH = MathF.Max(20f, rect.H * rect.H / total);
        var thumbY =
            rect.Y
            + (rect.H - thumbH) * (offset / ScrollMath.MaxOffset(itemCount, itemHeight, rect.H));
        Fill(new UiRect(rect.Right - 6, rect.Y, 6, rect.H), new Vector4(0f, 0f, 0f, 0.4f));
        Fill(new UiRect(rect.Right - 6, thumbY, 6, thumbH), Theme.Accent);
    }

    // ---------------------------------------------------------------- tabs

    /// <returns>True if the selection changed this frame.</returns>
    public bool Tabs(UiRect rect, IReadOnlyList<string> labels, ref int selected)
    {
        var changed = false;
        _blockers.Add(rect);
        var w = rect.W / MathF.Max(1, labels.Count);
        for (var i = 0; i < labels.Count; i++)
        {
            var tab = new UiRect(rect.X + i * w, rect.Y, w - 2, rect.H);
            var id = IdOf("tab:" + labels[i], tab);
            var state = _input.Interact(id, tab, true, Blocked);
            var isSel = i == selected;
            var visual =
                isSel ? ButtonVisual.Pressed
                : state.Hover ? ButtonVisual.Hover
                : ButtonVisual.Normal;
            Slice(
                "ui.button_rectangle",
                tab,
                isSel
                    ? Theme.ButtonTint(ButtonVisual.Hover, ButtonStyle.Primary)
                    : Theme.ButtonTint(visual, ButtonStyle.Normal)
            );
            CenteredText(tab, labels[i], Theme.ButtonText);
            if (isSel)
                Fill(new UiRect(tab.X + 6, tab.Bottom - 4, tab.W - 12, 3), Theme.Accent);
            if (state.Clicked && !isSel)
            {
                selected = i;
                changed = true;
                Sound?.Invoke(UiSound.Click);
            }
        }
        return changed;
    }

    // ---------------------------------------------------------------- modal

    /// <summary>
    /// Centred dialog over a dimmed screen; everything else is inert while it is shown. Call it
    /// every frame while open. <c>Enter</c>/<c>Space</c> activates <paramref name="defaultIndex"/>;
    /// <c>Esc</c> returns <paramref name="cancelIndex"/> (or <see cref="ModalDismissed"/>).
    /// </summary>
    /// <returns>The chosen button index, <see cref="ModalDismissed"/>, or <see cref="ModalPending"/>.</returns>
    public int Modal(
        string title,
        string body,
        IReadOnlyList<string> buttons,
        int defaultIndex = 0,
        int cancelIndex = -1
    )
    {
        _modalThisFrame = true;
        _inModal = true;
        try
        {
            var screen = new UiRect(0, 0, UiSpace.LogicalWidth, UiSpace.LogicalHeight);
            Fill(screen, Theme.Dim);
            _blockers.Add(screen);
            _input.Swallow(screen);

            const float width = 480f;
            var style = Theme.Body;
            var textW = width - 2f * Theme.Padding;
            // Tagged bodies are drawn line by line as rich text (no wrapping); plain bodies wrap.
            var tagged = body.Contains("[c=", StringComparison.Ordinal);
            var bodyLines = body.Split('\n');
            var lines = tagged
                ? bodyLines.Length
                : bodyLines.Sum(l =>
                    Math.Max(1, (int)MathF.Ceiling(_canvas.Measure(l, style) / textW))
                );
            var bodyH = lines * style.Size * 1.3f;
            var height = Theme.TitleBarHeight + bodyH + 48f + 3f * Theme.Padding + 12f;
            var panel = new UiRect(
                (UiSpace.LogicalWidth - width) / 2f,
                (UiSpace.LogicalHeight - height) / 2f,
                width,
                height
            );

            var content = Panel(panel, title);
            if (tagged)
                for (var i = 0; i < bodyLines.Length; i++)
                    _canvas.RichText(
                        bodyLines[i],
                        content.Position + new Vector2(0f, i * style.Size * 1.3f),
                        style
                    );
            else
                _canvas.WrappedText(body, content.Position, style, content.W);

            var result = ModalPending;
            var bw = Math.Min(
                140f,
                (content.W - 8f * (buttons.Count - 1)) / MathF.Max(1, buttons.Count)
            );
            var x = content.Right - buttons.Count * bw - (buttons.Count - 1) * 8f;
            for (var i = 0; i < buttons.Count; i++)
            {
                var b = new UiRect(x + i * (bw + 8f), panel.Bottom - Theme.Padding - 40f, bw, 40f);
                if (
                    Button(
                        b,
                        buttons[i],
                        i == defaultIndex ? ButtonStyle.Primary : ButtonStyle.Normal
                    )
                )
                    result = i;
            }

            // The keystroke that opened the modal must not also dismiss it.
            if (result == ModalPending && _modalWasOpenBeforeThisFrame && !InputSuspended)
            {
                if (bindings.WasPressed(GameAction.Cancel))
                    result = cancelIndex >= 0 ? cancelIndex : ModalDismissed;
                else if (
                    bindings.WasPressed(GameAction.Confirm)
                    && defaultIndex >= 0
                    && defaultIndex < buttons.Count
                )
                    result = defaultIndex;
            }
            if (result != ModalPending)
                _tooltip.Reset();
            return result;
        }
        finally
        {
            _inModal = false;
        }
    }

    // ---------------------------------------------------------------- toast

    public void Toast(string text, float seconds = 3f) =>
        _toasts.Add((text, seconds, MathF.Max(seconds, 0.01f)));

    private void DrawToasts()
    {
        var y = 20f;
        for (var i = _toasts.Count - 1; i >= 0; i--)
        {
            var (text, left, total) = _toasts[i];
            left -= _dt;
            if (left <= 0f)
            {
                _toasts.RemoveAt(i);
                continue;
            }
            _toasts[i] = (text, left, total);
        }
        foreach (var (text, left, _) in _toasts)
        {
            var alpha = Math.Clamp(left / ToastSeconds, 0f, 1f);
            var w = _canvas.Measure(text, Theme.Body) + 2f * Theme.Padding + 8f;
            var rect = new UiRect((UiSpace.LogicalWidth - w) / 2f, y, w, 36f);
            Slice("ui.panel_rectangle", rect, Theme.PanelTint with { W = alpha });
            Fill(
                new UiRect(rect.X + 4, rect.Y + 6, 3, rect.H - 12),
                Theme.Success with
                {
                    W = alpha,
                }
            );
            if (alpha >= 1f)
                CenteredText(rect, text, Theme.Body);
            else
                CenteredText(
                    rect,
                    text,
                    Theme.Body with
                    {
                        Color = new Yaeger.Graphics.Color(225, 230, 240, (byte)(alpha * 255)),
                    }
                );
            y += 42f;
        }
    }
}
