using System.Globalization;
using Kypelon.Pdf.Core;
using Kypelon.Pdf.Graphics;
using Kypelon.Pdf.Layout;
using Kypelon.Pdf.Text;

namespace Kypelon.Pdf;

/// <summary>Entry point for composing documents.</summary>
public static class Pdf
{
    /// <summary>Builds a document using a strongly typed fluent API.</summary>
    public static PdfDocument Document(Action<DocumentBuilder> compose)
    {
        ArgumentNullException.ThrowIfNull(compose);
        var builder = new DocumentBuilder();
        compose(builder);
        return builder.Build();
    }
    /// <summary>Alias for Document.</summary>
    public static PdfDocument Create(Action<DocumentBuilder> compose) => Document(compose);
}
/// <summary>Column sizing helpers.</summary>
public static class Column
{
    /// <summary>Exact width in points.</summary>
    public static ColumnDefinition Fixed(double points) => new(ColumnKind.Fixed, points);
    /// <summary>Weighted share of the remaining width.</summary>
    public static ColumnDefinition Flex(double weight = 1) => new(ColumnKind.Flex, weight);
    /// <summary>Content-sized column, capped at an equal share of the available width.</summary>
    public static ColumnDefinition Auto() => new(ColumnKind.Auto);
}
/// <summary>Document-wide style presets, applied when elements are added.</summary>
public sealed class DocumentStyles
{
    /// <summary>Body style.</summary>
    public TextStyle Default { get; set; } = new();
    /// <summary>Level-one heading style.</summary>
    public TextStyle H1 { get; set; } = new() { Size = 22, Bold = true, Color = Color.Hex("#14395B") };
    /// <summary>Table body style.</summary>
    public TextStyle Table { get; set; } = new() { Size = 9 };
    /// <summary>Table header style.</summary>
    public TextStyle TableHeader { get; set; } = new() { Size = 9, Bold = true, Color = Color.Hex("#14395B") };
}
/// <summary>Mutable document composition context; not thread-safe.</summary>
public sealed class DocumentBuilder
{
    private readonly List<PageTemplate> pages = [];
    private readonly Dictionary<string, (FontFace Regular, FontFace? Bold)> fonts = new(StringComparer.OrdinalIgnoreCase);
    private readonly DocumentStyles styles = new();
    private FontFace font = FontFace.Courier;
    private FontFace? bold;
    private ITextShaper shaper = BasicTextShaper.Instance;
    /// <summary>Registers explicit immutable fonts under a document-local name.</summary>
    public DocumentBuilder RegisterFont(string name, FontFace regular, FontFace? boldFace = null)
    {
        fonts.Add(name, (regular, boldFace));
        return this;
    }
    /// <summary>Selects a registered font family.</summary>
    public DocumentBuilder DefaultFont(string registeredName)
    {
        if (!fonts.TryGetValue(registeredName, out var f))
            throw new PdfFontException($"Font '{registeredName}' is not registered. Call RegisterFont first.");
        return DefaultFont(f.Regular, f.Bold);
    }
    /// <summary>Sets default regular and optional bold faces.</summary>
    public DocumentBuilder DefaultFont(FontFace regular, FontFace? boldFace = null)
    {
        font = regular ?? throw new ArgumentNullException(nameof(regular));
        bold = boldFace;
        return this;
    }
    /// <summary>Installs an optional specialized shaper.</summary>
    public DocumentBuilder TextShaper(ITextShaper value)
    {
        shaper = value ?? throw new ArgumentNullException(nameof(value));
        return this;
    }
    /// <summary>Configures reusable styles before adding content.</summary>
    public DocumentBuilder Style(Action<DocumentStyles> configure)
    {
        configure(styles);
        return this;
    }
    /// <summary>Adds a page template whose content may generate multiple physical pages.</summary>
    public DocumentBuilder Page(Action<PageBuilder> compose)
    {
        var page = new PageTemplate();
        compose(new(page, styles));
        pages.Add(page);
        return this;
    }
    internal PdfDocument Build() => new(pages.ToArray(), font, bold, shaper);
}
/// <summary>Configures a page template.</summary>
public sealed class PageBuilder
{
    private readonly PageTemplate page;
    private readonly DocumentStyles styles;
    internal PageBuilder(PageTemplate page, DocumentStyles styles)
    {
        this.page = page;
        this.styles = styles;
    }
    /// <summary>Selects ISO A4.</summary>
    public PageBuilder A4() => Size(PdfPageSize.A4);
    /// <summary>Sets the paper size in points.</summary>
    public PageBuilder Size(PdfPageSize value)
    {
        page.Size = value;
        return this;
    }
    /// <summary>Uses landscape orientation.</summary>
    public PageBuilder Landscape()
    {
        page.Size = page.Size.Landscape();
        return this;
    }
    /// <summary>Sets uniform margins in points.</summary>
    public PageBuilder Margin(double all) => Margin(new PdfMargins(all));
    /// <summary>Sets individual page margins.</summary>
    public PageBuilder Margin(PdfMargins margins)
    {
        page.Margins = margins;
        return this;
    }
    /// <summary>Composes a repeated header.</summary>
    public PageBuilder Header(Action<FlowBuilder> compose)
    {
        compose(new(page.Header.Children, styles));
        return this;
    }
    /// <summary>Composes flowing page content.</summary>
    public PageBuilder Content(Action<FlowBuilder> compose)
    {
        compose(new(page.Content.Children, styles));
        return this;
    }
    /// <summary>Composes a repeated footer.</summary>
    public PageBuilder Footer(Action<FlowBuilder> compose)
    {
        compose(new(page.Footer.Children, styles));
        return this;
    }
}
/// <summary>Shared fluent box configuration.</summary>
public class ElementBuilder<T> where T : ElementBuilder<T>
{
    /// <summary>The composed node.</summary>
    protected Element Element
    {
        get;
    }
    internal ElementBuilder(Element element) => Element = element;
    private T Self => (T)this;
    /// <summary>Assigns a diagnostic identifier.</summary>
    public T Id(string id)
    {
        Element.Id = id;
        return Self;
    }
    /// <summary>Sets all padding.</summary>
    public T Padding(double value)
    {
        Element.Box = Element.Box with
        {
            Padding = new(value)
        };
        return Self;
    }
    /// <summary>Sets individual padding.</summary>
    public T Padding(Insets value)
    {
        Element.Box = Element.Box with
        {
            Padding = value
        };
        return Self;
    }
    /// <summary>Sets all margins.</summary>
    public T Margin(double value)
    {
        Element.Box = Element.Box with
        {
            Margin = new(value)
        };
        return Self;
    }
    /// <summary>Sets individual margins.</summary>
    public T Margin(Insets value)
    {
        Element.Box = Element.Box with
        {
            Margin = value
        };
        return Self;
    }
    /// <summary>Sets the bottom margin.</summary>
    public T MarginBottom(double value)
    {
        Element.Box = Element.Box with
        {
            Margin = Element.Box.Margin with
            {
                Bottom = value
            }
        };
        return Self;
    }
    /// <summary>Sets exact box width.</summary>
    public T Width(double value)
    {
        Element.Box = Element.Box with
        {
            Width = value
        };
        return Self;
    }
    /// <summary>Sets exact box height; too-small heights fail rather than clip content.</summary>
    public T Height(double value)
    {
        Element.Box = Element.Box with
        {
            Height = value
        };
        return Self;
    }
    /// <summary>Sets width bounds.</summary>
    public T WidthRange(double minimum, double maximum)
    {
        Element.Box = Element.Box with
        {
            MinWidth = minimum,
            MaxWidth = maximum
        };
        return Self;
    }
    /// <summary>Sets height bounds.</summary>
    public T HeightRange(double minimum, double maximum)
    {
        Element.Box = Element.Box with
        {
            MinHeight = minimum,
            MaxHeight = maximum
        };
        return Self;
    }
    /// <summary>Sets box background.</summary>
    public T Background(string hex)
    {
        Element.Box = Element.Box with
        {
            Background = Color.Hex(hex)
        };
        return Self;
    }
    /// <summary>Sets box border.</summary>
    public T Border(string hex, double width = .5)
    {
        Element.Box = Element.Box with
        {
            Border = Color.Hex(hex),
            BorderWidth = width
        };
        return Self;
    }
    /// <summary>Keeps this entire element on one page or reports impossible height.</summary>
    public T KeepTogether()
    {
        Element.Box = Element.Box with
        {
            KeepTogether = true
        };
        return Self;
    }
    /// <summary>Aligns the box within its available width.</summary>
    public T AlignBox(HorizontalAlignment alignment)
    {
        Element.Box = Element.Box with
        {
            Alignment = alignment
        };
        return Self;
    }
}
/// <summary>Fluent configuration for a non-text box.</summary>
public sealed class BoxBuilder : ElementBuilder<BoxBuilder>
{
    internal BoxBuilder(Element element) : base(element) { }
}
/// <summary>Fluent text styling.</summary>
public sealed class TextBuilder : ElementBuilder<TextBuilder>
{
    private TextElement Text => (TextElement)Element;
    internal TextBuilder(TextElement text) : base(text) { }
    /// <summary>Sets font size in points.</summary>
    public TextBuilder FontSize(double size)
    {
        Text.Style = Text.Style with
        {
            Size = size
        };
        return this;
    }
    /// <summary>Sets an explicit face.</summary>
    public TextBuilder Font(FontFace font)
    {
        Text.Style = Text.Style with
        {
            Font = font
        };
        return this;
    }
    /// <summary>Requests bold text.</summary>
    public TextBuilder Bold()
    {
        Text.Style = Text.Style with
        {
            Bold = true
        };
        return this;
    }
    /// <summary>Sets a line-height multiplier.</summary>
    public TextBuilder LineHeight(double value)
    {
        Text.Style = Text.Style with
        {
            LineHeight = value
        };
        return this;
    }
    /// <summary>Sets text color.</summary>
    public TextBuilder Color(string hex)
    {
        Text.Style = Text.Style with
        {
            Color = Graphics.Color.Hex(hex)
        };
        return this;
    }
    /// <summary>Applies a reusable style.</summary>
    public TextBuilder Style(TextStyle style)
    {
        Text.Style = style;
        return this;
    }
    /// <summary>Centers text.</summary>
    public TextBuilder Center()
    {
        Text.Style = Text.Style with
        {
            Alignment = HorizontalAlignment.Center
        };
        return this;
    }
    /// <summary>Aligns text right.</summary>
    public TextBuilder AlignRight()
    {
        Text.Style = Text.Style with
        {
            Alignment = HorizontalAlignment.Right
        };
        return this;
    }
    /// <summary>Aligns text left.</summary>
    public TextBuilder AlignLeft()
    {
        Text.Style = Text.Style with
        {
            Alignment = HorizontalAlignment.Left
        };
        return this;
    }
}
/// <summary>Composes a flow or row of elements.</summary>
public sealed class FlowBuilder
{
    private readonly List<Element> children;
    private readonly DocumentStyles styles;
    internal FlowBuilder(List<Element> children, DocumentStyles styles)
    {
        this.children = children;
        this.styles = styles;
    }
    /// <summary>Adds a custom layout element, using the same measurement, pagination and rendering pipeline.</summary>
    public BoxBuilder Add(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        children.Add(element);
        return new(element);
    }
    /// <summary>Adds wrapping text.</summary>
    public TextBuilder Text(string text)
    {
        var e = new TextElement(text, styles.Default) { Box = new() { Margin = new(0, 0, 0, 5) } };
        children.Add(e);
        return new(e);
    }
    /// <summary>Adds a paragraph.</summary>
    public TextBuilder Paragraph(string text) => Text(text);
    /// <summary>Adds a styled level-one heading.</summary>
    public TextBuilder H1(string text) => Text(text).Style(styles.H1).Margin(new Insets(0, 8, 0, 10));
    /// <summary>Adds empty space.</summary>
    public BoxBuilder Spacer(double height)
    {
        var e = new SpacerElement(height);
        children.Add(e);
        return new(e);
    }
    /// <summary>Adds a horizontal rule.</summary>
    public BoxBuilder Line(string color = "#CBD5E1", double thickness = 1)
    {
        var e = new LineElement(Color.Hex(color), thickness);
        children.Add(e);
        return new(e);
    }
    /// <summary>Adds an aspect-preserving JPEG/PNG image.</summary>
    public BoxBuilder Image(PdfImage image)
    {
        var e = new ImageElement(image);
        children.Add(e);
        return new(e);
    }
    /// <summary>Adds an explicit page break.</summary>
    public void PageBreak() => children.Add(new PageBreakElement());
    /// <summary>Adds a page-number template. Use in headers or footers.</summary>
    public TextBuilder PageNumber(string format = "Page {page} of {pages}")
    {
        var e = new TextElement(format, styles.Default) { IsPageNumber = true };
        children.Add(e);
        return new(e);
    }
    /// <summary>Adds a flowing column. Styled columns are atomic.</summary>
    public BoxBuilder Column(Action<FlowBuilder> compose)
    {
        var e = new ColumnElement();
        compose(new(e.Children, styles));
        children.Add(e);
        return new(e);
    }
    /// <summary>Adds an atomic styled container.</summary>
    public BoxBuilder Container(Action<FlowBuilder> compose) => Column(compose).KeepTogether();
    /// <summary>Adds an atomic row with equal-width children.</summary>
    public BoxBuilder Row(Action<FlowBuilder> compose, double gap = 8)
    {
        var e = new RowElement { Gap = gap };
        compose(new(e.Children, styles));
        children.Add(e);
        return new(e);
    }
    /// <summary>Adds an equal-column grid that paginates between grid rows.</summary>
    public BoxBuilder Grid(int columns, Action<FlowBuilder> compose, double gap = 8)
    {
        if (columns is < 1 or > 256)
            throw new ArgumentOutOfRangeException(nameof(columns));
        var cells = new List<Element>();
        compose(new(cells, styles));
        var grid = new ColumnElement();
        for (int i = 0; i < cells.Count; i += columns)
        {
            var row = new RowElement { Gap = gap, Box = new() { Margin = new(0, 0, 0, gap) } };
            row.Children.AddRange(cells.Skip(i).Take(columns));
            while (row.Children.Count < columns)
                row.Children.Add(new SpacerElement(0));
            grid.Children.Add(row);
        }
        children.Add(grid);
        return new(grid);
    }
    /// <summary>Adds a table with automatic row pagination.</summary>
    public TableBuilder Table(Action<TableBuilder> compose)
    {
        var e = new TableElement { Style = styles.Table, HeaderStyle = styles.TableHeader };
        var builder = new TableBuilder(e);
        compose(builder);
        children.Add(e);
        return builder;
    }
}
/// <summary>Fluent table composition.</summary>
public sealed class TableBuilder : ElementBuilder<TableBuilder>
{
    private TableElement Table => (TableElement)Element;
    internal TableBuilder(TableElement table) : base(table) { }
    /// <summary>Defines columns before adding rows.</summary>
    public TableBuilder Columns(params ColumnDefinition[] columns)
    {
        Table.Columns.Clear();
        Table.Columns.AddRange(columns);
        return this;
    }
    /// <summary>Sets a repeated header row.</summary>
    public TableBuilder Header(params string[] values)
    {
        Table.Header = (string[])values.Clone();
        return this;
    }
    /// <summary>Adds a row, formatting values using invariant culture.</summary>
    public TableBuilder Row(params object?[] values)
    {
        Table.Rows.Add(values.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "").ToArray());
        return this;
    }
    /// <summary>Overrides a cell using zero-based row/column indices; row -1 styles the repeated header.</summary>
    public TableBuilder CellStyle(int row, int column, TableCellStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        Table.CellStyles[(row, column)] = style;
        return this;
    }
    /// <summary>Sets cell padding in points.</summary>
    public TableBuilder CellPadding(double value)
    {
        Table.CellPadding = value;
        return this;
    }
    /// <summary>Sets body and optional header text styles.</summary>
    public TableBuilder Style(TextStyle body, TextStyle? header = null)
    {
        Table.Style = body;
        Table.HeaderStyle = header ?? body with
        {
            Bold = true
        };
        return this;
    }
    /// <summary>Sets an individual column's text alignment.</summary>
    public TableBuilder AlignColumn(int index, HorizontalAlignment alignment)
    {
        if (index < 0 || index >= Table.Columns.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        Table.Alignments[index] = alignment;
        return this;
    }
    /// <summary>Sets header, alternating row and border colors.</summary>
    public TableBuilder Colors(string header, string alternate, string border)
    {
        Table.HeaderBackground = Color.Hex(header);
        Table.AlternateBackground = Color.Hex(alternate);
        Table.BorderColor = Color.Hex(border);
        return this;
    }
}
