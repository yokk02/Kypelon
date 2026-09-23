using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Kypelon.Pdf.Core;
using Xunit;

namespace Kypelon.Pdf.Tests;

public class CoreTests
{
    [Theory]
    [InlineData(1.25, "1.25")]
    [InlineData(-1234.5, "-1234.5")]
    [InlineData(0.000001, "0.000001")]
    [InlineData(0.0, "0")]
    public void NumbersAreInvariantAndNotExponential(double number, string expected)
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new("th-TH");
            Assert.Equal(expected, new PdfReal(number).ToPdfSyntax());
            CultureInfo.CurrentCulture = new("fr-FR");
            Assert.Equal(expected, new PdfReal(number).ToPdfSyntax());
        }
        finally { CultureInfo.CurrentCulture = old; }
    }
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e20)]
    public void InvalidNumbersFail(double value) => Assert.Throws<ArgumentOutOfRangeException>(() => new PdfReal(value).ToPdfSyntax());
    [Fact] public void LiteralsEscapeDelimitersAndControls() => Assert.Equal(@"(a\(b\)\\\012\000)", new PdfString("a(b)\\\n\0").ToPdfSyntax());
    [Fact] public void UnicodeStringsHaveUtf16Bom() => Assert.Equal("<FEFF0E440E170E22>", new PdfString("ไทย").ToPdfSyntax());
    [Fact] public void NamesEscapeBytes() => Assert.Equal("/A#20B#23#2F#25#E0#B9#84", new PdfName("A B#/%ไ").ToPdfSyntax());
    [Fact] public void HexIsUppercase() => Assert.Equal("<00ABFF>", new PdfHexString(new byte[] { 0, 171, 255 }).ToPdfSyntax());
    [Fact] public void ContainersSerializeInStableOrder() => Assert.Equal("<<\n/A [null true -42 ]\n/Z /Last\n>>", new PdfDictionary().Add("Z", new PdfName("Last")).Add("A", new PdfArray(PdfNull.Value, new PdfBoolean(true), new PdfInteger(-42))).ToPdfSyntax());
    [Fact]
    public void CyclesFailWithContext()
    {
        var d = new PdfDictionary();
        d.Add("Self", d);
        Assert.Throws<PdfWriteException>(() => d.ToPdfSyntax());
    }
    [Fact] public void IndirectGenerationsSerialize() => Assert.Equal("12 4 R", new PdfIndirectReference(new(12, 4)).ToPdfSyntax());
    [Fact]
    public void UnwrittenReservationsFail()
    {
        using var ms = new MemoryStream();
        using var w = new PdfFileWriter(ms);
        var root = w.Reserve();
        Assert.Throws<PdfWriteException>(() => w.Finish(root));
    }
    [Fact]
    public void DuplicateWritesFail()
    {
        using var ms = new MemoryStream();
        using var w = new PdfFileWriter(ms);
        var r = w.Reserve();
        w.Write(r, PdfNull.Value);
        Assert.Throws<PdfWriteException>(() => w.Write(r, PdfNull.Value));
    }
    [Fact]
    public void NonemptyDestinationFails()
    {
        using var ms = new MemoryStream([1, 2]);
        Assert.Throws<ArgumentException>(() => new PdfFileWriter(ms));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StreamLengthAndCompressionAreExact(bool compress)
    {
        byte[] payload = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("ไทย PDF stream\n", 100)));
        using var ms = new MemoryStream();
        using (var w = new PdfFileWriter(ms))
        {
            var root = w.Reserve();
            var stream = w.Reserve();
            w.Write(root, new PdfDictionary().Add("Payload", stream));
            w.Write(stream, new PdfStream(new(), payload, compress));
            w.Finish(root);
        }
        var objects = Inspect.Objects(ms.ToArray());
        var decoded = Inspect.Stream(objects[2]);
        Assert.Equal(payload, decoded);
        Assert.Contains("/Size 3", Encoding.Latin1.GetString(ms.ToArray()));
    }
    [Fact]
    public void OffsetsAreCorrectWithOutOfOrderObjects()
    {
        using var ms = new MemoryStream();
        using var w = new PdfFileWriter(ms);
        var a = w.Reserve();
        var b = w.Reserve();
        w.Write(b, new PdfString("content"));
        w.Write(a, new PdfDictionary().Add("Child", b));
        w.Finish(a);
        var objects = Inspect.Objects(ms.ToArray());
        Assert.Contains("/Child 2 0 R", objects[1]);
        Assert.Contains("(content)", objects[2]);
        Assert.StartsWith("%PDF-1.7\n%âãÏÓ", Encoding.Latin1.GetString(ms.ToArray()));
    }
    [Fact] public void NullInNameIsRejected() => Assert.Throws<PdfWriteException>(() => new PdfName("A\0B").ToPdfSyntax());
    [Fact]
    public void ForeignWriterReferenceIsRejected()
    {
        using var a = new PdfFileWriter(new MemoryStream());
        using var b = new PdfFileWriter(new MemoryStream());
        var other = a.Reserve();
        b.Reserve();
        Assert.Throws<PdfWriteException>(() => b.Write(other, PdfNull.Value));
    }
    [Fact]
    public void ExistingFilterCannotBeSilentlyOverwritten()
    {
        using var w = new PdfFileWriter(new MemoryStream());
        var r = w.Reserve();
        Assert.Throws<PdfWriteException>(() => w.Write(r, new PdfStream(new PdfDictionary().Add("Filter", new PdfName("DCTDecode")), new byte[] { 1, 2 }, true)));
    }
    [Fact]
    public void MetadataDeterminism()
    {
        var m = new PdfMetadata { Title = "ไทย", CreationDate = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero) };
        Assert.Contains("D:20260102030405Z", m.ToDictionary(true).ToPdfSyntax());
        Assert.DoesNotContain("CreationDate", new PdfMetadata().ToDictionary(true).ToPdfSyntax());
    }
}
internal static class Inspect
{
    internal static Dictionary<int, string> Objects(byte[] bytes)
    {
        string text = Encoding.Latin1.GetString(bytes);
        var sx = Regex.Match(text, @"startxref\n(\d+)\n%%EOF\n$");
        Assert.True(sx.Success, "Missing startxref/EOF");
        int xref = int.Parse(sx.Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.Equal("xref\n", text.Substring(xref, 5));
        string[] lines = text[xref..].Split('\n');
        int count = int.Parse(lines[1].Split(' ')[1], CultureInfo.InvariantCulture);
        Assert.Equal("0000000000 65535 f ", lines[2]);
        var starts = new List<(int Id, int At)>();
        for (int id = 1; id < count; id++)
        {
            string line = lines[id + 2];
            Assert.Matches(@"^\d{10} 00000 n $", line);
            int at = int.Parse(line[..10], CultureInfo.InvariantCulture);
            Assert.StartsWith($"{id} 0 obj\n", text[at..]);
            starts.Add((id, at));
        }
        starts.Sort((a, b) => a.At.CompareTo(b.At));
        var result = new Dictionary<int, string>();
        for (int i = 0; i < starts.Count; i++)
        {
            var (id, at) = starts[i];
            result[id] = text[at..(i + 1 < starts.Count ? starts[i + 1].At : xref)];
            Assert.EndsWith("endobj\n", result[id]);
        }
        Assert.Contains($"/Size {count}", text[xref..]);
        return result;
    }
    internal static byte[] Stream(string obj)
    {
        int p = obj.IndexOf("\nstream\n", StringComparison.Ordinal) + 8;
        Assert.True(p >= 8);
        int len = int.Parse(Regex.Match(obj[..p], @"/Length (\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.StartsWith("\nendstream\n", obj[(p + len)..]);
        byte[] raw = Encoding.Latin1.GetBytes(obj.Substring(p, len));
        if (!obj[..p].Contains("/FlateDecode", StringComparison.Ordinal))
            return raw;
        using var input = new MemoryStream(raw);
        using var z = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        z.CopyTo(output);
        return output.ToArray();
    }
}
