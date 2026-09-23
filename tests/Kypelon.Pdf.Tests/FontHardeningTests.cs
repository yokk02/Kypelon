using System.Buffers.Binary;
using System.Text;
using Kypelon.Pdf.Text;
using Xunit;
using Xunit.Abstractions;

namespace Kypelon.Pdf.Tests;

public class FontHardeningTests(ITestOutputHelper output)
{
    [Fact]
    public void OverlappingDirectoryAllocatesAtMostOneProgramPlusBoundedMetadata()
    {
        byte[] bytes = OverlapFixture();
        Assert.Throws<PdfFontException>(() => FontFace.Load(bytes)); // JIT/static warmup outside measurement
        long start = GC.GetAllocatedBytesForCurrentThread();
        PdfFontException? error = null;
        try { FontFace.Load(bytes); } catch (PdfFontException e) { error = e; }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        output.WriteLine($"F1 input={bytes.Length} bytes; allocated={allocated} bytes");
        Assert.NotNull(error);
        Assert.InRange(allocated, bytes.Length, bytes.Length * 2L + 262144);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(32)]
    [InlineData(256)]
    public void SharedPayloadDescriptorsAreRejected(int count) =>
        Assert.Contains("overlap", Assert.Throws<PdfFontException>(() => FontFace.Load(OverlapFixture(count))).Message, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void DuplicateTagsAreRejectedBeforePayloadCopies()
    {
        byte[] bytes = OverlapFixture();
        bytes.AsSpan(12, 4).CopyTo(bytes.AsSpan(28));
        long start = GC.GetAllocatedBytesForCurrentThread();
        var error = Assert.Throws<PdfFontException>(() => FontFace.Load(bytes));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Contains("Duplicate", error.Message);
        Assert.True(allocated < bytes.Length * 2L + 262144);
    }

    [Fact]
    public void PartlyOverlappingRequiredTablesAreRejected()
    {
        byte[] bytes = FontFixture.Create();
        int head = DirectoryEntry(bytes, "head"), hhea = DirectoryEntry(bytes, "hhea");
        Put32(bytes, hhea + 8, Get32(bytes, head + 8) + 1);
        Assert.Contains("overlap", Assert.Throws<PdfFontException>(() => FontFace.Load(bytes)).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmptyGlyfIsLegalForEmptyOutlines() => Assert.Equal(2, FontFace.Load(FontFixture.Create()).GlyphCount);

    [Theory]
    [InlineData("head")]
    [InlineData("cmap")]
    [InlineData("hmtx")]
    public void RequiredNonemptyTablesRejectZeroLength(string tag)
    {
        byte[] bytes = FontFixture.Create();
        Put32(bytes, DirectoryEntry(bytes, tag) + 12, 0);
        Assert.Contains(tag, Assert.Throws<PdfFontException>(() => FontFace.Load(bytes)).Message);
    }

    [Theory]
    [InlineData(uint.MaxValue, 1u)]
    [InlineData(16u, uint.MaxValue)]
    public void MalformedDirectoryArithmeticIsBounded(uint offset, uint length)
    {
        byte[] bytes = FontFixture.Create();
        Put32(bytes, 20, offset); Put32(bytes, 24, length);
        Assert.Throws<PdfFontException>(() => FontFace.Load(bytes));
    }

    [Fact]
    public void NonemptyTableCannotAliasTheDirectory()
    {
        byte[] bytes = FontFixture.Create(); Put32(bytes, 20, 12);
        Assert.Contains("directory", Assert.Throws<PdfFontException>(() => FontFace.Load(bytes)).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MaximumDirectorySupportsUnknownEmptyTables()
    {
        var tables = ReadTables(FontFixture.Create());
        for (int i = tables.Count; i < 256; i++) tables.Add($"X{i:000}", []);
        Assert.Equal(2, FontFace.Load(Build(tables)).GlyphCount);
        tables.Add("X256", []);
        Assert.Throws<PdfFontException>(() => FontFace.Load(Build(tables)));
    }

    [Fact]
    public void RepeatedCmapCandidatesUseSlicesInsteadOfPayloadCopies()
    {
        var tables = ReadTables(FontFixture.Create());
        const int maps = 64, subtableLength = 48 * 1024;
        int offset = 4 + maps * 8;
        byte[] cmap = new byte[offset + subtableLength];
        Put16(cmap, 2, maps);
        for (int i = 0; i < maps; i++)
        {
            Put16(cmap, 4 + i * 8, 3); Put16(cmap, 6 + i * 8, 10); Put32(cmap, 8 + i * 8, (uint)offset);
        }
        Put16(cmap, offset, 12); Put32(cmap, offset + 4, subtableLength); Put32(cmap, offset + 12, 1);
        Put32(cmap, offset + 16, 65); Put32(cmap, offset + 20, 65); Put32(cmap, offset + 24, 1);
        tables["cmap"] = cmap;
        byte[] bytes = Build(tables);
        _ = FontFace.Load(bytes);
        long start = GC.GetAllocatedBytesForCurrentThread();
        var font = FontFace.Load(bytes);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Equal(1, font.GetGlyphId(new Rune('A')));
        Assert.True(allocated < bytes.Length * 2L + 262144, $"cmap candidate allocation: {allocated}");
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CmapFourMappingsAndGlyphBoundsArePreserved(bool invalidGlyph)
    {
        var tables = ReadTables(FontFixture.Create());
        byte[] cmap = new byte[44];
        Put16(cmap, 2, 1); Put16(cmap, 4, 3); Put16(cmap, 6, 1); Put32(cmap, 8, 12);
        Put16(cmap, 12, 4); Put16(cmap, 14, 32); Put16(cmap, 18, 4);
        Put16(cmap, 26, 65); Put16(cmap, 28, 65535); // end codes
        Put16(cmap, 32, 65); Put16(cmap, 34, 65535); // start codes
        Put16(cmap, 36, invalidGlyph ? 0 : 65536 - 64); Put16(cmap, 38, 1); // deltas
        tables["cmap"] = cmap;
        if (invalidGlyph) Assert.Throws<PdfFontException>(() => FontFace.Load(Build(tables)));
        else { var font = FontFace.Load(Build(tables)); Assert.Equal(1, font.GetGlyphId(new Rune('A'))); Assert.Equal(0, font.GetGlyphId(new Rune('B'))); }
    }

    internal static byte[] OverlapFixture(int count = 32)
    {
        byte[] bytes = new byte[count == 256 ? 8192 : 1024 * 1024];
        Put32(bytes, 0, 0x10000); Put16(bytes, 4, count);
        int offset = Math.Max(1024, 12 + count * 16);
        for (int i = 0; i < count; i++)
        {
            int p = 12 + i * 16;
            Encoding.ASCII.GetBytes($"T{i:000}").CopyTo(bytes, p);
            Put32(bytes, p + 8, (uint)offset); Put32(bytes, p + 12, (uint)(bytes.Length - offset));
        }
        return bytes;
    }
    internal static int DirectoryEntry(byte[] bytes, string tag)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
        for (int i = 0; i < count; i++)
            if (Encoding.ASCII.GetString(bytes, 12 + i * 16, 4) == tag) return 12 + i * 16;
        throw new InvalidOperationException("Missing test table " + tag);
    }
    internal static Dictionary<string, byte[]> ReadTables(byte[] bytes)
    {
        var result = new Dictionary<string, byte[]>();
        int count = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
        for (int i = 0; i < count; i++)
        {
            int p = 12 + i * 16;
            result.Add(Encoding.ASCII.GetString(bytes, p, 4), bytes.AsSpan((int)Get32(bytes, p + 8), (int)Get32(bytes, p + 12)).ToArray());
        }
        return result;
    }
    internal static byte[] Build(Dictionary<string, byte[]> tables)
    {
        int offset = 12 + tables.Count * 16;
        var bytes = new byte[offset + tables.Values.Sum(b => b.Length)];
        Put32(bytes, 0, 0x10000); Put16(bytes, 4, tables.Count);
        int i = 0;
        foreach (var (tag, value) in tables)
        {
            int p = 12 + i++ * 16;
            Encoding.ASCII.GetBytes(tag).CopyTo(bytes, p);
            Put32(bytes, p + 8, (uint)offset); Put32(bytes, p + 12, (uint)value.Length);
            value.CopyTo(bytes, offset); offset += value.Length;
        }
        return bytes;
    }
    internal static void Put16(byte[] bytes, int p, int value) => BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(p), checked((ushort)value));
    internal static void Put32(byte[] bytes, int p, uint value) => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(p), value);
    internal static uint Get32(byte[] bytes, int p) => BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(p));
}
