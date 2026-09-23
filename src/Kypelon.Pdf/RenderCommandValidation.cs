using Kypelon.Pdf.Core;
using Kypelon.Pdf.Graphics;
using Kypelon.Pdf.Layout;

namespace Kypelon.Pdf;

/// <summary>Single final-command gate; runs before any destination is opened or written.</summary>
internal static class RenderCommandValidation
{
    internal static void Validate(RenderCommand command, PdfPageSize page, int pageNumber, int commandNumber)
    {
        string context = $"Page {pageNumber}, command {commandNumber} ({command.GetType().Name})";
        void Require(bool condition, string message)
        {
            if (!condition) throw new PdfLayoutException(context + ": " + message);
        }
        static bool Coordinate(double value) => double.IsFinite(value) && Math.Abs(value) <= 1_000_000_000_000;
        void ColorValue(Color value) => Require(double.IsFinite(value.R) && double.IsFinite(value.G) && double.IsFinite(value.B) &&
            value.R is >= 0 and <= 1 && value.G is >= 0 and <= 1 && value.B is >= 0 and <= 1, "Color components must be finite and in [0,1].");
        void Bounds(Rect bounds, bool positive)
        {
            Require(Coordinate(bounds.X) && Coordinate(bounds.Y) && Coordinate(bounds.Width) && Coordinate(bounds.Height) &&
                Coordinate(bounds.X + bounds.Width) && Coordinate(bounds.Y + bounds.Height), "Bounds must have finite coordinates within ±1,000,000,000,000pt.");
            Require(positive ? bounds.Width > 0 && bounds.Height > 0 : bounds.Width >= 0 && bounds.Height >= 0,
                positive ? "Width and height must be positive." : "Width and height must be nonnegative.");
        }
        switch (command)
        {
            case TextCommand t:
                Require(Coordinate(t.X) && Coordinate(t.Baseline), "Text position must be finite and within ±1,000,000,000,000pt.");
                Require(double.IsFinite(t.Size) && t.Size > 0 && t.Size <= 1_000_000, "Font size must be positive, finite and at most 1,000,000pt.");
                Require(Coordinate(t.ReservedWidth) && t.ReservedWidth >= 0, "ReservedWidth must be finite and nonnegative.");
                Require(Enum.IsDefined(t.Alignment), "Unknown horizontal alignment.");
                ColorValue(t.Color);
                var prepared = t.Prepared;
                Require(!t.PageNumber && prepared is not null, "Final text must contain a prepared run and resolved page numbers.");
                if (prepared is null) return;
                Require(prepared.Text == t.Text && ReferenceEquals(prepared.Run.Input.Font, t.Font) && prepared.FontSize == t.Size &&
                    prepared.Run.Input.Direction == t.Direction && prepared.Run.Input.Script == t.Script && prepared.Run.Input.Language == t.Language,
                    "Text command differs from its immutable prepared state.");
                Require(t.ReservedWidth == 0 || prepared.Width <= t.ReservedWidth + .001, "Prepared advance exceeds the reserved width.");
                // Relative offsets were validated during shaping; absolute rendered positions include the line origin.
                double x = t.X, y = t.Baseline, scale = t.Size / t.Font.Metrics.UnitsPerEm;
                foreach (var glyph in prepared.Run.Glyphs)
                {
                    Require(Coordinate(x + glyph.OffsetX * scale) && Coordinate(y - glyph.OffsetY * scale),
                        "Positioned glyph exceeds the absolute coordinate limit.");
                    x += glyph.AdvanceX * scale; y -= glyph.AdvanceY * scale;
                    Require(Coordinate(x) && Coordinate(y), "Glyph pen position exceeds the absolute coordinate limit.");
                }
                break;
            case RectangleCommand rectangle:
                Bounds(rectangle.Bounds, false);
                Require(Coordinate(rectangle.LineWidth) && rectangle.LineWidth >= 0, "Line width must be finite and nonnegative.");
                Require(Coordinate(rectangle.CornerRadius) && rectangle.CornerRadius >= 0, "Corner radius must be finite and nonnegative.");
                if (rectangle.Fill is { } fill) ColorValue(fill);
                if (rectangle.Stroke is { } stroke) ColorValue(stroke);
                break;
            case ImageCommand image:
                Require(image.Image is not null, "Image resource must not be null.");
                Bounds(image.Bounds, true);
                break;
            case LinkCommand link:
                Require(link.Bounds.X + link.Bounds.Width <= page.Width + .001, "Link annotation exceeds the page width.");
                _ = new PdfLinkAnnotation(link.Url, link.Bounds.X, link.Bounds.Y, link.Bounds.Width, link.Bounds.Height).ToDictionary(page.Height);
                break;
            default:
                throw new PdfLayoutException(context + ": unknown render command.");
        }
    }
}
