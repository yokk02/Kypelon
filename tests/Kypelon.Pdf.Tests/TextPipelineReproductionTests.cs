using Kypelon.Pdf.Text;
using Xunit;
namespace Kypelon.Pdf.Tests;
public class TextPipelineReproductionTests
{
    [Fact] public void PositiveContextualAdjustmentDoesNotOverflow()
    {
        var lines = LineBreaker.Wrap("AV", FontFace.Courier, 10, 15, new PairShaper(600, 1800));
        Assert.Equal(new[] { "A", "V" }, lines.Select(l => l.Text));
        Assert.All(lines, l => Assert.True(l.Width <= 15));
    }
    [Fact] public void NegativeContextualAdjustmentDoesNotBreakPrematurely()
    {
        var line = Assert.Single(LineBreaker.Wrap("AV", FontFace.Courier, 10, 15, new PairShaper(1000, 1400)));
        Assert.Equal("AV", line.Text);
        Assert.Equal(14, line.Width);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task InvalidCustomGlyphFailsBeforeAnyOutput(bool asynchronous)
    {
        var d = Pdf.Document(b => { b.TextShaper(new InvalidShaper()); b.Page(p => p.Content(c => { c.Text("A"); c.PageBreak(); c.Text("Z"); })); });
        using var destination = new MemoryStream();
        await Assert.ThrowsAsync<PdfFontException>(async () => { if (asynchronous) await d.WriteAsync(destination); else d.Write(destination); });
        Assert.Equal(0, destination.Length);
    }
    private sealed class PairShaper(double single, double pair) : ITextShaper
    {
        public GlyphRun Shape(TextRun input) => new(input.Text.Select((c, i) => new TextCluster(input.SourceStart + i, 1)).ToArray(), input.Text.Select((c, i) => new PositionedGlyph((ushort)c, i, input.Text == "AV" ? pair / 2 : single)).ToArray());
    }
    private sealed class InvalidShaper : ITextShaper
    {
        public GlyphRun Shape(TextRun input) => new(input.Text.Select((c, i) => new TextCluster(input.SourceStart + i, 1)).ToArray(), input.Text.Select((c, i) => new PositionedGlyph(c == 'Z' ? (ushort)999 : (ushort)c, i, 600)).ToArray());
    }
}
