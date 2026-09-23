namespace Kypelon.Pdf.Core;

/// <summary>A low-level page descriptor, independent of layout.</summary>
public sealed record PdfPage(PdfPageSize Size, PdfIndirectReference Parent, PdfIndirectReference Resources, PdfIndirectReference Contents)
{
    /// <summary>Creates the PDF page dictionary.</summary>
    public PdfDictionary ToDictionary()
    {
        Size.Validate();
        return new PdfDictionary().Add("Type", new PdfName("Page")).Add("Parent", Parent)
            .Add("MediaBox", new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfReal(Size.Width), new PdfReal(Size.Height)))
            .Add("Resources", Resources).Add("Contents", Contents);
    }
}
/// <summary>Resource names for fonts and image XObjects. Mutable and not thread-safe.</summary>
public sealed class PdfResources
{
    private readonly PdfDictionary fonts = new();
    private readonly PdfDictionary images = new();
    /// <summary>Registers a font resource reference.</summary>
    public PdfResources AddFont(string name, PdfIndirectReference reference)
    {
        fonts.Add(name, reference);
        return this;
    }
    /// <summary>Registers an image XObject reference.</summary>
    public PdfResources AddImage(string name, PdfIndirectReference reference)
    {
        images.Add(name, reference);
        return this;
    }
    /// <summary>Creates a snapshot resource dictionary.</summary>
    public PdfDictionary ToDictionary() => new PdfDictionary().Add("Font", fonts.Copy()).Add("XObject", images.Copy());
}
/// <summary>Uncompressed drawing commands for one page.</summary>
public sealed record PdfContentStream(ReadOnlyMemory<byte> Commands)
{
    /// <summary>Creates a stream for the serializer.</summary>
    public PdfStream ToStream(bool compress = true) => new(new(), Commands, compress);
}
