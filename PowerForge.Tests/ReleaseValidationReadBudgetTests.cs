namespace PowerForge.Tests;

public sealed class ReleaseValidationReadBudgetTests
{
    [Theory]
    [InlineData("array")]
    [InlineData("span")]
    [InlineData("byte")]
    public void Exact_budget_accepts_short_final_read_and_repeated_eof(string readKind)
    {
        var bytes = new byte[ArchiveMetadataReadStream.MaximumMetadataBytes];
        bytes[^1] = 73;
        using var source = new MemoryStream(bytes);
        using var stream = new ArchiveMetadataReadStream(source);
        Consume(stream, bytes.Length - 1);
        var buffer = new byte[32];

        Assert.Equal(readKind == "byte" ? 73 : 1, Read(stream, readKind, buffer));
        if (readKind != "byte") { Assert.Equal(73, buffer[0]); }
        Assert.Equal(bytes.Length, source.Position);
        Assert.Equal(readKind == "byte" ? -1 : 0, Read(stream, readKind, buffer));
        Assert.Equal(readKind == "byte" ? -1 : 0, Read(stream, readKind, buffer));
        Assert.Equal(bytes.Length, source.Position);
    }

    [Theory]
    [InlineData("array")]
    [InlineData("span")]
    [InlineData("byte")]
    public void Actual_byte_beyond_budget_is_rejected(string readKind)
    {
        using var source = new MemoryStream(new byte[ArchiveMetadataReadStream.MaximumMetadataBytes + 1]);
        using var stream = new ArchiveMetadataReadStream(source);
        Consume(stream, (int)ArchiveMetadataReadStream.MaximumMetadataBytes - 1);
        Assert.Equal(readKind == "byte" ? 0 : 1, Read(stream, readKind, new byte[1]));

        var error = Assert.Throws<InvalidDataException>(() => Read(stream, readKind, new byte[32]));

        Assert.Contains("metadata", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("limit", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("array")]
    [InlineData("span")]
    [InlineData("byte")]
    public void Completed_metadata_allows_payload_reads_but_preserves_cancellation(string readKind)
    {
        var bytes = new byte[ArchiveMetadataReadStream.MaximumMetadataBytes + 3];
        bytes[^3] = 73;
        using var cancellation = new CancellationTokenSource();
        using var source = new MemoryStream(bytes);
        using var stream = new ArchiveMetadataReadStream(source, cancellation.Token);
        Consume(stream, (int)ArchiveMetadataReadStream.MaximumMetadataBytes);
        stream.CompleteMetadataInspection();
        var buffer = new byte[32];

        Assert.Equal(readKind == "byte" ? 73 : 3, Read(stream, readKind, buffer));
        if (readKind != "byte") { Assert.Equal(73, buffer[0]); }
        Assert.Equal(ArchiveMetadataReadStream.MaximumMetadataBytes + (readKind == "byte" ? 1 : 3), source.Position);
        cancellation.Cancel();
        var position = source.Position;
        Assert.ThrowsAny<OperationCanceledException>(() => Read(stream, readKind, buffer));
        Assert.Equal(position, source.Position);
    }

    [Theory]
    [InlineData("array")]
    [InlineData("span")]
    [InlineData("byte")]
    public void Cancellation_raised_during_inner_read_is_observed_before_return(string readKind)
    {
        using var cancellation = new CancellationTokenSource();
        using var source = new CancelDuringReadStream(cancellation);
        using var stream = new ArchiveMetadataReadStream(source, cancellation.Token);

        Assert.ThrowsAny<OperationCanceledException>(() => Read(stream, readKind, new byte[1]));
        Assert.Equal(1, source.Position);
    }

    private static int Read(ArchiveMetadataReadStream stream, string readKind, byte[] buffer) => readKind switch
    {
        "array" => stream.Read(buffer, 0, buffer.Length),
        "span" => stream.Read(buffer.AsSpan()),
        "byte" => stream.ReadByte(),
        _ => throw new ArgumentOutOfRangeException(nameof(readKind))
    };

    private static void Consume(Stream stream, int count)
    {
        var buffer = new byte[65536];
        while (count > 0)
        {
            var read = stream.Read(buffer, 0, Math.Min(count, buffer.Length));
            Assert.True(read > 0, "The fixture must supply all bytes up to the budget boundary.");
            count -= read;
        }
    }

    private sealed class CancelDuringReadStream(CancellationTokenSource cancellation) : MemoryStream(new byte[] { 73 })
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            cancellation.Cancel();
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = base.Read(buffer);
            cancellation.Cancel();
            return read;
        }

        public override int ReadByte()
        {
            var value = base.ReadByte();
            cancellation.Cancel();
            return value;
        }
    }
}
