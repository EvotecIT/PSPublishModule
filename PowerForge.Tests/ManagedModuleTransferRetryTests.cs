using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace PowerForge.Tests;

public sealed class ManagedModuleTransferRetryTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Package_transfer_retries_body_disconnects_and_deadlines_with_fresh_destinations(bool buffered, bool stall)
    {
        using var root = new DownloadDirectory();
        var payload = Package();
        var calls = 0;
        var failedStream = new FailingBody(stall);
        using var http = new HttpClient(new Handler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = ++calls == 1 ? new StreamContent(failedStream) : new ByteArrayContent(payload)
        }));
        var client = Client(http);
        ManagedModuleDownloadResult result;
        if (buffered)
        {
            using var package = await client.DownloadPackageToMemoryAsync(Repository(), "Sample", "1.0.0").WaitAsync(TimeSpan.FromSeconds(10));
            result = package.Download;
            using var copy = new MemoryStream();
            await package.PackageStream.CopyToAsync(copy);
            Assert.Equal(payload, copy.ToArray());
        }
        else
        {
            result = await client.DownloadPackageAsync(Repository(), "Sample", "1.0.0", root.Path).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(payload, File.ReadAllBytes(result.PackagePath));
        }
        Assert.Equal(2, calls);
        Assert.True(failedStream.Disposed);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(payload)), result.PackageSha256, ignoreCase: true);
    }

    [Fact]
    public async Task Permanent_body_failure_removes_partial_files_and_stops_at_the_retry_limit()
    {
        using var root = new DownloadDirectory();
        var calls = 0;
        using var http = new HttpClient(new Handler(() =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new FailingBody(stall: false)) };
        }));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Client(http).DownloadPackageAsync(Repository(), "Sample", "1.0.0", root.Path));
        Assert.Equal(2, calls);
        Assert.Empty(Directory.EnumerateFiles(root.Path, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Caller_cancellation_aborts_the_body_without_another_attempt(bool noDeadline)
    {
        using var root = new DownloadDirectory();
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        using var http = new HttpClient(new Handler(() =>
        {
            calls++;
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(50));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new FailingBody(stall: true)) };
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Client(http, noDeadline).DownloadPackageAsync(Repository(), "Sample", "1.0.0", root.Path, cancellationToken: cancellation.Token));
        Assert.Equal(1, calls);
        Assert.Empty(Directory.EnumerateFiles(root.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Destination_io_failures_are_not_retried_or_reclassified_as_network_failures()
    {
        using var root = new DownloadDirectory();
        var repository = Repository();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(repository.Source.Trim())))[..16].ToLowerInvariant();
        var target = System.IO.Path.Combine(root.Path, key, "sample.1.0.0.nupkg");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
        FileStream? lockedDestination = null;
        var calls = 0;
        using var http = new HttpClient(new Handler(() =>
        {
            calls++;
            lockedDestination = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Package()) };
        }));
        try
        {
            await Assert.ThrowsAnyAsync<IOException>(() => Client(http).DownloadPackageAsync(repository, "Sample", "1.0.0", root.Path));
            Assert.Equal(1, calls);
        }
        finally { lockedDestination?.Dispose(); }
    }

    private static ManagedModuleRepository Repository()
        => new("Test", "https://example.test/packages", ManagedModuleRepositoryKind.NuGetV3);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ambiguous_publish_timeout_or_transport_failure_is_not_resubmitted(bool transport)
    {
        using var root = new DownloadDirectory();
        var path = Path.Combine(root.Path, "Sample.1.0.0.nupkg");
        File.WriteAllBytes(path, Package());
        var calls = 0;
        using var http = new HttpClient(new Handler(() =>
        {
            calls++;
            if (transport) throw new HttpRequestException("Response connection lost after upload");
            throw new OperationCanceledException("Response timed out after upload");
        }));
        var client = Client(http);
        var repository = new ManagedModuleRepository("Publish", "https://example.test/api/v2/package", ManagedModuleRepositoryKind.NuGetV2);
        var error = await Record.ExceptionAsync(() => client.PublishPackageAsync(repository, path, new RepositoryCredential { Secret = "test-only-key" }));
        Assert.NotNull(error);
        Assert.Equal(1, calls);
        if (transport) Assert.IsAssignableFrom<HttpRequestException>(error);
        else Assert.IsAssignableFrom<OperationCanceledException>(error);
    }

    private static ManagedModuleRepositoryClient Client(HttpClient http, bool noDeadline = false)
        => new(new NullLogger(), http, options: new ManagedModuleRepositoryClientOptions
        {
            MaxRetries = 1,
            RetryDelay = TimeSpan.Zero,
            RequestTimeout = noDeadline ? null : TimeSpan.FromSeconds(1),
            DisableManagedModuleCatalog = true
        });

    private static byte[] Package()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("Sample.nuspec").Open(), Encoding.UTF8);
            writer.Write("<package><metadata><id>Sample</id><version>1.0.0</version><authors>Test</authors><description>Test</description></metadata></package>");
        }
        return stream.ToArray();
    }

    private sealed class Handler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response());
    }

    // Models native HTTP streams whose pending read is released by disposal rather
    // than by honoring the ReadAsync cancellation argument.
    private sealed class FailingBody(bool stall) : Stream
    {
        private readonly TaskCompletionSource<int> _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _started;
        internal bool Disposed { get; private set; }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (!_started)
            {
                _started = true;
                buffer[offset] = 0x50;
                return Task.FromResult(1);
            }
            return stall ? _pending.Task : Task.FromException<int>(new IOException("Connection reset during package body"));
        }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            _pending.TrySetException(new ObjectDisposedException(nameof(FailingBody)));
            base.Dispose(disposing);
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class DownloadDirectory : IDisposable
    {
        internal string Path { get; } = Directory.CreateTempSubdirectory("pf-transfer-retry-").FullName;
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
