using System.Text;

namespace Kypelon.Pdf.Text;

/// <summary>Logical source ownership in absolute normalized UTF-16 coordinates. Clusters partition the input in logical order.</summary>
public readonly record struct TextCluster(int SourceStart, int SourceLength);
/// <summary>A visual-order glyph, with a cluster index and signed font-unit advances/offsets (positive Y upwards).</summary>
public readonly record struct PositionedGlyph(ushort GlyphId, int Cluster, double AdvanceX, double AdvanceY = 0, double OffsetX = 0, double OffsetY = 0);
/// <summary>Untrusted adapter output. Preparation validates and snapshots both collections before layout can use them.</summary>
public sealed record GlyphRun(IReadOnlyList<TextCluster> Clusters, IReadOnlyList<PositionedGlyph> Glyphs);
/// <summary>Shapes one complete candidate line. Must be deterministic for identical input and must not mutate input or output while called.</summary>
public interface ITextShaper
{
    /// <summary>Returns visual-order glyphs in native font units and an exhaustive, nonoverlapping logical source-cluster partition. Every nonempty source cluster must own at least one glyph, including default-ignorable source; adapters must attach it to a suitable adjacent cluster or supply a genuinely invisible glyph.</summary>
    GlyphRun Shape(TextRun input);
}
/// <summary>Unicode cmap and native advances only; no GSUB, GPOS, bidi or Thai contextual positioning.</summary>
public sealed class BasicTextShaper : ITextShaper
{
    /// <summary>Stateless and safe to share.</summary>
    public static BasicTextShaper Instance { get; } = new();
    /// <summary>Convenience shaping of normalized single-line text. Use PreparedGlyphRun.Create before encoding custom output.</summary>
    public GlyphRun Shape(FontFace font, string text)
    {
        var source = TextSource.Normalize(text);
        return Shape(new(font, source, 0, source.Text.Length));
    }
    /// <inheritdoc />
    public GlyphRun Shape(TextRun input)
    {
        if (input.Direction != TextDirection.LeftToRight)
            throw new PdfFontException("BasicTextShaper does not implement right-to-left shaping. Supply a capable ITextShaper.");
        var clusters = new List<TextCluster>(input.SourceLength);
        var glyphs = new List<PositionedGlyph>(input.SourceLength);
        foreach (var range in input.Source.Graphemes(input.SourceStart, input.SourceLength))
        {
            int cluster = clusters.Count;
            clusters.Add(new(range.Start, range.Length));
            var remaining = input.Source.Text.AsSpan(range.Start, range.Length);
            while (!remaining.IsEmpty)
            {
                Rune.DecodeFromUtf16(remaining, out var rune, out int consumed); remaining = remaining[consumed..];
                ushort gid = input.Font.GetGlyphId(rune);
                if (gid == 0) throw new PdfFontException($"Font '{input.Font.Name}' has no glyph for U+{rune.Value:X4}. Register a font covering this text.");
                glyphs.Add(new(gid, cluster, input.Font.GetAdvance(gid)));
            }
        }
        return new(clusters, glyphs);
    }
}

