using System.Buffers.Binary;
using System.Text;
using Kypelon.Pdf.Core;
using Kypelon.Pdf.Graphics;
using Kypelon.Pdf.Text;
using Xunit;

namespace Kypelon.Pdf.Tests;

public class TextGraphicsTests
{
    [Fact]
    public void FontParserReadsMetricsAndFormat12()
    {
        var f = FontFace.Load(FontFixture.Create());
        Assert.Equal(1000, f.Metrics.UnitsPerEm);
        Assert.Equal(2, f.GlyphCount);
        Assert.Equal(600, f.GetAdvance(1));
        Assert.Equal(1, f.GetGlyphId(new Rune(0x1F600)));
        Assert.Equal(1, f.GetGlyphId(new Rune('ก')));
        Assert.Equal("TestFont", f.Name);
    }
    [Fact]
    public void FontOwnsACopy()
    {
        byte[] b = FontFixture.Create();
        var f = FontFace.Load(b);
        b[0] = 255;
        var copy = f.GetFontProgram();
        copy[0] = 255;
        Assert.Equal(0, f.GetFontProgram()[0]);
    }
    [Fact]
    public void TruncatedFontFailsCleanly()
    {
        byte[] b = FontFixture.Create();
        for (int n = 0; n < b.Length; n += 7)
            Assert.Throws<PdfFontException>(() => FontFace.Load(b.AsSpan(0, n)));
    }
    [Fact]
    public void UntrustedTableOffsetsFail()
    {
        var b = FontFixture.Create();
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(20), uint.MaxValue);
        Assert.Throws<PdfFontException>(() => FontFace.Load(b));
    }
    [Fact] public void EmbeddingRightsAreEnforced() => Assert.Throws<PdfFontException>(() => FontFace.Load(FontFixture.Create(2)));
    [Fact] public void InvalidGlyphMappingFails() => Assert.Throws<PdfFontException>(() => FontFace.Load(FontFixture.Create(glyphId: 60000)));
    [Fact] public void MissingGlyphIsExplicit() => Assert.Contains("U+0E44", Assert.Throws<PdfFontException>(() => BasicTextShaper.Instance.Shape(FontFace.Courier, "ไทย")).Message);
    [Fact] public void InvalidSurrogateIsRejected() => Assert.Throws<PdfFontException>(() => BasicTextShaper.Instance.Shape(FontFace.Courier, "\uD800"));
    [Fact] public void TabsExpandToSpaces() => Assert.Equal("A    B", Assert.Single(LineBreaker.Wrap("A\tB", FontFace.Courier, 10, 100)).Text);
    [Fact] public void StandardFontMeasurementUsesExactCourierAdvance() => Assert.Equal(36, LineBreaker.Measure("ABCDEF", FontFace.Courier, 10));
    [Fact]
    public void WrappingRespectsWidthsAndExplicitNewlines()
    {
        var lines = LineBreaker.Wrap("One two three\nFour", FontFace.Courier, 10, 45);
        Assert.Equal(new[] { "One two", "three", "Four" }, lines.Select(l => l.Text));
        Assert.All(lines, l => Assert.True(l.Width <= 45));
    }
    [Fact]
    public void LongWordsBreakWithoutLoss()
    {
        string text = new('X', 101);
        var lines = LineBreaker.Wrap(text, FontFace.Courier, 10, 30);
        Assert.Equal(text, string.Concat(lines.Select(l => l.Text)));
        Assert.Equal(21, lines.Count);
    }
    [Fact] public void TooNarrowClusterFails() => Assert.Throws<PdfFontException>(() => LineBreaker.Wrap("A", FontFace.Courier, 10, 2));
    [Fact]
    public void UnicodeAliasesRetainDistinctCidsAndSurrogates()
    {
        var f = FontFace.Load(FontFixture.Create());
        using var ms = new MemoryStream();
        using var w = new PdfFileWriter(ms);
        var resource = new FontResource(w, f, false);
        var encoded = resource.Encode(LineBreaker.Prepare("ก😀", f, 10).Run);
        Assert.Equal(new byte[] { 0, 1, 0, 2 }, encoded);
        resource.Write();
        w.Finish(resource.Reference);
        var objects = Inspect.Objects(ms.ToArray());
        var cmap = objects.Values.Where(s => s.Contains("\nstream\n", StringComparison.Ordinal)).Select(Inspect.Stream).Select(Encoding.Latin1.GetString).Single(s => s.Contains("beginbfchar", StringComparison.Ordinal));
        Assert.Contains("<0001> <0E01>", cmap);
        Assert.Contains("<0002> <D83DDE00>", cmap);
        Assert.Contains("/CIDToGIDMap", string.Join("\n", objects.Values));
    }
    [Fact]
    public void InstalledThaiFontMapsRequiredCorpus()
    {
        string? configured = Environment.GetEnvironmentVariable("KYPELON_TEST_FONT");
        var font = FontFace.Load(configured ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "tahoma.ttf"));
        foreach (var rune in "ภาษาไทยบริษัท ทดสอบ จำกัดรายงานผลการตรวจสอบข้อมูลพนักงานปีงบประมาณ 2569๐๑๒๓๔๕๖๗๘๙".EnumerateRunes())
            Assert.NotEqual(0, font.GetGlyphId(rune));
        Assert.True(font.Metrics.Ascender > 0);
        Assert.True(LineBreaker.Measure("ภาษาไทย", font, 12) > 0);
    }
    [Fact(Skip = "Tracked Thai shaping gap: BasicTextShaper does not apply contextual GPOS mark positioning. See docs/roadmap/thai.md.")]
    public void ThaiStackedMarksReceiveContextualOffsets()
    {
        var font = FontFace.Load(Environment.GetEnvironmentVariable("KYPELON_TEST_FONT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "tahoma.ttf"));
        var run = BasicTextShaper.Instance.Shape(font, "ปี่");
        Assert.Contains(run.Glyphs, g => g.OffsetX != 0 || g.OffsetY != 0);
    }
    [Fact]
    public void GraphicsUseTopLeftTransformAndUprightText()
    {
        var c = new PdfCanvas(800);
        c.SetFillColor(Color.FromRgb(255, 0, 0)).Rectangle(10, 20, 30, 40).Fill().SetStrokeColor(Color.Black).MoveTo(1, 2).LineTo(3, 4).Stroke().DrawText("F1", 12, "ABC"u8, 10, 30);
        string s = Encoding.ASCII.GetString(c.ToArray());
        Assert.Contains("1 0 0 -1 0 800 cm", s);
        Assert.Contains("10 20 30 40 re", s);
        Assert.Contains("1 0 0 -1 10 30 Tm", s);
        Assert.Contains("<414243> Tj", s);
    }
    [Fact]
    public void GraphicsStateMustBalance()
    {
        var c = new PdfCanvas(800);
        Assert.Throws<InvalidOperationException>(() => c.RestoreState());
        c.SaveState();
        Assert.Throws<InvalidOperationException>(() => c.ToArray());
        c.RestoreState();
        Assert.NotEmpty(c.ToArray());
    }
    [Fact]
    public void TransformsAndClipEmitOperators()
    {
        var c = new PdfCanvas(800);
        c.SaveState().Translate(10, 20).Scale(2, 3).Rotate(90).Rectangle(0, 0, 100, 100).Clip().RestoreState();
        var s = Encoding.ASCII.GetString(c.ToArray());
        Assert.Contains("1 0 0 1 10 20 cm", s);
        Assert.Contains("2 0 0 3 0 0 cm", s);
        Assert.Contains("W n", s);
    }
    [Theory]
    [InlineData("sample.jpg", 320, 180)]
    [InlineData("sample-rgb.png", 320, 180)]
    [InlineData("sample-rgba.png", 200, 120)]
    public void ImageDimensionsAreParsed(string name, int width, int height)
    {
        var image = PdfImage.Load(Path.Combine(AppContext.BaseDirectory, "images", name));
        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
    }
    [Fact]
    public void PngCrcCorruptionFails()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "images", "sample-rgb.png"));
        bytes[25] ^= 1;
        Assert.Throws<PdfImageException>(() => PdfImage.Load(bytes));
    }
    [Fact]
    public void TruncatedJpegFails()
    {
        var b = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "images", "sample.jpg"));
        Assert.Throws<PdfImageException>(() => PdfImage.Load(b.AsSpan(0, b.Length - 10)));
    }
    [Fact]
    public void AlphaCreatesSoftMask()
    {
        var image = PdfImage.Load(Path.Combine(AppContext.BaseDirectory, "images", "sample-rgba.png"));
        using var ms = new MemoryStream();
        using var w = new PdfFileWriter(ms);
        var r = w.Reserve();
        var streams = image.CreateStreams(w, r);
        Assert.Equal(2, streams.Count);
        Assert.Contains("/SMask", streams[1].Stream.Dictionary.ToPdfSyntax());
    }
}
internal static class FontFixture
{
    internal static byte[] Create(ushort rights = 0, uint glyphId = 1, uint[]? codePoints = null)
    {
        var tables = new Dictionary<string, byte[]>();
        void U16(byte[] b, int p, int v) => BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(p), unchecked((ushort)v));
        void U32(byte[] b, int p, uint v) => BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(p), v);
        byte[] head = new byte[54];
        U32(head, 12, 0x5F0F3CF5);
        U16(head, 18, 1000);
        tables["head"] = head;
        byte[] hhea = new byte[36];
        U16(hhea, 4, 800);
        U16(hhea, 6, -200);
        U16(hhea, 34, 2);
        tables["hhea"] = hhea;
        byte[] maxp = new byte[6];
        U32(maxp, 0, 0x00010000);
        U16(maxp, 4, 2);
        tables["maxp"] = maxp;
        byte[] hmtx = new byte[8];
        U16(hmtx, 0, 600);
        U16(hmtx, 4, 600);
        tables["hmtx"] = hmtx;
        byte[] os2 = new byte[10];
        U16(os2, 8, rights);
        tables["OS/2"] = os2;
        tables["loca"] = new byte[6];
        tables["glyf"] = [];
        byte[] name = new byte[34];
        U16(name, 2, 1);
        U16(name, 4, 18);
        U16(name, 6, 3);
        U16(name, 8, 1);
        U16(name, 12, 6);
        U16(name, 14, 16);
        Encoding.BigEndianUnicode.GetBytes("TestFont").CopyTo(name, 18);
        tables["name"] = name;
        uint[] points = codePoints ?? [0x0E01, 0x1F600];
        byte[] cmap = new byte[28 + points.Length * 12];
        U16(cmap, 2, 1);
        U16(cmap, 4, 3);
        U16(cmap, 6, 10);
        U32(cmap, 8, 12);
        U16(cmap, 12, 12);
        U32(cmap, 16, (uint)(16 + points.Length * 12));
        U32(cmap, 24, (uint)points.Length);
        for (int i = 0; i < points.Length; i++)
        {
            U32(cmap, 28 + i * 12, points[i]);
            U32(cmap, 32 + i * 12, points[i]);
            U32(cmap, 36 + i * 12, glyphId);
        }
        tables["cmap"] = cmap;
        int start = 12 + tables.Count * 16;
        var result = new byte[start + tables.Values.Sum(b => b.Length)];
        U32(result, 0, 0x10000);
        U16(result, 4, tables.Count);
        int index = 0;
        foreach (var (tag, b) in tables)
        {
            int p = 12 + index++ * 16;
            Encoding.ASCII.GetBytes(tag).CopyTo(result, p);
            U32(result, p + 8, (uint)start);
            U32(result, p + 12, (uint)b.Length);
            b.CopyTo(result, start);
            start += b.Length;
        }
        return result;
    }
}
