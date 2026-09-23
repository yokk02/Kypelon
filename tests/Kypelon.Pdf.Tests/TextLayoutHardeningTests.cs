using Kypelon.Pdf.Core;
using Kypelon.Pdf.Layout;
using Kypelon.Pdf.Text;
using Xunit;

namespace Kypelon.Pdf.Tests;

public class TextLayoutHardeningTests
{
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task PreCancelledSaveDoesNotTouchDestination(bool exists)
    {
        string path = Path.Combine(Path.GetTempPath(), "kypelon-cancel-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (exists) await File.WriteAllTextAsync(path, "KEEP");
            var document = Pdf.Document(d => d.Page(p => p.Content(c => c.Text("Test"))));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => document.SaveAsync(path, new(true)));
            Assert.Equal(exists, File.Exists(path));
            if (exists) Assert.Equal("KEEP", await File.ReadAllTextAsync(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    public static IEnumerable<object[]> WhitespaceCases()
    {
        foreach (string text in new[] { "A\r\nB", "A\rB", "A\nB", "A\tB" })
            foreach (bool auto in new[] { false, true })
                foreach (bool header in new[] { false, true })
                    foreach (bool styled in new[] { false, true }) yield return [text, auto, header, styled];
    }
    [Theory]
    [MemberData(nameof(WhitespaceCases))]
    public void TableWidthsAndRenderingUseSameNormalization(string text, bool auto, bool header, bool styled)
    {
        var table = new TableElement { Header = [header ? text : "H", "Header"], Style = new() { Size = 10 }, HeaderStyle = new() { Size = 10 } };
        table.Columns.AddRange([auto ? Column.Auto() : Column.Flex(), Column.Flex()]);
        table.Rows.Add([header ? "D" : text, "Data"]);
        if (styled) table.CellStyles[(header ? -1 : 0, 0)] = new(new() { Size = 12 });
        var measured = table.Measure(new(), 400);
        string[] expected = text.Contains('\t') ? ["A    B"] : ["A", "B"];
        var commands = measured.Commands.OfType<TextCommand>().Select(c => c.Text).ToArray();
        foreach (string line in expected) Assert.Contains(line, commands);
        Assert.DoesNotContain(commands, line => line.Contains('\r') || line.Contains('\t'));
        if (auto)
        {
            double expectedWidth = (text.Contains('\t') ? 6 : 1) * (styled ? 12 : 10) * .6 + 10;
            Assert.Equal(expectedWidth, measured.Commands.OfType<RectangleCommand>().First().Bounds.Width, 6);
        }
    }

    [Theory]
    [InlineData("A\r\nBB", 12)] [InlineData("A\rBB", 12)] [InlineData("A\nBB", 12)] [InlineData("A\tB", 36)]
    public void SourceTextMeasurementReturnsMaximumNormalizedLineWidth(string text, double expected) =>
        Assert.Equal(expected, LineBreaker.Measure(text, FontFace.Courier, 10));

    internal static FontFace PortableFont() => FontFace.Load(FontFixture.Create(codePoints:
        Enumerable.Range(32, 95).Select(c => (uint)c).Concat(new uint[] { 0xA0, 0x2007, 0x202F, 0xFEFF }).ToArray()));

    [Theory]
    [InlineData("FY\u00A026")] [InlineData("1\u202F000")] [InlineData("A\u00A0B")] [InlineData("A\uFEFFB")] [InlineData("A\u2007B")]
    public void NoBreakSequencesMoveIntactToNextLine(string token)
    {
        var font = PortableFont();
        var lines = LineBreaker.Wrap("X " + token, font, 10, token.Length * 6);
        Assert.Equal(new[] { "X", token }, lines.Select(l => l.Text));
        Assert.All(lines, line => Assert.True(line.Width <= token.Length * 6));
        Assert.NotEmpty(font.GetFontProgram());
    }

    [Theory]
    [InlineData("FY\u00A026")] [InlineData("1\u202F000")] [InlineData("A\u00A0B")] [InlineData("A\uFEFFB")] [InlineData("A\u2007B")]
    public void NoBreakSequencesWiderThanTheFullLineFailClearly(string token)
    {
        var error = Assert.Throws<PdfFontException>(() => LineBreaker.Wrap(token, PortableFont(), 10, token.Length * 6 - 1));
        Assert.Contains("no-break", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoBreakSourceIsPreservedInEmbeddedToUnicode()
    {
        var font = PortableFont();
        var doc = Pdf.Document(d => { d.DefaultFont(font); d.Page(p => p.Content(c => c.Text("FY\u00A026 1\u202F000 A\uFEFFB"))); });
        doc.Options.CompressStreams = false;
        var objects = Inspect.Objects(doc.ToArray());
        string mappings = string.Join("\n", objects.Values);
        Assert.Contains("<00A0>", mappings); Assert.Contains("<202F>", mappings); Assert.Contains("<FEFF>", mappings);
        Assert.Contains("/FontFile2", mappings);
    }
}
