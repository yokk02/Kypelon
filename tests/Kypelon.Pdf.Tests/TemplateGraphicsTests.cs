using System.Text;
using System.Text.RegularExpressions;
using Kypelon.Pdf.Core;
using Kypelon.Pdf.Graphics;
using Kypelon.Pdf.Layout;
using Xunit;

namespace Kypelon.Pdf.Tests;

public class TemplateGraphicsTests
{
    [Fact]
    public void UriLinkConvertsTopLeftRectangleAndEscapesInternationalUrl()
    {
        var syntax = new PdfLinkAnnotation("https://example.com/ไทย?q=a%20b#details", 20, 30, 90, 25).ToDictionary(200).ToPdfSyntax();
        Assert.Contains("/Rect [20 145 110 170 ]", syntax);
        Assert.Contains("/Border [0 0 0 ]", syntax);
        Assert.Contains("/Subtype /Link", syntax);
        Assert.Contains("/S /URI", syntax);
        Assert.Contains("https://example.com/%E0%B9%84%E0%B8%97%E0%B8%A2?q=a%20b#details", syntax);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/secret.txt")]
    [InlineData("relative/path")]
    [InlineData("https://example.com/\nheader")]
    public void LinksRejectUnsupportedTargets(string url) =>
        Assert.Throws<PdfWriteException>(() => new PdfLinkAnnotation(url, 10, 10, 30, 20).ToDictionary(200));

    [Theory]
    [InlineData(-1, 10, 20, 20)]
    [InlineData(10, 190, 20, 20)]
    [InlineData(10, 10, 0, 20)]
    [InlineData(double.NaN, 10, 20, 20)]
    public void LinksRejectInvalidGeometry(double x, double y, double width, double height) =>
        Assert.Throws<PdfWriteException>(() => new PdfLinkAnnotation("https://example.com", x, y, width, height).ToDictionary(200));

    [Fact]
    public void RoundedRectangleUsesFourContinuousCubicCorners()
    {
        var canvas = new PdfCanvas(200);
        canvas.RoundedRectangle(10, 20, 100, 40, 10).Fill();
        string syntax = Encoding.ASCII.GetString(canvas.ToArray());
        Assert.Contains("20 20 m", syntax);
        Assert.Equal(4, Regex.Matches(syntax, @" c\n").Count);
        Assert.Contains("20 20 c\nh\nf", syntax);
        var clamped = new PdfCanvas(200).RoundedRectangle(10, 20, 100, 40, 100).Fill().ToArray();
        Assert.Contains("30 20 m", Encoding.ASCII.GetString(clamped));
        var square = new PdfCanvas(200).RoundedRectangle(10, 20, 100, 40, 0).Fill().ToArray();
        Assert.Contains("10 20 100 40 re", Encoding.ASCII.GetString(square));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfCanvas(200).RoundedRectangle(0, 0, 30, 30, double.NaN));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomElementLinkIsAttachedToPageAndWrittenAtCorrectOffset(bool async)
    {
        var doc = Pdf.Document(d => d.Page(p =>
        {
            p.Size(new PdfPageSize(200, 240)).Margin(20);
            p.Content(c => c.Add(new ButtonElement("https://example.com/request/42")));
        }));
        doc.Options.Deterministic = true;
        byte[] bytes;
        if (async)
        {
            var destination = new AsyncOnlyStream();
            await doc.WriteAsync(destination);
            bytes = destination.Bytes;
        }
        else bytes = doc.ToArray();
        var objects = Inspect.Objects(bytes); // validates every xref offset, including annotations
        var annotation = Assert.Single(objects, o => o.Value.Contains("/Subtype /Link", StringComparison.Ordinal));
        Assert.Contains("/Rect [30 181 110 205 ]", annotation.Value);
        var page = Assert.Single(objects.Values, s => s.Contains("/Type /Page\n", StringComparison.Ordinal));
        Assert.Contains($"/Annots [{annotation.Key} 0 R ]", page);
        Assert.Equal(bytes, doc.ToArray());
    }

    [Fact]
    public void InvalidCustomLinkFailsBeforeOutputStarts()
    {
        var doc = Pdf.Document(d => d.Page(p => p.Content(c => c.Add(new ButtonElement("javascript:alert(1)")))));
        using var destination = new MemoryStream();
        Assert.Throws<PdfWriteException>(() => doc.Write(destination));
        Assert.Equal(0, destination.Length);
    }

    private sealed class ButtonElement(string url) : Element
    {
        protected override MeasuredBlock MeasureContent(LayoutContext context, double width) => new(width, 50,
        [new RectangleCommand(new(10, 15, 80, 24), Color.Black, null, 0, CornerRadius: 4),
         new LinkCommand(url, new(10, 15, 80, 24))]);
    }
}
