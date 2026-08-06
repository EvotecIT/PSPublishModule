using PowerForge;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PowerForge.Tests;

public sealed class GitHubReleasePublisherRetryTests
{
    [Fact]
    public async Task PublishRelease_RetriesTransientAssetFailureWithFreshContent()
    {
        var listener = new HttpListener();
        var port = GetAvailablePort();
        var apiBaseUrl = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(apiBaseUrl);
        listener.Start();
        var assetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        var expectedBytes = Encoding.UTF8.GetBytes("retry-safe-release-asset");
        await File.WriteAllBytesAsync(assetPath, expectedBytes);
        var receivedUploads = new List<byte[]>();

        async Task<HttpListenerContext> NextRequest()
            => await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(10));

        static async Task Respond(HttpListenerContext context, string json, int statusCode = 200)
        {
            var responseBytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = responseBytes.Length;
            await context.Response.OutputStream.WriteAsync(responseBytes);
            context.Response.Close();
        }

        var assetName = Path.GetFileName(assetPath);
        var server = Task.Run(async () =>
        {
            var create = await NextRequest();
            await Respond(
                create,
                $$"""{"id":42,"html_url":"{{apiBaseUrl}}release","upload_url":"{{apiBaseUrl}}uploads{?name,label}"}""",
                201);

            var firstUpload = await NextRequest();
            using (var stream = new MemoryStream())
            {
                await firstUpload.Request.InputStream.CopyToAsync(stream);
                receivedUploads.Add(stream.ToArray());
            }
            await Respond(firstUpload, "{\"message\":\"Bad Gateway\"}", 502);

            await Respond(await NextRequest(), "{\"message\":\"Service Unavailable\"}", 503);

            var secondUpload = await NextRequest();
            using (var stream = new MemoryStream())
            {
                await secondUpload.Request.InputStream.CopyToAsync(stream);
                receivedUploads.Add(stream.ToArray());
            }
            await Respond(secondUpload, $$"""{"id":99,"name":"{{assetName}}"}""", 201);

            var assets = $$"""[{"id":99,"name":"{{assetName}}"}]""";
            await Respond(await NextRequest(), assets);
            await Respond(await NextRequest(), assets);
        });

        try
        {
            var result = new GitHubReleasePublisher(new NullLogger()).PublishRelease(
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
            Assert.Equal(assetName, Assert.Single(result.UploadedAssets));
            Assert.Equal(2, receivedUploads.Count);
            Assert.All(receivedUploads, bytes => Assert.Equal(expectedBytes, bytes));
        }
        finally
        {
            listener.Stop();
            listener.Close();
            File.Delete(assetPath);
        }
    }

    [Fact]
    public async Task PublishRelease_RemovesExistingStarterAssetBeforeRetry()
    {
        var listener = new HttpListener();
        var port = GetAvailablePort();
        var apiBaseUrl = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(apiBaseUrl);
        listener.Start();
        var assetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllTextAsync(assetPath, "starter-recovery");
        var assetName = Path.GetFileName(assetPath);
        var requests = new List<string>();

        async Task<HttpListenerContext> NextRequest()
        {
            var context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(10));
            requests.Add($"{context.Request.HttpMethod} {context.Request.Url!.AbsolutePath}");
            return context;
        }

