using System.Numerics;
using Firewall.Web.Rendering;
using Yaeger.Graphics;
using Yaeger.Platform;

namespace Firewall.Web.Tests;

public class RichTextTests
{
    // Every glyph advances 10 px.
    private sealed class FakeMetrics : IGlyphMetricsProvider
    {
        public FontLineMetrics GetLineMetrics(string fontKey, int fontSize) => new(20f, 16f);

        public bool TryGetGlyph(string fontKey, int fontSize, int codepoint, out GlyphMetrics glyph)
        {
            glyph = new GlyphMetrics(10f, 0f, 0f, 10f, 12f, Vector2.Zero, Vector2.One, "atlas");
            return true;
        }
    }

    private static readonly Color Green = new(0x7C, 0xFC, 0x00);

    [Fact]
    public void PlainTextIsOneRunWithoutColor()
    {
        var runs = RichText.Parse("Hello world");
        Assert.Equal([new TextRun("Hello world", null)], runs);
    }

    [Fact]
    public void TaggedTextBecomesColouredRun()
    {
        var runs = RichText.Parse("Hit [c=#7CFC00]72%[/c] now");
        Assert.Equal(
            [new TextRun("Hit ", null), new TextRun("72%", Green), new TextRun(" now", null)],
            runs
        );
    }

    [Fact]
    public void AdjacentTagsProduceNoEmptyRuns()
    {
        var runs = RichText.Parse("[c=#F00]a[/c][c=#0F0]b[/c]");
        Assert.Equal(
            [new TextRun("a", new Color(255, 0, 0)), new TextRun("b", new Color(0, 255, 0))],
            runs
        );
    }

    [Fact]
    public void NestedTagsRestoreTheEnclosingColour()
    {
        var runs = RichText.Parse("[c=#F00]a[c=#00F]b[/c]c[/c]d");
        var red = new Color(255, 0, 0);
        Assert.Equal(
            [
                new TextRun("a", red),
                new TextRun("b", new Color(0, 0, 255)),
                new TextRun("c", red),
                new TextRun("d", null),
            ],
            runs
        );
    }

    [Theory]
    [InlineData("[b]bold[/b]")]
    [InlineData("[c=red]x[/c]")]
    [InlineData("[c=#12]x")]
    [InlineData("stray [/c] close")]
    [InlineData("open [ bracket")]
    public void UnknownTagsAreLiteral(string input)
    {
        Assert.Equal(input, RichText.Strip(RichText.Parse(input)));
        Assert.All(RichText.Parse(input), r => Assert.Null(r.Color));
    }

    [Fact]
    public void UnterminatedTagColoursToTheEnd()
    {
        var runs = RichText.Parse("a [c=#7CFC00]open");
        Assert.Equal([new TextRun("a ", null), new TextRun("open", Green)], runs);
    }

    [Fact]
    public void RunOffsetsAreCumulativeMeasuredWidths()
    {
        var runs = RichText.Parse("ab[c=#F00]cde[/c]f");
        var placed = RichText.Position(runs, new FakeMetrics(), "font", 16);

        Assert.Equal([0f, 20f, 50f], placed.Select(p => p.X));
        Assert.Equal(["ab", "cde", "f"], placed.Select(p => p.Text));
    }
}
