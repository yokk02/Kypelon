using System.Buffers.Binary;
using System.IO.Compression;
using Kypelon.Pdf.Core;

namespace Kypelon.Pdf.Graphics;

/// <summary>Reports malformed or unsupported image data.</summary>
public class PdfImageException(string message) : Exception(message);
/// <summary>Validated PNG pixels or structurally checked JPEG passthrough data (not entropy-decoded). Immutable and shareable.</summary>
public sealed class PdfImage
{
    private readonly byte[] pixels;
    private readonly byte[]? alpha;
    private readonly bool jpeg;
    private readonly bool gray;
    /// <summary>Pixel width.</summary>
    public int Width
    {
        get;
    }
    /// <summary>Pixel height.</summary>
    public int Height
    {
        get;
    }
    private PdfImage(int w, int h, byte[] pixels, byte[]? alpha, bool jpeg, bool gray = false)
    {
        Width = w;
        Height = h;
        this.pixels = pixels;
        this.alpha = alpha;
        this.jpeg = jpeg;
        this.gray = gray;
    }
    /// <summary>Loads JPEG or non-interlaced 8-bit RGB/RGBA PNG (maximum 32 MiB, 16 million pixels).</summary>
    public static PdfImage Load(string path)
    {
        using var f = File.OpenRead(path);
        if (f.Length > 32 * 1024 * 1024)
            throw new PdfImageException("Image exceeds 32 MiB.");
        var b = new byte[checked((int)f.Length)];
        f.ReadExactly(b);
        return Load(b);
    }
    /// <summary>Loads supported image bytes.</summary>
    public static PdfImage Load(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 8 or > 32 * 1024 * 1024)
            throw new PdfImageException("Image size must be between 8 bytes and 32 MiB.");
        try
        {
            if (bytes[0] == 255 && bytes[1] == 216)
                return Jpeg(bytes);
            if (bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                return Png(bytes);
            throw new PdfImageException("Unsupported image signature. Expected JPEG or PNG.");
        }
        catch (OverflowException) { throw new PdfImageException("Image dimensions or chunk lengths overflow."); }
        catch (InvalidDataException e) { throw new PdfImageException("Invalid PNG compression: " + e.Message); }
        catch (EndOfStreamException) { throw new PdfImageException("Truncated PNG pixels."); }
    }
    private static void Dimensions(int w, int h)
    {
        if (w <= 0 || h <= 0 || w > 32768 || h > 32768 || (long)w * h > 16_000_000)
            throw new PdfImageException("Image dimensions exceed 32768 per side or 16 million pixels.");
    }
    // Structural passthrough validation (ITU-T T.81 B.2): bounded marker lengths,
    // supported frame and scan headers, component selectors, nonempty entropy
    // segments and terminal EOI. Does not decode coefficients or validate Huffman
    // tables, restart intervals, or progressive scan completeness.
    private static PdfImage Jpeg(ReadOnlySpan<byte> b)
    {
        int p = 2, w = 0, h = 0, components = 0, frame = 0, scans = 0;
        Span<byte> componentIds = stackalloc byte[3];
        while (p < b.Length)
        {
            if (b[p++] != 255) throw new PdfImageException("Invalid JPEG marker.");
            while (p < b.Length && b[p] == 255) p++;
            if (p >= b.Length) break;
            byte marker = b[p++];
            if (marker == 0xD9)
            {
                if (frame == 0 || scans == 0) throw new PdfImageException("JPEG requires a supported frame and scan before EOI.");
                if (p != b.Length) throw new PdfImageException("Unexpected bytes after JPEG EOI.");
                return new(w, h, b.ToArray(), null, true, components == 1);
            }
            if (marker is 0 or 0xD8 or 1 or >= 0xD0 and <= 0xD7)
                throw new PdfImageException("Unexpected standalone JPEG marker outside a scan.");
            if (p > b.Length - 2) throw new PdfImageException("Truncated JPEG segment length.");
            int len = BinaryPrimitives.ReadUInt16BigEndian(b[p..]);
            if (len < 2 || p > b.Length - len) throw new PdfImageException("Truncated JPEG segment.");
            if (marker is 0xC0 or 0xC1 or 0xC2)
            {
                if (frame != 0) throw new PdfImageException("Multiple JPEG frames are unsupported.");
                if (len < 8 || b[p + 2] != 8) throw new PdfImageException("Only 8-bit JPEG precision is supported.");
                h = BinaryPrimitives.ReadUInt16BigEndian(b[(p + 3)..]);
                w = BinaryPrimitives.ReadUInt16BigEndian(b[(p + 5)..]);
                components = b[p + 7];
                if (components is not (1 or 3) || len != 8 + 3 * components)
                    throw new PdfImageException("Only grayscale or three-component JPEG is supported; CMYK is not supported.");
                Dimensions(w, h);
                for (int i = 0; i < components; i++)
                {
                    int at = p + 8 + 3 * i;
                    byte id = b[at], sampling = b[at + 1];
                    if (componentIds[..i].Contains(id) || (sampling >> 4) is < 1 or > 4 || (sampling & 15) is < 1 or > 4 || b[at + 2] > 3)
                        throw new PdfImageException("Invalid JPEG frame component specification.");
                    componentIds[i] = id;
                }
                frame = marker;
            }
            else if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
                throw new PdfImageException("Unsupported JPEG frame encoding.");
            else if (marker == 0xDA)
            {
                if (frame == 0 || len < 6) throw new PdfImageException("JPEG scan requires a complete frame and scan header.");
                int count = b[p + 2];
                if (count < 1 || count > components || len != 6 + 2 * count)
                    throw new PdfImageException("Invalid JPEG scan header length or component count.");
                int selected = 0;
                for (int i = 0; i < count; i++)
                {
                    int at = p + 3 + i * 2;
                    int index = componentIds[..components].IndexOf(b[at]);
                    byte tables = b[at + 1];
                    if (index < 0 || (selected & (1 << index)) != 0 || (tables >> 4) > 3 || (tables & 15) > 3)
                        throw new PdfImageException("Invalid JPEG scan component selector or entropy table destination.");
                    selected |= 1 << index;
                }
                int ss = b[p + len - 3], se = b[p + len - 2], ah = b[p + len - 1] >> 4, al = b[p + len - 1] & 15;
                bool valid = frame == 0xC2
                    ? ss <= se && se <= 63 && (ss != 0 || se == 0) && (ss == 0 || count == 1) && ah <= 13 && al <= 13 && (ah == 0 || ah == al + 1)
                    : ss == 0 && se == 63 && ah == 0 && al == 0;
                if (!valid) throw new PdfImageException("Invalid JPEG scan spectral or approximation parameters.");
                p += len;
                bool entropy = false;
                while (p < b.Length)
                {
                    if (b[p] != 255) { entropy = true; p++; continue; }
                    int markerStart = p++;
                    while (p < b.Length && b[p] == 255) p++;
                    if (p >= b.Length) throw new PdfImageException("Truncated JPEG entropy marker.");
                    if (b[p] == 0) { entropy = true; p++; continue; } // stuffed FF data byte
                    if (b[p] is >= 0xD0 and <= 0xD7) { p++; continue; } // restart marker
                    p = markerStart;
                    break;
                }
                if (!entropy) throw new PdfImageException("JPEG scan has no entropy data.");
                scans++;
                continue;
            }
            p += len;
        }
        throw new PdfImageException("JPEG is missing its end marker or complete scan.");
    }
    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0);
        }
        return ~crc;
    }
    private static PdfImage Png(ReadOnlySpan<byte> b)
    {
        int p = 8, w = 0, h = 0, channels = 0;
        bool ended = false, seenData = false, dataEnded = false;
        using var compressed = new MemoryStream();
        while (p < b.Length)
        {
            if (p > b.Length - 12)
                throw new PdfImageException("Truncated PNG chunk.");
            int len = checked((int)BinaryPrimitives.ReadUInt32BigEndian(b[p..]));
            if (len < 0 || len > b.Length - p - 12)
                throw new PdfImageException("Invalid PNG chunk length.");
            var type = b.Slice(p + 4, 4);
            var data = b.Slice(p + 8, len);
            if (Crc(b.Slice(p + 4, len + 4)) != BinaryPrimitives.ReadUInt32BigEndian(b[(p + 8 + len)..]))
                throw new PdfImageException("PNG chunk CRC mismatch.");
            if (p == 8 && !type.SequenceEqual("IHDR"u8))
                throw new PdfImageException("PNG must begin with IHDR.");
            if (type.SequenceEqual("IHDR"u8))
            {
                if (p != 8 || len != 13)
                    throw new PdfImageException("Invalid PNG IHDR.");
                w = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data));
                h = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]));
                Dimensions(w, h);
                if (data[8] != 8 || data[9] is not (2 or 6) || data[10] != 0 || data[11] != 0 || data[12] != 0)
                    throw new PdfImageException("Only non-interlaced 8-bit RGB and RGBA PNG are supported.");
                channels = data[9] == 6 ? 4 : 3;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (dataEnded)
                    throw new PdfImageException("PNG IDAT chunks must be consecutive.");
                seenData = true;
                compressed.Write(data);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                if (len != 0 || !seenData)
                    throw new PdfImageException("Invalid PNG IEND.");
                ended = true;
                p += 12;
                break;
            }
            else
            {
                if (seenData)
                    dataEnded = true;
                if (type.SequenceEqual("tRNS"u8) || type.SequenceEqual("acTL"u8))
                    throw new PdfImageException("PNG tRNS transparency and animation are unsupported; use RGBA.");
                if ((type[0] & 32) == 0 && !type.SequenceEqual("PLTE"u8))
                    throw new PdfImageException("Unsupported critical PNG chunk.");
            }
            p += len + 12;
        }
        if (!ended || p != b.Length)
            throw new PdfImageException("PNG IEND is missing or trailing bytes exist.");
        int stride = checked(w * channels);
        byte[] current = new byte[stride], previous = new byte[stride], rgb = new byte[checked(w * h * 3)];
        byte[]? alpha = channels == 4 ? new byte[w * h] : null;
        compressed.Position = 0;
        using var z = new ZLibStream(compressed, CompressionMode.Decompress);
        for (int y = 0; y < h; y++)
        {
            int filter = z.ReadByte();
            if (filter is < 0 or > 4)
                throw new PdfImageException("Invalid PNG scanline filter.");
            z.ReadExactly(current);
            for (int x = 0; x < stride; x++)
            {
                int left = x >= channels ? current[x - channels] : 0, up = previous[x], ul = x >= channels ? previous[x - channels] : 0;
                int predict = filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, ul),
                    _ => 0
                };
                current[x] = (byte)(current[x] + predict);
            }
            for (int x = 0; x < w; x++)
            {
                int src = x * channels, dst = (y * w + x) * 3;
                rgb[dst] = current[src];
                rgb[dst + 1] = current[src + 1];
                rgb[dst + 2] = current[src + 2];
                if (alpha is not null)
                    alpha[y * w + x] = current[src + 3];
            }
            (current, previous) = (previous, current);
        }
        if (z.ReadByte() != -1)
            throw new PdfImageException("PNG contains more pixels than declared.");
        return new(w, h, rgb, alpha, false);
    }
    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
    /// <summary>Creates image streams, reserving a soft-mask object when needed.</summary>
    public IReadOnlyList<(PdfIndirectReference Reference, PdfStream Stream)> CreateStreams(PdfFileWriter writer, PdfIndirectReference reference, bool compress = true)
    {
        var d = new PdfDictionary().Add("Type", new PdfName("XObject")).Add("Subtype", new PdfName("Image")).Add("Width", new PdfInteger(Width)).Add("Height", new PdfInteger(Height)).Add("BitsPerComponent", new PdfInteger(8)).Add("ColorSpace", new PdfName(gray ? "DeviceGray" : "DeviceRGB"));
        var result = new List<(PdfIndirectReference, PdfStream)>();
        if (alpha is not null)
        {
            var mask = writer.Reserve();
            d.Add("SMask", mask);
            result.Add((mask, new PdfStream(new PdfDictionary().Add("Type", new PdfName("XObject")).Add("Subtype", new PdfName("Image")).Add("Width", new PdfInteger(Width)).Add("Height", new PdfInteger(Height)).Add("BitsPerComponent", new PdfInteger(8)).Add("ColorSpace", new PdfName("DeviceGray")), alpha, compress)));
        }
        if (jpeg)
            d.Add("Filter", new PdfName("DCTDecode"));
        result.Add((reference, new PdfStream(d, pixels, !jpeg && compress)));
        return result;
    }
}
