using System.Buffers.Binary;
using System.Text;

namespace Kypelon.Pdf.Text;

/// <summary>Reports unsupported, malformed, or unsuitable fonts.</summary>
public class PdfFontException(string message) : Exception(message);
/// <summary>Font metrics in native font units.</summary>
public readonly record struct FontMetrics(int UnitsPerEm, int Ascender, int Descender, int LineGap, int XMin, int YMin, int XMax, int YMax);
/// <summary>Immutable font face. Safe to share between documents and threads.</summary>
public sealed class FontFace
{
    private readonly byte[] data;
    private readonly ushort[] widths;
    private readonly ushort[]? bmp;
    private readonly (uint Start, uint End, uint Glyph)[]? groups;
    /// <summary>PostScript-compatible face name.</summary>
    public string Name
    {
        get;
    }
    /// <summary>Native font metrics.</summary>
    public FontMetrics Metrics
    {
        get;
    }
    /// <summary>True when this face uses a PDF Standard 14 font.</summary>
    public bool IsStandard => data.Length == 0;
    /// <summary>Glyph count.</summary>
    public int GlyphCount => widths.Length;
    /// <summary>PDF Courier, restricted to printable ASCII for reliable encoding.</summary>
    public static FontFace Courier { get; } = new("Courier");
    /// <summary>PDF Courier Bold, restricted to printable ASCII.</summary>
    public static FontFace CourierBold { get; } = new("Courier-Bold");
    private FontFace(string name)
    {
        Name = name;
        data = [];
        widths = [];
        Metrics = new(1000, 800, -200, 0, -23, -250, 715, 805);
    }
    private FontFace(byte[] bytes)
    {
        data = bytes;
        if (U32(bytes, 0) != 0x00010000)
            throw new PdfFontException("Only standalone TrueType outlines are supported; CFF, collections and variable fonts are not supported.");
        int count = U16(bytes, 4);
        if (count is < 1 or > 256)
            throw new PdfFontException("Invalid sfnt table count.");
        Slice(bytes, 12, count * 16);
        var tables = new Dictionary<string, (int Offset, int Length)>(count, StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            int p = 12 + i * 16;
            string tag = Encoding.ASCII.GetString(bytes, p, 4);
            uint offset = U32(bytes, p + 8), length = U32(bytes, p + 12);
            if (offset > bytes.Length || length > bytes.Length - offset)
                throw new PdfFontException($"Font table '{tag}' extends beyond the file.");
            if (!tables.TryAdd(tag, ((int)offset, (int)length)))
                throw new PdfFontException($"Duplicate font table '{tag}'.");
        }
        // Nonempty tables may neither alias the directory nor each other. Empty glyf
        // and unknown optional tables are legal and consume no byte range.
        int directoryEnd = 12 + count * 16;
        foreach (var table in tables.Values.Where(t => t.Length != 0).OrderBy(t => t.Offset))
        {
            if (table.Offset < 12 + count * 16)
                throw new PdfFontException("Font table overlaps its directory.");
            if (table.Offset < directoryEnd)
                throw new PdfFontException("Font table ranges overlap.");
            directoryEnd = checked(table.Offset + table.Length);
        }
        ReadOnlySpan<byte> Table(string tag, int minimum)
        {
            if (!tables.TryGetValue(tag, out var t) || t.Length < minimum)
                throw new PdfFontException($"Required table '{tag}' is missing or truncated.");
            return Slice(bytes, t.Offset, t.Length);
        }
        if (tables.ContainsKey("fvar"))
            throw new PdfFontException("Variable fonts require static instantiation before loading.");
        var head = Table("head", 54);
        var hhea = Table("hhea", 36);
        var maxp = Table("maxp", 6);
        var hmtx = Table("hmtx", 4);
        if (U32(head, 12) != 0x5F0F3CF5)
            throw new PdfFontException("Invalid TrueType head magic.");
        int units = U16(head, 18), glyphs = U16(maxp, 4), metrics = U16(hhea, 34);
        if (units is < 16 or > 16384 || glyphs < 1 || metrics < 1 || metrics > glyphs)
            throw new PdfFontException("Invalid font metrics or glyph count.");
        Slice(hmtx, 0, metrics * 4 + (glyphs - metrics) * 2);
        Metrics = new(units, I16(hhea, 4), I16(hhea, 6), I16(hhea, 8), I16(head, 36), I16(head, 38), I16(head, 40), I16(head, 42));
        widths = new ushort[glyphs];
        for (int i = 0; i < glyphs; i++)
            widths[i] = U16(hmtx, Math.Min(i, metrics - 1) * 4);
        var os2 = Table("OS/2", 10);
        int rights = U16(os2, 8);
        if ((rights & 0x0202) != 0)
            throw new PdfFontException("Font embedding permissions prohibit outline embedding (OS/2 fsType).");
        var glyf = Table("glyf", 0);
        var loca = Table("loca", 2);
        int format = I16(head, 50);
        if (format is not (0 or 1))
            throw new PdfFontException("Invalid loca format.");
        Slice(loca, 0, checked((glyphs + 1) * (format == 0 ? 2 : 4)));
        uint previous = 0;
        for (int i = 0; i <= glyphs; i++)
        {
            uint at = format == 0 ? (uint)U16(loca, i * 2) * 2 : U32(loca, i * 4);
            if (at < previous || at > glyf.Length)
                throw new PdfFontException("Invalid glyph outline offsets.");
            previous = at;
        }
        var names = Table("name", 6);
        int n = U16(names, 2), storage = U16(names, 4);
        Slice(names, 6, n * 12);
        string? name = null;
        for (int i = 0; i < n; i++)
        {
            int p = 6 + i * 12;
            int platform = U16(names, p);
            int len = U16(names, p + 8), off = U16(names, p + 10);
            var value = Slice(names, checked(storage + off), len);
            if (U16(names, p + 6) == 6 && platform is 0 or 3)
                name ??= Encoding.BigEndianUnicode.GetString(value);
        }
        Name = new string((name ?? "EmbeddedFont").Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').Take(63).ToArray());
        if (Name.Length == 0)
            Name = "EmbeddedFont";
        var cmap = Table("cmap", 4);
        int maps = U16(cmap, 2);
        Slice(cmap, 4, maps * 8);
        ReadOnlySpan<byte> chosen = default;
        int chosenFormat = 0;
        for (int i = 0; i < maps; i++)
        {
            int p = 4 + i * 8;
            int platform = U16(cmap, p), encoding = U16(cmap, p + 2);
            uint off = U32(cmap, p + 4);
            if (!(platform == 0 || (platform == 3 && encoding is 1 or 10)))
                continue;
            if (off > int.MaxValue)
                throw new PdfFontException("Invalid cmap offset.");
            int f = U16(cmap, (int)off);
            if (f is not (4 or 12) || f < chosenFormat)
                continue;
            uint length = f == 4 ? U16(cmap, (int)off + 2) : U32(cmap, (int)off + 4);
            if (length > int.MaxValue)
                throw new PdfFontException("Invalid cmap length.");
            chosen = Slice(cmap, (int)off, (int)length);
            chosenFormat = f;
        }
        if (chosenFormat == 0)
            throw new PdfFontException("No supported Unicode cmap (format 4 or 12).");
        if (chosenFormat == 4)
        {
            int segments = U16(chosen, 6) / 2;
            if (segments is < 1 or > 8192)
                throw new PdfFontException("Invalid cmap segment count.");
            Slice(chosen, 0, 16 + segments * 8);
            bmp = new ushort[65536];
            int last = -1;
            for (int i = 0; i < segments; i++)
            {
                int end = U16(chosen, 14 + i * 2), start = U16(chosen, 16 + segments * 2 + i * 2), delta = I16(chosen, 16 + segments * 4 + i * 2), rp = 16 + segments * 6 + i * 2, ro = U16(chosen, rp);
                if (start > end || start <= last)
                    throw new PdfFontException("Unsorted or overlapping cmap segments.");
                last = end;
                for (int code = start; code <= end; code++)
                {
                    int gid = ro == 0 ? (code + delta) & 65535 : U16(chosen, checked(rp + ro + (code - start) * 2));
                    if (ro != 0 && gid != 0)
                        gid = (gid + delta) & 65535;
                    if (gid >= glyphs)
                        throw new PdfFontException("cmap references a nonexistent glyph.");
                    bmp[code] = (ushort)gid;
                }
            }
        }
        else
        {
            uint groupCount = U32(chosen, 12);
            if (groupCount > 100_000)
                throw new PdfFontException("Excessive cmap groups.");
            Slice(chosen, 16, checked((int)groupCount * 12));
            groups = new (uint, uint, uint)[groupCount];
            uint last = 0;
            for (int i = 0; i < groups.Length; i++)
            {
                int p = 16 + i * 12;
                uint start = U32(chosen, p), end = U32(chosen, p + 4), gid = U32(chosen, p + 8);
                if (end < start || end > 0x10FFFF || (i > 0 && start <= last) || (ulong)gid + end - start >= (ulong)glyphs)
                    throw new PdfFontException("Invalid cmap group.");
                groups[i] = (start, end, gid);
                last = end;
            }
        }
    }
    /// <summary>Loads a bounded (32 MiB maximum) TrueType file.</summary>
    public static FontFace Load(string path)
    {
        using var f = File.OpenRead(path);
        if (f.Length > 32 * 1024 * 1024)
            throw new PdfFontException("Font exceeds the 32 MiB limit.");
        var b = new byte[checked((int)f.Length)];
        f.ReadExactly(b);
        return ParseOwned(b);
    }
    /// <summary>Loads and validates TrueType data, taking an immutable copy.</summary>
    public static FontFace Load(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 12 or > 32 * 1024 * 1024)
            throw new PdfFontException("Font size must be between 12 bytes and 32 MiB.");
        return ParseOwned(bytes.ToArray());
    }
    private static FontFace ParseOwned(byte[] bytes)
    {
        if (bytes.Length < 12)
            throw new PdfFontException("Font size must be between 12 bytes and 32 MiB.");
        try
        {
            return new FontFace(bytes);
        }
        catch (OverflowException) { throw new PdfFontException("Overflow in font table metadata."); }
    }
    /// <summary>Returns an independent copy of the font program for full embedding.</summary>
    public byte[] GetFontProgram() => (byte[])data.Clone();
    /// <summary>Maps a Unicode scalar to a glyph. Zero indicates a missing glyph.</summary>
    public ushort GetGlyphId(Rune rune)
    {
        int code = rune.Value;
        if (IsStandard)
            return code is >= 32 and <= 126 ? (ushort)code : (ushort)0;
        if (bmp is not null)
            return code <= 65535 ? bmp[code] : (ushort)0;
        int lo = 0, hi = groups!.Length - 1;
        while (lo <= hi)
        {
            int m = lo + (hi - lo) / 2;
            var g = groups[m];
            if (code < g.Start)
                hi = m - 1;
            else if (code > g.End)
                lo = m + 1;
            else
                return (ushort)(g.Glyph + code - g.Start);
        }
        return 0;
    }
    /// <summary>Returns the horizontal advance in font units.</summary>
    public int GetAdvance(ushort glyph)
    {
        if (IsStandard)
            return 600;
        if (glyph >= widths.Length)
            throw new PdfFontException("Glyph index exceeds font glyph count.");
        return widths[glyph];
    }
    private static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> b, int p, int length)
    {
        if (p < 0 || length < 0 || p > b.Length - length)
            throw new PdfFontException("Truncated or out-of-range font table.");
        return b.Slice(p, length);
    }
    private static ushort U16(ReadOnlySpan<byte> b, int p) => BinaryPrimitives.ReadUInt16BigEndian(Slice(b, p, 2));
    private static short I16(ReadOnlySpan<byte> b, int p) => BinaryPrimitives.ReadInt16BigEndian(Slice(b, p, 2));
    private static uint U32(ReadOnlySpan<byte> b, int p) => BinaryPrimitives.ReadUInt32BigEndian(Slice(b, p, 4));
}
