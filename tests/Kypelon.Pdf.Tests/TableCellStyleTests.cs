using Kypelon.Pdf.Core;
using Kypelon.Pdf.Graphics;
using Kypelon.Pdf.Layout;
using Xunit;

namespace Kypelon.Pdf.Tests;

public class TableCellStyleTests
{
    [Fact]
    public void CellFontSizeChangesRowHeightAndRenderedStyle()
    {
        var table = new TableElement { Style = new() { Size = 10 }, CellPadding = 5 };
        table.Columns.AddRange([Column.Flex(), Column.Flex()]);
        table.Rows.Add(["Large", "Normal"]);
        var before = table.Measure(new(), 240);
        var red = Color.Hex("#C02020");
        var green = Color.Hex("#EFF8E7");
        table.CellStyles[(0, 0)] = new(new() { Size = 22, Bold = true, Color = red }, green);
        var after = table.Measure(new(), 240);
        Assert.True(after.Height > before.Height);
        Assert.Equal(39.7, after.Height, 6);
        var text = Assert.Single(after.Commands.OfType<TextCommand>(), t => t.Text == "Large");
        Assert.Equal(22, text.Size);
        Assert.Equal(red, text.Color);
        Assert.Equal("Courier-Bold", text.Font.Name);
        Assert.Contains(after.Commands.OfType<RectangleCommand>(), r => r.Fill == green && r.Bounds.Height == after.Height);
    }

    [Fact]
    public void AutoColumnMeasuresHeaderAndPerCellFonts()
    {
        var table = new TableElement { Style = new() { Size = 8 }, HeaderStyle = new() { Size = 20, Bold = true }, Header = ["WWWW", "Header"] };
        table.Columns.AddRange([Column.Auto(), Column.Flex()]);
        table.Rows.Add(["MMMMMMMM", "Body"]);
        var normal = table.Measure(new(), 240);
        Assert.Equal(58, normal.Commands.OfType<RectangleCommand>().First().Bounds.Width, 6);
        table.CellStyles[(0, 0)] = new(new() { Size = 24 });
        var styled = table.Measure(new(), 240);
        Assert.Equal(120, styled.Commands.OfType<RectangleCommand>().First().Bounds.Width, 6);
    }

    [Fact]
    public void HeaderAndBodyOverridesSurviveAutomaticPagination()
    {
        var white = new TextStyle { Size = 14, Bold = true, Color = Color.White };
        var blue = Color.Hex("#245B8E");
        var amber = Color.Hex("#FFF4E5");
        var doc = Pdf.Document(d => d.Page(p =>
        {
            p.Size(new PdfPageSize(220, 140)).Margin(10);
            p.Content(c => c.Table(t =>
            {
                t.Columns(Column.Flex(), Column.Flex()).Style(new() { Size = 8 }).CellPadding(2).Header("State", "Record");
                for (int i = 0; i < 30; i++) t.Row("Open", $"Row{i}");
                t.CellStyle(-1, 0, new(white, blue));
                t.CellStyle(27, 1, new(new() { Size = 20 }, amber));
            }));
        }));
        var pages = doc.Plan();
        Assert.True(pages.Count > 1);
        foreach (var page in pages)
        {
            var header = Assert.Single(page.Commands.OfType<TextCommand>(), t => t.Text == "State");
            Assert.Equal(14, header.Size);
            Assert.Equal(Color.White, header.Color);
            Assert.Contains(page.Commands.OfType<RectangleCommand>(), r => r.Fill == blue);
        }
        var body = pages.SelectMany(p => p.Commands).OfType<TextCommand>().Where(t => t.Text.StartsWith("Row", StringComparison.Ordinal)).ToArray();
        Assert.Equal(30, body.Length);
        Assert.Equal(20, Assert.Single(body, t => t.Text == "Row27").Size);
        Assert.Single(pages.SelectMany(p => p.Commands).OfType<RectangleCommand>(), r => r.Fill == amber);
        Inspect.Objects(doc.ToArray());
    }

    [Theory]
    [InlineData(-2, 0)]
    [InlineData(0, 2)]
    [InlineData(1, 0)]
    [InlineData(-1, 0)]
    public void NonexistentCellOverridesFailWithContext(int row, int column)
    {
        var table = new TableElement();
        table.Columns.Add(Column.Flex());
        table.Rows.Add(["Only cell"]);
        table.CellStyles[(row, column)] = new(Background: Color.Black);
        Assert.Contains("nonexistent row or column", Assert.Throws<PdfLayoutException>(() => table.Measure(new(), 200)).Message);
    }
}
