using Yaeger.Graphics;
using Yaeger.Platform;

namespace Firewall.Web.Rendering;

/// <summary>A stretch of text in one colour; <c>null</c> means the style's own colour.</summary>
public readonly record struct TextRun(string Text, Color? Color);

/// <summary>A <see cref="TextRun"/> with the x offset (px) it starts at on its line.</summary>
public readonly record struct PositionedRun(string Text, Color? Color, float X);

/// <summary>
/// Inline colour tags: <c>"Hit chance [c=#7CFC00]72%[/c]"</c>. Tags nest (<c>[/c]</c> restores
/// the enclosing colour), accept <c>#RGB</c> or <c>#RRGGBB</c>, and an unterminated tag colours
/// to the end. Anything else in brackets, including a stray <c>[/c]</c>, is literal text.
/// </summary>
/// <remarks>Rich text is a single line: there is no word wrap. Wrap untagged text with <c>TextLayoutOptions</c>.</remarks>
public static class RichText
{
    public static List<TextRun> Parse(string text)
    {
        var runs = new List<TextRun>();
        var colors = new Stack<Color>();
        var buffer = new System.Text.StringBuilder();

        void Flush()
        {
            if (buffer.Length == 0)
                return;
            runs.Add(new TextRun(buffer.ToString(), colors.Count > 0 ? colors.Peek() : null));
            buffer.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '[')
            {
                if (string.CompareOrdinal(text, i, "[/c]", 0, 4) == 0 && colors.Count > 0)
                {
                    Flush();
                    colors.Pop();
                    i += 3;
                    continue;
                }
                var end = text.IndexOf(']', i);
                if (
                    end > i + 4
                    && string.CompareOrdinal(text, i, "[c=#", 0, 4) == 0
                    && TryParseHex(text.AsSpan(i + 4, end - i - 4), out var color)
                )
                {
                    Flush();
                    colors.Push(color);
                    i = end;
                    continue;
                }
            }
            buffer.Append(text[i]);
        }
        Flush();
        return runs;
    }

    /// <summary>The text with all tags removed.</summary>
    public static string Strip(IEnumerable<TextRun> runs) =>
        string.Concat(runs.Select(r => r.Text));

    /// <summary>Each run's x offset: the cumulative measured width of the runs before it.</summary>
    public static List<PositionedRun> Position(
        IReadOnlyList<TextRun> runs,
        IGlyphMetricsProvider metrics,
        string fontKey,
        int fontSize
    )
    {
        var result = new List<PositionedRun>(runs.Count);
        var x = 0f;
        foreach (var run in runs)
        {
            result.Add(new PositionedRun(run.Text, run.Color, x));
            x += TextLayout.Measure(run.Text, metrics, fontKey, fontSize).X;
        }
        return result;
    }

    private static bool TryParseHex(ReadOnlySpan<char> hex, out Color color)
    {
        color = default;
        if (hex.Length is not (3 or 6))
            return false;
        var step = hex.Length / 3;
        Span<byte> rgb = stackalloc byte[3];
        for (var c = 0; c < 3; c++)
        {
            if (
                !byte.TryParse(
                    hex.Slice(c * step, step),
                    System.Globalization.NumberStyles.HexNumber,
                    null,
                    out var v
                )
            )
                return false;
            rgb[c] = step == 1 ? (byte)(v * 17) : v;
        }
        color = new Color(rgb[0], rgb[1], rgb[2]);
        return true;
    }
}
