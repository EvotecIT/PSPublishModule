using System.Net;
using System.Reflection;
using System.Text;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed partial class WebAgentContentSecurityScannerTests
{
    private static string CreateArtifact(string name, string content)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-agent-content-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, name), content);
        return root;
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage XmlResponse(string xml)
        => new(HttpStatusCode.OK) { Content = new StringContent(xml, Encoding.UTF8, "application/atom+xml") };

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // Best-effort test cleanup.
        }
    }

    private sealed class RegistryHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUri = request.RequestUri;
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class DelayedRegistryHandler(TimeSpan delay, HttpResponseMessage response) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(delay).ConfigureAwait(false);
            return response;
        }
    }

    private sealed class FailingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("simulated truncated response");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromException<int>(new IOException("simulated truncated response"));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

