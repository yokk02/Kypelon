namespace Kypelon.Pdf.Core;

/// <summary>A page size in points (72 per inch).</summary>
public readonly record struct PdfPageSize(double Width, double Height)
{
    /// <summary>ISO A4.</summary>
    public static PdfPageSize A4 => new(595.276, 841.89);
    /// <summary>ISO A3.</summary>
    public static PdfPageSize A3 => new(841.89, 1190.551);
    /// <summary>US Letter.</summary>
    public static PdfPageSize Letter => new(612, 792);
    /// <summary>US Legal.</summary>
    public static PdfPageSize Legal => new(612, 1008);
    /// <summary>Returns a landscape orientation.</summary>
    public PdfPageSize Landscape() => new(Math.Max(Width, Height), Math.Min(Width, Height));
    /// <summary>Returns a portrait orientation.</summary>
    public PdfPageSize Portrait() => new(Math.Min(Width, Height), Math.Max(Width, Height));
    /// <summary>Checks the PDF 1.7 default user-space page limits.</summary>
    public void Validate()
    {
        if (!double.IsFinite(Width) || !double.IsFinite(Height) || Width < 3 || Height < 3 || Width > 14400 || Height > 14400)
            throw new ArgumentOutOfRangeException(nameof(Width), "Page dimensions must be between 3 and 14400 points.");
    }
}
/// <summary>Page margins in logical top-left coordinates.</summary>
public readonly record struct PdfMargins(double Left, double Top, double Right, double Bottom)
{
    /// <summary>Creates uniform margins.</summary>
    public PdfMargins(double all) : this(all, all, all, all) { }
}
/// <summary>PDF information dictionary fields.</summary>
public sealed class PdfMetadata
{
    /// <summary>Document title.</summary>
    public string? Title
    {
        get; set;
    }
    /// <summary>Document author.</summary>
    public string? Author
    {
        get; set;
    }
    /// <summary>Document subject.</summary>
    public string? Subject
    {
        get; set;
    }
    /// <summary>Search keywords.</summary>
    public string? Keywords
    {
        get; set;
    }
    /// <summary>Originating application.</summary>
    public string? Creator
    {
        get; set;
    }
    /// <summary>PDF producer.</summary>
    public string Producer { get; set; } = "Kypelon.Pdf 0.2.0-alpha.2";
    /// <summary>Explicit creation time, preserved in deterministic mode.</summary>
    public DateTimeOffset? CreationDate
    {
        get; set;
    }
    /// <summary>Explicit modification time.</summary>
    public DateTimeOffset? ModificationDate
    {
        get; set;
    }
    /// <summary>Builds the PDF information dictionary.</summary>
    public PdfDictionary ToDictionary(bool deterministic)
    {
        var d = new PdfDictionary();
        void Add(string k, string? v)
        {
            if (v is not null)
                d.Add(k, new PdfString(v));
        }
        Add("Title", Title);
        Add("Author", Author);
        Add("Subject", Subject);
        Add("Keywords", Keywords);
        Add("Creator", Creator);
        Add("Producer", Producer);
        var created = CreationDate ?? (deterministic ? (DateTimeOffset?)null : DateTimeOffset.UtcNow);
        if (created.HasValue)
            Add("CreationDate", Date(created.Value));
        if (ModificationDate.HasValue)
            Add("ModDate", Date(ModificationDate.Value));
        return d;
    }
    private static string Date(DateTimeOffset time) => time.UtcDateTime.ToString("'D:'yyyyMMddHHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);
}
