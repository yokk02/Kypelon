using System.Text;
using Kypelon.Pdf.Core;

namespace Kypelon.Pdf.Graphics;

/// <summary>An RGB color with components in [0,1].</summary>
public readonly record struct Color(double R, double G, double B)
{
    /// <summary>Creates RGB from byte components.</summary>
    public static Color FromRgb(byte r, byte g, byte b) => new(r / 255.0, g / 255.0, b / 255.0);
    /// <summary>Creates gray from a component in [0,1].</summary>
    public static Color Gray(double value) => new(value, value, value);
    /// <summary>Parses #RRGGBB.</summary>
    public static Color Hex(string hex)
    {
        if (hex.Length != 7 || hex[0] != '#')
            throw new ArgumentException("Expected #RRGGBB.", nameof(hex));
        var b = Convert.FromHexString(hex[1..]);
        return FromRgb(b[0], b[1], b[2]);
    }
    /// <summary>Black.</summary>
    public static Color Black => new(0, 0, 0);
    /// <summary>White.</summary>
    public static Color White => new(1, 1, 1);
    internal void Validate()
    {
        if (!double.IsFinite(R + G + B) || R is < 0 or > 1 || G is < 0 or > 1 || B is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(R), "Color components must be in [0,1].");
    }
}
/// <summary>One page's PDF graphics commands, using a top-left origin and downward Y axis. Not thread-safe.</summary>
public sealed class PdfCanvas
{
    private readonly StringBuilder s = new();
    private int states;
    private int markedContent;
    /// <summary>Creates a logical top-left drawing surface.</summary>
    public PdfCanvas(double pageHeight)
    {
        if (!double.IsFinite(pageHeight) || pageHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageHeight));
        s.Append("q\n1 0 0 -1 0 ").Append(N(pageHeight)).Append(" cm\n");
    }
    private static string N(double value) => PdfReal.Format(value);
    /// <summary>Saves the graphics state.</summary>
    public PdfCanvas SaveState()
    {
        states++;
        s.Append("q\n");
        return this;
    }
    /// <summary>Restores a previously saved state.</summary>
    public PdfCanvas RestoreState()
    {
        if (states == 0)
            throw new InvalidOperationException("No saved graphics state.");
        states--;
        s.Append("Q\n");
        return this;
    }
    /// <summary>Concatenates an affine transform.</summary>
    public PdfCanvas Transform(double a, double b, double c, double d, double e, double f)
    {
        s.AppendJoin(' ', N(a), N(b), N(c), N(d), N(e), N(f)).Append(" cm\n");
        return this;
    }
    /// <summary>Translates the logical coordinates.</summary>
    public PdfCanvas Translate(double x, double y) => Transform(1, 0, 0, 1, x, y);
    /// <summary>Scales the logical coordinates.</summary>
    public PdfCanvas Scale(double x, double y) => Transform(x, 0, 0, y, 0, 0);
    /// <summary>Rotates clockwise in degrees.</summary>
    public PdfCanvas Rotate(double degrees)
    {
        double r = degrees * Math.PI / 180;
        return Transform(Math.Cos(r), Math.Sin(r), -Math.Sin(r), Math.Cos(r), 0, 0);
    }
    /// <summary>Begins a path at a point.</summary>
    public PdfCanvas MoveTo(double x, double y)
    {
        s.Append(N(x)).Append(' ').Append(N(y)).Append(" m\n");
        return this;
    }
    /// <summary>Adds a line segment.</summary>
    public PdfCanvas LineTo(double x, double y)
    {
        s.Append(N(x)).Append(' ').Append(N(y)).Append(" l\n");
        return this;
    }
    /// <summary>Adds a rectangle to the path.</summary>
    public PdfCanvas Rectangle(double x, double y, double width, double height)
    {
        s.AppendJoin(' ', N(x), N(y), N(width), N(height)).Append(" re\n");
        return this;
    }
    /// <summary>Adds a cubic Bezier segment ending at x3,y3.</summary>
    public PdfCanvas BezierTo(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        s.AppendJoin(' ', N(x1), N(y1), N(x2), N(y2), N(x3), N(y3)).Append(" c\n");
        return this;
    }
    /// <summary>Adds a rounded rectangle; the radius is clamped to half the shortest side.</summary>
    public PdfCanvas RoundedRectangle(double x, double y, double width, double height, double radius)
    {
        if (!double.IsFinite(radius) || radius < 0 || !double.IsFinite(width) || width < 0 || !double.IsFinite(height) || height < 0)
            throw new ArgumentOutOfRangeException(nameof(radius), "Rounded rectangle dimensions and radius must be finite and nonnegative.");
        double r = Math.Min(radius, Math.Min(width, height) / 2), k = r * 0.5522847498307936;
        if (r == 0) return Rectangle(x, y, width, height);
        return MoveTo(x + r, y).LineTo(x + width - r, y)
            .BezierTo(x + width - r + k, y, x + width, y + r - k, x + width, y + r)
            .LineTo(x + width, y + height - r)
            .BezierTo(x + width, y + height - r + k, x + width - r + k, y + height, x + width - r, y + height)
            .LineTo(x + r, y + height)
            .BezierTo(x + r - k, y + height, x, y + height - r + k, x, y + height - r)
            .LineTo(x, y + r).BezierTo(x, y + r - k, x + r - k, y, x + r, y).ClosePath();
    }
    /// <summary>Closes the current subpath.</summary>
    public PdfCanvas ClosePath()
    {
        s.Append("h\n");
        return this;
    }
    /// <summary>Strokes the current path.</summary>
    public PdfCanvas Stroke()
    {
        s.Append("S\n");
        return this;
    }
    /// <summary>Fills the current path with the nonzero winding rule.</summary>
    public PdfCanvas Fill()
    {
        s.Append("f\n");
        return this;
    }
    /// <summary>Fills and strokes the path.</summary>
    public PdfCanvas FillAndStroke()
    {
        s.Append("B\n");
        return this;
    }
    /// <summary>Clips to the current path and ends the path.</summary>
    public PdfCanvas Clip()
    {
        s.Append("W n\n");
        return this;
    }
    /// <summary>Sets the nonnegative line width.</summary>
    public PdfCanvas SetLineWidth(double width)
    {
        if (width < 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        s.Append(N(width)).Append(" w\n");
        return this;
    }
    /// <summary>Sets stroke RGB.</summary>
    public PdfCanvas SetStrokeColor(Color c)
    {
        c.Validate();
        s.AppendJoin(' ', N(c.R), N(c.G), N(c.B)).Append(" RG\n");
        return this;
    }
    /// <summary>Sets fill RGB.</summary>
    public PdfCanvas SetFillColor(Color c)
    {
        c.Validate();
        s.AppendJoin(' ', N(c.R), N(c.G), N(c.B)).Append(" rg\n");
        return this;
    }
    /// <summary>Begins a Span whose ActualText replaces all enclosed glyphs during logical extraction (PDF 1.5+).</summary>
    public PdfCanvas BeginActualText(string text)
    {
        string syntax = new PdfString(text).ToPdfSyntax();
        s.Append("/Span << /ActualText ").Append(syntax).Append(" >> BDC\n");
        markedContent++;
        return this;
    }
    /// <summary>Closes the most recent ActualText span.</summary>
    public PdfCanvas EndActualText()
    {
        if (markedContent == 0) throw new InvalidOperationException("No ActualText span is open.");
        s.Append("EMC\n"); markedContent--; return this;
    }
    /// <summary>Paints encoded text at a logical baseline. Font resources must be registered by the caller.</summary>
    public PdfCanvas DrawText(string fontResource, double size, ReadOnlySpan<byte> encoded, double x, double baseline, bool syntheticBold = false)
    {
        if (size <= 0)
            throw new ArgumentOutOfRangeException(nameof(size));
        s.Append("q\nBT\n").Append(new PdfName(fontResource).ToPdfSyntax()).Append(' ').Append(N(size)).Append(" Tf\n");
        if (syntheticBold)
            s.Append("2 Tr\n").Append(N(size * 0.022)).Append(" w\n");
        s.Append("1 0 0 -1 ").Append(N(x)).Append(' ').Append(N(baseline)).Append(" Tm\n<").Append(Convert.ToHexString(encoded)).Append("> Tj\nET\nQ\n");
        return this;
    }
    /// <summary>Paints an image resource into a logical rectangle.</summary>
    public PdfCanvas DrawImage(string resource, double x, double y, double width, double height)
    {
        SaveState().Transform(width, 0, 0, -height, x, y + height);
        s.Append(new PdfName(resource).ToPdfSyntax()).Append(" Do\n");
        return RestoreState();
    }
    /// <summary>Returns this page's uncompressed commands. All saved states must have been restored.</summary>
    public byte[] ToArray()
    {
        if (markedContent != 0) throw new InvalidOperationException("Unbalanced ActualText spans.");
        if (states != 0)
            throw new InvalidOperationException("Unbalanced graphics SaveState/RestoreState.");
        return Encoding.ASCII.GetBytes(s.ToString() + "Q\n");
    }
}
