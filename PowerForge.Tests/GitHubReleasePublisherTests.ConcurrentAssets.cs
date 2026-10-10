using System.Net;
using System.Text;

namespace PowerForge.Tests;

public sealed partial class GitHubReleasePublisherTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task PublishRelease_FreshReleaseAllowsConcurrentAssetsOnlyWhenPublished(
        bool appearDuringUploads, bool replaceExistingAssets, bool isDraft)
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
        const string otherAssets = "{\"id\":300,\"name\":\"plugin.zip\"}, {\"id\":301,\"name\":\"plugin.zip.sha256\"}";

        async Task Respond(HttpListenerContext context, string json, int statusCode = 200)
        {
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
            await Respond(await listener.GetContextAsync(),
                $$"""{"id":42,"html_url":"{{apiBaseUrl}}release","upload_url":"{{apiBaseUrl}}uploads{?name,label}","draft":{{isDraft.ToString().ToLowerInvariant()}}}""", 201);
            var uploadedCount = 0;
            var readsAfterLastUpload = 0;
            while (true)
            {
                var context = await listener.GetContextAsync();
                if (context.Request.HttpMethod == "POST" && context.Request.Url!.AbsolutePath == "/uploads")
                {
                    uploadedCount++;
                    await Respond(context, uploadedCount == 1 ? firstAsset : secondAsset, 201);
                }
                else
                {
                    Assert.Equal("GET", context.Request.HttpMethod);
                    Assert.Equal("/repos/EvotecIT/example/releases/42/assets", context.Request.Url!.AbsolutePath);
                    var finalRead = uploadedCount == 2 && ++readsAfterLastUpload == 2;
                    var listedAssets = new List<string>();
                    if (uploadedCount > 0) listedAssets.Add(firstAsset);
                    if (uploadedCount > 1) listedAssets.Add(secondAsset);
                    if (appearDuringUploads || finalRead) listedAssets.Add(otherAssets);
                    await Respond(context, $"[{string.Join(",", listedAssets)}]");
                    if (finalRead) break;
                }
            }
        });

        try
        {
            var request = new GitHubReleasePublishRequest
            {
                Owner = "EvotecIT",
                Repository = "example",
                Token = "synthetic-token",
                ApiBaseUrl = apiBaseUrl,
                TagName = "v1.2.3",
                ReplaceExistingAssets = replaceExistingAssets,
                IsDraft = isDraft,
                AssetFilePaths = [firstAssetPath, secondAssetPath]
            };
            var publisher = new GitHubReleasePublisher(new NullLogger());
            if (isDraft)
            {
                var exception = Assert.Throws<InvalidOperationException>(() => publisher.PublishRelease(request));
                Assert.Contains("outside the authorized recovery set", exception.Message, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                var result = publisher.PublishRelease(request);
                Assert.True(result.Succeeded);
                Assert.True(result.AllAssetUploadsSucceeded);
                Assert.False(result.ReusedExistingRelease);
                Assert.Equal(["library.zip", "tool.zip"], result.UploadedAssets);
                Assert.Empty(result.ReplacedExistingAssets);
                Assert.Empty(result.SkippedExistingAssets);
            }

            await server.WaitAsync(TimeSpan.FromSeconds(10));
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
