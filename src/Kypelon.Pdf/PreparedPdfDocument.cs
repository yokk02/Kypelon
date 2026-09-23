using Kypelon.Pdf.Core;
using Kypelon.Pdf.Graphics;
using Kypelon.Pdf.Layout;
using Kypelon.Pdf.Text;
using System.Globalization;

namespace Kypelon.Pdf;

/// <summary>Immutable prepared document. Reusable writes create independent writer/font/image state; no shaper or mutable model is retained.</summary>
public sealed class PreparedPdfDocument
{
    private readonly PdfOptions options;
    private readonly PdfDictionary information;
    /// <summary>Immutable page plans with resolved numbers and retained validated glyph runs.</summary>
    public IReadOnlyList<PagePlan> Pages { get; }
    /// <summary>Number of final pages.</summary>
    public int PageCount => Pages.Count;

    internal PreparedPdfDocument(IReadOnlyList<PagePlan> pages, PdfOptions options, PdfMetadata metadata, CancellationToken token)
    {
        Pages = pages;
        this.options = options with { Diagnostic = null };
        information = metadata.ToDictionary(options.Deterministic);
        _ = information.ToPdfSyntax();
        // Same CID identity and limit as FontResource.Encode; no writer or encoded page bytes are retained.
        var fonts = new Dictionary<FontFace, HashSet<(ushort Glyph, string? Text)>>();
        var seen = new HashSet<PreparedGlyphRun>();
        foreach (var page in pages)
            foreach (var text in page.Commands.OfType<TextCommand>())
            {
                token.ThrowIfCancellationRequested();
                var run = text.Prepared!.Run;
                if (text.Font.IsStandard || !seen.Add(run)) continue;
                if (!fonts.TryGetValue(text.Font, out var codes)) fonts.Add(text.Font, codes = []);
                for (int i = 0; i < run.Glyphs.Count; i++)
                {
                    codes.Add((run.Glyphs[i].GlyphId, run.GetUnicodeMapping(i)));
                    if (codes.Count > FontResource.MaximumCidCount) throw new PdfFontException("Font CID limit exceeded during preparation.");
                }
            }
    }
    /// <summary>Writes this snapshot to an initially empty stream; leaveOpen controls stream ownership. No layout/shaping is repeated.</summary>
    public void Write(Stream stream, bool leaveOpen = true)
    {
        using var writer = new PdfFileWriter(stream, leaveOpen);
        var session = new Session(this, writer, Pages, options);
        foreach (var item in session.PageAndImageObjects(CancellationToken.None)) item.Write(writer);
        foreach (var font in session.Fonts.Values) font.Resource.Write();
        foreach (var item in session.FinalObjects()) item.Write(writer);
        writer.Finish(session.Catalog, session.Info);
    }
    /// <summary>Writes through asynchronous destination I/O. Cancellation/I/O errors can leave partial output.</summary>
    public async Task WriteAsync(Stream stream, CancellationToken cancellationToken = default, bool leaveOpen = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var writer = new PdfFileWriter(stream, leaveOpen);
        var session = new Session(this, writer, Pages, options);
        foreach (var item in session.PageAndImageObjects(cancellationToken)) await item.WriteAsync(writer, cancellationToken).ConfigureAwait(false);
        foreach (var font in session.Fonts.Values) await font.Resource.WriteAsync(cancellationToken).ConfigureAwait(false);
        foreach (var item in session.FinalObjects()) await item.WriteAsync(writer, cancellationToken).ConfigureAwait(false);
        await writer.FinishAsync(session.Catalog, session.Info, cancellationToken).ConfigureAwait(false);
    }
    /// <summary>Creates/replaces a file from this validated snapshot. I/O failure may leave partial output.</summary>
    public void Save(string path)
    {
        using var stream = File.Create(path);
        Write(stream);
    }
    /// <summary>Checks cancellation before creating/replacing the file; asynchronous output is not an atomic-save transaction.</summary>
    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await WriteAsync(stream, cancellationToken).ConfigureAwait(false);
    }
    /// <summary>Convenience API buffering the entire PDF.</summary>
    public byte[] ToArray()
    {
        using var stream = new MemoryStream();
        Write(stream);
        return stream.ToArray();
    }
    private sealed record Item(PdfIndirectReference Reference, PdfObject? Object = null, PdfStream? Stream = null)
    {
        internal void Write(PdfFileWriter writer)
        {
            if (Object is not null)
                writer.Write(Reference, Object);
            else
                writer.Write(Reference, Stream!);
        }
        internal ValueTask WriteAsync(PdfFileWriter writer, CancellationToken token) => Object is not null ? writer.WriteAsync(Reference, Object, token) : writer.WriteAsync(Reference, Stream!, token);
    }
    private sealed class Session
    {
        private readonly PreparedPdfDocument document;
        private readonly PdfFileWriter writer;
        private readonly IReadOnlyList<PagePlan> plans;
        private readonly PdfOptions options;
        private readonly PdfIndirectReference pages, resources;
        private readonly List<PdfIndirectReference> pageRefs = [];
        internal PdfIndirectReference Catalog
        {
            get;
        }
        internal PdfIndirectReference Info
        {
            get;
        }
        internal Dictionary<FontFace, (string Name, FontResource Resource)> Fonts { get; } = [];
        private readonly Dictionary<PdfImage, (string Name, PdfIndirectReference Reference)> images = [];
        private readonly Dictionary<PreparedGlyphRun, byte[]> encodedRuns = [];
        internal Session(PreparedPdfDocument document, PdfFileWriter writer, IReadOnlyList<PagePlan> plans, PdfOptions options)
        {
            this.document = document;
            this.writer = writer;
            this.plans = plans;
            this.options = options;
            Catalog = writer.Reserve();
            pages = writer.Reserve();
            Info = writer.Reserve();
            resources = writer.Reserve();
            foreach (var p in plans)
                foreach (var c in p.Commands)
                    if (c is TextCommand t)
                    {
                        if (!Fonts.TryGetValue(t.Font, out var entry))
                        {
                            entry = ("F" + (Fonts.Count + 1).ToString(CultureInfo.InvariantCulture), new FontResource(writer, t.Font, options.CompressStreams));
                            Fonts.Add(t.Font, entry);
                        }
                        var run = t.Prepared?.Run ?? throw new PdfLayoutException("Finalized text command has no prepared glyph run.");
                        // Resolve every CID before any destination I/O, including the font's CID limit.
                        if (!encodedRuns.ContainsKey(run)) encodedRuns.Add(run, entry.Resource.Encode(run));
                    }
                    else if (c is ImageCommand i && !images.ContainsKey(i.Image))
                        images.Add(i.Image, ("Im" + (images.Count + 1).ToString(CultureInfo.InvariantCulture), writer.Reserve()));
        }
        private byte[] Render(PagePlan plan, CancellationToken token)
        {
            var canvas = new PdfCanvas(plan.Size.Height);
            foreach (var command in plan.Commands)
            {
                token.ThrowIfCancellationRequested();
                switch (command)
                {
                    case RectangleCommand r:
                        if (r.DebugOnly && !options.DebugLayout)
                            break;
                        canvas.SaveState().SetLineWidth(r.LineWidth);
                        if (r.Fill.HasValue)
                            canvas.SetFillColor(r.Fill.Value);
                        if (r.Stroke.HasValue)
                            canvas.SetStrokeColor(r.Stroke.Value);
                        if (r.CornerRadius != 0)
                            canvas.RoundedRectangle(r.Bounds.X, r.Bounds.Y, r.Bounds.Width, r.Bounds.Height, r.CornerRadius);
                        else
                            canvas.Rectangle(r.Bounds.X, r.Bounds.Y, r.Bounds.Width, r.Bounds.Height);
                        if (r.Fill.HasValue && r.Stroke.HasValue)
                            canvas.FillAndStroke();
                        else if (r.Fill.HasValue)
                            canvas.Fill();
                        else
                            canvas.Stroke();
                        canvas.RestoreState();
                        break;
                    case LinkCommand:
                        break; // Link hit areas are serialized as page annotations.
                    case ImageCommand i:
                        canvas.DrawImage(images[i.Image].Name, i.Bounds.X, i.Bounds.Y, i.Bounds.Width, i.Bounds.Height);
                        break;
                    case TextCommand t:
                        var resource = Fonts[t.Font];
                        var run = t.Prepared!.Run;
                        var encoded = encodedRuns[run];
                        canvas.SetFillColor(t.Color).SetStrokeColor(t.Color);
                        if (run.RequiresActualText) canvas.BeginActualText(run.Text);
                        if (run.UsesNativeAdvances)
                            canvas.DrawText(resource.Name, t.Size, encoded, t.X, t.Baseline, t.SyntheticBold);
                        else
                        {
                            double x = t.X, y = t.Baseline, scale = t.Size / t.Font.Metrics.UnitsPerEm;
                            int stride = t.Font.IsStandard ? 1 : 2;
                            for (int n = 0; n < run.Glyphs.Count; n++)
                            {
                                var g = run.Glyphs[n];
                                canvas.DrawText(resource.Name, t.Size, encoded.AsSpan(n * stride, stride), x + g.OffsetX * scale, y - g.OffsetY * scale, t.SyntheticBold);
                                x += g.AdvanceX * scale; y -= g.AdvanceY * scale;
                            }
                        }
                        if (run.RequiresActualText) canvas.EndActualText();
                        break;
                    default:
                        throw new PdfLayoutException("Unknown render command.");
                }
            }
            return canvas.ToArray();
        }
        internal IEnumerable<Item> PageAndImageObjects(CancellationToken token)
        {
            foreach (var plan in plans)
            {
                token.ThrowIfCancellationRequested();
                var page = writer.Reserve();
                var content = writer.Reserve();
                pageRefs.Add(page);
                yield return new(content, Stream: new PdfContentStream(Render(plan, token)).ToStream(options.CompressStreams));
                var pageDictionary = new PdfPage(plan.Size, pages, resources, content).ToDictionary();
                var links = plan.Commands.OfType<LinkCommand>().ToArray();
                if (links.Length > 0)
                {
                    var annotations = new List<PdfObject>();
                    foreach (var link in links)
                    {
                        if (link.Bounds.X + link.Bounds.Width > plan.Size.Width + .001)
                            throw new PdfWriteException("PDF link rectangle exceeds the page width.");
                        var reference = writer.Reserve();
                        annotations.Add(reference);
                        yield return new(reference, new PdfLinkAnnotation(link.Url, link.Bounds.X, link.Bounds.Y, link.Bounds.Width, link.Bounds.Height).ToDictionary(plan.Size.Height));
                    }
                    pageDictionary.Add("Annots", new PdfArray(annotations.ToArray()));
                }
                yield return new(page, pageDictionary);
            }
            foreach (var (image, entry) in images)
                foreach (var item in image.CreateStreams(writer, entry.Reference, options.CompressStreams))
                    yield return new(item.Reference, Stream: item.Stream);
        }
        internal IEnumerable<Item> FinalObjects()
        {
            var resourceDictionary = new PdfResources();
            foreach (var entry in Fonts.Values)
                resourceDictionary.AddFont(entry.Name, entry.Resource.Reference);
            foreach (var entry in images.Values)
                resourceDictionary.AddImage(entry.Name, entry.Reference);
            yield return new(resources, resourceDictionary.ToDictionary());
            yield return new(pages, new PdfDictionary().Add("Type", new PdfName("Pages")).Add("Kids", new PdfArray(pageRefs.Cast<PdfObject>().ToArray())).Add("Count", new PdfInteger(pageRefs.Count)));
            yield return new(Catalog, new PdfDictionary().Add("Type", new PdfName("Catalog")).Add("Pages", pages));
            yield return new(Info, document.information);
        }
    }
}
