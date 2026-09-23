using System.Text;
using Microsoft.AspNetCore.Http;
using Kypelon.Pdf.AspNetCore;
using Kypelon.Pdf.Core;
using Xunit;

namespace Kypelon.Pdf.Tests;

public class IntegrationTests
{
    private static PdfDocument Hello()
    {
        var d = Pdf.Document(b => b.Page(p => { p.Content(c => c.Text("Hello (world) \\ Kypelon.Pdf")); p.Footer(f => f.PageNumber()); }));
        d.Options.Deterministic = true;
        return d;
    }
    [Fact]
    public void RepeatedGenerationIsByteIdentical()
    {
        var doc = Hello();
        Assert.Equal(doc.ToArray(), doc.ToArray());
    }
    [Fact]
    public async Task SyncAndAsyncAreIdentical()
    {
        var doc = Hello();
        using var ms = new MemoryStream();
        await doc.WriteAsync(ms);
        Assert.Equal(doc.ToArray(), ms.ToArray());
    }
    [Fact]
    public void PageTreeTrailerAndMetadataArePresent()
    {
        var d = LayoutTests.Table(500);
        d.Metadata.Title = "Test report";
        var objects = Inspect.Objects(d.ToArray());
        Assert.Single(objects.Values, s => s.Contains("/Type /Catalog", StringComparison.Ordinal));
        Assert.Contains(objects.Values, s => s.Contains("/Title (Test report)", StringComparison.Ordinal));
        Assert.Contains(objects.Values, s => s.Contains($"/Count {d.Plan().Count}", StringComparison.Ordinal));
        Assert.Equal(d.Plan().Count, objects.Values.Count(s => s.Contains("/Type /Page\n", StringComparison.Ordinal)));
    }
    [Fact]
    public async Task AsyncUsesOnlyAsyncDestinationIoAndSupportsNonseekable()
    {
        var stream = new AsyncOnlyStream();
        await Hello().WriteAsync(stream);
        Assert.True(stream.AsyncWrites > 0);
        Assert.True(stream.Flushed);
        Assert.False(stream.Disposed);
        Inspect.Objects(stream.Bytes);
    }
    [Fact]
    public async Task AsyncLeaveOpenFalseDisposes()
    {
        var stream = new AsyncOnlyStream();
        await Hello().WriteAsync(stream, leaveOpen: false);
        Assert.True(stream.Disposed);
    }
    [Fact]
    public void SyncLeaveOpenFalseDisposes()
    {
        var stream = new MemoryStream();
        Hello().Write(stream, false);
        Assert.False(stream.CanWrite);
    }
    [Fact]
    public async Task PreCanceledWriteDoesNotTouchDestination()
    {
        var stream = new AsyncOnlyStream();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Hello().WriteAsync(stream, cts.Token));
        Assert.Empty(stream.Bytes);
    }
    [Fact]
    public async Task CancellationDuringIoStopsWriting()
    {
        using var cts = new CancellationTokenSource();
        var stream = new AsyncOnlyStream(() => cts.Cancel());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Hello().WriteAsync(stream, cts.Token));
        Assert.True(stream.Bytes.Length > 0);
        Assert.DoesNotContain("%%EOF", Encoding.ASCII.GetString(stream.Bytes));
    }
    [Fact]
    public void CancellationDuringPaginationStops()
    {
        using var cts = new CancellationTokenSource();
        var d = LayoutTests.Table(5000);
        d.Options.Diagnostic = _ => cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => d.Plan(cts.Token));
    }
    [Fact]
    public void CompressionChangesSizeWithoutChangingPageCount()
    {
        var d = LayoutTests.Table(100);
        d.Options.CompressStreams = false;
        var raw = d.ToArray();
        d.Options.CompressStreams = true;
        var compressed = d.ToArray();
        Assert.True(compressed.Length < raw.Length / 2);
        Assert.Equal(Inspect.Objects(raw).Count, Inspect.Objects(compressed).Count);
    }
    [Fact]
    public void DebugCommandsAreAbsentFromProductionStreams()
    {
        var d = Hello();
        d.Options.CompressStreams = false;
        var plain = d.ToArray();
        d.Options.DebugLayout = true;
        var debug = d.ToArray();
        Assert.True(debug.Length > plain.Length);
        Assert.DoesNotContain("0.87843137 0.29803922 0.65882353 RG", Encoding.ASCII.GetString(plain));
        Assert.Contains("0.87843137 0.29803922 0.65882353 RG", Encoding.ASCII.GetString(debug));
    }
    [Fact]
    public async Task AspNetResultStreamsPdfAndLeavesBodyOpen()
    {
        var context = new DefaultHttpContext();
        var stream = new AsyncOnlyStream();
        context.Response.Body = stream;
        await Hello().PdfFile("รายงาน.pdf").ExecuteAsync(context);
        Assert.Equal("application/pdf", context.Response.ContentType);
        Assert.Contains("filename*=UTF-8''", context.Response.Headers.ContentDisposition.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(stream.Disposed);
        Inspect.Objects(stream.Bytes);
    }
    [Fact]
    public async Task AspNetRejectsHeaderInjection()
    {
        var context = new DefaultHttpContext();
        await Assert.ThrowsAsync<ArgumentException>(() => Hello().PdfFile("bad\r\nX: y").ExecuteAsync(context));
    }
    [Fact]
    public void InvalidLayoutWritesNoPdfBytes()
    {
        var doc = Pdf.Document(d => d.Page(p => p.Content(c => c.Spacer(5000))));
        using var ms = new MemoryStream();
        Assert.Throws<Kypelon.Pdf.Layout.PdfLayoutException>(() => doc.Write(ms));
        Assert.Equal(0, ms.Length);
    }
}
internal sealed class AsyncOnlyStream(Action? onWrite = null) : Stream
{
    private readonly MemoryStream sink = new(); public byte[] Bytes => sink.ToArray(); public int AsyncWrites; public bool Disposed; public bool Flushed;
    public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => !Disposed;
    public override long Length => throw new NotSupportedException(); public override long Position
    {
        get => throw new NotSupportedException(); set => throw new NotSupportedException();
    }
    public override void Flush() => throw new InvalidOperationException("Sync Flush used");
    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Flushed = true;
        return Task.CompletedTask;
    }
    public override void Write(byte[] b, int o, int c) => throw new InvalidOperationException("Sync Write used");
    public override void Write(ReadOnlySpan<byte> b) => throw new InvalidOperationException("Sync Write used");
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> b, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        sink.Write(b.Span);
        AsyncWrites++;
        onWrite?.Invoke();
        return ValueTask.CompletedTask;
    }
    public override int Read(byte[] b, int o, int c) => throw new NotSupportedException(); public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException(); public override void SetLength(long v) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
    public override ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
