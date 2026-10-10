using System.Net;
using System.Text;

namespace PowerForge.Tests;

public sealed partial class GitHubReleasePublisherTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublishRelease_FreshReleasePreservesAssetsAddedByAnotherPublisher(bool appearDuringUploads)
    {
        var port = GetAvailablePort();
        var apiBaseUrl = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(apiBaseUrl);
        listener.Start();

        var assetDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(assetDirectory);
        var firstAssetPath = Path.Combine(assetDirectory, "library.zip");
        var secondAssetPath = Path.Combine(assetDirectory, "tool.zip");
        await File.WriteAllTextAsync(firstAssetPath, "library");
        await File.WriteAllTextAsync(secondAssetPath, "tool");
        var requests = new List<string>();
        const string firstAsset = "{\"id\":100,\"name\":\"library.zip\"}";
        const string secondAsset = "{\"id\":200,\"name\":\"tool.zip\"}";
        const string otherAssets = ", {\"id\":300,\"name\":\"plugin.zip\"}, {\"id\":301,\"name\":\"plugin.zip.sha256\"}";

        async Task Respond(string json, int statusCode = 200)
        {
            var context = await listener.GetContextAsync();
            requests.Add($"{context.Request.HttpMethod} {context.Request.Url!.AbsolutePath}");
            await context.Request.InputStream.CopyToAsync(Stream.Null);
            var bytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }

        var server = Task.Run(async () =>
        {
            await Respond($$"""{"id":42,"html_url":"{{apiBaseUrl}}release","upload_url":"{{apiBaseUrl}}uploads{?name,label}"}""", 201);
            await Respond(firstAsset, 201);
            await Respond($"[{firstAsset}{(appearDuringUploads ? otherAssets : string.Empty)}]");
            await Respond(secondAsset, 201);
            await Respond($"[{firstAsset},{secondAsset}{(appearDuringUploads ? otherAssets : string.Empty)}]");
            await Respond($"[{firstAsset},{secondAsset}{otherAssets}]");
        });

        try
        {
            var result = new GitHubReleasePublisher(new NullLogger()).PublishRelease(new GitHubReleasePublishRequest
            {
                Owner = "EvotecIT",
                Repository = "example",
                Token = "synthetic-token",
                ApiBaseUrl = apiBaseUrl,
                TagName = "v1.2.3",
                AssetFilePaths = [firstAssetPath, secondAssetPath]
            });

            await server.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(result.Succeeded);
            Assert.True(result.AllAssetUploadsSucceeded);
            Assert.False(result.ReusedExistingRelease);
            Assert.Equal(["library.zip", "tool.zip"], result.UploadedAssets);
            Assert.Empty(result.ReplacedExistingAssets);
            Assert.Empty(result.SkippedExistingAssets);
            Assert.Equal(2, requests.Count(request => request == "POST /uploads"));
            Assert.DoesNotContain(requests, request => request.StartsWith("DELETE ", StringComparison.Ordinal));
        }
        finally
        {
            listener.Stop();
            try
            {
                await server.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (HttpListenerException) when (!listener.IsListening)
            {
                // A publisher failure can leave the server waiting for its next scripted request.
            }
            catch (ObjectDisposedException) when (!listener.IsListening)
            {
                // Listener shutdown must not replace the original publisher failure.
            }
            finally
            {
                Directory.Delete(assetDirectory, recursive: true);
            }
        }
    }
}