/// <summary>Validated, immutable, size-independent shaped text. Owns snapshots; rendering never calls a shaper.</summary>
public sealed class PreparedGlyphRun
{
    private readonly TextRange?[] mappings;
    /// <summary>Immutable input including font, source, direction and language.</summary>
    public TextRun Input { get; }
    /// <summary>Logical normalized text retained for extraction.</summary>
    public string Text { get; }
    /// <summary>Logical source clusters.</summary>
    public IReadOnlyList<TextCluster> Clusters { get; }
    /// <summary>Ordered positioned glyph snapshot.</summary>
    public IReadOnlyList<PositionedGlyph> Glyphs { get; }
    /// <summary>Horizontal advance in native units; an advance width, not an ink bounding box.</summary>
    public double Advance { get; }
    /// <summary>True when native PDF widths reproduce all glyph positions exactly.</summary>
    public bool UsesNativeAdvances { get; }
    /// <summary>True when marked-content replacement text is needed for unambiguous logical extraction.</summary>
    public bool RequiresActualText { get; }
    private PreparedGlyphRun(TextRun input, GlyphRun result)
    {
        Input = input; Text = input.Text;
        if (result.Clusters is null || result.Glyphs is null || result.Clusters.Count > input.SourceLength || result.Glyphs.Count > Math.Min(1_000_000, 16L * input.SourceLength + 16))
            throw new PdfFontException("Shaper returned null or excessive cluster/glyph collections.");
        var clusters = result.Clusters.ToArray(); var glyphs = result.Glyphs.ToArray();
        Clusters = Array.AsReadOnly(clusters); Glyphs = Array.AsReadOnly(glyphs);
        int next = input.SourceStart;
        for (int i = 0; i < clusters.Length; i++)
        {
            var c = clusters[i];
            if (c.SourceStart != next || c.SourceLength <= 0 || c.SourceLength > input.SourceStart + input.SourceLength - next || !input.Source.IsBoundary(next + c.SourceLength))
                throw new PdfFontException($"Shaper cluster {i} must partition the input at grapheme boundaries without gaps, overlap or escaping source ranges.");
            next += c.SourceLength;
        }
        if (next != input.SourceStart + input.SourceLength) throw new PdfFontException("Shaper clusters do not cover the complete input source range.");
        var counts = new int[clusters.Length]; var first = Enumerable.Repeat(-1, clusters.Length).ToArray();
        double x = 0, y = 0;
        bool native = true;
        static bool Valid(double n) => double.IsFinite(n) && Math.Abs(n) <= 10_000_000;
        for (int i = 0; i < glyphs.Length; i++)
        {
            var g = glyphs[i];
            if (g.Cluster < 0 || g.Cluster >= clusters.Length) throw new PdfFontException($"Shaper glyph {i} refers to an invalid cluster index.");
            if (input.Font.IsStandard ? g.GlyphId is < 32 or > 126 : g.GlyphId >= input.Font.GlyphCount)
                throw new PdfFontException($"Shaper glyph {i} has invalid GlyphId {g.GlyphId} for font '{input.Font.Name}'.");
            if (!Valid(g.AdvanceX) || !Valid(g.AdvanceY) || !Valid(g.OffsetX) || !Valid(g.OffsetY))
                throw new PdfFontException($"Shaper glyph {i} has a nonfinite or excessive advance/offset (limit 10,000,000 font units).");
            x += g.AdvanceX; y += g.AdvanceY;
            if (Math.Abs(x) > 1_000_000_000 || Math.Abs(y) > 1_000_000_000) throw new PdfFontException("Shaper cumulative advance exceeds the 1,000,000,000 font-unit limit.");
            native &= g.AdvanceX == input.Font.GetAdvance(g.GlyphId) && g.AdvanceY == 0 && g.OffsetX == 0 && g.OffsetY == 0;
            counts[g.Cluster]++;
            if (first[g.Cluster] == -1) first[g.Cluster] = i;
        }
        if (counts.Any(c => c == 0) || x < 0) throw new PdfFontException("Every source cluster must own a glyph and the final horizontal advance must be nonnegative.");
        Advance = x; UsesNativeAdvances = native;
        mappings = new TextRange?[glyphs.Length];
        for (int c = 0; c < clusters.Length; c++)
        {
            var cluster = clusters[c];
            int at = first[c];
            var span = input.Source.Text.AsSpan(cluster.SourceStart, cluster.SourceLength);
            // A scalar-by-scalar cmap sequence has a lossless ToUnicode decomposition,
            // even when several scalars belong to one combining cluster.
            int scalarCount = 0; bool identity = true;
            var remaining = span;
            while (!remaining.IsEmpty)
            {
                Rune.DecodeFromUtf16(remaining, out var rune, out int consumed); remaining = remaining[consumed..];
                int g = at + scalarCount++;
                identity &= g < glyphs.Length && glyphs[g].Cluster == c && glyphs[g].GlyphId == input.Font.GetGlyphId(rune);
            }
            if (identity && counts[c] == scalarCount)
            {
                int offset = cluster.SourceStart;
                remaining = span;
                while (!remaining.IsEmpty)
                {
                    Rune.DecodeFromUtf16(remaining, out var rune, out int consumed); remaining = remaining[consumed..];
                    mappings[at++] = new(offset, rune.Utf16SequenceLength); offset += rune.Utf16SequenceLength;
                }
            }
            else mappings[at] = new(cluster.SourceStart, cluster.SourceLength);
        }
        next = input.SourceStart;
        bool logical = true;
        foreach (var mapping in mappings)
        {
            if (mapping is not { } range || range.Start != next) { logical = false; continue; }
            next += range.Length;
        }
        // Standard fonts have a fixed built-in encoding; substitutions also need replacement text.
        bool standardIdentity = !input.Font.IsStandard || (glyphs.Length == Text.Length && glyphs.Select((g, i) => g.GlyphId == Text[i]).All(v => v));
        RequiresActualText = !native || !logical || next != input.SourceStart + input.SourceLength || !standardIdentity;
    }
    /// <summary>Calls the adapter once, validates its complete topology/numerics and takes immutable snapshots.</summary>
    public static PreparedGlyphRun Create(TextRun input, ITextShaper? shaper = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        var result = (shaper ?? BasicTextShaper.Instance).Shape(input) ?? throw new PdfFontException("Shaper returned a null glyph run.");
        return new(input, result);
    }
    /// <summary>Returns a CID's logical mapping, or null for extra glyphs whose extraction is owned by ActualText.</summary>
    public string? GetUnicodeMapping(int glyphIndex) => mappings[glyphIndex] is { } range ? Input.Source.Text.Substring(range.Start, range.Length) : null;
}
