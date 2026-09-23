using Kypelon.Pdf.Core;
using Kypelon.Pdf.Layout;
using Xunit;

namespace Kypelon.Pdf.Tests;

public class LayoutTests
{
    internal static PdfDocument Table(int rows = 500) => Pdf.Document(d => d.Page(p => { p.Header(h => h.Text("Report header")); p.Content(c => { c.Table(t => { t.Columns(Column.Fixed(50), Column.Flex()); t.Header("No.", "Employee"); for (int i = 1; i <= rows; i++) t.Row(i, $"Employee {i:D4}"); }); }); p.Footer(f => f.PageNumber().Center()); }));
    [Fact]
    public void FiveHundredRowsSurvivePaginationExactlyOnce()
    {
        var plans = Table().Plan();
        Assert.True(plans.Count > 10);
        var all = plans.SelectMany(p => p.Commands.OfType<TextCommand>()).ToArray();
        for (int i = 1; i <= 500; i++)
            Assert.Single(all, t => t.Text == $"Employee {i:D4}");
        for (int i = 0; i < plans.Count; i++)
        {
            var text = plans[i].Commands.OfType<TextCommand>().Select(t => t.Text).ToArray();
            Assert.Contains("No.", text);
            Assert.Contains("Employee", text);
            Assert.Contains("Report header", text);
            Assert.Contains($"Page {i + 1} of {plans.Count}", text);
        }
    }
    [Fact]
    public void TableCellsStayWithinContentBounds()
    {
        foreach (var p in Table().Plan())
            foreach (var r in p.Commands.OfType<RectangleCommand>().Where(r => r.Fill.HasValue))
            {
                Assert.True(r.Bounds.Y >= p.ContentBounds.Y - .01);
                Assert.True(r.Bounds.Y + r.Bounds.Height <= p.ContentBounds.Y + p.ContentBounds.Height + .01);
                Assert.True(r.Bounds.X + r.Bounds.Width <= p.ContentBounds.X + p.ContentBounds.Width + .01);
            }
    }
    [Fact]
    public void TallRowsFailInsteadOfLoopingOrClipping()
    {
        var doc = Pdf.Document(d => d.Page(p => p.Content(c => c.Table(t => { t.Columns(Column.Flex()); t.Header("Header"); t.Row(string.Join("\n", Enumerable.Repeat("line", 100))); }))));
        var ex = Assert.Throws<PdfLayoutException>(() => doc.Plan());
        Assert.Contains("row 1", ex.Message);
        Assert.Contains("content height", ex.Message);
    }
    [Fact]
    public void ParagraphSplitsWithoutDroppingLines()
    {
        var doc = Pdf.Document(d => d.Page(p => p.Size(new(200, 200)).Margin(20).Content(c => c.Paragraph(string.Join("\n", Enumerable.Range(1, 200).Select(i => $"Line {i:D3}"))))));
        var plans = doc.Plan();
        Assert.True(plans.Count > 10);
        var lines = plans.SelectMany(p => p.Commands.OfType<TextCommand>()).Select(t => t.Text).ToArray();
        Assert.Equal(Enumerable.Range(1, 200).Select(i => $"Line {i:D3}"), lines);
    }
    [Fact]
    public void ExplicitPageBreakStartsAnotherPage()
    {
        var doc = Pdf.Document(d => d.Page(p => p.Content(c => { c.Text("First"); c.PageBreak(); c.Text("Second"); })));
        Assert.Equal(2, doc.Plan().Count);
    }
    [Fact]
    public void KeepTogetherMovesToNextPage()
    {
        var doc = Pdf.Document(d => d.Page(p => p.Size(new(200, 200)).Margin(10).Content(c => { c.Spacer(140); c.Container(k => { k.Text("A"); k.Text("B"); k.Text("C"); }); })));
        var p = doc.Plan();
        Assert.Equal(2, p.Count);
        Assert.DoesNotContain(p[0].Commands.OfType<TextCommand>(), t => t.Text == "A");
        Assert.Contains(p[1].Commands.OfType<TextCommand>(), t => t.Text == "C");
    }
    [Fact]
    public void OversizeKeepTogetherFails()
    {
        var doc = Pdf.Document(d => d.Page(p => p.Content(c => c.Spacer(1000).KeepTogether())));
        Assert.Throws<PdfLayoutException>(() => doc.Plan());
    }
    [Fact]
    public void HeadersAndFootersReserveSpace()
    {
        var p = Table(1).Plan()[0];
        var h = p.Commands.OfType<TextCommand>().Single(t => t.Text == "Report header");
        var f = p.Commands.OfType<TextCommand>().Single(t => t.Text.StartsWith("Page ", StringComparison.Ordinal));
        Assert.True(h.Baseline < p.ContentBounds.Y);
        Assert.True(f.Baseline > p.ContentBounds.Y + p.ContentBounds.Height);
    }
    [Fact]
    public void ColumnsCannotExceedWidth()
    {
        var doc = Pdf.Document(d => d.Page(p => p.Content(c => c.Table(t => { t.Columns(Column.Fixed(999)); t.Row("Value"); }))));
        Assert.Throws<PdfLayoutException>(() => doc.Plan());
    }
    [Fact]
    public void CellCountMismatchReportsContext()
    {
        var doc = Pdf.Document(d => d.Page(p => p.Content(c => c.Table(t => { t.Columns(Column.Flex(), Column.Flex()); t.Row("Only one"); }))));
        Assert.Contains("expected 2", Assert.Throws<PdfLayoutException>(() => doc.Plan()).Message);
    }
    [Fact]
    public void RowUsesMaximumChildHeight()
    {
        var row = new RowElement();
        row.Children.Add(new SpacerElement(10));
        row.Children.Add(new SpacerElement(30));
        Assert.Equal(30, row.Measure(new(), 100).Height);
    }
    [Fact]
    public void BoxMeasurementIncludesPaddingAndMargins()
    {
        var e = new SpacerElement(20) { Box = new() { Padding = new(4), Margin = new(3) } };
        var measured = e.Measure(new(), 100);
        Assert.Equal(34, measured.Height);
        Assert.Equal(100, measured.Width);
    }
    [Fact]
    public void WidthAndHeightConstraintsRejectOverflow()
    {
        var e = new TextElement("long content") { Box = new() { Width = 200 } };
        Assert.Throws<PdfLayoutException>(() => e.Measure(new(), 100));
        e.Box = new()
        {
            Height = 1
        };
        Assert.Throws<PdfLayoutException>(() => e.Measure(new(), 100));
    }
    [Fact]
    public void GridPaginatesAtRowBoundaries()
    {
        var doc = Pdf.Document(d => d.Page(p => p.Size(new(240, 150)).Margin(10).Content(c => c.Grid(2, g => { for (int i = 0; i < 20; i++) g.Text("Cell " + i).Height(40); }))));
        var pages = doc.Plan();
        Assert.True(pages.Count > 1);
        Assert.Equal(20, pages.SelectMany(p => p.Commands.OfType<TextCommand>()).Count());
    }
    [Fact]
    public void DiagnosticsReportTableSplits()
    {
        var doc = Table();
        var events = new List<LayoutDiagnostic>();
        doc.Options.Diagnostic = events.Add;
        doc.Plan();
        Assert.Contains(events, e => e.Decision == "Split table" && e.RemainingRows > 0);
    }
    [Fact]
    public void PageNumberTemplateMustFit()
    {
        var doc = Pdf.Document(d => d.Page(p => p.Size(new(100, 200)).Margin(20).Footer(f => f.PageNumber("A very long page number template {page} of {pages}"))));
        Assert.Throws<PdfLayoutException>(() => doc.Plan());
    }
    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidMarginsFail(double margin)
    {
        var doc = Pdf.Document(d => d.Page(p => p.Margin(margin)));
        Assert.Throws<PdfLayoutException>(() => doc.Plan());
    }
    [Fact]
    public void AutoColumnsAreMeasuredAndCapped()
    {
        var doc = Pdf.Document(d => d.Page(p => p.Content(c => c.Table(t => { t.Columns(Column.Auto(), Column.Flex()); t.Header("Code", "Description"); t.Row("A1", "An auto-width column"); }))));
        Assert.Single(doc.Plan());
    }
}
