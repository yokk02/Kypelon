using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Kypelon.Pdf.Core;

/// <summary>Forward-only PDF 1.7 serializer. Single-use and not thread-safe; supports non-seekable output.
/// Validation failures before output permit retry. Any destination write/flush failure faults the writer permanently; no rollback is attempted.</summary>
public sealed class PdfFileWriter : IDisposable, IAsyncDisposable
{
    private enum WriterState { Active, Finished, Faulted, Disposed }
    private readonly Stream output;
    private readonly bool leaveOpen;
    private readonly List<long> offsets = [-1];
    private readonly HashSet<PdfIndirectReference> reserved = new(ReferenceEqualityComparer.Instance);
    private long position;
    private bool started;
    private WriterState state;
    /// <summary>Creates a writer. The destination must be empty or positioned at zero.</summary>
    public PdfFileWriter(Stream output, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite) throw new ArgumentException("Destination is not writable.", nameof(output));
        if (output.CanSeek && (output.Position != 0 || output.Length != 0))
            throw new ArgumentException("Destination must be empty and positioned at zero.", nameof(output));
        this.output = output;
        this.leaveOpen = leaveOpen;
    }
    /// <summary>Reserves a generation-zero indirect reference, permitting forward references.</summary>
    public PdfIndirectReference Reserve()
    {
        EnsureActive();
        if (offsets.Count >= 10_000_000) throw new PdfWriteException("Object limit (10 million) exceeded.");
        var result = new PdfIndirectReference(new(offsets.Count));
        offsets.Add(-1);
        reserved.Add(result);
        return result;
    }
    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(state == WriterState.Disposed, this);
        if (state == WriterState.Faulted) throw new PdfWriteException("Writer is faulted after an output failure and cannot be reused.");
        if (state == WriterState.Finished) throw new PdfWriteException("Writer is already finished.");
    }
    private void ValidateTarget(PdfIndirectReference reference)
    {
        EnsureActive();
        if (!reserved.Contains(reference) || offsets[reference.Id.Number] != -1)
            throw new PdfWriteException("Object must be reserved by this writer and written exactly once.");
    }
    private void ValidateReferences(PdfObject value)
    {
        var path = new HashSet<PdfObject>(ReferenceEqualityComparer.Instance);
        Visit(value, 0);
        void Visit(PdfObject item, int depth)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (item is PdfIndirectReference reference)
            {
                if (!reserved.Contains(reference))
                    throw new PdfWriteException("Nested indirect references must be reserved by this writer; imported objects require reference remapping.");
                return;
            }
            if (item is not (PdfArray or PdfDictionary)) return;
            PdfObject.CheckDepth(depth);
            if (!path.Add(item)) throw new PdfWriteException("Cyclic direct PDF containers are not supported.");
            try
            {
                var children = item is PdfArray array ? (IEnumerable<PdfObject>)array.Items : ((PdfDictionary)item).Values;
                foreach (var child in children) Visit(child, depth + 1);
            }
            finally { path.Remove(item); }
        }
    }
    private static byte[] Bytes(string text) => Encoding.Latin1.GetBytes(text);
    private void Put(ReadOnlySpan<byte> data)
    {
        output.Write(data);
        position = checked(position + data.Length);
    }
    private async ValueTask PutAsync(ReadOnlyMemory<byte> data, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await output.WriteAsync(data, token).ConfigureAwait(false);
        position = checked(position + data.Length);
    }
    // Called only after all validation/serialization has completed. Offsets become
    // provisional until the entire write succeeds; failures make them unusable.
    private byte[] Prefix(PdfIndirectReference reference)
    {
        string header = started ? "" : "%PDF-1.7\n%\u00E2\u00E3\u00CF\u00D3\n";
        var bytes = Bytes(header + reference.Id.Number.ToString(CultureInfo.InvariantCulture) + " 0 obj\n");
        offsets[reference.Id.Number] = position + header.Length;
        started = true;
        return bytes;
    }
    /// <summary>Writes a reserved direct object, validating nested reference ownership before output.</summary>
    public void Write(PdfIndirectReference reference, PdfObject value)
    {
        ValidateTarget(reference);
        ValidateReferences(value);
        var bytes = Bytes(value.ToPdfSyntax());
        var prefix = Prefix(reference);
        try
        {
            Put(prefix); Put(bytes); Put("\nendobj\n"u8);
        }
        catch { state = WriterState.Faulted; throw; }
    }
    /// <summary>Writes a reserved direct object asynchronously.</summary>
    public async ValueTask WriteAsync(PdfIndirectReference reference, PdfObject value, CancellationToken token = default)
    {
        ValidateTarget(reference);
        token.ThrowIfCancellationRequested();
        ValidateReferences(value);
        var bytes = Bytes(value.ToPdfSyntax());
        token.ThrowIfCancellationRequested();
        var prefix = Prefix(reference);
        try
        {
            await PutAsync(prefix, token).ConfigureAwait(false);
            await PutAsync(bytes, token).ConfigureAwait(false);
            await PutAsync("\nendobj\n"u8.ToArray(), token).ConfigureAwait(false);
        }
        catch { state = WriterState.Faulted; throw; }
    }
    private static ReadOnlyMemory<byte> Encode(PdfStream value, out PdfDictionary dictionary)
    {
        dictionary = value.Dictionary.Copy();
        var data = value.Data;
        if (value.Compress && dictionary.ContainsKey("Filter"))
            throw new PdfWriteException("Automatic compression cannot replace a pre-existing stream filter. Pass Compress=false for encoded data.");
        if (value.Compress)
        {
            using var buffer = new MemoryStream();
            using (var z = new ZLibStream(buffer, CompressionLevel.Fastest, true)) z.Write(data.Span);
            data = buffer.ToArray();
            dictionary.Add("Filter", new PdfName("FlateDecode"));
        }
        dictionary.Add("Length", new PdfInteger(data.Length));
        return data;
    }
    /// <summary>Writes a stream, buffering only this stream when compression is requested.</summary>
    public void Write(PdfIndirectReference reference, PdfStream value)
    {
        ValidateTarget(reference);
        ValidateReferences(value.Dictionary);
        var data = Encode(value, out var dict);
        var syntax = Bytes(dict.ToPdfSyntax() + "\nstream\n");
        var prefix = Prefix(reference);
        try
        {
            Put(prefix); Put(syntax); Put(data.Span); Put("\nendstream\nendobj\n"u8);
        }
        catch { state = WriterState.Faulted; throw; }
    }
    /// <summary>Writes a stream through asynchronous destination I/O.</summary>
    public async ValueTask WriteAsync(PdfIndirectReference reference, PdfStream value, CancellationToken token = default)
    {
        ValidateTarget(reference);
        token.ThrowIfCancellationRequested();
        ValidateReferences(value.Dictionary);
        var data = Encode(value, out var dict);
        var syntax = Bytes(dict.ToPdfSyntax() + "\nstream\n");
        token.ThrowIfCancellationRequested();
        var prefix = Prefix(reference);
        try
        {
            await PutAsync(prefix, token).ConfigureAwait(false);
            await PutAsync(syntax, token).ConfigureAwait(false);
            await PutAsync(data, token).ConfigureAwait(false);
            await PutAsync("\nendstream\nendobj\n"u8.ToArray(), token).ConfigureAwait(false);
        }
        catch { state = WriterState.Faulted; throw; }
    }
    private byte[] FinishBytes(PdfIndirectReference root, PdfIndirectReference? info)
    {
        EnsureActive();
        if (!started || offsets.Skip(1).Any(x => x < 0))
            throw new PdfWriteException("All reserved objects must be written before finishing.");
        if (!reserved.Contains(root) || (info is not null && !reserved.Contains(info)))
            throw new PdfWriteException("Trailer references belong to another writer.");
        long xref = position;
        var s = new StringBuilder("xref\n0 ").Append(offsets.Count.ToString(CultureInfo.InvariantCulture)).Append("\n0000000000 65535 f \n");
        foreach (long offset in offsets.Skip(1))
        {
            if (offset > 9_999_999_999) throw new PdfWriteException("Classic xref offsets exceed ten digits.");
            s.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }
        var trailer = new PdfDictionary().Add("Size", new PdfInteger(offsets.Count)).Add("Root", root);
        if (info is not null) trailer.Add("Info", info);
        s.Append("trailer\n").Append(trailer.ToPdfSyntax()).Append("\nstartxref\n").Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");
        return Bytes(s.ToString());
    }
    /// <summary>Writes xref and trailer, then flushes. Failure makes this writer permanently faulted.</summary>
    public void Finish(PdfIndirectReference root, PdfIndirectReference? info = null)
    {
        var bytes = FinishBytes(root, info);
        try
        {
            Put(bytes); output.Flush(); state = WriterState.Finished;
        }
        catch { state = WriterState.Faulted; throw; }
    }
    /// <summary>Writes xref and trailer, then asynchronously flushes.</summary>
    public async ValueTask FinishAsync(PdfIndirectReference root, PdfIndirectReference? info = null, CancellationToken token = default)
    {
        EnsureActive();
        token.ThrowIfCancellationRequested();
        var bytes = FinishBytes(root, info);
        token.ThrowIfCancellationRequested();
        try
        {
            await PutAsync(bytes, token).ConfigureAwait(false);
            await output.FlushAsync(token).ConfigureAwait(false);
            state = WriterState.Finished;
        }
        catch { state = WriterState.Faulted; throw; }
    }
    /// <summary>Closes an owned destination. Secondary disposal errors are suppressed after an output fault to preserve the original failure.</summary>
    public void Dispose()
    {
        if (state == WriterState.Disposed) return;
        bool faulted = state == WriterState.Faulted;
        state = WriterState.Disposed;
        if (!leaveOpen)
            try { output.Dispose(); }
            catch when (faulted) { /* Preserve the original output failure. */ }
    }
    /// <summary>Asynchronously closes an owned destination, preserving any earlier output failure.</summary>
    public async ValueTask DisposeAsync()
    {
        if (state == WriterState.Disposed) return;
        bool faulted = state == WriterState.Faulted;
        state = WriterState.Disposed;
        if (!leaveOpen)
            try { await output.DisposeAsync().ConfigureAwait(false); }
            catch when (faulted) { /* Preserve the original output failure. */ }
    }
}
