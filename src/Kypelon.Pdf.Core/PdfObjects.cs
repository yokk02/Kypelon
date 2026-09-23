using System.Globalization;
using System.Text;

namespace Kypelon.Pdf.Core;

/// <summary>A direct PDF value. Containers must be acyclic.</summary>
public abstract record PdfObject
{
    internal abstract void Append(StringBuilder output, int depth);
    internal static void CheckDepth(int depth)
    {
        if (depth > 64)
            throw new PdfWriteException("PDF object nesting exceeds 64 levels (possibly cyclic).");
    }
    /// <summary>Serializes a direct value to PDF syntax.</summary>
    public string ToPdfSyntax()
    {
        var s = new StringBuilder();
        Append(s, 0);
        return s.ToString();
    }
}
/// <summary>The PDF null value.</summary>
public sealed record PdfNull : PdfObject
{
    /// <summary>Shared null value.</summary>
    public static PdfNull Value { get; } = new();
    internal override void Append(StringBuilder s, int depth) => s.Append("null");
}
/// <summary>A PDF boolean.</summary>
public sealed record PdfBoolean(bool Value) : PdfObject
{
    internal override void Append(StringBuilder s, int depth) => s.Append(Value ? "true" : "false");
}
/// <summary>A signed PDF integer.</summary>
public sealed record PdfInteger(long Value) : PdfObject
{
    internal override void Append(StringBuilder s, int depth) => s.Append(Value.ToString(CultureInfo.InvariantCulture));
}
/// <summary>A finite PDF real number.</summary>
public sealed record PdfReal(double Value) : PdfObject
{
    /// <summary>Formats a number without exponent notation, independent of culture.</summary>
    public static string Format(double value)
    {
        if (!double.IsFinite(value) || Math.Abs(value) > 1e12)
            throw new ArgumentOutOfRangeException(nameof(value), "PDF numbers must be finite and within 1e12.");
        return value.ToString("0.########", CultureInfo.InvariantCulture);
    }
    internal override void Append(StringBuilder s, int depth) => s.Append(Format(Value));
}
/// <summary>A name, escaped as UTF-8 bytes using PDF name escapes.</summary>
public sealed record PdfName(string Value) : PdfObject
{
    internal override void Append(StringBuilder s, int depth)
    {
        if (Value.Contains('\0'))
            throw new PdfWriteException("PDF names cannot contain a null character.");
        s.Append('/');
        foreach (var b in Encoding.UTF8.GetBytes(Value))
            if (b is >= 33 and <= 126 && !"()<>[]{}/%#".Contains((char)b))
                s.Append((char)b);
            else
                s.Append('#').Append(b.ToString("X2", CultureInfo.InvariantCulture));
    }
}
/// <summary>A text string: ASCII literal or BOM-prefixed UTF-16BE hex.</summary>
public sealed record PdfString(string Value) : PdfObject
{
    internal override void Append(StringBuilder s, int depth)
    {
        if (Value.Any(c => c > 126))
        {
            s.Append("<FEFF").Append(Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(Value))).Append('>');
            return;
        }
        s.Append('(');
        foreach (char c in Value)
            if (c is '(' or ')' or '\\')
                s.Append('\\').Append(c);
            else if (c < 32 || c == 127)
                s.Append('\\').Append(Convert.ToString(c, 8).PadLeft(3, '0'));
            else
                s.Append(c);
        s.Append(')');
    }
}
/// <summary>A binary PDF string.</summary>
public sealed record PdfHexString(ReadOnlyMemory<byte> Value) : PdfObject
{
    internal override void Append(StringBuilder s, int depth) => s.Append('<').Append(Convert.ToHexString(Value.Span)).Append('>');
}
/// <summary>An ordered direct array.</summary>
public sealed record PdfArray(params PdfObject[] Items) : PdfObject
{
    internal override void Append(StringBuilder s, int depth)
    {
        CheckDepth(depth);
        s.Append('[');
        foreach (var item in Items)
        {
            item.Append(s, depth + 1);
            s.Append(' ');
        }
        s.Append(']');
    }
}
/// <summary>A mutable dictionary, serialized in ordinal key order for repeatability.</summary>
public sealed record PdfDictionary : PdfObject
{
    private readonly SortedDictionary<string, PdfObject> entries = new(StringComparer.Ordinal);
    internal IEnumerable<PdfObject> Values => entries.Values;
    /// <summary>Gets or sets a dictionary entry.</summary>
    public PdfObject this[string key] { get => entries[key]; set => entries[key] = value ?? throw new ArgumentNullException(nameof(value)); }
    /// <summary>Adds an entry and returns this dictionary.</summary>
    public PdfDictionary Add(string key, PdfObject value)
    {
        this[key] = value;
        return this;
    }
    /// <summary>Reports whether a key is present.</summary>
    public bool ContainsKey(string key) => entries.ContainsKey(key);
    /// <summary>Creates a shallow copy.</summary>
    public PdfDictionary Copy()
    {
        var copy = new PdfDictionary();
        foreach (var pair in entries)
            copy.Add(pair.Key, pair.Value);
        return copy;
    }
    internal override void Append(StringBuilder s, int depth)
    {
        CheckDepth(depth);
        s.Append("<<");
        foreach (var (key, value) in entries)
        {
            s.Append('\n');
            new PdfName(key).Append(s, depth + 1);
            s.Append(' ');
            value.Append(s, depth + 1);
        }
        s.Append("\n>>");
    }
}
/// <summary>An indirect object identifier.</summary>
public readonly record struct PdfObjectId(int Number, int Generation = 0);
/// <summary>An indirect reference; identifiers are allocated by a writer.</summary>
public sealed record PdfIndirectReference(PdfObjectId Id) : PdfObject
{
    internal override void Append(StringBuilder s, int depth)
    {
        if (Id.Number <= 0 || Id.Generation is < 0 or > 65535)
            throw new PdfWriteException("Invalid indirect object identifier.");
        s.Append(Id.Number.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(Id.Generation.ToString(CultureInfo.InvariantCulture)).Append(" R");
    }
}
/// <summary>An indirect value and its identifier.</summary>
public sealed record PdfIndirectObject(PdfObjectId Id, PdfObject Value);
/// <summary>A stream payload. The writer computes the encoded length.</summary>
public sealed record PdfStream(PdfDictionary Dictionary, ReadOnlyMemory<byte> Data, bool Compress = true);
/// <summary>Reports invalid PDF output or writer state.</summary>
public class PdfWriteException(string message) : Exception(message);
