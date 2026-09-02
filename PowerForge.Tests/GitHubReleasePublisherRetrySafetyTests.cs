using PowerForge;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PowerForge.Tests;

public sealed class GitHubReleasePublisherRetrySafetyTests
{
    [Fact]
    public async Task PublishRelease_HonorsRetryAfterDuringPostUploadVerification()
    {
        using var listener = new HttpListener();
        var port = GetAvailablePort();
        var apiBaseUrl = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(apiBaseUrl);
        listener.Start();
        var assetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllTextAsync(assetPath, "retry-after-verification");
        var assetName = Path.GetFileName(assetPath);
        var delays = new List<TimeSpan>();

        async Task<HttpListenerContext> NextRequest()
            => await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(10));

        static async Task Respond(
            HttpListenerContext context,
            string json,
            int statusCode = 200,
            string? retryAfter = null)
        {
            await context.Request.InputStream.CopyToAsync(Stream.Null);
            var responseBytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            if (!string.IsNullOrWhiteSpace(retryAfter))
                context.Response.Headers["Retry-After"] = retryAfter;
            context.Response.ContentLength64 = responseBytes.Length;
            await context.Response.OutputStream.WriteAsync(responseBytes);
            context.Response.Close();
        }

        var server = Task.Run(async () =>
        {
            await Respond(
                await NextRequest(),
                $$"""{"id":42,"html_url":"{{apiBaseUrl}}release","upload_url":"{{apiBaseUrl}}uploads{?name,label}"}""",
                201);
            await Respond(await NextRequest(), $$"""{"id":99,"name":"{{assetName}}"}""", 201);
            await Respond(
                await NextRequest(),
                "{\"message\":\"rate limited\"}",
                429,
                retryAfter: "30");

            var uploadedAsset = $$"""[{"id":99,"name":"{{assetName}}","state":"uploaded"}]""";
            await Respond(await NextRequest(), uploadedAsset);
            await Respond(await NextRequest(), uploadedAsset);
        });

        try
        {
            var publisher = new GitHubReleasePublisher(
                new NullLogger(),
                (delay, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    delays.Add(delay);
                });
            var result = publisher.PublishRelease(
                new GitHubReleasePublishRequest
                {
                    Owner = "EvotecIT",
                    Repository = "example",
                    Token = "token",
                    ApiBaseUrl = apiBaseUrl,
                    TagName = "v1.2.3",
                    AssetFilePaths = [assetPath]
                });

            await server.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(result.Succeeded);
            Assert.Equal(TimeSpan.FromSeconds(30), Assert.Single(delays));
        }
        finally
        {
            listener.Stop();
            File.Delete(assetPath);
        }
    }

    [Fact]
    public async Task PublishRelease_RefusesReplacementStarterIdChangeAcrossReconciliationRetries()
    {
        using var listener = new HttpListener();
        var port = GetAvailablePort();
        var apiBaseUrl = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(apiBaseUrl);
        listener.Start();
        var assetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllTextAsync(assetPath, "starter-id-race");
        var assetName = Path.GetFileName(assetPath);
        var requests = new List<string>();
        var delays = new List<TimeSpan>();

        async Task<HttpListenerContext> NextRequest()
        {
            var context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(10));
            requests.Add($"{context.Request.HttpMethod} {context.Request.Url!.AbsolutePath}");
            return context;
        }

        static async Task Respond(
            HttpListenerContext context,
            string json,
            int statusCode = 200,
            string? retryAfter = null)
        {
            await context.Request.InputStream.CopyToAsync(Stream.Null);
            var responseBytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            if (!string.IsNullOrWhiteSpace(retryAfter))
                context.Response.Headers["Retry-After"] = retryAfter;
            context.Response.ContentLength64 = responseBytes.Length;
            await context.Response.OutputStream.WriteAsync(responseBytes);
            context.Response.Close();
        }

        var server = Task.Run(async () =>
        {
            await Respond(
                await NextRequest(),
                $$"""{"id":42,"html_url":"{{apiBaseUrl}}release","upload_url":"{{apiBaseUrl}}uploads{?name,label}"}""",
                201);
            await Respond(await NextRequest(), "{\"message\":\"Bad Gateway\"}", 502);

            var originalStarter = $$"""[{"id":88,"name":"{{assetName}}","state":"starter"}]""";
            await Respond(await NextRequest(), originalStarter);
            await Respond(await NextRequest(), originalStarter);
            await Respond(
                await NextRequest(),
                "{\"message\":\"rate limited\"}",
                429,
                retryAfter: "30");

            var replacementStarter = $$"""[{"id":89,"name":"{{assetName}}","state":"starter"}]""";
            await Respond(await NextRequest(), replacementStarter);
        });

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                new GitHubReleasePublisher(
                    new NullLogger(),
                    (delay, cancellationToken) =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        delays.Add(delay);
                    })
                .PublishRelease(
                    new GitHubReleasePublishRequest
                    {
                        Owner = "EvotecIT",
                        Repository = "example",
                        Token = "token",
                        ApiBaseUrl = apiBaseUrl,
                        TagName = "v1.2.3",
                        AssetFilePaths = [assetPath]
                    }));

            await server.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains("changed from id 88 to 89", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(TimeSpan.FromSeconds(30), Assert.Single(delays));
            Assert.Single(requests, request => request == "DELETE /repos/EvotecIT/example/releases/assets/88");
            Assert.DoesNotContain(requests, request => request.EndsWith("/89", StringComparison.Ordinal));
        }
        finally
        {
            listener.Stop();
            File.Delete(assetPath);
        }
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
