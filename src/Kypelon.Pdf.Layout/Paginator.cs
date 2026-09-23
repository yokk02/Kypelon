using Kypelon.Pdf.Core;
using Kypelon.Pdf.Graphics;

namespace Kypelon.Pdf.Layout;

/// <summary>A page template with independently reserved header, footer and flowing content.</summary>
public sealed class PageTemplate
{
    /// <summary>Physical paper size.</summary>
    public PdfPageSize Size { get; set; } = PdfPageSize.A4;
    /// <summary>Page margins.</summary>
    public PdfMargins Margins { get; set; } = new(32);
    /// <summary>Repeated header content.</summary>
    public ColumnElement Header { get; } = new();
    /// <summary>Flow content.</summary>
    public ColumnElement Content { get; } = new();
    /// <summary>Repeated footer content.</summary>
    public ColumnElement Footer { get; } = new();
    /// <summary>Gap after nonempty header.</summary>
    public double HeaderGap { get; set; } = 12;
    /// <summary>Gap before nonempty footer.</summary>
    public double FooterGap { get; set; } = 10;
}
/// <summary>A diagnostic pagination decision.</summary>
public sealed record LayoutDiagnostic(string Element, double MeasuredHeight, double AvailableHeight, string Decision, int RemainingRows = 0);
/// <summary>One arranged page, without serialized or compressed PDF content.</summary>
public sealed record PagePlan(PdfPageSize Size, Rect ContentBounds, IReadOnlyList<RenderCommand> Commands);
/// <summary>Own deterministic two-pass page planner.</summary>
public static class Paginator
{
    /// <summary>Measures and arranges all templates into lightweight page plans.</summary>
    public static IReadOnlyList<PagePlan> Plan(IEnumerable<PageTemplate> templates, LayoutContext context, Action<LayoutDiagnostic>? diagnostic = null)
    {
        var pages = new List<PagePlan>();
        foreach (var template in templates)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            template.Size.Validate();
            var m = template.Margins;
            new Insets(m.Left, m.Top, m.Right, m.Bottom).Validate();
            if (!double.IsFinite(template.HeaderGap + template.FooterGap) || template.HeaderGap < 0 || template.FooterGap < 0)
                throw new PdfLayoutException("Header/footer gaps must be finite and nonnegative.");
            double width = template.Size.Width - m.Left - m.Right;
            var header = template.Header.Measure(context, width);
            var footer = template.Footer.Measure(context, width);
            double top = m.Top + header.Height + (template.Header.Children.Count > 0 ? template.HeaderGap : 0);
            double bottom = template.Size.Height - m.Bottom - footer.Height - (template.Footer.Children.Count > 0 ? template.FooterGap : 0), capacity = bottom - top;
            if (capacity <= 0)
                throw new PdfLayoutException("Header, footer and margins leave no content area.");
            var bounds = new Rect(m.Left, top, width, capacity);
            List<RenderCommand> commands = [];
            double y = top;
            bool hasContent = false;
            void Begin()
            {
                commands = [];
                y = top;
                hasContent = false;
                commands.AddRange(header.Arrange(m.Left, m.Top));
                commands.AddRange(footer.Arrange(m.Left, template.Size.Height - m.Bottom - footer.Height));
                if (context.DebugLayout)
                    commands.Add(new RectangleCommand(bounds, null, Color.Hex("#D97706"), .75, true));
            }
            void End()
            {
                if (pages.Count >= 10_000)
                    throw new PdfLayoutException("Page limit (10,000) exceeded.");
                pages.Add(new(template.Size, bounds, commands.AsReadOnly()));
            }
            void Next()
            {
                End();
                Begin();
            }
            void Add(MeasuredBlock block)
            {
                commands.AddRange(block.Arrange(m.Left, y));
                y += block.Height;
                hasContent |= block.Height > 0;
            }
            void Atomic(Element element)
            {
                var block = element.Measure(context, width);
                if (block.Height > capacity + .001)
                    throw new PdfLayoutException($"{element.Label}: measured height {block.Height:0.##}pt exceeds page content height {capacity:0.##}pt. Reduce content or remove KeepTogether.");
                if (block.Height > bottom - y + .001 && hasContent)
                {
                    diagnostic?.Invoke(new(element.Label, block.Height, bottom - y, "Move to next page"));
                    Next();
                }
                Add(block);
            }
            void Paragraph(TextElement text)
            {
                if (text.Box.KeepTogether || text.Box.Height.HasValue || text.Box.MinHeight > 0 || !double.IsPositiveInfinity(text.Box.MaxHeight) || text.IsPageNumber)
                {
                    Atomic(text);
                    return;
                }
                var whole = text.Measure(context, width);
                if (whole.Height <= bottom - y + .001)
                {
                    Add(whole);
                    return;
                }
                var lines = whole.Commands.OfType<TextCommand>().Select(t => t.Prepared!).ToArray();
                var metrics = text.Metrics(context);
                int start = 0;
                while (start < lines.Length)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    var margin = text.Box.Margin with
                    {
                        Top = start == 0 ? text.Box.Margin.Top : 0
                    };
                    double space = bottom - y - text.Box.Padding.Vertical - margin.Vertical;
                    int count = (int)Math.Floor((space + .001) / metrics.Step);
                    if (count < 1)
                    {
                        if (hasContent)
                        {
                            Next();
                            continue;
                        }
                        throw new PdfLayoutException($"{text.Label}: one text line plus padding cannot fit the {capacity:0.##}pt content height.");
                    }
                    count = Math.Min(count, lines.Length - start);
                    if (start + count < lines.Length)
                        margin = margin with
                        {
                            Bottom = 0
                        };
                    var fragment = new TextElement(string.Join("\n", lines.Skip(start).Take(count).Select(l => l.Text)), text.Style) { Box = text.Box with { Margin = margin }, Id = text.Id, PreparedLines = Array.AsReadOnly(lines.Skip(start).Take(count).ToArray()) };
                    Add(fragment.Measure(context, width));
                    start += count;
                    if (start < lines.Length)
                    {
                        diagnostic?.Invoke(new(text.Label, whole.Height, capacity, "Split paragraph"));
                        Next();
                    }
                }
            }
            void Table(TableElement table)
            {
                if (table.Box.KeepTogether)
                {
                    Atomic(table);
                    return;
                }
                if (table.Box != new BoxStyle())
                    throw new PdfLayoutException($"{table.Label}: flowing tables use cell styling; outer box constraints require KeepTogether.");
                var widths = table.ResolveWidths(context, width);
                var head = table.Header is null ? null : table.MeasureRow(context, widths, table.Header, -1, true);
                if (table.Rows.Count == 0)
                {
                    if (head is not null)
                    {
                        if (head.Height > capacity)
                            throw new PdfLayoutException("Table header exceeds content height.");
                        if (head.Height > bottom - y && hasContent)
                            Next();
                        Add(head);
                    }
                    return;
                }
                bool needsHeader = true;
                for (int i = 0; i < table.Rows.Count; i++)
                {
                    var row = table.MeasureRow(context, widths, table.Rows[i], i);
                    double minimum = row.Height + (head?.Height ?? 0);
                    if (minimum > capacity + .001)
                        throw new PdfLayoutException($"{table.Label}: row {i + 1} plus repeated header requires {minimum:0.##}pt, exceeding page content height {capacity:0.##}pt.");
                    double required = row.Height + (needsHeader ? head?.Height ?? 0 : 0);
                    if (required > bottom - y + .001)
                    {
                        diagnostic?.Invoke(new(table.Label, required, bottom - y, "Split table", table.Rows.Count - i));
                        Next();
                        needsHeader = true;
                    }
                    if (needsHeader && head is not null)
                        Add(head);
                    needsHeader = false;
                    Add(row);
                }
            }
            void Flow(Element element)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                switch (element)
                {
                    case PageBreakElement:
                        Next();
                        break;
                    case ColumnElement column when column.Box == new BoxStyle():
                        foreach (var child in column.Children)
                            Flow(child);
                        break;
                    case TableElement table:
                        Table(table);
                        break;
                    case TextElement text:
                        Paragraph(text);
                        break;
                    default:
                        Atomic(element);
                        break;
                }
            }
            Begin();
            Flow(template.Content);
            End();
        }
        if (pages.Count == 0)
            throw new PdfLayoutException("A document must contain at least one page template.");
        return pages.AsReadOnly();
    }
}
