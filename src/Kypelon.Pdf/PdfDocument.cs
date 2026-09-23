using System.Globalization;
using Kypelon.Pdf.Core;
using Kypelon.Pdf.Graphics;
using Kypelon.Pdf.Layout;
using Kypelon.Pdf.Text;

namespace Kypelon.Pdf;

/// <summary>Generation options. Do not mutate during a write.</summary>
public sealed record PdfOptions
{
    /// <summary>Fresh default options.</summary>
    public static PdfOptions Default => new();
    /// <summary>Flate-compress content, embedded fonts and PNG pixels.</summary>
    public bool CompressStreams { get; set; } = true;
    /// <summary>Omit implicit dates. Explicit metadata timestamps remain unchanged.</summary>
    public bool Deterministic
    {
        get; set;
    }
    /// <summary>Draw page, margin, padding and cell bounds.</summary>
    public bool DebugLayout
    {
        get; set;
    }
    /// <summary>Optional pagination diagnostics; null incurs no callback work.</summary>
    public Action<LayoutDiagnostic>? Diagnostic
    {
        get; set;
    }
}
/// <summary>A composed document. Reusable sequentially; builders, metadata and options must not be mutated while writing.</summary>
public sealed class PdfDocument
{
    private readonly IReadOnlyList<PageTemplate> templates;
    private readonly FontFace font;
    private readonly FontFace? bold;
    private readonly ITextShaper shaper;
    /// <summary>Generation options.</summary>
    public PdfOptions Options { get; set; } = new();
    /// <summary>Basic PDF document information.</summary>
    public PdfMetadata Metadata { get; } = new();
    internal PdfDocument(IReadOnlyList<PageTemplate> templates, FontFace font, FontFace? bold, ITextShaper shaper)
    {
        this.templates = templates;
        this.font = font;
        this.bold = bold;
        this.shaper = shaper;
    }
    /// <summary>Plans all pages without creating PDF bytes. Useful for diagnostics.</summary>
    public IReadOnlyList<PagePlan> Plan(CancellationToken cancellationToken = default) => Prepare(cancellationToken).Pages;

    /// <summary>Creates an immutable validated snapshot. Subsequent writes do not measure or shape again.</summary>
    public PreparedPdfDocument Prepare(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = Options with { };
        var metadata = new PdfMetadata
        {
            Title = Metadata.Title, Author = Metadata.Author, Subject = Metadata.Subject,
            Keywords = Metadata.Keywords, Creator = Metadata.Creator, Producer = Metadata.Producer,
            CreationDate = Metadata.CreationDate, ModificationDate = Metadata.ModificationDate
        };
        return new PreparedPdfDocument(BuildPlans(options, cancellationToken), options, metadata, cancellationToken);
    }

    private IReadOnlyList<PagePlan> BuildPlans(PdfOptions options, CancellationToken cancellationToken)
    {
        var context = new LayoutContext { Font = font, BoldFont = bold, Shaper = shaper, DebugLayout = options.DebugLayout, CancellationToken = cancellationToken };
        var plans = Paginator.Plan(templates, context, options.Diagnostic);
        var resolved = new List<PagePlan>(plans.Count);
        for (int i = 0; i < plans.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var commands = new List<RenderCommand>(plans[i].Commands.Count);
            foreach (var c in plans[i].Commands)
            {
                if (c is TextCommand { PageNumber: true } t)
                {
                    string value = t.Text.Replace("{page}", (i + 1).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal).Replace("{pages}", plans.Count.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
                    var prepared = LineBreaker.Prepare(value, t.Font, t.Size, shaper, t.Direction, t.Script, t.Language);
                    double width = prepared.Width;
                    if (width > t.ReservedWidth + .001)
                        throw new PdfLayoutException("Page-number template exceeds its reserved width.");
                    double offset = t.Alignment switch
                    {
                        HorizontalAlignment.Center => (t.ReservedWidth - width) / 2,
                        HorizontalAlignment.Right => t.ReservedWidth - width,
                        _ => 0
                    };
                    commands.Add(t with
                    {
                        Text = value,
                        Prepared = prepared,
                        X = t.X + offset,
                        PageNumber = false
                    });
                }
                else if (c is TextCommand text)
                {
                    var prepared = text.Prepared ?? LineBreaker.Prepare(text.Text, text.Font, text.Size, shaper, text.Direction, text.Script, text.Language);
                    commands.Add(text with { Text = text.Prepared is null ? prepared.Text : text.Text, Prepared = prepared });
                }
                else commands.Add(c);
            }
            for (int commandIndex = 0; commandIndex < commands.Count; commandIndex++)
                RenderCommandValidation.Validate(commands[commandIndex], plans[i].Size, i + 1, commandIndex + 1);
            resolved.Add(plans[i] with
            {
                Commands = commands.AsReadOnly()
            });
        }
        return resolved.AsReadOnly();
    }
    /// <summary>Prepares once and writes to a writable, initially empty stream.</summary>
    public void Write(Stream stream, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        PreparedPdfDocument prepared;
        try { prepared = Prepare(); }
        catch { if (!leaveOpen) { try { stream.Dispose(); } catch { } } throw; }
        prepared.Write(stream, leaveOpen);
    }
    /// <summary>Prepares once then uses asynchronous destination I/O. CPU preparation is synchronous and cancellable.</summary>
    public async Task WriteAsync(Stream stream, CancellationToken cancellationToken = default, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();
        PreparedPdfDocument prepared;
        try { prepared = Prepare(cancellationToken); }
        catch { if (!leaveOpen) { try { await stream.DisposeAsync().ConfigureAwait(false); } catch { } } throw; }
        await prepared.WriteAsync(stream, cancellationToken, leaveOpen).ConfigureAwait(false);
    }
    /// <summary>Completes deterministic preparation before creating/replacing the file. Later I/O failure may leave partial output.</summary>
    public void Save(string path) => Prepare().Save(path);
    /// <summary>Checks cancellation and completes preparation before opening the file. This is not an atomic-save API.</summary>
    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Prepare(cancellationToken).SaveAsync(path, cancellationToken).ConfigureAwait(false);
    }
    /// <summary>Convenience API buffering the whole PDF. Prefer streaming for large output.</summary>
    public byte[] ToArray() => Prepare().ToArray();
}
