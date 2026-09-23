namespace Kypelon.Pdf.Text;

/// <summary>Immutable final line: exact shaped run, point size, advance width and native line metrics.</summary>
public sealed class TextLine
{
    /// <summary>Validated glyph positions used for both fitting and rendering.</summary>
    public PreparedGlyphRun Run { get; }
    /// <summary>Logical shaped text. Wrap intentionally omits boundary U+0020 spaces; the original source remains on Run.Input.Source.</summary>
    public string Text => Run.Text;
    /// <summary>Point size.</summary>
    public double FontSize { get; }
    /// <summary>Exact shaped horizontal advance in points, excluding ink overhang.</summary>
    public double Width { get; }
    /// <summary>Native ascent scaled to points.</summary>
    public double Ascent => Run.Input.Font.Metrics.Ascender * FontSize / Run.Input.Font.Metrics.UnitsPerEm;
    /// <summary>Native descent scaled to points.</summary>
    public double Descent => Run.Input.Font.Metrics.Descender * FontSize / Run.Input.Font.Metrics.UnitsPerEm;
    internal TextLine(PreparedGlyphRun run, double size)
    {
        if (!double.IsFinite(size) || size <= 0 || size > 1_000_000) throw new PdfFontException("Text size must be finite, positive and at most 1,000,000pt.");
        Run = run; FontSize = size; Width = run.Advance * size / run.Input.Font.Metrics.UnitsPerEm;
        if (!double.IsFinite(Width) || Width > 1_000_000_000) throw new PdfFontException("Prepared line advance exceeds the 1,000,000,000pt limit.");
        double x = 0, y = 0, scale = size / run.Input.Font.Metrics.UnitsPerEm;
        foreach (var glyph in run.Glyphs)
        {
            if (Math.Abs((x + glyph.OffsetX) * scale) > 1_000_000_000 || Math.Abs((y + glyph.OffsetY) * scale) > 1_000_000_000)
                throw new PdfFontException("Prepared glyph position exceeds the 1,000,000,000pt coordinate limit.");
            x += glyph.AdvanceX; y += glyph.AdvanceY;
        }
    }
}

