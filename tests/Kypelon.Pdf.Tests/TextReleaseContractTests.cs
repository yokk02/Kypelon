using System.Text;
using Kypelon.Pdf.Core;
using Kypelon.Pdf.Graphics;
using Kypelon.Pdf.Layout;
using Kypelon.Pdf.Text;
using Xunit;

namespace Kypelon.Pdf.Tests;

public class TextReleaseContractTests
{
    [Theory]
    [InlineData("A ", 100, "A", "0:1", "1")]
    [InlineData("A B", 6, "A|B", "0:1|2:1", "1")]
    [InlineData("A  B", 6, "A|B", "0:1|3:1", "1,2")]
    [InlineData("word word ", 24, "word|word", "0:4|5:4", "4,9")]
    [InlineData("word word ", 100, "word word", "0:9", "9")]
    [InlineData(" A", 100, " A", "0:2", "")]
    [InlineData("   ", 6, "", "0:0", "0,1,2")]
    public void BoundarySpacesAreExplicitExtractionNormalization(string source, double width, string visible, string ranges, string omitted)
    {
        var lines = LineBreaker.Wrap(source, FontFace.Courier, 10, width);
        Assert.Equal(visible, string.Join("|", lines.Select(l => l.Text)));
        Assert.Equal(ranges, string.Join("|", lines.Select(l => $"{l.Run.Input.SourceStart}:{l.Run.Input.SourceLength}")));
        var covered = lines.SelectMany(l => Enumerable.Range(l.Run.Input.SourceStart, l.Run.Input.SourceLength)).ToHashSet();
        var missing = Enumerable.Range(0, source.Length).Where(i => !covered.Contains(i)).ToArray();
        Assert.Equal(omitted, string.Join(",", missing));
        Assert.All(missing, index => Assert.Equal(' ', source[index]));
        Assert.All(lines, l => Assert.Equal(source, l.Run.Input.Source.OriginalText));
    }
    [Theory] [InlineData("\u00A0")] [InlineData("\u202F")] [InlineData("\uFEFF")] [InlineData("\u2007")]
    public void NonBreakingSourceIsNeverSpaceTrimmed(string space)
    {
        var font = TextPipelineTests.PortableFont(); string text = "A" + space + "B";
        var line = Assert.Single(LineBreaker.Wrap(text, font, 10, 100));
        Assert.Equal(text, line.Text); Assert.Equal(3, line.Run.Input.SourceLength);
        Assert.Throws<PdfFontException>(() => LineBreaker.Wrap(text, font, 10, 6));
    }
    [Theory] [InlineData("A\r\nB", "A\nB")] [InlineData("A\rB", "A\nB")] [InlineData("A\tB", "A    B")]
    public void ExistingNormalizationAndOriginalMappingRemain(string original, string normalized)
    {
        var lines = LineBreaker.Wrap(original, FontFace.Courier, 10, 6);
        Assert.Equal(new[] { "A", "B" }, lines.Select(l => l.Text));
        Assert.All(lines, line => { Assert.Equal(original, line.Run.Input.Source.OriginalText); Assert.Equal(normalized, line.Run.Input.Source.Text); });
        var last = lines[^1].Run.Input;
        Assert.Equal(new TextRange(original.Length - 1, 1), last.Source.GetOriginalRange(last.SourceStart, last.SourceLength));
    }
    [Theory]
    [InlineData("\uFEFFA")] [InlineData("A\uFEFF")] [InlineData("A\uFEFFB")] [InlineData("\uFEFF")]
    public void AdapterMustOwnDefaultIgnorablesEvenWhenTheyAreInvisible(string source)
    {
        var font = TextPipelineTests.PortableFont();
        // Adapter contract fixture: one exhaustive source cluster, one empty-outline glyph.
        // A real adapter must choose a genuinely invisible glyph for an all-ignorable run.
        var shaper = new TextPipelineTests.DelegateShaper(i => new([new(i.SourceStart, i.SourceLength)], [new(1, 0, i.Text.All(c => c == '\uFEFF') ? 0 : 600)]));
        var line = LineBreaker.Prepare(source, font, 10, shaper);
        Assert.Equal(source, line.Text); Assert.Equal(source, line.Run.GetUnicodeMapping(0));
        Assert.Equal(new TextCluster(0, source.Length), Assert.Single(line.Run.Clusters));
        var dropping = new TextPipelineTests.DelegateShaper(_ => new([], []));
        Assert.Throws<PdfFontException>(() => LineBreaker.Prepare(source, font, 10, dropping));
        var glyphless = new TextPipelineTests.DelegateShaper(i => new([new(i.SourceStart, i.SourceLength)], []));
        Assert.Throws<PdfFontException>(() => LineBreaker.Prepare(source, font, 10, glyphless));
    }
    [Theory]
    [InlineData(0, 600, 0, 0, 0, 6)]
    [InlineData(-200, 800, 0, 0, 0, 6)]
    [InlineData(800, -200, 0, 0, 0, 6)]
    [InlineData(600, 600, 100, 0, 0, 12)]
    [InlineData(600, 600, 0, 100, 0, 12)]
    [InlineData(600, 600, 0, 0, 100, 12)]
    public void SignedPositioningUsesAdvanceWidthAndExactRetainedCoordinates(double first, double second, double offsetX, double offsetY, double advanceY, double expectedWidth)
    {
        var font = TextPipelineTests.PortableFont();
        var shaper = new TextPipelineTests.DelegateShaper(i => new([new(i.SourceStart, 1), new(i.SourceStart + 1, 1)],
            [new(1, 0, first, advanceY, offsetX, offsetY), new(1, 1, second)]));
        var line = LineBreaker.Prepare("AB", font, 10, shaper);
        Assert.Equal(expectedWidth, line.Width);
        var doc = Pdf.Document(d => { d.DefaultFont(font).TextShaper(shaper); d.Page(p => { p.Margin(0); p.Content(c => c.Add(new PreparationReproductionTests.CommandElement(
            new TextCommand("AB", font, 10, Color.Black, 20, 40, false) { Prepared = line }))); }); });
        doc.Options.CompressStreams = false; var prepared = doc.Prepare(); int calls = shaper.Calls;
        string pdf = Encoding.ASCII.GetString(prepared.ToArray());
        Assert.Contains($"1 0 0 -1 {PdfReal.Format(20 + offsetX / 100)} {PdfReal.Format(40 - offsetY / 100)} Tm", pdf);
        Assert.Contains($"1 0 0 -1 {PdfReal.Format(20 + first / 100)} {PdfReal.Format(40 - advanceY / 100)} Tm", pdf);
        Assert.Equal(calls, shaper.Calls);
    }
    [Fact]
    public void NonMonotonicCandidatesMayUnderfillButEveryFinalAdvanceFits()
    {
        var shaper = new TextPipelineTests.DelegateShaper(i =>
        {
            double advance = i.Text switch { "A" => 600, "AB" => 1800, "ABC" => 1200, "ABCD" => 2400, _ => 600 * i.SourceLength };
            return new([new(i.SourceStart, i.SourceLength)], [new(1, 0, advance)]);
        });
        var lines = LineBreaker.Wrap("ABCD", TextPipelineTests.PortableFont(), 10, 15, shaper);
        Assert.Equal("ABCD", string.Concat(lines.Select(l => l.Text)));
        Assert.All(lines, l => { Assert.InRange(l.Width, 0, 15.001); Assert.Equal(l.Run.Advance * .01, l.Width); });
    }
}
