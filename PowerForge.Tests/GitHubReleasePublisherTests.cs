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
    public void TryReserveExistingAssetNameForReplacement_AllowsOnlyAssetsFromOriginalSnapshot()
    {
        var replaceableAssetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PowerForge-win-x64.zip"
        };

        Assert.True(GitHubReleasePublisher.TryReserveExistingAssetNameForReplacement(replaceableAssetNames, "powerforge-win-x64.zip"));
        Assert.False(GitHubReleasePublisher.TryReserveExistingAssetNameForReplacement(replaceableAssetNames, "PowerForge-win-x64.zip"));
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
}