/// <summary>Break discovery followed by complete-candidate shaping. Limited whitespace/hyphen rules, not full UAX #14.</summary>
public static class LineBreaker
{
    /// <summary>Prepares one normalized logical line. Rendering must retain and consume its Run.</summary>
    public static TextLine Prepare(string text, FontFace font, double size, ITextShaper? shaper = null, TextDirection direction = TextDirection.LeftToRight, string? script = null, string? language = null)
    {
        var source = TextSource.Normalize(text);
        return Prepare(new(font, source, 0, source.Text.Length, direction, script, language), size, shaper);
    }
    /// <summary>Prepares an explicit source range without discarding ownership.</summary>
    public static TextLine Prepare(TextRun input, double size, ITextShaper? shaper = null) => new(PreparedGlyphRun.Create(input, shaper), size);
    /// <summary>Measures the widest normalized logical line using complete shaped runs.</summary>
    public static double Measure(string text, FontFace font, double size, ITextShaper? shaper = null, TextDirection direction = TextDirection.LeftToRight, string? script = null, string? language = null)
    {
        var source = TextSource.Normalize(text);
        double maximum = 0;
        int start = 0;
        while (start <= source.Text.Length)
        {
            int end = source.Text.IndexOf('\n', start); if (end < 0) end = source.Text.Length;
            maximum = Math.Max(maximum, Prepare(new(font, source, start, end - start, direction, script, language), size, shaper).Width);
            start = end + 1;
        }
        return maximum;
    }
    private readonly record struct Unit(int Start, int End, bool LegalAfter, bool Protected);
    private static List<Unit> Discover(TextSource source, int start, int length)
    {
        var raw = new List<Unit>();
        foreach (var range in source.Graphemes(start, length))
        {
            string value = source.Text.Substring(range.Start, range.Length);
            raw.Add(new(range.Start, range.Start + range.Length, TextNormalization.BreakableWhitespace(value) || value == "-", TextNormalization.HasNoBreak(value)));
        }
        var units = new List<Unit>(raw.Count);
        for (int i = 0; i < raw.Count;)
        {
            if (raw[i].LegalAfter) { units.Add(raw[i++]); continue; }
            int end = i; bool protect = false;
            while (end < raw.Count && !raw[end].LegalAfter) { protect |= raw[end].Protected; end++; }
            if (protect) units.Add(new(raw[i].Start, raw[end - 1].End, false, true));
            else for (int j = i; j < end; j++) units.Add(raw[j]);
            i = end;
        }
        return units;
    }
    /// <summary>Fits complete shaped candidates and retains each accepted run. Ordinary U+0020 spaces at a wrapped line end, consumed before the next line, or at a paragraph end are intentionally omitted from layout/extraction; ownership is retained only in the full source, not claimed by the visible line. Initial leading spaces are preserved when they fit. No-break tokens never emergency-split; ordinary long words use source-grapheme emergency boundaries. Hints are explicit; no script/bidi itemizer is included.</summary>
    public static IReadOnlyList<TextLine> Wrap(string text, FontFace font, double size, double width, ITextShaper? shaper = null, TextDirection direction = TextDirection.LeftToRight, string? script = null, string? language = null, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(width) || width <= 0 || !double.IsFinite(size) || size <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var source = TextSource.Normalize(text);
        var lines = new List<TextLine>();
        int paragraphStart = 0;
        long shapedUnits = 0, workLimit = Math.Max(1_000_000, 64L * source.Text.Length);
        TextLine Shape(int start, int end)
        {
            cancellationToken.ThrowIfCancellationRequested();
            shapedUnits += end - start;
            if (shapedUnits > workLimit) throw new PdfFontException("Contextual line fitting exceeded its bounded shaping-work budget. Split this unusually complex text element.");
            return Prepare(new(font, source, start, end - start, direction, script, language), size, shaper);
        }
        while (paragraphStart <= source.Text.Length)
        {
            int paragraphEnd = source.Text.IndexOf('\n', paragraphStart); if (paragraphEnd < 0) paragraphEnd = source.Text.Length;
            int trimmedEnd = paragraphEnd;
            while (trimmedEnd > paragraphStart && source.Text[trimmedEnd - 1] == ' ') trimmedEnd--;
            var whole = Shape(paragraphStart, trimmedEnd);
            if (whole.Width <= width + .001)
            {
                lines.Add(whole); paragraphStart = paragraphEnd + 1; continue;
            }
            var units = Discover(source, paragraphStart, paragraphEnd - paragraphStart);
            // A whole-paragraph run supplies a search hint only. It never determines
            // final widths by summing separately shaped graphemes.
            var guide = new double[units.Count + 1];
            var clusterAdvances = new double[whole.Run.Clusters.Count];
            foreach (var g in whole.Run.Glyphs) clusterAdvances[g.Cluster] += g.AdvanceX * size / font.Metrics.UnitsPerEm;
            int unitIndex = 0;
            for (int c = 0; c < whole.Run.Clusters.Count; c++)
            {
                var cluster = whole.Run.Clusters[c];
                while (unitIndex < units.Count - 1 && units[unitIndex].End < cluster.SourceStart + cluster.SourceLength) unitIndex++;
                guide[unitIndex + 1] += Math.Max(0, clusterAdvances[c]);
            }
            for (int i = 1; i < guide.Length; i++) guide[i] += guide[i - 1];
            var legal = Enumerable.Range(1, units.Count).Where(i => units[i - 1].LegalAfter || i == units.Count).ToArray();
            int first = 0;
            while (first < units.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidates = new Dictionary<int, TextLine>();
                TextLine Candidate(int end)
                {
                    if (candidates.TryGetValue(end, out var cached)) return cached;
                    int at = units[end - 1].End;
                    while (at > units[first].Start && source.Text[at - 1] == ' ') at--;
                    var line = first == 0 && at == trimmedEnd ? whole : Shape(units[first].Start, at);
                    candidates.Add(end, line); return line;
                }
                int estimate = Array.BinarySearch(guide, guide[first] + width);
                if (estimate < 0) estimate = ~estimate - 1;
                estimate = Math.Clamp(estimate, first + 1, units.Count);
                int lower = Array.BinarySearch(legal, first + 1); if (lower < 0) lower = ~lower;
                int chosen = Array.BinarySearch(legal, estimate); if (chosen < 0) chosen = ~chosen - 1;
                chosen = Math.Max(lower, chosen);
                int accepted = 0;
                for (int i = chosen; i >= lower; i--)
                    if (Candidate(legal[i]).Width <= width + .001) { accepted = legal[i]; chosen = i; break; }
                if (accepted != 0)
                {
                    // Probe complete following candidates: negative context adjustments
                    // can fit more text than the paragraph hint predicts.
                    for (int i = chosen + 1; i < legal.Length; i++)
                    {
                        if (Candidate(legal[i]).Width > width + .001) break;
                        accepted = legal[i];
                    }
                }
                else
                {
                    int limit = legal[lower];
                    int probe = Math.Clamp(estimate, first + 1, limit);
                    for (int end = probe; end > first; end--)
                        if (Candidate(end).Width <= width + .001) { accepted = end; break; }
                    // No prefix fit: allow contextual contraction to rescue a later
                    // grapheme boundary before declaring an indivisible unit too wide.
                    if (accepted == 0)
                        for (int end = probe + 1; end <= limit; end++)
                            if (Candidate(end).Width <= width + .001) { accepted = end; break; }
                    if (accepted != 0)
                        for (int end = accepted + 1; end <= limit; end++)
                        {
                            if (Candidate(end).Width > width + .001) break;
                            accepted = end;
                        }
                }
                if (accepted == 0)
                    throw new PdfFontException($"A {(units[first].Protected ? "no-break sequence" : "text cluster")} is wider than the available {width:0.##}pt. Increase width or reduce font size.");
                lines.Add(Candidate(accepted));
                first = accepted;
                while (first < units.Count && units[first].End - units[first].Start == 1 && source.Text[units[first].Start] == ' ') first++;
            }
            paragraphStart = paragraphEnd + 1;
        }
        return lines.AsReadOnly();
    }
}
