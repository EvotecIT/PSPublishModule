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
                retryAfter: "120");

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
            Assert.Equal(TimeSpan.FromMinutes(2), Assert.Single(delays));
        }
        finally
        {
            listener.Stop();
            File.Delete(assetPath);
        }
    }

    [Fact]
    public async Task PublishRelease_RetriesDirectUploadForbiddenWithRetryAfter()
    {
        using var listener = new HttpListener();
        var port = GetAvailablePort();
        var apiBaseUrl = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(apiBaseUrl);
        listener.Start();
        var assetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllTextAsync(assetPath, "direct-forbidden-retry");
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
            await Respond(
                await NextRequest(),
                "{\"message\":\"rate limited\"}",
                403,
                retryAfter: "120");
            await Respond(await NextRequest(), "[]");
            await Respond(await NextRequest(), $$"""{"id":99,"name":"{{assetName}}"}""", 201);
            var uploadedAsset = $$"""[{"id":99,"name":"{{assetName}}","state":"uploaded"}]""";
            await Respond(await NextRequest(), uploadedAsset);
            await Respond(await NextRequest(), uploadedAsset);
        });

        try
        {
            var result = new GitHubReleasePublisher(
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
                    });

            await server.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(result.Succeeded);
            Assert.Equal(TimeSpan.FromMinutes(2), Assert.Single(delays));
            Assert.Equal(2, requests.Count(request => request == "POST /uploads"));
        }
        finally
        {
            listener.Stop();
            File.Delete(assetPath);
        }
    }

    [Fact]
    public async Task PublishRelease_RetriesReplacementInventoryAndAmbiguousDelete()
    {
        using var listener = new HttpListener();
        var port = GetAvailablePort();
        var apiBaseUrl = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(apiBaseUrl);
        listener.Start();
        var assetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllTextAsync(assetPath, "replacement-retry");
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

        var release = $$"""{"id":42,"html_url":"{{apiBaseUrl}}release","upload_url":"{{apiBaseUrl}}uploads{?name,label}"}""";
        var existingAsset = $$"""[{"id":88,"name":"{{assetName}}","state":"uploaded"}]""";
        var uploadedAsset = $$"""[{"id":99,"name":"{{assetName}}","state":"uploaded"}]""";
        var server = Task.Run(async () =>
        {
            await Respond(await NextRequest(), release);
            await Respond(await NextRequest(), "{\"message\":\"rate limited\"}", 403, retryAfter: "120");
            await Respond(await NextRequest(), existingAsset);
            await Respond(await NextRequest(), existingAsset);
            await Respond(await NextRequest(), "{\"message\":\"Service Unavailable\"}", 503, retryAfter: "30");
            await Respond(await NextRequest(), "[]");
            await Respond(await NextRequest(), $$"""{"id":99,"name":"{{assetName}}"}""", 201);
            await Respond(await NextRequest(), uploadedAsset);
            await Respond(await NextRequest(), uploadedAsset);
        });

        try
        {
            var result = new GitHubReleasePublisher(
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
                        ReuseExistingReleaseOnConflict = true,
                        RequireExpectedExistingRelease = true,
                        ExpectedExistingReleaseId = 42,
                        ReplaceExistingAssets = true,
                        AssetFilePaths = [assetPath]
                    });

            await server.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(result.Succeeded);
            Assert.Equal([TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(30)], delays);
            Assert.Equal(assetName, Assert.Single(result.ReplacedExistingAssets));
            Assert.Equal(assetName, Assert.Single(result.UploadedAssets));
        }
        finally
        {
            listener.Stop();
            File.Delete(assetPath);
        }
    }

    [Fact]
    public async Task PublishRelease_ReusedMixedAssetsRejectEarlierUploadedIdentityReplacement()
    {
        using var listener = new HttpListener();
        var port = GetAvailablePort();
        var apiBaseUrl = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(apiBaseUrl);
        listener.Start();
        var firstAssetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        var skippedAssetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllTextAsync(firstAssetPath, "first");
        await File.WriteAllTextAsync(skippedAssetPath, "skipped");
        var firstAssetName = Path.GetFileName(firstAssetPath);
        var skippedAssetName = Path.GetFileName(skippedAssetPath);

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

        var changedAssets = $$"""[{"id":101,"name":"{{firstAssetName}}","state":"uploaded"},{"id":200,"name":"{{skippedAssetName}}","state":"uploaded"}]""";
        var server = Task.Run(async () =>
        {
            await Respond(
                await NextRequest(),
                $$"""{"id":42,"html_url":"{{apiBaseUrl}}release","upload_url":"{{apiBaseUrl}}uploads{?name,label}"}""");
            await Respond(await NextRequest(), $$"""{"id":100,"name":"{{firstAssetName}}"}""", 201);
            await Respond(await NextRequest(), $$"""[{"id":100,"name":"{{firstAssetName}}","state":"uploaded"}]""");
            await Respond(
                await NextRequest(),
                "{\"message\":\"Validation Failed\",\"errors\":[{\"resource\":\"ReleaseAsset\",\"code\":\"already_exists\",\"field\":\"name\"}]}",
                422);
            await Respond(await NextRequest(), changedAssets);
            await Respond(await NextRequest(), changedAssets);
            await Respond(await NextRequest(), changedAssets);
        });

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                new GitHubReleasePublisher(
                    new NullLogger(),
                    static (_, cancellationToken) => cancellationToken.ThrowIfCancellationRequested())
                .PublishRelease(
                    new GitHubReleasePublishRequest
                    {
                        Owner = "EvotecIT",
                        Repository = "example",
                        Token = "token",
                        ApiBaseUrl = apiBaseUrl,
                        TagName = "v1.2.3",
                        ReuseExistingReleaseOnConflict = true,
                        RequireExpectedExistingRelease = true,
                        ExpectedExistingReleaseId = 42,
                        AssetFilePaths = [firstAssetPath, skippedAssetPath]
                    }));

            await server.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains("changed from id 100 to 101", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            listener.Stop();
            File.Delete(firstAssetPath);
            File.Delete(skippedAssetPath);
        }
    }

    [Fact]
    public async Task PublishRelease_DoesNotTreatTransientInventoryAsAmbiguousDelete()
    {
        using var listener = new HttpListener();
        var port = GetAvailablePort();
        var apiBaseUrl = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(apiBaseUrl);
        listener.Start();
        var assetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllTextAsync(assetPath, "inventory-before-delete");
        var assetName = Path.GetFileName(assetPath);
        var requests = new List<string>();
        var delays = new List<TimeSpan>();

        async Task<HttpListenerContext> NextRequest()
            => await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(10));

        async Task Respond(HttpListenerContext context, string json, int statusCode = 200, string? retryAfter = null)
        {
            requests.Add($"{context.Request.HttpMethod} {context.Request.Url!.AbsolutePath}");
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

        var existingAsset = $$"""[{"id":88,"name":"{{assetName}}","state":"uploaded"}]""";
        var server = Task.Run(async () =>
        {
            await Respond(
                await NextRequest(),
                $$"""{"id":42,"html_url":"{{apiBaseUrl}}release","upload_url":"{{apiBaseUrl}}uploads{?name,label}"}""");
            await Respond(await NextRequest(), existingAsset);
            await Respond(await NextRequest(), "{\"message\":\"rate limited\"}", 403, retryAfter: "15");
            await Respond(await NextRequest(), "[]");
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
                        ReuseExistingReleaseOnConflict = true,
                        RequireExpectedExistingRelease = true,
                        ExpectedExistingReleaseId = 42,
                        ReplaceExistingAssets = true,
                        AssetFilePaths = [assetPath]
                    }));

            await server.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains("disappeared before deletion", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(TimeSpan.FromSeconds(15), Assert.Single(delays));
            Assert.DoesNotContain(requests, request => request.StartsWith("DELETE ", StringComparison.Ordinal));
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
