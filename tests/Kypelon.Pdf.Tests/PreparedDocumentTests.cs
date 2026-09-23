using System.Text;
using Microsoft.AspNetCore.Http;
using Kypelon.Pdf.AspNetCore;
using Kypelon.Pdf.Core;
using Kypelon.Pdf.Graphics;
using Kypelon.Pdf.Layout;
using Kypelon.Pdf.Text;
using Xunit;

namespace Kypelon.Pdf.Tests;

public class PreparedDocumentTests
{
    private static TextCommand Text() => new("A", FontFace.Courier, 10, Color.Black, 0, 10, false);
    private static RectangleCommand Rectangle() => new(new(0, 0, 10, 10), Color.White, Color.Black, 1);
    private static PdfDocument Document(RenderCommand command) => Pdf.Document(d => d.Page(p => p.Content(c =>
    {
        c.Text("Valid first page"); c.PageBreak(); c.Add(new PreparationReproductionTests.CommandElement(command));
    })));
    private static RenderCommand Invalid(string kind) => kind switch
    {
        "text-x" => Text() with { X = double.NaN },
        "text-y" => Text() with { Baseline = double.PositiveInfinity },
        "text-size-zero" => Text() with { Size = 0 },
        "text-size-nan" => Text() with { Size = double.NaN },
        "text-color-range" => Text() with { Color = new(-.1, 0, 0) },
        "text-color-nan" => Text() with { Color = new(0, double.NaN, 0) },
        "text-width-nan" => Text() with { ReservedWidth = double.NaN },
        "text-width-negative" => Text() with { ReservedWidth = -1 },
        "text-width-infinite" => Text() with { ReservedWidth = double.PositiveInfinity },
        "text-alignment" => Text() with { Alignment = (HorizontalAlignment)999 },
        "text-prepared-source" => Text() with { Prepared = LineBreaker.Prepare("B", FontFace.Courier, 10) },
        "text-prepared-size" => Text() with { Prepared = LineBreaker.Prepare("A", FontFace.Courier, 11) },
        "text-prepared-font" => Text() with { Prepared = LineBreaker.Prepare("A", FontFace.CourierBold, 10) },
        "text-absolute-position" => Text() with { X = 1e12, Prepared = LineBreaker.Prepare("A", FontFace.Courier, 10) },
        "rectangle-x" => Rectangle() with { Bounds = new(double.NaN, 0, 10, 10) },
        "rectangle-y" => Rectangle() with { Bounds = new(0, double.PositiveInfinity, 10, 10) },
        "rectangle-width" => Rectangle() with { Bounds = new(0, 0, -1, 10) },
        "rectangle-height" => Rectangle() with { Bounds = new(0, 0, 10, double.NaN) },
        "rectangle-fill" => Rectangle() with { Fill = new Color(1.1, 0, 0) },
        "rectangle-stroke" => Rectangle() with { Stroke = new Color(0, 0, double.PositiveInfinity) },
        "rectangle-line-negative" => Rectangle() with { LineWidth = -1 },
        "rectangle-line-nan" => Rectangle() with { LineWidth = double.NaN },
        "rectangle-radius-negative" => Rectangle() with { CornerRadius = -1 },
        "rectangle-radius-nan" => Rectangle() with { CornerRadius = double.NaN },
        "image-null" => new ImageCommand(null!, new(0, 0, 10, 10)),
        "image-zero" => new ImageCommand(PdfImage.Load(Path.Combine(AppContext.BaseDirectory, "images/sample-rgb.png")), new(0, 0, 0, 10)),
        "image-x" => new ImageCommand(PdfImage.Load(Path.Combine(AppContext.BaseDirectory, "images/sample-rgb.png")), new(double.NaN, 0, 10, 10)),
        "image-height" => new ImageCommand(PdfImage.Load(Path.Combine(AppContext.BaseDirectory, "images/sample-rgb.png")), new(0, 0, 10, -1)),
        "link-url" => new LinkCommand("javascript:alert(1)", new(0, 0, 10, 10)),
        "link-outside" => new LinkCommand("https://example.com", new(900, 0, 10, 10)),
        _ => throw new InvalidOperationException(kind)
    };
    [Theory]
    [InlineData("text-x")] [InlineData("text-y")] [InlineData("text-size-zero")] [InlineData("text-size-nan")]
    [InlineData("text-color-range")] [InlineData("text-color-nan")] [InlineData("text-width-nan")]
    [InlineData("text-width-negative")] [InlineData("text-width-infinite")] [InlineData("text-alignment")]
    [InlineData("text-prepared-source")] [InlineData("text-prepared-size")] [InlineData("text-prepared-font")]
    [InlineData("text-absolute-position")]
    [InlineData("rectangle-x")] [InlineData("rectangle-y")] [InlineData("rectangle-width")] [InlineData("rectangle-height")]
    [InlineData("rectangle-fill")] [InlineData("rectangle-stroke")] [InlineData("rectangle-line-negative")]
    [InlineData("rectangle-line-nan")] [InlineData("rectangle-radius-negative")] [InlineData("rectangle-radius-nan")]
    [InlineData("image-null")] [InlineData("image-zero")] [InlineData("image-x")] [InlineData("image-height")]
    [InlineData("link-url")] [InlineData("link-outside")]
    public async Task AllFinalCommandFieldsAreValidatedBeforeOutput(string kind)
    {
        var doc = Document(Invalid(kind));
        var error = Record.Exception(() => doc.Prepare());
        Assert.True(error is PdfLayoutException or PdfFontException or PdfWriteException, error?.ToString() ?? "No preparation error");
        using var sync = new MemoryStream(); Assert.NotNull(Record.Exception(() => doc.Write(sync))); Assert.Equal(0, sync.Length);
        using var asyncOutput = new MemoryStream(); Assert.NotNull(await Record.ExceptionAsync(() => doc.WriteAsync(asyncOutput))); Assert.Equal(0, asyncOutput.Length);
        var http = new DefaultHttpContext(); using var body = new MemoryStream(); http.Response.Body = body;
        Assert.NotNull(await Record.ExceptionAsync(() => doc.PdfFile().ExecuteAsync(http)));
        Assert.Equal(0, body.Length); Assert.Null(http.Response.ContentType);
    }
    [Fact]
    public void DiagnosticsIdentifyThePageAndCommand()
    {
        var error = Assert.Throws<PdfLayoutException>(() => PreparationReproductionTests.BadSecondPage().Prepare());
        Assert.Contains("Page 2", error.Message); Assert.Contains("RectangleCommand", error.Message); Assert.Contains("Color", error.Message);
    }
    [Fact]
    public void ValidZeroWidthStrokeAndCornerClampingStillWork()
    {
        var prepared = Document(Rectangle() with { LineWidth = 0, CornerRadius = 1000 }).Prepare();
        Assert.Equal(2, prepared.PageCount); Assert.NotEmpty(prepared.ToArray());
    }
    [Fact]
    public async Task PreparedSnapshotSurvivesModelOptionsMetadataAndAdapterMutation()
    {
        bool reject = false;
        var shaper = new TextPipelineTests.DelegateShaper(i => reject ? throw new InvalidOperationException("Reshaped") : BasicTextShaper.Instance.Shape(i));
        var text = new TextElement("Original", new() { Size = 10 });
        var doc = Pdf.Document(d => { d.TextShaper(shaper); d.Page(p => { p.Content(c => { c.Add(text); c.PageBreak(); c.Text("Second"); }); p.Footer(f => f.PageNumber()); }); });
        doc.Options.Deterministic = true; doc.Metadata.Title = "Snapshot title";
        var prepared = doc.Prepare(); int calls = shaper.Calls;
        byte[] expected = prepared.ToArray();
        reject = true; text.Text = "Changed"; text.Style = new() { Size = 28, Font = FontFace.CourierBold };
        doc.Options.CompressStreams = false; doc.Options.DebugLayout = true; doc.Metadata.Title = "Changed";
        Assert.Equal(expected, prepared.ToArray());
        using var output = new MemoryStream(); await prepared.WriteAsync(output); Assert.Equal(expected, output.ToArray());
        var http = new DefaultHttpContext(); using var body = new MemoryStream(); http.Response.Body = body;
        await prepared.PdfFile("prepared.pdf").ExecuteAsync(http); Assert.Equal(expected, body.ToArray());
        Assert.Equal(calls, shaper.Calls); Assert.Equal(2, prepared.PageCount);
        var commands = prepared.Pages.SelectMany(p => p.Commands).OfType<TextCommand>().ToArray();
        Assert.All(commands, t => Assert.False(t.PageNumber));
        Assert.Contains(commands, t => t.Text == "Page 2 of 2");
        Assert.Throws<NotSupportedException>(() => ((IList<PagePlan>)prepared.Pages).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<RenderCommand>)prepared.Pages[0].Commands).Clear());
    }
    [Fact]
    public async Task PreparedSaveAndCancellationPreserveSnapshotAndFile()
    {
        var prepared = Pdf.Document(d => d.Page(p => p.Content(c => c.Text("Prepared save")))).Prepare();
        string file = Path.Combine(Path.GetTempPath(), "kypelon-prepared-" + Guid.NewGuid() + ".pdf");
        try
        {
            prepared.Save(file); byte[] expected = await File.ReadAllBytesAsync(file);
            await prepared.SaveAsync(file); Assert.Equal(expected, await File.ReadAllBytesAsync(file));
            await File.WriteAllTextAsync(file, "KEEP");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => prepared.SaveAsync(file, new CancellationToken(true)));
            Assert.Equal("KEEP", await File.ReadAllTextAsync(file));
            using var output = new MemoryStream();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => prepared.WriteAsync(output, new CancellationToken(true)));
            Assert.Equal(0, output.Length);
        }
        finally { File.Delete(file); }
    }
    [Fact]
    public void FailedDestinationDoesNotPoisonPreparedSnapshot()
    {
        var prepared = Pdf.Document(d => d.Page(p => p.Content(c => c.Text("Retry on a new stream")))).Prepare();
        using var broken = new ThrowingDestination();
        Assert.Throws<IOException>(() => prepared.Write(broken));
        byte[] first = prepared.ToArray(); Assert.Equal(first, prepared.ToArray());
        using var owned = new MemoryStream(); prepared.Write(owned, leaveOpen: false); Assert.False(owned.CanWrite);
    }
    private sealed class ThrowingDestination : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer) => throw new IOException("Destination failure");
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("Destination failure");
    }
    [Fact]
    public async Task PreparedWritesHaveIndependentConcurrentResourceState()
    {
        var doc = Pdf.Document(d => { d.DefaultFont(TextPipelineTests.PortableFont()); d.Page(p => p.Content(c => c.Text("ภาษาไทย"))); });
        doc.Options.Deterministic = true; var prepared = doc.Prepare();
        byte[] expected = prepared.ToArray();
        var results = await Task.WhenAll(Task.Run(prepared.ToArray), Task.Run(prepared.ToArray));
        Assert.All(results, value => Assert.Equal(expected, value));
    }
}
