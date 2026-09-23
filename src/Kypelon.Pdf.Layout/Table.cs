using Kypelon.Pdf.Graphics;
using Kypelon.Pdf.Text;

namespace Kypelon.Pdf.Layout;

/// <summary>Table width policy.</summary>
public enum ColumnKind
{ /// <summary>Exact points.</summary>
    Fixed, /// <summary>Weighted remaining space.</summary>
    Flex, /// <summary>Measured content width, capped to an equal share.</summary>
    Auto
}
/// <summary>A table column definition.</summary>
public readonly record struct ColumnDefinition(ColumnKind Kind, double Value = 1);
/// <summary>Optional complete text-style and background overrides for a table cell.</summary>
public sealed record TableCellStyle(TextStyle? Text = null, Color? Background = null);
/// <summary>Paginated business table. Rows are atomic and headers repeat on continuation pages.</summary>
public sealed class TableElement : Element
{
    /// <summary>Column definitions.</summary>
    public List<ColumnDefinition> Columns { get; } = [];
    /// <summary>Optional repeated header.</summary>
    public string[]? Header
    {
        get; set;
    }
    /// <summary>Body row values.</summary>
    public List<string[]> Rows { get; } = [];
    /// <summary>Body cell style.</summary>
    public TextStyle Style { get; set; } = new() { Size = 10 };
    /// <summary>Header cell style.</summary>
    public TextStyle HeaderStyle { get; set; } = new() { Size = 10, Bold = true };
    /// <summary>Uniform cell padding.</summary>
    public double CellPadding { get; set; } = 5;
    /// <summary>Grid border color.</summary>
    public Color BorderColor { get; set; } = Color.Hex("#D6DEE8");
    /// <summary>Header fill.</summary>
    public Color HeaderBackground { get; set; } = Color.Hex("#E9EFF7");
    /// <summary>Alternating row fill.</summary>
    public Color AlternateBackground { get; set; } = Color.Hex("#F7F9FC");
    /// <summary>Optional per-column alignment.</summary>
    public Dictionary<int, HorizontalAlignment> Alignments { get; } = [];
    /// <summary>Cell overrides keyed by zero-based body row and column. Row -1 addresses the repeated header.</summary>
    public Dictionary<(int Row, int Column), TableCellStyle> CellStyles { get; } = [];
    private TextStyle ResolveStyle(int row, int column, bool header)
    {
        var style = header ? HeaderStyle : Style;
        if (Alignments.TryGetValue(column, out var alignment)) style = style with { Alignment = alignment };
        return CellStyles.TryGetValue((row, column), out var cell) && cell.Text is not null ? cell.Text : style;
    }
    internal double[] ResolveWidths(LayoutContext context, double width)
    {
        if (Columns.Count is < 1 or > 256 || !double.IsFinite(CellPadding) || CellPadding < 0)
            throw new PdfLayoutException($"{Label}: define 1 to 256 columns and nonnegative cell padding.");
        if (Columns.Any(c => !double.IsFinite(c.Value) || c.Value <= 0 || !Enum.IsDefined(c.Kind)))
            throw new PdfLayoutException($"{Label}: invalid column width or weight.");
        if (CellStyles.Any(c => c.Key.Column < 0 || c.Key.Column >= Columns.Count || c.Key.Row < -1 || c.Key.Row >= Rows.Count || (c.Key.Row == -1 && Header is null) || c.Value is null))
            throw new PdfLayoutException($"{Label}: cell style points to a nonexistent row or column.");
        var widths = new double[Columns.Count];
        double used = 0, weight = 0;
        for (int i = 0; i < Columns.Count; i++)
        {
            var c = Columns[i];
            if (c.Kind == ColumnKind.Flex)
            {
                weight += c.Value;
                continue;
            }
            double desired = c.Value;
            if (c.Kind == ColumnKind.Auto)
            {
                desired = CellPadding * 2 + 1;
                for (int rowIndex = Header is null ? 0 : -1; rowIndex < Rows.Count; rowIndex++)
                {
                    var row = rowIndex == -1 ? Header! : Rows[rowIndex];
                    context.CancellationToken.ThrowIfCancellationRequested();
                    if (row.Length != Columns.Count)
                        throw new PdfLayoutException($"{Label}: cell count does not match column count.");
                    string value = row[i] ?? "";
                    var textStyle = ResolveStyle(rowIndex, i, rowIndex == -1);
                    var font = context.Resolve(textStyle).Font;
                    desired = Math.Max(desired, LineBreaker.Measure(value, font, textStyle.Size, context.Shaper, textStyle.Direction, textStyle.Script, textStyle.Language) + CellPadding * 2);
                }
                desired = Math.Min(desired, width / Columns.Count);
            }
            widths[i] = desired;
            used += desired;
        }
        if (used > width + 0.001)
            throw new PdfLayoutException($"{Label}: fixed/auto columns require {used:0.##}pt but only {width:0.##}pt is available.");
        for (int i = 0; i < Columns.Count; i++)
            if (Columns[i].Kind == ColumnKind.Flex)
                widths[i] = (width - used) * Columns[i].Value / weight;
        if (widths.Any(w => w <= CellPadding * 2))
            throw new PdfLayoutException($"{Label}: a column is narrower than its cell padding.");
        return widths;
    }
    internal MeasuredBlock MeasureRow(LayoutContext context, double[] widths, string[] values, int rowIndex, bool header = false)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (values.Length != widths.Length)
            throw new PdfLayoutException($"{Label}: row {rowIndex + 1} has {values.Length} cells; expected {widths.Length}.");
        var cells = new MeasuredBlock[values.Length];
        double height = 0;
        for (int i = 0; i < cells.Length; i++)
        {
            var style = ResolveStyle(rowIndex, i, header);
            try
            {
                cells[i] = new TextElement(values[i], style).Measure(context, widths[i] - CellPadding * 2);
            }
            catch (PdfFontException e) { throw new PdfLayoutException($"{Label}: row {rowIndex + 1}, cell {i + 1}: {e.Message}"); }
            height = Math.Max(height, cells[i].Height + CellPadding * 2);
        }
        var commands = new List<RenderCommand>();
        double x = 0;
        for (int i = 0; i < cells.Length; i++)
        {
            Color fill = header ? HeaderBackground : rowIndex % 2 == 1 ? AlternateBackground : Color.White;
            if (CellStyles.TryGetValue((rowIndex, i), out var cellStyle) && cellStyle.Background is { } background) fill = background;
            commands.Add(new RectangleCommand(new(x, 0, widths[i], height), fill, BorderColor, .4));
            commands.AddRange(cells[i].Arrange(x + CellPadding, CellPadding));
            if (context.DebugLayout)
                commands.Add(new RectangleCommand(new(x, 0, widths[i], height), null, Color.Hex("#E04CA8"), .35, true));
            x += widths[i];
        }
        return new(widths.Sum(), height, commands);
    }
    /// <inheritdoc />
    protected override MeasuredBlock MeasureContent(LayoutContext context, double width)
    {
        var widths = ResolveWidths(context, width);
        var commands = new List<RenderCommand>();
        double y = 0;
        void Add(MeasuredBlock b)
        {
            commands.AddRange(b.Arrange(0, y));
            y += b.Height;
        }
        if (Header is not null)
            Add(MeasureRow(context, widths, Header, -1, true));
        for (int i = 0; i < Rows.Count; i++)
            Add(MeasureRow(context, widths, Rows[i], i));
        return new(width, y, commands);
    }
}
