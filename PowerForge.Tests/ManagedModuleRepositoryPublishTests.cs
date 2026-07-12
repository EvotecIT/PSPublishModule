using System.Net;
using System.Net.Http;
using System.Text;

namespace PowerForge.Tests;

public sealed class ManagedModuleRepositoryPublishTests
{
    [Fact]
    public async Task PublishPackageAsync_reports_repository_reason_and_redacts_api_key()
    {
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "Company.Tools.1.0.0.nupkg");
        File.WriteAllBytes(packagePath, TestPackageFactory.CreateBytes("Company.Tools", "1.0.0"));
        using var client = new HttpClient(new PublishFailureHandler("publish-key"));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("PSGallery", ManagedModuleCatalogDefaults.PowerShellGalleryV2);

        var exception = await Assert.ThrowsAsync<ManagedModuleRepositoryException>(() =>
            repositoryClient.PublishPackageAsync(
                repository,
                packagePath,
                new RepositoryCredential { Secret = "publish-key" }));

        Assert.Equal(400, exception.StatusCode);
        Assert.Contains("client version '4.1.0'", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[REDACTED]", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("publish-key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishPackageAsync_redacts_reflected_basic_authorization_value()
    {
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "Company.Tools.1.0.0.nupkg");
        File.WriteAllBytes(packagePath, TestPackageFactory.CreateBytes("Company.Tools", "1.0.0"));
        using var client = new HttpClient(new ReflectedAuthorizationFailureHandler());
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("PrivateGallery", "https://packages.example.test/api/v2");
        var credential = new RepositoryCredential { UserName = "publish-user", Secret = "publish-secret" };
        var encodedCredential = Convert.ToBase64String(Encoding.ASCII.GetBytes("publish-user:publish-secret"));

        var exception = await Assert.ThrowsAsync<ManagedModuleRepositoryException>(() =>
            repositoryClient.PublishPackageAsync(repository, packagePath, credential));

        Assert.Contains("Basic [REDACTED]", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("publish-secret", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(encodedCredential, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishPackageAsync_bounds_repository_error_body_reads()
    {
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "Company.Tools.1.0.0.nupkg");
        File.WriteAllBytes(packagePath, TestPackageFactory.CreateBytes("Company.Tools", "1.0.0"));
        var responseStream = new CountingReadStream(Encoding.UTF8.GetBytes(new string('x', 1024 * 1024)));
        using var client = new HttpClient(new StreamFailureHandler(responseStream));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("PrivateGallery", "https://packages.example.test/api/v2");

        var exception = await Assert.ThrowsAsync<ManagedModuleRepositoryException>(() =>
            repositoryClient.PublishPackageAsync(repository, packagePath));

        Assert.InRange(responseStream.BytesRead, 1, 8192);
        Assert.Contains("Repository response:", exception.Message, StringComparison.Ordinal);
        Assert.True(exception.Message.Length < 4096, "Repository diagnostics should remain bounded.");
    }

    [Fact]
    public async Task PublishPackageAsync_passes_cancellation_to_repository_error_body_read()
    {
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "Company.Tools.1.0.0.nupkg");
        File.WriteAllBytes(packagePath, TestPackageFactory.CreateBytes("Company.Tools", "1.0.0"));
        var responseStream = new CancellationObservingStream();
        using var client = new HttpClient(new StreamFailureHandler(responseStream));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("PrivateGallery", "https://packages.example.test/api/v2");
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAsync<ManagedModuleRepositoryException>(() =>
            repositoryClient.PublishPackageAsync(repository, packagePath, cancellationToken: cancellation.Token));

        Assert.True(responseStream.SawCancelableToken);
    }

    private sealed class PublishFailureHandler : HttpMessageHandler
    {
        private readonly string _apiKey;

        public PublishFailureHandler(string apiKey)
        {
            _apiKey = apiKey;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                ReasonPhrase = "A client version '4.1.0' or higher is required.",
                Content = new StringContent($"Rejected credential {_apiKey}")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class ReflectedAuthorizationFailureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var reflectedAuthorization = request.Headers.Authorization?.ToString() ?? string.Empty;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent($"Rejected request. Authorization: {reflectedAuthorization}")
            });
        }
    }

    private sealed class StreamFailureHandler : HttpMessageHandler
    {
        private readonly Stream _stream;

        public StreamFailureHandler(Stream stream)
        {
            _stream = stream;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StreamContent(_stream)
            });
    }

    private sealed class CountingReadStream : MemoryStream
    {
        public CountingReadStream(byte[] buffer)
            : base(buffer)
        {
        }

        public int BytesRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }
    }

    private sealed class CancellationObservingStream : Stream
    {
        public bool SawCancelableToken { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => 0;

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            SawCancelableToken = cancellationToken.CanBeCanceled;
            return Task.FromResult(0);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
