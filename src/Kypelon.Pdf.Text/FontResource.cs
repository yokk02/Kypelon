using System.Globalization;
using System.Text;
using Kypelon.Pdf.Core;

namespace Kypelon.Pdf.Text;

/// <summary>Per-document font resource and Unicode encoding. Not thread-safe.</summary>
public sealed class FontResource
{
    /// <summary>Maximum distinct CID mappings supported by one embedded font resource.</summary>
    public const int MaximumCidCount = 65534;
    private readonly FontFace font;
    private readonly PdfFileWriter writer;
    private readonly bool compress;
    private readonly Dictionary<(ushort Glyph, string? Text), ushort> codes = [];
    private readonly List<(ushort Glyph, string? Text)> entries = [];
    /// <summary>Reference to the PDF font dictionary.</summary>
    public PdfIndirectReference Reference
    {
        get;
    }
    /// <summary>Creates a font resource owned by a single writer.</summary>
    public FontResource(PdfFileWriter writer, FontFace font, bool compress = true)
    {
        this.writer = writer;
        this.font = font;
        this.compress = compress;
        Reference = writer.Reserve();
    }
    /// <summary>Encodes a validated run for this resource. Extra cluster glyphs have no independent ToUnicode entry; surround complex runs with ActualText.</summary>
    public byte[] Encode(PreparedGlyphRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (!ReferenceEquals(font, run.Input.Font)) throw new PdfFontException("Prepared glyph run belongs to a different font resource.");
        var result = new byte[checked(run.Glyphs.Count * (font.IsStandard ? 1 : 2))];
        int p = 0;
        for (int i = 0; i < run.Glyphs.Count; i++)
        {
            var g = run.Glyphs[i];
            if (font.IsStandard) { result[p++] = (byte)g.GlyphId; continue; }
            var key = (g.GlyphId, run.GetUnicodeMapping(i));
            if (!codes.TryGetValue(key, out ushort cid))
            {
                if (entries.Count >= MaximumCidCount) throw new PdfFontException("Font CID limit exceeded.");
                entries.Add(key); cid = (ushort)entries.Count; codes.Add(key, cid);
            }
            result[p++] = (byte)(cid >> 8); result[p++] = (byte)cid;
        }
        return result;
    }
    private IEnumerable<(PdfIndirectReference Id, PdfObject? Object, PdfStream? Stream)> Objects()
    {
        if (font.IsStandard)
        {
            yield return (Reference, new PdfDictionary().Add("Type", new PdfName("Font")).Add("Subtype", new PdfName("Type1")).Add("BaseFont", new PdfName(font.Name)).Add("Encoding", new PdfName("WinAnsiEncoding")), null);
            yield break;
        }
        var file = writer.Reserve();
        var descriptor = writer.Reserve();
        var cidFont = writer.Reserve();
        var cmap = writer.Reserve();
        var mapping = writer.Reserve();
        var program = font.GetFontProgram();
        yield return (file, null, new PdfStream(new PdfDictionary().Add("Length1", new PdfInteger(program.Length)), program, compress));
        var m = font.Metrics;
        double Scale(int value) => value * 1000.0 / m.UnitsPerEm;
        yield return (descriptor, new PdfDictionary().Add("Type", new PdfName("FontDescriptor")).Add("FontName", new PdfName(font.Name)).Add("Flags", new PdfInteger(4)).Add("FontBBox", new PdfArray(new PdfReal(Scale(m.XMin)), new PdfReal(Scale(m.YMin)), new PdfReal(Scale(m.XMax)), new PdfReal(Scale(m.YMax)))).Add("ItalicAngle", new PdfInteger(0)).Add("Ascent", new PdfReal(Scale(m.Ascender))).Add("Descent", new PdfReal(Scale(m.Descender))).Add("CapHeight", new PdfReal(Scale(m.Ascender))).Add("StemV", new PdfInteger(80)).Add("FontFile2", file), null);
        var map = new byte[(entries.Count + 1) * 2];
        for (int i = 0; i < entries.Count; i++)
        {
            map[(i + 1) * 2] = (byte)(entries[i].Glyph >> 8);
            map[(i + 1) * 2 + 1] = (byte)entries[i].Glyph;
        }
        yield return (mapping, null, new PdfStream(new(), map, compress));
        var widths = entries.Select(g => (PdfObject)new PdfReal(Scale(font.GetAdvance(g.Glyph)))).ToArray();
        yield return (cidFont, new PdfDictionary().Add("Type", new PdfName("Font")).Add("Subtype", new PdfName("CIDFontType2")).Add("BaseFont", new PdfName(font.Name)).Add("CIDSystemInfo", new PdfDictionary().Add("Registry", new PdfString("Adobe")).Add("Ordering", new PdfString("Identity")).Add("Supplement", new PdfInteger(0))).Add("FontDescriptor", descriptor).Add("CIDToGIDMap", mapping).Add("DW", new PdfInteger(1000)).Add("W", new PdfArray(new PdfInteger(1), new PdfArray(widths))), null);
        var s = new StringBuilder("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n/CMapName /KypelonUnicode def\n/CMapType 2 def\n1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
        var mapped = entries.Select((entry, index) => (entry.Text, Cid: index + 1)).Where(e => e.Text is not null).ToArray();
        for (int start = 0; start < mapped.Length; start += 100)
        {
            int count = Math.Min(100, mapped.Length - start);
            s.Append(count.ToString(CultureInfo.InvariantCulture)).Append(" beginbfchar\n");
            for (int i = start; i < start + count; i++)
                s.Append('<').Append(mapped[i].Cid.ToString("X4", CultureInfo.InvariantCulture)).Append("> <").Append(Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(mapped[i].Text!))).Append(">\n");
            s.Append("endbfchar\n");
        }
        s.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
        yield return (cmap, null, new PdfStream(new(), Encoding.ASCII.GetBytes(s.ToString()), compress));
        yield return (Reference, new PdfDictionary().Add("Type", new PdfName("Font")).Add("Subtype", new PdfName("Type0")).Add("BaseFont", new PdfName(font.Name)).Add("Encoding", new PdfName("Identity-H")).Add("DescendantFonts", new PdfArray(cidFont)).Add("ToUnicode", cmap), null);
    }
    /// <summary>Writes the accumulated resource after all content has been encoded.</summary>
    public void Write()
    {
        foreach (var item in Objects())
            if (item.Object is not null)
                writer.Write(item.Id, item.Object);
            else
                writer.Write(item.Id, item.Stream!);
    }
    /// <summary>Asynchronously writes the accumulated resource.</summary>
    public async ValueTask WriteAsync(CancellationToken token = default)
    {
        foreach (var item in Objects())
            if (item.Object is not null)
                await writer.WriteAsync(item.Id, item.Object, token).ConfigureAwait(false);
            else
                await writer.WriteAsync(item.Id, item.Stream!, token).ConfigureAwait(false);
    }
}
