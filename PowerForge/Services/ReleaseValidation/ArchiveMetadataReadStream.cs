namespace PowerForge;

/// <summary>Bounds actual archive metadata IO before framework ZIP inventory materialization.</summary>
/// <remarks>ZIP interpretation remains owned by System.IO.Compression. Payload reads can lift the
/// metadata budget after the inventory has passed its entry-count and path checks.</remarks>
internal sealed class ArchiveMetadataReadStream : Stream
{
    internal const long MaximumMetadataBytes = 16 * 1024 * 1024;
    private readonly Stream _inner;
    private readonly CancellationToken _cancellationToken;
    private long _remaining = MaximumMetadataBytes;
    private bool _metadataComplete;

    internal ArchiveMetadataReadStream(Stream inner, CancellationToken cancellationToken = default)
    {
        _inner = inner;
        _cancellationToken = cancellationToken;
    }

    internal void CompleteMetadataInspection() => _metadataComplete = true;
    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;
    public override long Position { get => _inner.Position; set { _cancellationToken.ThrowIfCancellationRequested(); _inner.Position = value; } }

    private int LimitReadCount(int count)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        // Permit one extra byte to distinguish a short read/EOF from an actual overflow.
        return _metadataComplete ? count : (int)Math.Min(count, Math.Max(0, _remaining) + 1);
    }

    private void RecordRead(int read)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (!_metadataComplete) _remaining -= read;
        if (!_metadataComplete && _remaining < 0)
            throw new InvalidDataException($"Archive metadata exceeds the {MaximumMetadataBytes} byte read limit.");
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (offset < 0 || count < 0 || offset > buffer.Length - count) throw new ArgumentOutOfRangeException(nameof(count));
        var read = _inner.Read(buffer, offset, LimitReadCount(count));
        RecordRead(read);
        return read;
    }

#if !NET472 && !NETSTANDARD2_0
    public override int Read(Span<byte> buffer)
    {
        var read = _inner.Read(buffer.Slice(0, LimitReadCount(buffer.Length)));
        RecordRead(read);
        return read;
    }
#endif

    public override int ReadByte()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var value = _inner.ReadByte();
        RecordRead(value >= 0 ? 1 : 0);
        return value;
    }
    public override long Seek(long offset, SeekOrigin origin) { _cancellationToken.ThrowIfCancellationRequested(); return _inner.Seek(offset, origin); }
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
