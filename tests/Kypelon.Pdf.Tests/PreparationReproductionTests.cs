using Microsoft.AspNetCore.Http;
using Kypelon.Pdf.AspNetCore;
using Kypelon.Pdf.Graphics;
using Kypelon.Pdf.Layout;
using Xunit;

namespace Kypelon.Pdf.Tests;

public class PreparationReproductionTests
{
    internal sealed class CommandElement(RenderCommand command) : Element
    {
        protected override MeasuredBlock MeasureContent(LayoutContext context, double width) => new(width, 20, [command]);
    }
    internal static PdfDocument BadSecondPage() => Pdf.Document(d => d.Page(p => p.Content(c =>
    {
        c.Text("Valid first page");
        c.PageBreak();
        c.Add(new CommandElement(new RectangleCommand(new(0, 0, 20, 20), new Color(2, 0, 0), null, 1)));
    })));
    [Fact]
    public void InvalidSecondPageFailsInPlan() =>
        Assert.Throws<PdfLayoutException>(() => BadSecondPage().Plan());

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task InvalidSecondPageEmitsNoDestinationBytes(bool asynchronous)
    {
        using var output = new MemoryStream();
        var exception = await Record.ExceptionAsync(async () =>
        {
            if (asynchronous) await BadSecondPage().WriteAsync(output);
            else BadSecondPage().Write(output);
        });
        Assert.NotNull(exception);
        Assert.Equal(0, output.Length);
    }
    [Fact]
    public async Task InvalidSecondPageEmitsNoHttpBodyBytes()
    {
        var http = new DefaultHttpContext(); using var output = new MemoryStream(); http.Response.Body = output;
        Assert.NotNull(await Record.ExceptionAsync(() => BadSecondPage().PdfFile().ExecuteAsync(http)));
        Assert.Equal(0, output.Length);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task InvalidSecondPageDoesNotReplaceExistingSaveTarget(bool asynchronous)
    {
        string file = Path.Combine(Path.GetTempPath(), "kypelon-preflight-" + Guid.NewGuid() + ".pdf");
        try
        {
            await File.WriteAllTextAsync(file, "KEEP");
            Assert.NotNull(await Record.ExceptionAsync(async () =>
            {
                if (asynchronous) await BadSecondPage().SaveAsync(file);
                else BadSecondPage().Save(file);
            }));
            Assert.Equal("KEEP", await File.ReadAllTextAsync(file));
        }
        finally { File.Delete(file); }
    }
}
