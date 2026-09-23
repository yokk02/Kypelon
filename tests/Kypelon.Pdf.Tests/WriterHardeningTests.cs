using Kypelon.Pdf.Core;
using Xunit;

namespace Kypelon.Pdf.Tests;

public class WriterHardeningTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidStreamDictionaryWritesNothingAndReservationCanBeRetried(bool async)
    {
        using var sink = new MemoryStream(); using var writer = new PdfFileWriter(sink);
        var root = writer.Reserve();
        var bad = new PdfStream(new PdfDictionary().Add("bad\0key", PdfNull.Value), new byte[] { 1 }, false);
        if (async) await Assert.ThrowsAsync<PdfWriteException>(() => writer.WriteAsync(root, bad).AsTask());
        else Assert.Throws<PdfWriteException>(() => writer.Write(root, bad));
        Assert.Equal(0, sink.Length);
        await Assert.ThrowsAsync<PdfWriteException>(() => writer.FinishAsync(root).AsTask());
        Assert.Equal(0, sink.Length);
        writer.Write(root, PdfNull.Value); writer.Finish(root);
        Inspect.Objects(sink.ToArray());
    }

    public static IEnumerable<object[]> WriteFailures()
    {
        foreach (bool async in new[] { false, true })
            foreach (bool stream in new[] { false, true })
                foreach (int call in new[] { 1, 2, 3 }) yield return [async, stream, call];
    }
    [Theory]
    [MemberData(nameof(WriteFailures))]
    public async Task PartialPrefixBodyOrTerminatorFaultsWriter(bool async, bool stream, int call)
    {
        using var sink = new FailingStream { FailAt = call };
        using var writer = new PdfFileWriter(sink); var root = writer.Reserve();
        var value = new PdfStream(new(), new byte[] { 1, 2, 3 }, false);
        Exception error;
        if (async) error = await Assert.ThrowsAsync<IOException>(() => stream ? writer.WriteAsync(root, value).AsTask() : writer.WriteAsync(root, new PdfString("body")).AsTask());
        else error = Assert.Throws<IOException>(() => { if (stream) writer.Write(root, value); else writer.Write(root, new PdfString("body")); });
        Assert.Same(sink.Failure, error);
        Assert.True(sink.Bytes.Length > 0);
        await AssertTerminal(writer, root);
        Assert.Equal(call, sink.Writes);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task XrefOrFlushFailureFaultsWriter(bool async, bool flush)
    {
        using var sink = new FailingStream(); using var writer = new PdfFileWriter(sink);
        var root = writer.Reserve(); writer.Write(root, PdfNull.Value);
        if (flush) sink.FailFlush = true; else sink.FailAt = sink.Writes + 1;
        if (async) Assert.Same(sink.Failure, await Assert.ThrowsAsync<IOException>(() => writer.FinishAsync(root).AsTask()));
        else Assert.Same(sink.Failure, Assert.Throws<IOException>(() => writer.Finish(root)));
        await AssertTerminal(writer, root);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringAsyncObjectWriteIsTerminal(bool stream)
    {
        using var cancellation = new CancellationTokenSource();
        using var sink = new FailingStream { CancelAt = 2, Cancellation = cancellation };
        using var writer = new PdfFileWriter(sink); var root = writer.Reserve();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream
            ? writer.WriteAsync(root, new PdfStream(new(), new byte[] { 1, 2, 3 }, false), cancellation.Token).AsTask()
            : writer.WriteAsync(root, new PdfString("body"), cancellation.Token).AsTask());
        Assert.Equal(2, sink.Writes);
        await AssertTerminal(writer, root);
        Assert.Equal(2, sink.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposeDoesNotReplaceOriginalWriteFailure(bool async)
    {
        var sink = new FailingStream { FailAt = 1, FailDispose = true };
        if (async)
        {
            var error = await Assert.ThrowsAsync<IOException>(async () =>
            {
                await using var writer = new PdfFileWriter(sink, false);
                await writer.WriteAsync(writer.Reserve(), PdfNull.Value);
            });
            Assert.Same(sink.Failure, error);
        }
        else
        {
            var error = Assert.Throws<IOException>(() =>
            {
                using var writer = new PdfFileWriter(sink, false);
                writer.Write(writer.Reserve(), PdfNull.Value);
            });
            Assert.Same(sink.Failure, error);
        }
        Assert.True(sink.WasDisposed);
    }

    [Fact]
    public async Task PreCancelledObjectWriteLeavesWriterActive()
    {
        using var sink = new MemoryStream(); using var writer = new PdfFileWriter(sink);
        var root = writer.Reserve();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.WriteAsync(root, PdfNull.Value, new(true)).AsTask());
        Assert.Empty(sink.ToArray());
        writer.Write(root, PdfNull.Value); writer.Finish(root); Inspect.Objects(sink.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameWriterForwardReferencesAndTrailerRemainValid(bool async)
    {
        using var sink = new MemoryStream(); using var writer = new PdfFileWriter(sink);
        var root = writer.Reserve(); var child = writer.Reserve(); var info = writer.Reserve();
        var graph = new PdfDictionary().Add("Child", new PdfArray(new PdfDictionary().Add("Ref", child)));
        if (async) await writer.WriteAsync(root, graph); else writer.Write(root, graph);
        writer.Write(child, new PdfStream(new PdfDictionary().Add("Owner", root), new byte[] { 42 }, false));
        writer.Write(info, new PdfDictionary().Add("Title", new PdfString("test")));
        if (async) await writer.FinishAsync(root, info); else writer.Finish(root, info);
        Assert.Equal(3, Inspect.Objects(sink.ToArray()).Count);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(0, true)]
    [InlineData(1, false)] [InlineData(1, true)]
    [InlineData(2, false)] [InlineData(2, true)]
    [InlineData(3, false)] [InlineData(3, true)]
    public async Task UnownedNestedReferencesFailBeforePrefix(int kind, bool async)
    {
        using var sink = new MemoryStream(); using var writer = new PdfFileWriter(sink);
        using var other = new PdfFileWriter(new MemoryStream());
        var root = writer.Reserve();
        var reference = kind == 0 ? new PdfIndirectReference(new(999)) : kind == 3 ? new PdfIndirectReference(root.Id) : other.Reserve();
        var graph = new PdfDictionary().Add("Child", new PdfArray(new PdfDictionary().Add("Ref", reference)));
        if (async) await Assert.ThrowsAsync<PdfWriteException>(() => kind == 2 ? writer.WriteAsync(root, new PdfStream(graph, Array.Empty<byte>(), false)).AsTask() : writer.WriteAsync(root, graph).AsTask());
        else Assert.Throws<PdfWriteException>(() => { if (kind == 2) writer.Write(root, new PdfStream(graph, Array.Empty<byte>(), false)); else writer.Write(root, graph); });
        Assert.Empty(sink.ToArray());
        writer.Write(root, PdfNull.Value); writer.Finish(root);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CyclicContainersFailWithoutEmittingAndSharedAcyclicContainersWork(bool stream)
    {
        using var sink = new MemoryStream(); using var writer = new PdfFileWriter(sink); var root = writer.Reserve();
        var dictionary = new PdfDictionary(); var array = new PdfArray(dictionary); dictionary.Add("Cycle", array);
        if (stream) await Assert.ThrowsAsync<PdfWriteException>(() => writer.WriteAsync(root, new PdfStream(dictionary, Array.Empty<byte>())).AsTask());
        else Assert.Throws<PdfWriteException>(() => writer.Write(root, array));
        Assert.Empty(sink.ToArray());
        var shared = new PdfDictionary().Add("Value", new PdfInteger(1));
        writer.Write(root, new PdfArray(shared, shared)); writer.Finish(root); Inspect.Objects(sink.ToArray());
    }

    [Theory]
    [InlineData(64, true)] [InlineData(65, false)]
    public void ContainerDepthMatchesSerializationPolicy(int depth, bool allowed)
    {
        using var sink = new MemoryStream(); using var writer = new PdfFileWriter(sink); var root = writer.Reserve();
        PdfObject value = new PdfArray(PdfNull.Value);
        for (int i = 0; i < depth; i++) value = new PdfArray(value);
        if (allowed) { writer.Write(root, value); writer.Finish(root); Inspect.Objects(sink.ToArray()); }
        else { Assert.Throws<PdfWriteException>(() => writer.Write(root, value)); Assert.Empty(sink.ToArray()); }
    }

    private static async Task AssertTerminal(PdfFileWriter writer, PdfIndirectReference root)
    {
        Assert.Contains("fault", Assert.Throws<PdfWriteException>(() => writer.Reserve()).Message, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<PdfWriteException>(() => writer.Write(root, PdfNull.Value));
        Assert.Throws<PdfWriteException>(() => writer.Write(root, new PdfStream(new(), Array.Empty<byte>())));
        await Assert.ThrowsAsync<PdfWriteException>(() => writer.WriteAsync(root, PdfNull.Value).AsTask());
        await Assert.ThrowsAsync<PdfWriteException>(() => writer.WriteAsync(root, new PdfStream(new(), Array.Empty<byte>())).AsTask());
        Assert.Throws<PdfWriteException>(() => writer.Finish(root));
        await Assert.ThrowsAsync<PdfWriteException>(() => writer.FinishAsync(root).AsTask());
    }

    private sealed class FailingStream : Stream
    {
        private readonly MemoryStream sink = new();
        public IOException Failure { get; } = new("Injected partial output failure");
        public int FailAt { get; set; } = -1;
        public int CancelAt { get; set; } = -1;
        public CancellationTokenSource? Cancellation { get; set; }
        public bool FailFlush { get; set; }
        public bool FailDispose { get; set; }
        public bool WasDisposed { get; private set; }
        public int Writes { get; private set; }
        public byte[] Bytes => sink.ToArray();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        private void Emit(ReadOnlySpan<byte> bytes)
        {
            Writes++;
            if (Writes == FailAt || Writes == CancelAt)
            {
                sink.Write(bytes[..Math.Min(2, bytes.Length)]);
                if (Writes == CancelAt) { Cancellation!.Cancel(); throw new OperationCanceledException(Cancellation.Token); }
                throw Failure;
            }
            sink.Write(bytes);
        }
        public override void Write(ReadOnlySpan<byte> bytes) => Emit(bytes);
        public override void Write(byte[] bytes, int offset, int count) => Emit(bytes.AsSpan(offset, count));
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); Emit(bytes.Span); return ValueTask.CompletedTask;
        }
        public override void Flush() { if (FailFlush) throw Failure; }
        public override Task FlushAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); Flush(); return Task.CompletedTask; }
        protected override void Dispose(bool disposing) { WasDisposed = true; sink.Dispose(); if (FailDispose) throw new InvalidOperationException("Secondary dispose failure"); base.Dispose(disposing); }
        public override ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
