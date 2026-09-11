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
    public override long Position { get => _inner.Position; set { CheckRead(0); _inner.Position = value; } }

    private void CheckRead(int count)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (!_metadataComplete && count > _remaining)
            throw new InvalidDataException($"Archive metadata exceeds the {MaximumMetadataBytes} byte read limit.");
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        CheckRead(count);
        var read = _inner.Read(buffer, offset, count);
        if (!_metadataComplete) _remaining -= read;
        _cancellationToken.ThrowIfCancellationRequested();
        return read;
    }

#if !NET472 && !NETSTANDARD2_0
    public override int Read(Span<byte> buffer)
    {
        CheckRead(buffer.Length);
        var read = _inner.Read(buffer);
        if (!_metadataComplete) _remaining -= read;
        _cancellationToken.ThrowIfCancellationRequested();
        return read;
    }
#endif

    public override int ReadByte()
    {
        CheckRead(1);
        var value = _inner.ReadByte();
        if (!_metadataComplete && value >= 0) _remaining--;
        return value;
    }
    public override long Seek(long offset, SeekOrigin origin) { CheckRead(0); return _inner.Seek(offset, origin); }
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
