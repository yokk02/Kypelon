using Kypelon.Pdf.Core;
using Kypelon.Pdf.Graphics;
using Kypelon.Pdf.Text;

namespace Kypelon.Pdf.Layout;

/// <summary>Reports impossible geometry or pagination.</summary>
public class PdfLayoutException(string message) : Exception(message);
/// <summary>Logical horizontal alignment.</summary>
public enum HorizontalAlignment
{ /// <summary>Start edge.</summary>
    Left, /// <summary>Center.</summary>
    Center, /// <summary>End edge.</summary>
    Right
}
/// <summary>Insets in points.</summary>
public readonly record struct Insets(double Left, double Top, double Right, double Bottom)
{
    /// <summary>Uniform insets.</summary>
    public Insets(double all) : this(all, all, all, all) { }
    internal double Horizontal => Left + Right;
    internal double Vertical => Top + Bottom;
    internal void Validate()
    {
        if (new[] { Left, Top, Right, Bottom }.Any(v => !double.IsFinite(v) || v < 0))
            throw new PdfLayoutException("Insets must be finite and nonnegative.");
    }
}
/// <summary>Reusable immutable text style; dimensions are in points.</summary>
public sealed record TextStyle
{
    /// <summary>Explicit face, or the document default.</summary>
    public FontFace? Font
    {
        get; init;
    }
    /// <summary>Font size.</summary>
    public double Size { get; init; } = 11;
    /// <summary>Explicit shaping direction; automatic bidi itemization is not implemented.</summary>
    public TextDirection Direction { get; init; }
    /// <summary>Optional OpenType script hint.</summary>
    public string? Script { get; init; }
    /// <summary>Optional language hint.</summary>
    public string? Language { get; init; }
    /// <summary>Line-height multiplier (at least one).</summary>
    public double LineHeight { get; init; } = 1.35;
    /// <summary>Bold selection, using a registered bold face or synthetic stroke.</summary>
    public bool Bold
    {
        get; init;
    }
    /// <summary>Text color.</summary>
    public Color Color { get; init; } = Color.Black;
    /// <summary>Horizontal text alignment.</summary>
    public HorizontalAlignment Alignment
    {
        get; init;
    }
}
/// <summary>Immutable box constraints. Dimensions include padding, excluding margin.</summary>
public sealed record BoxStyle
{
    /// <summary>Outer spacing.</summary>
    public Insets Margin
    {
        get; init;
    }
    /// <summary>Inner spacing.</summary>
    public Insets Padding
    {
        get; init;
    }
    /// <summary>Optional exact width.</summary>
    public double? Width
    {
        get; init;
    }
    /// <summary>Optional exact height.</summary>
    public double? Height
    {
        get; init;
    }
    /// <summary>Minimum width.</summary>
    public double MinWidth
    {
        get; init;
    }
    /// <summary>Maximum width.</summary>
    public double MaxWidth { get; init; } = double.PositiveInfinity;
    /// <summary>Minimum height.</summary>
    public double MinHeight
    {
        get; init;
    }
    /// <summary>Maximum height.</summary>
    public double MaxHeight { get; init; } = double.PositiveInfinity;
    /// <summary>Box alignment in its available width.</summary>
    public HorizontalAlignment Alignment
    {
        get; init;
    }
    /// <summary>Optional fill.</summary>
    public Color? Background
    {
        get; init;
    }
    /// <summary>Optional border color.</summary>
    public Color? Border
    {
        get; init;
    }
    /// <summary>Border width.</summary>
    public double BorderWidth { get; init; } = 0.5;
    /// <summary>Requests atomic placement.</summary>
    public bool KeepTogether
    {
        get; init;
    }
}
/// <summary>Measurement dependencies, without PDF serialization concerns.</summary>
public sealed class LayoutContext
{
    /// <summary>Default regular font.</summary>
    public FontFace Font { get; init; } = FontFace.Courier;
    /// <summary>Optional matching bold face.</summary>
    public FontFace? BoldFont
    {
        get; init;
    }
    /// <summary>Text shaper used only during preparation and measurement.</summary>
    public ITextShaper Shaper { get; init; } = BasicTextShaper.Instance;
    /// <summary>Include diagnostic bounds in page plans.</summary>
    public bool DebugLayout
    {
        get; init;
    }
    /// <summary>Cancellation during pagination.</summary>
    public CancellationToken CancellationToken
    {
        get; init;
    }
    internal (FontFace Font, bool Synthetic) Resolve(TextStyle style)
    {
        var regular = style.Font ?? Font;
        if (!style.Bold)
            return (regular, false);
        if (regular == FontFace.Courier)
            return (FontFace.CourierBold, false);
        return style.Font is null && BoldFont is not null ? (BoldFont, false) : (regular, true);
    }
}
/// <summary>A logical rectangle in top-left coordinates.</summary>
public readonly record struct Rect(double X, double Y, double Width, double Height);
/// <summary>Base page render instruction, independent of PDF syntax.</summary>
public abstract record RenderCommand;
/// <summary>Text positioned by baseline.</summary>
public sealed record TextCommand(string Text, FontFace Font, double Size, Color Color, double X, double Baseline, bool SyntheticBold, bool PageNumber = false, double ReservedWidth = 0, HorizontalAlignment Alignment = HorizontalAlignment.Left) : RenderCommand
{
    /// <summary>Immutable shaped state; required in finalized document plans.</summary>
    public TextLine? Prepared { get; init; }
    /// <summary>Explicit shaping direction.</summary>
    public TextDirection Direction { get; init; }
    /// <summary>Optional script hint.</summary>
    public string? Script { get; init; }
    /// <summary>Optional language hint.</summary>
    public string? Language { get; init; }
}
/// <summary>A filled or stroked rectangle.</summary>
public sealed record RectangleCommand(Rect Bounds, Color? Fill, Color? Stroke, double LineWidth, bool DebugOnly = false, double CornerRadius = 0) : RenderCommand;
/// <summary>An HTTP(S) link hit area, positioned in logical top-left coordinates.</summary>
public sealed record LinkCommand(string Url, Rect Bounds) : RenderCommand;
/// <summary>An image in logical bounds.</summary>
public sealed record ImageCommand(PdfImage Image, Rect Bounds) : RenderCommand;
/// <summary>Measured local render tree; Arrange translates it into page coordinates.</summary>
public sealed record MeasuredBlock(double Width, double Height, IReadOnlyList<RenderCommand> Commands)
{
    /// <summary>Places the measured block at page coordinates.</summary>
    public IEnumerable<RenderCommand> Arrange(double x, double y) => Commands.Select<RenderCommand, RenderCommand>(c => c switch
    {
        TextCommand t => t with { X = t.X + x, Baseline = t.Baseline + y },
        RectangleCommand r => r with { Bounds = r.Bounds with { X = r.Bounds.X + x, Y = r.Bounds.Y + y } },
        LinkCommand l => l with { Bounds = l.Bounds with { X = l.Bounds.X + x, Y = l.Bounds.Y + y } },
        ImageCommand i => i with { Bounds = i.Bounds with { X = i.Bounds.X + x, Y = i.Bounds.Y + y } },
        _ => throw new PdfLayoutException("Unknown render command.")
    });
}
/// <summary>Base layout node. Builders and nodes are mutable and not thread-safe.</summary>
public abstract class Element
{
    /// <summary>Optional identifier for diagnostics.</summary>
    public string? Id
    {
        get; set;
    }
    /// <summary>Box constraints.</summary>
    public BoxStyle Box { get; set; } = new();
    /// <summary>Measures and creates a local render tree.</summary>
    public MeasuredBlock Measure(LayoutContext context, double availableWidth)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        Box.Margin.Validate();
        Box.Padding.Validate();
        if (!double.IsFinite(availableWidth) || availableWidth <= 0)
            throw new PdfLayoutException($"{Label}: no positive available width.");
        if (Box.MinWidth < 0 || Box.MinHeight < 0 || !double.IsFinite(Box.MinWidth + Box.MinHeight) || double.IsNaN(Box.MaxWidth) || double.IsNaN(Box.MaxHeight) || Box.MaxWidth < Box.MinWidth || Box.MaxHeight < Box.MinHeight || !double.IsFinite(Box.BorderWidth) || Box.BorderWidth < 0)
            throw new PdfLayoutException($"{Label}: invalid box constraints.");
        double width = Box.Width ?? Math.Min(availableWidth - Box.Margin.Horizontal, Box.MaxWidth);
        if (!double.IsFinite(width) || width <= Box.Padding.Horizontal || width < Box.MinWidth || width > Box.MaxWidth || width + Box.Margin.Horizontal > availableWidth + 0.001)
            throw new PdfLayoutException($"{Label}: width constraints exceed available {availableWidth:0.##}pt.");
        var body = MeasureContent(context, width - Box.Padding.Horizontal);
        double natural = body.Height + Box.Padding.Vertical;
        double height = Box.Height ?? Math.Max(natural, Box.MinHeight);
        if (!double.IsFinite(height) || height < natural - 0.001 || height < Box.MinHeight || height > Box.MaxHeight)
            throw new PdfLayoutException($"{Label}: content height {natural:0.##}pt does not fit requested height constraints.");
        double x = Box.Margin.Left + (Box.Alignment switch
        {
            HorizontalAlignment.Center => (availableWidth - width - Box.Margin.Horizontal) / 2,
            HorizontalAlignment.Right => availableWidth - width - Box.Margin.Horizontal,
            _ => 0
        });
        double y = Box.Margin.Top;
        var commands = new List<RenderCommand>();
        var bounds = new Rect(x, y, width, height);
        if (Box.Background is not null || Box.Border is not null)
            commands.Add(new RectangleCommand(bounds, Box.Background, Box.Border, Box.BorderWidth));
        commands.AddRange(body.Arrange(x + Box.Padding.Left, y + Box.Padding.Top));
        if (context.DebugLayout)
        {
            commands.Add(new RectangleCommand(new(0, 0, availableWidth, height + Box.Margin.Vertical), null, Color.Hex("#E04CA8"), .25, true));
            commands.Add(new RectangleCommand(bounds, null, Color.Hex("#3B82F6"), .25, true));
            commands.Add(new RectangleCommand(new(x + Box.Padding.Left, y + Box.Padding.Top, body.Width, body.Height), null, Color.Hex("#22A06B"), .25, true));
        }
        return new(availableWidth, height + Box.Margin.Vertical, commands);
    }
    /// <summary>Measures the inner content at an exact width.</summary>
    protected abstract MeasuredBlock MeasureContent(LayoutContext context, double width);
    internal string Label => GetType().Name + (Id is null ? "" : "#" + Id);
}
/// <summary>A wrapped paragraph.</summary>
public sealed class TextElement(string text, TextStyle? style = null) : Element
{
    /// <summary>Text in logical Unicode order.</summary>
    public string Text { get; set; } = text;
    /// <summary>Reusable text style.</summary>
    public TextStyle Style { get; set; } = style ?? new();
    /// <summary>True for page-number templates in headers/footers.</summary>
    public bool IsPageNumber
    {
        get; set;
    }
    internal (FontFace Font, bool Synthetic, double Step, double Ascent) Metrics(LayoutContext context)
    {
        if (!double.IsFinite(Style.Size) || Style.Size <= 0 || !double.IsFinite(Style.LineHeight) || Style.LineHeight < 1)
            throw new PdfLayoutException($"{Label}: invalid font size or line height.");
        var resolved = context.Resolve(Style);
        var m = resolved.Font.Metrics;
        double natural = (m.Ascender - m.Descender) * Style.Size / m.UnitsPerEm;
        double step = Math.Max(Style.Size * Style.LineHeight, natural);
        return (resolved.Font, resolved.Synthetic, step, m.Ascender * Style.Size / m.UnitsPerEm + (step - natural) / 2);
    }
    internal IReadOnlyList<TextLine>? PreparedLines { get; init; }
    internal IReadOnlyList<TextLine> Lines(LayoutContext context, double width)
    {
        var metrics = Metrics(context);
        return PreparedLines ?? LineBreaker.Wrap(Text, metrics.Font, Style.Size, width, context.Shaper, Style.Direction, Style.Script, Style.Language, context.CancellationToken);
    }
    /// <inheritdoc />
    protected override MeasuredBlock MeasureContent(LayoutContext context, double width)
    {
        var m = Metrics(context);
        if (IsPageNumber)
            return new(width, m.Step, [new TextCommand(Text, m.Font, Style.Size, Style.Color, 0, m.Ascent, m.Synthetic, true, width, Style.Alignment) { Direction = Style.Direction, Script = Style.Script, Language = Style.Language }]);
        var lines = Lines(context, width);
        var commands = new List<RenderCommand>();
        for (int i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            double x = Style.Alignment switch
            {
                HorizontalAlignment.Center => (width - l.Width) / 2,
                HorizontalAlignment.Right => width - l.Width,
                _ => 0
            };
            commands.Add(new TextCommand(l.Text, m.Font, Style.Size, Style.Color, IsPageNumber ? 0 : x, i * m.Step + m.Ascent, m.Synthetic, IsPageNumber, width, Style.Alignment) { Prepared = l, Direction = Style.Direction, Script = Style.Script, Language = Style.Language });
        }
        return new(width, lines.Count * m.Step, commands);
    }
}
/// <summary>Vertical empty space.</summary>
public sealed class SpacerElement(double height) : Element
{ /// <inheritdoc />
    protected override MeasuredBlock MeasureContent(LayoutContext context, double width)
    {
        if (!double.IsFinite(height) || height < 0)
            throw new PdfLayoutException("Spacer height must be nonnegative.");
        return new(width, height, []);
    }
}
/// <summary>A horizontal rule.</summary>
public sealed class LineElement(Color color, double thickness = 1) : Element
{ /// <inheritdoc />
    protected override MeasuredBlock MeasureContent(LayoutContext context, double width)
    {
        if (!double.IsFinite(thickness) || thickness <= 0)
            throw new PdfLayoutException("Line thickness must be positive.");
        return new(width, thickness, [new RectangleCommand(new(0, 0, width, thickness), color, null, 0)]);
    }
}
/// <summary>An aspect-preserving image scaled to the available width.</summary>
public sealed class ImageElement(PdfImage image) : Element
{ /// <inheritdoc />
    protected override MeasuredBlock MeasureContent(LayoutContext context, double width)
    {
        double height = width * image.Height / image.Width;
        return new(width, height, [new ImageCommand(image, new(0, 0, width, height))]);
    }
}
/// <summary>An explicit page boundary, only valid in flow content.</summary>
public sealed class PageBreakElement : Element
{ /// <inheritdoc />
    protected override MeasuredBlock MeasureContent(LayoutContext context, double width) => throw new PdfLayoutException("PageBreak is only supported in flowing page content.");
}
/// <summary>A column of children. An unstyled flow column may paginate between children.</summary>
public sealed class ColumnElement : Element
{
    /// <summary>Ordered child nodes.</summary>
    public List<Element> Children { get; } = [];
    /// <inheritdoc />
    protected override MeasuredBlock MeasureContent(LayoutContext context, double width)
    {
        var commands = new List<RenderCommand>();
        double y = 0;
        foreach (var c in Children)
        {
            var b = c.Measure(context, width);
            commands.AddRange(b.Arrange(0, y));
            y += b.Height;
        }
        return new(width, y, commands);
    }
}
/// <summary>A row of equally wide cells, kept together on a page.</summary>
public sealed class RowElement : Element
{
    /// <summary>Cells in source order.</summary>
    public List<Element> Children { get; } = [];
    /// <summary>Space between cells.</summary>
    public double Gap { get; set; } = 8;
    /// <inheritdoc />
    protected override MeasuredBlock MeasureContent(LayoutContext context, double width)
    {
        if (!double.IsFinite(Gap) || Gap < 0)
            throw new PdfLayoutException("Row gap must be nonnegative.");
        if (Children.Count == 0)
            return new(width, 0, []);
        double cell = (width - Gap * (Children.Count - 1)) / Children.Count, height = 0;
        var commands = new List<RenderCommand>();
        for (int i = 0; i < Children.Count; i++)
        {
            var b = Children[i].Measure(context, cell);
            height = Math.Max(height, b.Height);
            commands.AddRange(b.Arrange(i * (cell + Gap), 0));
        }
        return new(width, height, commands);
    }
}
