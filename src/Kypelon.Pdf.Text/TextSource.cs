using System.Buffers;
using System.Globalization;
using System.Text;

namespace Kypelon.Pdf.Text;

/// <summary>A half-open UTF-16 source range.</summary>
public readonly record struct TextRange(int Start, int Length);

/// <summary>Immutable normalized text plus traceability to the original UTF-16 source. Safe to share.</summary>
public sealed class TextSource
{
    private readonly int[]? originalStarts, originalEnds;
    private readonly int[] boundaries;
    /// <summary>Original application text.</summary>
    public string OriginalText { get; }
    /// <summary>Logical text: CRLF/CR become LF; each tab becomes four spaces.</summary>
    public string Text { get; }
    private TextSource(string original)
    {
        OriginalText = original;
        if (original.Length > 1_000_000) throw new PdfFontException("A text source cannot exceed one million UTF-16 code units.");
        var remaining = original.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out _, out int consumed) != OperationStatus.Done)
                throw new PdfFontException("Text contains an unpaired UTF-16 surrogate.");
            remaining = remaining[consumed..];
        }
        Text = TextNormalization.Normalize(original);
        if (Text.Length > 1_000_000) throw new PdfFontException("Normalized text cannot exceed one million UTF-16 code units.");
        if (!ReferenceEquals(Text, original))
        {
            originalStarts = new int[Text.Length]; originalEnds = new int[Text.Length];
            int at = 0;
            for (int i = 0; i < original.Length; i++)
            {
                int length = original[i] == '\r' && i + 1 < original.Length && original[i + 1] == '\n' ? 2 : 1;
                int copies = original[i] == '\t' ? 4 : 1;
                for (int j = 0; j < copies; j++) { originalStarts[at] = i; originalEnds[at++] = i + length; }
                i += length - 1;
            }
        }
        boundaries = StringInfo.ParseCombiningCharacters(Text);
    }
    /// <summary>Validates and normalizes source text once.</summary>
    public static TextSource Normalize(string text) => new(text ?? throw new ArgumentNullException(nameof(text)));
    /// <summary>Maps a normalized range to the smallest original range covering it. Expanded-tab ranges may share original ownership.</summary>
    public TextRange GetOriginalRange(int start, int length)
    {
        ValidateRange(start, length);
        if (originalStarts is null) return new(start, length);
        int first = start == Text.Length ? OriginalText.Length : originalStarts[start];
        return new(first, length == 0 ? 0 : originalEnds![start + length - 1] - first);
    }
    internal void ValidateRange(int start, int length)
    {
        if (start < 0 || length < 0 || start > Text.Length || length > Text.Length - start)
            throw new PdfFontException("Text source range extends outside its normalized input.");
    }
    internal bool IsBoundary(int at) => at == Text.Length || Array.BinarySearch(boundaries, at) >= 0;
    internal IEnumerable<TextRange> Graphemes(int start, int length)
    {
        int end = start + length;
        if (length == 0) yield break;
        int index = Array.BinarySearch(boundaries, start);
        for (; index < boundaries.Length && boundaries[index] < end; index++)
        {
            int next = index + 1 < boundaries.Length ? boundaries[index + 1] : Text.Length;
            yield return new(boundaries[index], next - boundaries[index]);
        }
    }
}

/// <summary>Requested shaping direction. No automatic bidi itemization is performed.</summary>
public enum TextDirection
{
    /// <summary>Horizontal left-to-right source.</summary>
    LeftToRight,
    /// <summary>Horizontal right-to-left source; an adapter must supply visual glyph order.</summary>
    RightToLeft
}

/// <summary>Immutable shaping input. Ranges and clusters use absolute offsets in Source.Text, not glyph indices.</summary>
public sealed class TextRun
{
    /// <summary>Immutable normalized source with original-source mapping.</summary>
    public TextSource Source { get; }
    /// <summary>Start in normalized UTF-16 units.</summary>
    public int SourceStart { get; }
    /// <summary>Length in normalized UTF-16 units.</summary>
    public int SourceLength { get; }
    /// <summary>Font identity. Metrics are in native font units, independent of point size.</summary>
    public FontFace Font { get; }
    /// <summary>Explicit direction hint; no bidi algorithm is implied.</summary>
    public TextDirection Direction { get; }
    /// <summary>Optional four-letter OpenType script hint.</summary>
    public string? Script { get; }
    /// <summary>Optional language hint.</summary>
    public string? Language { get; }
    /// <summary>Logical text of this range.</summary>
    public string Text => Source.Text.Substring(SourceStart, SourceLength);
    /// <summary>Creates a grapheme-aligned single-line shaping request.</summary>
    public TextRun(FontFace font, TextSource source, int sourceStart, int sourceLength, TextDirection direction = TextDirection.LeftToRight, string? script = null, string? language = null)
    {
        Font = font ?? throw new ArgumentNullException(nameof(font));
        Source = source ?? throw new ArgumentNullException(nameof(source));
        source.ValidateRange(sourceStart, sourceLength);
        if (!source.IsBoundary(sourceStart) || !source.IsBoundary(sourceStart + sourceLength))
            throw new PdfFontException("A shaping range must not split a surrogate pair or grapheme sequence.");
        if (source.Text.AsSpan(sourceStart, sourceLength).Contains('\n')) throw new PdfFontException("A shaping run must contain one logical line.");
        if (!Enum.IsDefined(direction) || (script is not null && (script.Length != 4 || script.Any(c => !char.IsAsciiLetter(c)))) || language?.Length > 128)
            throw new PdfFontException("Invalid direction, script or language shaping hint.");
        SourceStart = sourceStart; SourceLength = sourceLength; Direction = direction; Script = script; Language = language;
    }
}
