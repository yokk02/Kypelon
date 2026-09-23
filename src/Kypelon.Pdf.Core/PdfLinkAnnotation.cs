namespace Kypelon.Pdf.Core;

/// <summary>A borderless HTTP(S) URI link, defined in logical top-left page coordinates.</summary>
public sealed record PdfLinkAnnotation(string Url, double X, double Y, double Width, double Height)
{
    /// <summary>Creates a PDF Link annotation and converts its rectangle to bottom-left coordinates.</summary>
    public PdfDictionary ToDictionary(double pageHeight)
    {
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || Url.Any(char.IsControl))
            throw new PdfWriteException("PDF links require an absolute HTTP or HTTPS URL without control characters.");
        if (!double.IsFinite(X) || !double.IsFinite(Y) || !double.IsFinite(Width) || !double.IsFinite(Height) || !double.IsFinite(pageHeight) || !double.IsFinite(X + Width) ||
            X < 0 || Y < 0 || Width <= 0 || Height <= 0 || Y + Height > pageHeight + .001)
            throw new PdfWriteException("PDF link rectangle is invalid or extends beyond the page height.");
        var escapedUrl = new UriBuilder(uri) { Host = uri.IdnHost }.Uri.AbsoluteUri;
        return new PdfDictionary().Add("Type", new PdfName("Annot")).Add("Subtype", new PdfName("Link"))
            .Add("Rect", new PdfArray(new PdfReal(X), new PdfReal(pageHeight - Y - Height), new PdfReal(X + Width), new PdfReal(pageHeight - Y)))
            .Add("Border", new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfInteger(0)))
            .Add("A", new PdfDictionary().Add("S", new PdfName("URI")).Add("URI", new PdfString(escapedUrl)));
    }
}