        static async Task Respond(HttpListenerContext context, string json, int statusCode = 200)
        {
            await context.Request.InputStream.CopyToAsync(Stream.Null);
            var responseBytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
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
            await Respond(
                await NextRequest(),
                "{\"message\":\"Validation Failed\",\"errors\":[{\"resource\":\"ReleaseAsset\",\"code\":\"already_exists\",\"field\":\"name\"}]}",
                422);

            var starterAsset = $$"""[{"id":88,"name":"{{assetName}}","state":"starter"}]""";
            await Respond(await NextRequest(), starterAsset);
            await Respond(await NextRequest(), starterAsset);
            await Respond(await NextRequest(), string.Empty, 204);

            await Respond(await NextRequest(), $$"""{"id":99,"name":"{{assetName}}"}""", 201);
            var uploadedAsset = $$"""[{"id":99,"name":"{{assetName}}","state":"uploaded"}]""";
            await Respond(await NextRequest(), uploadedAsset);
            await Respond(await NextRequest(), uploadedAsset);
        });

        try
        {
            var result = new GitHubReleasePublisher(new NullLogger()).PublishRelease(
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
            Assert.Equal(assetName, Assert.Single(result.UploadedAssets));
            Assert.Equal(
                [
                    "POST /repos/EvotecIT/example/releases",
                    "POST /uploads",
                    "GET /repos/EvotecIT/example/releases/42/assets",
                    "GET /repos/EvotecIT/example/releases/42/assets",
                    "DELETE /repos/EvotecIT/example/releases/assets/88",
                    "POST /uploads",
                    "GET /repos/EvotecIT/example/releases/42/assets",
                    "GET /repos/EvotecIT/example/releases/42/assets"
                ],
                requests);
        }
        finally
        {
            listener.Stop();
            listener.Close();
            File.Delete(assetPath);
        }
    }

    [Fact]
    public async Task PublishRelease_ReconcilesLateStarterAfterInterruptedUpload()
    {
        var listener = new HttpListener();
        var port = GetAvailablePort();
        var apiBaseUrl = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(apiBaseUrl);
        listener.Start();
        var assetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllTextAsync(assetPath, "late-starter-recovery");
        var assetName = Path.GetFileName(assetPath);

        async Task<HttpListenerContext> NextRequest()
            => await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(10));

        static async Task Respond(HttpListenerContext context, string json, int statusCode = 200)
        {
            await context.Request.InputStream.CopyToAsync(Stream.Null);
            var responseBytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
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
            await Respond(await NextRequest(), "[]");
            await Respond(
                await NextRequest(),
                "{\"message\":\"Validation Failed\",\"errors\":[{\"resource\":\"ReleaseAsset\",\"code\":\"already_exists\",\"field\":\"name\"}]}",
                422);

            var starterAsset = $$"""[{"id":88,"name":"{{assetName}}","state":"starter"}]""";
            await Respond(await NextRequest(), starterAsset);
            await Respond(await NextRequest(), starterAsset);
            await Respond(await NextRequest(), string.Empty, 204);

            await Respond(await NextRequest(), $$"""{"id":99,"name":"{{assetName}}"}""", 201);
            var uploadedAsset = $$"""[{"id":99,"name":"{{assetName}}","state":"uploaded"}]""";
            await Respond(await NextRequest(), uploadedAsset);
            await Respond(await NextRequest(), uploadedAsset);
        });

        try
        {
            var result = new GitHubReleasePublisher(new NullLogger()).PublishRelease(
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
            Assert.Equal(assetName, Assert.Single(result.UploadedAssets));
        }
        finally
        {
            listener.Stop();
            listener.Close();
            File.Delete(assetPath);
        }
    }

    [Fact]
    public async Task PublishRelease_TerminalTransientFailureRemovesStarterBeforeFailing()
    {
        var listener = new HttpListener();
        var port = GetAvailablePort();
        var apiBaseUrl = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(apiBaseUrl);
        listener.Start();
        var assetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllTextAsync(assetPath, "terminal-starter-cleanup");
        var assetName = Path.GetFileName(assetPath);
        var deleteObserved = false;

        async Task<HttpListenerContext> NextRequest()
            => await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(10));

        static async Task Respond(HttpListenerContext context, string json, int statusCode = 200)
        {
            await context.Request.InputStream.CopyToAsync(Stream.Null);
            var responseBytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
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
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                await Respond(await NextRequest(), "{\"message\":\"Bad Gateway\"}", 502);
                await Respond(await NextRequest(), "[]");
            }

            await Respond(await NextRequest(), "{\"message\":\"Bad Gateway\"}", 502);
            var starterAsset = $$"""[{"id":88,"name":"{{assetName}}","state":"starter"}]""";
            await Respond(await NextRequest(), starterAsset);
            await Respond(await NextRequest(), starterAsset);
            var delete = await NextRequest();
            deleteObserved = delete.Request.HttpMethod == "DELETE";
            await Respond(delete, string.Empty, 204);
        });

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                new GitHubReleasePublisher(new NullLogger()).PublishRelease(
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
            Assert.Contains("502", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(deleteObserved);
        }
        finally
        {
            listener.Stop();
            listener.Close();
            File.Delete(assetPath);
        }
    }

    [Fact]
    public async Task PublishRelease_CancellationDuringRecoveryStopsBeforeRetry()
    {
        var listener = new HttpListener();
        var port = GetAvailablePort();
        var apiBaseUrl = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(apiBaseUrl);
        listener.Start();
        var assetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllTextAsync(assetPath, "cancel-recovery");
        using var cancellation = new CancellationTokenSource();
        var requests = 0;

        async Task<HttpListenerContext> NextRequest()
        {
            var context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Interlocked.Increment(ref requests);
            return context;
        }

        static async Task Respond(HttpListenerContext context, string json, int statusCode = 200)
        {
            await context.Request.InputStream.CopyToAsync(Stream.Null);
            var responseBytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
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
            await Respond(await NextRequest(), "[]");
            cancellation.Cancel();
        });

        try
        {
            Assert.ThrowsAny<OperationCanceledException>(() =>
                new GitHubReleasePublisher(new NullLogger()).PublishRelease(
                    new GitHubReleasePublishRequest
                    {
                        Owner = "EvotecIT",
                        Repository = "example",
                        Token = "token",
                        ApiBaseUrl = apiBaseUrl,
                        TagName = "v1.2.3",
                        AssetFilePaths = [assetPath]
                    },
                    cancellation.Token));

            await server.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(3, requests);
        }
        finally
        {
            listener.Stop();
            listener.Close();
            File.Delete(assetPath);
        }
    }

    [Fact]
    public void UploadRetryClassification_CoversStreamTransportFailuresButNotCancellation()
    {
        Assert.True(GitHubReleasePublisher.IsTransientAssetUploadException(
            new HttpRequestException("Error while copying content to a stream."),
            CancellationToken.None));
        Assert.True(GitHubReleasePublisher.IsTransientAssetUploadException(
            new IOException("connection reset"),
            CancellationToken.None));
        Assert.False(GitHubReleasePublisher.IsTransientAssetUploadException(
            new InvalidOperationException("invalid response"),
            CancellationToken.None));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.False(GitHubReleasePublisher.IsTransientAssetUploadException(
            new TaskCanceledException("cancelled"),
            cancellation.Token));
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData((HttpStatusCode)429, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.GatewayTimeout, true)]
    [InlineData(HttpStatusCode.UnprocessableEntity, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    public void UploadRetryClassification_RetriesOnlyTransientHttpStatuses(
        HttpStatusCode statusCode,
        bool expected)
        => Assert.Equal(expected, GitHubReleasePublisher.IsTransientAssetUploadStatus(statusCode));

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
