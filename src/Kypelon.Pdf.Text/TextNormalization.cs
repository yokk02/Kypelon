namespace Kypelon.Pdf.Text;

// Shared normalization; TextSource additionally retains original UTF-16 ownership.
internal static class TextNormalization
{
    internal static string Normalize(string text) => text.Replace("\t", "    ", StringComparison.Ordinal)
        .Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    internal static bool HasNoBreak(string text) => text.AsSpan().IndexOfAny("\u00A0\u202F\uFEFF\u2007") >= 0;
    // Explicit supported separators, not a claim of Unicode line-breaking conformance.
    internal static bool BreakableWhitespace(string text) => text.Length == 1 && text[0] is
        ' ' or '\u000B' or '\u000C' or '\u0085' or '\u1680' or
        >= '\u2000' and <= '\u2006' or >= '\u2008' and <= '\u200A' or
        '\u2028' or '\u2029' or '\u205F' or '\u3000';
}
