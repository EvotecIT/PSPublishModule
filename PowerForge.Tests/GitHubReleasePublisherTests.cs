using PowerForge;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace PowerForge.Tests;

public sealed class GitHubReleasePublisherTests
{
    [Fact]
    public async Task PublishRelease_SendsMetadataWithGeneratedReleaseNotes()
    {
        var listener = new HttpListener();
        var port = GetAvailablePort();
        var apiBaseUrl = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(apiBaseUrl);
        listener.Start();
        string? requestBody = null;
        var server = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            using var reader = new StreamReader(
                context.Request.InputStream,
                context.Request.ContentEncoding);
            requestBody = await reader.ReadToEndAsync();
            var responseBody = Encoding.UTF8.GetBytes(
                $$"""{"id":42,"html_url":"{{apiBaseUrl}}release","upload_url":"{{apiBaseUrl}}uploads{?name,label}","body":"generated"}""");
            context.Response.StatusCode = 201;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = responseBody.Length;
            await context.Response.OutputStream.WriteAsync(responseBody);
            context.Response.Close();
        });
        const string metadata = "<!-- release provenance -->";
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
                    ReleaseNotes = metadata,
                    GenerateReleaseNotes = true
                });

            await server.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(result.Succeeded);
            Assert.Equal(42, result.ReleaseId);
            var request = JsonNode.Parse(requestBody!)!.AsObject();
            Assert.Equal(metadata, request["body"]!.GetValue<string>());
            Assert.True(request["generate_release_notes"]!.GetValue<bool>());
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }

    [Fact]
    public async Task PublishRelease_RejectsSuccessfulResponseWithoutReleaseId()
    {
        var listener = new HttpListener();
        var port = GetAvailablePort();
        var apiBaseUrl = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(apiBaseUrl);
        listener.Start();
        var server = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            var responseBody = Encoding.UTF8.GetBytes(
                $$"""{"html_url":"{{apiBaseUrl}}release","upload_url":"{{apiBaseUrl}}uploads{?name,label}"}""");
            context.Response.StatusCode = 201;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = responseBody.Length;
            await context.Response.OutputStream.WriteAsync(responseBody);
            context.Response.Close();
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
                        TagName = "v1.2.3"
                    }));

            await server.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains("invalid release identifier", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }

    [Fact]
    public async Task PublishRelease_ReportsPlannedAndByteLevelAssetProgress()
    {
        var listener = new HttpListener();
        var port = GetAvailablePort();
        var apiBaseUrl = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(apiBaseUrl);
        listener.Start();
        var assetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllBytesAsync(assetPath, new byte[5 * 1024 * 1024]);
        var progress = new RecordingGitHubProgress();
        var server = Task.Run(async () =>
        {
            var create = await listener.GetContextAsync();
            var createBody = Encoding.UTF8.GetBytes(
                $$"""{"id":42,"html_url":"{{apiBaseUrl}}release","upload_url":"{{apiBaseUrl}}uploads{?name,label}","body":"generated"}""");
            create.Response.StatusCode = 201;
            create.Response.ContentType = "application/json";
            create.Response.ContentLength64 = createBody.Length;
            await create.Response.OutputStream.WriteAsync(createBody);
            create.Response.Close();

            var upload = await listener.GetContextAsync();
            await upload.Request.InputStream.CopyToAsync(Stream.Null);
            var assetName = Path.GetFileName(assetPath);
            var uploadBody = Encoding.UTF8.GetBytes($"{{\"id\":99,\"name\":\"{assetName}\"}}");
            upload.Response.StatusCode = 201;
            upload.Response.ContentType = "application/json";
            upload.Response.ContentLength64 = uploadBody.Length;
            await upload.Response.OutputStream.WriteAsync(uploadBody);
            upload.Response.Close();

            var assets = await listener.GetContextAsync();
            var assetsBody = Encoding.UTF8.GetBytes($"[{{\"id\":99,\"name\":\"{assetName}\"}}]");
            assets.Response.StatusCode = 200;
            assets.Response.ContentType = "application/json";
            assets.Response.ContentLength64 = assetsBody.Length;
            await assets.Response.OutputStream.WriteAsync(assetsBody);
            assets.Response.Close();

            var finalAssets = await listener.GetContextAsync();
            finalAssets.Response.StatusCode = 200;
            finalAssets.Response.ContentType = "application/json";
            finalAssets.Response.ContentLength64 = assetsBody.Length;
            await finalAssets.Response.OutputStream.WriteAsync(assetsBody);
            finalAssets.Response.Close();
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
                    AssetFilePaths = [assetPath],
                    Progress = progress
                });

            await server.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(result.Succeeded);
            Assert.Equal(Path.GetFileName(assetPath), Assert.Single(result.UploadedAssets));
            Assert.Equal(GitHubReleaseAssetProgressState.Planned, progress.Updates[0].State);
            Assert.Contains(
                progress.Updates,
                update => update.State == GitHubReleaseAssetProgressState.Uploading &&
                          update.BytesTransferred > 0 &&
                          update.TotalBytes == new FileInfo(assetPath).Length);
            var completed = progress.Updates[^1];
            Assert.Equal(GitHubReleaseAssetProgressState.Uploaded, completed.State);
            Assert.Equal(completed.TotalBytes, completed.BytesTransferred);
        }
        finally
        {
            listener.Stop();
            listener.Close();
            File.Delete(assetPath);
        }
    }

    [Fact]
    public async Task PublishRelease_FreshUploadsRejectEarlierAssetReplacementDuringLaterUpload()
    {
        var listener = new HttpListener();
        var port = GetAvailablePort();
        var apiBaseUrl = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(apiBaseUrl);
        listener.Start();
        var firstAssetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        var secondAssetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllTextAsync(firstAssetPath, "first");
        await File.WriteAllTextAsync(secondAssetPath, "second");
        var firstAssetName = Path.GetFileName(firstAssetPath);
        var secondAssetName = Path.GetFileName(secondAssetPath);

        async Task Respond(string json, int statusCode = 200)
        {
            var context = await listener.GetContextAsync();
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
            await Respond($$"""{"id":42,"html_url":"{{apiBaseUrl}}release","upload_url":"{{apiBaseUrl}}uploads{?name,label}"}""");
            await Respond($$"""{"id":100,"name":"{{firstAssetName}}"}""", 201);
            await Respond($$"""[{"id":100,"name":"{{firstAssetName}}"}]""");
            await Respond($$"""{"id":200,"name":"{{secondAssetName}}"}""", 201);
            await Respond($$"""[{"id":101,"name":"{{firstAssetName}}"},{"id":200,"name":"{{secondAssetName}}"}]""");
            await Respond($$"""[{"id":101,"name":"{{firstAssetName}}"},{"id":200,"name":"{{secondAssetName}}"}]""");
        });

        try
        {
            var request = new GitHubReleasePublishRequest
            {
                Owner = "EvotecIT",
                Repository = "example",
                Token = "token",
                ApiBaseUrl = apiBaseUrl,
                TagName = "v1.2.3",
                AssetFilePaths = [firstAssetPath, secondAssetPath]
            };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new GitHubReleasePublisher(new NullLogger()).PublishRelease(request));
            await server.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains("changed from id 100 to 101", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            listener.Stop();
            listener.Close();
            File.Delete(firstAssetPath);
            File.Delete(secondAssetPath);
        }
    }

    [Fact]
    public async Task PublishRelease_NewReleaseRejectsConcurrentSameNameAsset()
    {
        var listener = new HttpListener();
        var port = GetAvailablePort();
        var apiBaseUrl = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(apiBaseUrl);
        listener.Start();
        var assetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllTextAsync(assetPath, "asset");

        async Task Respond(string json, int statusCode)
        {
            var context = await listener.GetContextAsync();
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
            await Respond(
                """{"message":"Validation Failed","errors":[{"resource":"ReleaseAsset","code":"already_exists","field":"name"}]}""",
                422);
            await Respond($$"""[{"id":99,"name":"{{Path.GetFileName(assetPath)}}","state":"uploaded"}]""", 200);
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
            Assert.Contains("first publication", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("unverified bytes", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            listener.Stop();
            listener.Close();
            File.Delete(assetPath);
        }
    }

    [Fact]
    public async Task PublishRelease_ReusedReleaseRetainsOrdinarySameNameSkipCompatibility()
    {
        var listener = new HttpListener();
        var port = GetAvailablePort();
        var apiBaseUrl = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(apiBaseUrl);
        listener.Start();
        var assetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllTextAsync(assetPath, "asset");

        async Task Respond(string json, int statusCode)
        {
            var context = await listener.GetContextAsync();
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
            await Respond($$"""{"id":42,"html_url":"{{apiBaseUrl}}release","upload_url":"{{apiBaseUrl}}uploads{?name,label}"}""", 200);
            await Respond(
                """{"message":"Validation Failed","errors":[{"resource":"ReleaseAsset","code":"already_exists","field":"name"}]}""",
                422);
            await Respond($$"""[{"id":99,"name":"{{Path.GetFileName(assetPath)}}","state":"uploaded"}]""", 200);
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
                    ReuseExistingReleaseOnConflict = true,
                    RequireExpectedExistingRelease = true,
                    ExpectedExistingReleaseId = 42,
                    AssetFilePaths = [assetPath]
                });
            await server.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(result.Succeeded);
            Assert.Equal(Path.GetFileName(assetPath), Assert.Single(result.SkippedExistingAssets));
            Assert.Empty(result.UploadedAssets);
        }
        finally
        {
            listener.Stop();
            listener.Close();
            File.Delete(assetPath);
        }
    }

    [Fact]
    public void PublishRelease_ThrowsWhenAssetDoesNotExist()
    {
        var publisher = new GitHubReleasePublisher(new NullLogger());

        var request = new GitHubReleasePublishRequest
        {
            Owner = "EvotecIT",
            Repository = "PSPublishModule",
            Token = "token",
            TagName = "v1.2.3",
            AssetFilePaths = new[] { Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip") }
        };

        Assert.Throws<FileNotFoundException>(() => publisher.PublishRelease(request));
    }

    [Fact]
    public void PublishRelease_honors_cancellation_before_contacting_github()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var request = new GitHubReleasePublishRequest
        {
            Owner = "EvotecIT",
            Repository = "PSPublishModule",
            Token = "token",
            TagName = "v1.2.3"
        };

        Assert.ThrowsAny<OperationCanceledException>(() =>
            new GitHubReleasePublisher(new NullLogger())
                .PublishRelease(request, cancellation.Token));
    }

    [Fact]
    public async Task PublishRelease_ReusesOnlyPreflightBoundStableReleaseAndRevalidatesTagBeforeUpload()
    {
        var listener = new HttpListener();
        var port = GetAvailablePort();
        var apiBaseUrl = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(apiBaseUrl);
        listener.Start();
        var assetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllTextAsync(assetPath, "asset");
        const string commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var requests = new List<string>();

        async Task Respond(HttpListenerContext context, string json, int statusCode = 200)
        {
            requests.Add($"{context.Request.HttpMethod} {context.Request.Url!.AbsolutePath}");
            await context.Request.InputStream.CopyToAsync(Stream.Null);
            var bytes = statusCode == 204 ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }

        var releaseJson = $$"""{"id":42,"html_url":"{{apiBaseUrl}}release","upload_url":"{{apiBaseUrl}}uploads{?name,label}","body":"generated","draft":false,"prerelease":false,"published_at":"2026-07-29T00:00:00Z"}""";
        var server = Task.Run(async () =>
        {
            await Respond(await listener.GetContextAsync(), releaseJson);
            await Respond(await listener.GetContextAsync(), $"[{{\"id\":99,\"name\":\"{Path.GetFileName(assetPath)}\"}}]");
            await Respond(await listener.GetContextAsync(), releaseJson);
            await Respond(await listener.GetContextAsync(), $"{{\"object\":{{\"sha\":\"{commit}\",\"type\":\"commit\"}}}}");
            await Respond(await listener.GetContextAsync(), $"[{{\"id\":99,\"name\":\"{Path.GetFileName(assetPath)}\"}}]");
            await Respond(await listener.GetContextAsync(), string.Empty, 204);
            await Respond(await listener.GetContextAsync(), releaseJson);
            await Respond(await listener.GetContextAsync(), $"{{\"object\":{{\"sha\":\"{commit}\",\"type\":\"commit\"}}}}");
            await Respond(await listener.GetContextAsync(), $"{{\"id\":100,\"name\":\"{Path.GetFileName(assetPath)}\"}}", 201);
            await Respond(await listener.GetContextAsync(), releaseJson);
            await Respond(await listener.GetContextAsync(), $"{{\"object\":{{\"sha\":\"{commit}\",\"type\":\"commit\"}}}}");
            await Respond(await listener.GetContextAsync(), $"[{{\"id\":100,\"name\":\"{Path.GetFileName(assetPath)}\"}}]");
            await Respond(await listener.GetContextAsync(), releaseJson);
            await Respond(await listener.GetContextAsync(), $"{{\"object\":{{\"sha\":\"{commit}\",\"type\":\"commit\"}}}}");
            await Respond(await listener.GetContextAsync(), $"[{{\"id\":100,\"name\":\"{Path.GetFileName(assetPath)}\"}}]");
            await Respond(await listener.GetContextAsync(), releaseJson);
            await Respond(await listener.GetContextAsync(), $"{{\"object\":{{\"sha\":\"{commit}\",\"type\":\"commit\"}}}}");
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
                    ReuseExistingReleaseOnConflict = true,
                    RequireExpectedExistingRelease = true,
                    ExpectedExistingReleaseId = 42,
                    RequirePublishedStableRelease = true,
                    ExpectedTagCommitSha = commit,
                    ReplaceExistingAssets = true,
                    AssetFilePaths = [assetPath]
                });

            await server.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(result.ReusedExistingRelease);
            Assert.Equal(Path.GetFileName(assetPath), Assert.Single(result.ReplacedExistingAssets));
            Assert.Equal(Path.GetFileName(assetPath), Assert.Single(result.UploadedAssets));
            Assert.Equal(
                [
                    "GET /repos/EvotecIT/example/releases/tags/v1.2.3",
                    "GET /repos/EvotecIT/example/releases/42/assets",
                    "GET /repos/EvotecIT/example/releases/tags/v1.2.3",
                    "GET /repos/EvotecIT/example/git/ref/tags/v1.2.3",
                    "GET /repos/EvotecIT/example/releases/42/assets",
                    "DELETE /repos/EvotecIT/example/releases/assets/99",
                    "GET /repos/EvotecIT/example/releases/tags/v1.2.3",
                    "GET /repos/EvotecIT/example/git/ref/tags/v1.2.3",
                    "POST /uploads",
                    "GET /repos/EvotecIT/example/releases/tags/v1.2.3",
                    "GET /repos/EvotecIT/example/git/ref/tags/v1.2.3",
                    "GET /repos/EvotecIT/example/releases/42/assets",
                    "GET /repos/EvotecIT/example/releases/tags/v1.2.3",
                    "GET /repos/EvotecIT/example/git/ref/tags/v1.2.3",
                    "GET /repos/EvotecIT/example/releases/42/assets",
                    "GET /repos/EvotecIT/example/releases/tags/v1.2.3",
                    "GET /repos/EvotecIT/example/git/ref/tags/v1.2.3"
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
    public async Task PublishRelease_RecoveryRejectsUnauthorizedExistingAssetBeforeMutation()
    {
        var listener = new HttpListener();
        var port = GetAvailablePort();
        var apiBaseUrl = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(apiBaseUrl);
        listener.Start();
        var assetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllTextAsync(assetPath, "asset");
        var releaseJson = $$"""{"id":42,"html_url":"{{apiBaseUrl}}release","upload_url":"{{apiBaseUrl}}uploads{?name,label}","draft":false,"prerelease":false,"published_at":"2026-07-29T00:00:00Z"}""";
        var requests = new List<string>();

        async Task Respond(string json)
        {
            var context = await listener.GetContextAsync();
            requests.Add($"{context.Request.HttpMethod} {context.Request.Url!.AbsolutePath}");
            var bytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }

        var server = Task.Run(async () =>
        {
            await Respond(releaseJson);
            await Respond("[{\"id\":91,\"name\":\"obsolete-unreviewed.zip\"}]");
        });

        try
        {
            var request = new GitHubReleasePublishRequest
            {
                Owner = "EvotecIT",
                Repository = "example",
                Token = "token",
                ApiBaseUrl = apiBaseUrl,
                TagName = "v1.2.3",
                ReuseExistingReleaseOnConflict = true,
                RequireExpectedExistingRelease = true,
                ExpectedExistingReleaseId = 42,
                RequirePublishedStableRelease = true,
                ReplaceExistingAssets = true,
                AssetFilePaths = [assetPath]
            };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new GitHubReleasePublisher(new NullLogger()).PublishRelease(request));
            await server.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains("outside the authorized recovery set", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(
                [
                    "GET /repos/EvotecIT/example/releases/tags/v1.2.3",
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
    public async Task PublishRelease_RejectsUploadedAssetIdentityReplacedBeforeSuccess()
    {
        var listener = new HttpListener();
        var port = GetAvailablePort();
        var apiBaseUrl = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(apiBaseUrl);
        listener.Start();
        var assetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllTextAsync(assetPath, "asset");
        const string commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var fileName = Path.GetFileName(assetPath);
        var releaseJson = $$"""{"id":42,"html_url":"{{apiBaseUrl}}release","upload_url":"{{apiBaseUrl}}uploads{?name,label}","draft":false,"prerelease":false,"published_at":"2026-07-29T00:00:00Z"}""";

        async Task Respond(string json, int statusCode = 200)
        {
            var context = await listener.GetContextAsync();
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
            await Respond(releaseJson);
            await Respond("[]");
            await Respond(releaseJson);
            await Respond($"{{\"object\":{{\"sha\":\"{commit}\",\"type\":\"commit\"}}}}");
            await Respond($"{{\"id\":100,\"name\":\"{fileName}\"}}", 201);
            await Respond(releaseJson);
            await Respond($"{{\"object\":{{\"sha\":\"{commit}\",\"type\":\"commit\"}}}}");
            await Respond($"[{{\"id\":101,\"name\":\"{fileName}\"}}]");
        });

        try
        {
            var request = new GitHubReleasePublishRequest
            {
                Owner = "EvotecIT",
                Repository = "example",
                Token = "token",
                ApiBaseUrl = apiBaseUrl,
                TagName = "v1.2.3",
                ReuseExistingReleaseOnConflict = true,
                RequireExpectedExistingRelease = true,
                ExpectedExistingReleaseId = 42,
                RequirePublishedStableRelease = true,
                ExpectedTagCommitSha = commit,
                ReplaceExistingAssets = true,
                AssetFilePaths = [assetPath]
            };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new GitHubReleasePublisher(new NullLogger()).PublishRelease(request));
            await server.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains("changed from id 100 to 101", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            listener.Stop();
            listener.Close();
            File.Delete(assetPath);
        }
    }

    [Fact]
    public async Task PublishRelease_ReplacementFailsWhenSameNameAssetAppearsAfterVerifiedSnapshot()
    {
        var listener = new HttpListener();
        var port = GetAvailablePort();
        var apiBaseUrl = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(apiBaseUrl);
        listener.Start();
        var assetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllTextAsync(assetPath, "asset");
        const string commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var releaseJson = $$"""{"id":42,"html_url":"{{apiBaseUrl}}release","upload_url":"{{apiBaseUrl}}uploads{?name,label}","body":"generated","draft":false,"prerelease":false,"published_at":"2026-07-29T00:00:00Z"}""";

        async Task Respond(string json, int statusCode = 200)
        {
            var context = await listener.GetContextAsync();
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
            await Respond(releaseJson);
            await Respond("[]");
            await Respond(releaseJson);
            await Respond($"{{\"object\":{{\"sha\":\"{commit}\",\"type\":\"commit\"}}}}");
            await Respond("{\"message\":\"Validation Failed\",\"errors\":[{\"resource\":\"ReleaseAsset\",\"code\":\"already_exists\",\"field\":\"name\"}]}", 422);
            await Respond($$"""[{"id":101,"name":"{{Path.GetFileName(assetPath)}}","state":"uploaded"}]""");
        });

        try
        {
            var request = new GitHubReleasePublishRequest
            {
                Owner = "EvotecIT",
                Repository = "example",
                Token = "token",
                ApiBaseUrl = apiBaseUrl,
                TagName = "v1.2.3",
                ReuseExistingReleaseOnConflict = true,
                RequireExpectedExistingRelease = true,
                ExpectedExistingReleaseId = 42,
                RequirePublishedStableRelease = true,
                ExpectedTagCommitSha = commit,
                ReplaceExistingAssets = true,
                AssetFilePaths = [assetPath]
            };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new GitHubReleasePublisher(new NullLogger()).PublishRelease(request));
            await server.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains("refusing to skip unverified bytes", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            listener.Stop();
            listener.Close();
            File.Delete(assetPath);
        }
    }

    [Fact]
    public void TryReserveExistingAssetForReplacement_BindsNameToOriginalAssetId()
    {
        var replaceableAssets = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["PowerForge-win-x64.zip"] = 42
        };

        Assert.True(GitHubReleasePublisher.TryReserveExistingAssetForReplacement(
            replaceableAssets, "powerforge-win-x64.zip", out var assetId));
        Assert.Equal(42, assetId);
        Assert.False(GitHubReleasePublisher.TryReserveExistingAssetForReplacement(
            replaceableAssets, "PowerForge-win-x64.zip", out _));

        GitHubReleasePublisher.ValidateExpectedAssetId("PowerForge-win-x64.zip", 42, 42);
        Assert.Throws<InvalidOperationException>(() =>
            GitHubReleasePublisher.ValidateExpectedAssetId("PowerForge-win-x64.zip", 42, 99));
    }

    [Fact]
    public void ValidateExpectedExistingRelease_RejectsUnverifiedConflictBeforeAssetReplacement()
    {
        var missing = Assert.Throws<InvalidOperationException>(() =>
            GitHubReleasePublisher.ValidateExpectedExistingRelease("v1.2.3", true, null, 99));
        var mismatch = Assert.Throws<InvalidOperationException>(() =>
            GitHubReleasePublisher.ValidateExpectedExistingRelease("v1.2.3", true, 42, 99));

        Assert.Contains("not preflight-verified", missing.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not preflight-verified", mismatch.Message, StringComparison.OrdinalIgnoreCase);
        GitHubReleasePublisher.ValidateExpectedExistingRelease("v1.2.3", true, 99, 99);
    }

    [Fact]
    public void ValidatePublishedStableRelease_RejectsUnsafeRecoveryStates()
    {
        Assert.Throws<InvalidOperationException>(() =>
            GitHubReleasePublisher.ValidatePublishedStableRelease("v1.2.3", true, true, false, "2026-07-29T00:00:00Z"));
        Assert.Throws<InvalidOperationException>(() =>
            GitHubReleasePublisher.ValidatePublishedStableRelease("v1.2.3", true, false, true, "2026-07-29T00:00:00Z"));
        Assert.Throws<InvalidOperationException>(() =>
            GitHubReleasePublisher.ValidatePublishedStableRelease("v1.2.3", true, false, false, null));

        GitHubReleasePublisher.ValidatePublishedStableRelease(
            "v1.2.3", true, false, false, "2026-07-29T00:00:00Z");
    }

    [Fact]
    public void ValidateExpectedReleaseState_RejectsReleaseOrTagChangesBeforeAssetMutation()
    {
        const string marker = "<!-- powerforge-homeassistant source-pr:42 -->";
        const string commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        Assert.Throws<InvalidOperationException>(() => GitHubReleasePublisher.ValidateExpectedReleaseState(
            "v1.2.3", 42, 99, marker, marker, commit, commit));
        Assert.Throws<InvalidOperationException>(() => GitHubReleasePublisher.ValidateExpectedReleaseState(
            "v1.2.3", 42, 42, "foreign body", marker, commit, commit));
        Assert.Throws<InvalidOperationException>(() => GitHubReleasePublisher.ValidateExpectedReleaseState(
            "v1.2.3", 42, 42, marker, marker, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", commit));

        GitHubReleasePublisher.ValidateExpectedReleaseState(
            "v1.2.3", 42, 42, marker, marker, commit, commit);
    }

    [Fact]
    public void BuildApiUri_PreservesGitHubEnterpriseApiPrefix()
    {
        var uri = GitHubReleasePublisher.BuildApiUri(
            "https://github.enterprise.example/api/v3/",
            "/repos/EvotecIT/example/releases");

        Assert.Equal("https://github.enterprise.example/api/v3/repos/EvotecIT/example/releases", uri.AbsoluteUri);
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

    private sealed class RecordingGitHubProgress : IGitHubReleaseProgressReporter
    {
        public List<GitHubReleaseAssetProgress> Updates { get; } = new();

        public void Report(GitHubReleaseAssetProgress progress)
            => Updates.Add(progress);
    }
}
