using Kypelon.Pdf.Core;
using Kypelon.Pdf.Graphics;
using Xunit;

namespace Kypelon.Pdf.Tests;

public class JpegHardeningTests
{
    [Theory]
    [InlineData("")] // SOF + EOI only
    [InlineData("FFDA")] // missing scan length/header
    [InlineData("FFDA000C030100")] // truncated component selectors
    [InlineData("FFDA0008030100003F00")] // length/count mismatch
    [InlineData("FFDA000C03010002000300003F")] // truncated last scan parameter
    [InlineData("FFDA000C03010002000300003F00")] // no entropy bytes
    [InlineData("FFDA0008010400003F0000")] // undefined component selector
    [InlineData("FFDA000A0201000100003F0000")] // duplicate component selector
    public void IncompleteOrMalformedScansAreRejected(string scan)
    {
        byte[] bytes = Convert.FromHexString("FFD8FFC00011080001000103011100021101031101" + scan + "FFD9");
        Assert.Throws<PdfImageException>(() => PdfImage.Load(bytes));
    }

    [Theory]
    [InlineData("sample.jpg", 0xC0)]
    [InlineData("sample-progressive.jpg", 0xC2)]
    public void ValidJpegRemainsByteExactPassthrough(string name, byte frameMarker)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "images", name));
        Assert.True(bytes.AsSpan().IndexOf(new byte[] { 255, frameMarker }) >= 0);
        var image = PdfImage.Load(bytes);
        Assert.Equal(320, image.Width); Assert.Equal(180, image.Height);
        using var writer = new PdfFileWriter(new MemoryStream());
        var stream = Assert.Single(image.CreateStreams(writer, writer.Reserve())).Stream;
        Assert.Equal(bytes, stream.Data.ToArray());
        Assert.False(stream.Compress);
        Assert.Contains("/DCTDecode", stream.Dictionary.ToPdfSyntax());
        Assert.Throws<PdfImageException>(() => PdfImage.Load(bytes.AsSpan(0, bytes.Length - 2)));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(65535)]
    public void MalformedSegmentLengthsAfterFrameAreRejected(int length)
    {
        byte[] bytes = Convert.FromHexString("FFD8FFC00011080001000103011100021101031101FFFE000200FFD9");
        bytes[23] = (byte)(length >> 8); bytes[24] = (byte)length;
        Assert.Throws<PdfImageException>(() => PdfImage.Load(bytes));
    }
}
