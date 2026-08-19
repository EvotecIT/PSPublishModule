using System.Net;
using System.Text.Json.Nodes;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed partial class CloudflareIncrementalCachePurgeTests
{
    [Fact]
    public void IncrementalPurge_ShouldPurgeConfiguredVariantsWhenManifestIsUnchanged()
    {
        var root = NewTempDirectory();
        try
        {
            var entries = new[] { Entry("apps/converter/", 'a'), Entry("apps/converter/index.html", 'a') };
            var previousPath = WriteManifest(root, "previous.json", entries, baseUrl: "https://example.test/project/");
            var currentPath = WriteManifest(root, "current.json", entries, baseUrl: "https://example.test/project/");
            var handler = new RecordingHandler(SuccessResponse());
            using var client = NewClient(handler);

            var result = CloudflareIncrementalCachePurger.Purge(
                ZoneId,
                "secret-token",
                "https://example.test/project/",
                currentPath,
                previousPath,
                dryRun: false,
                logger: null,
                client,
                alwaysPurgePaths:
                [
                    "/apps/converter/",
                    "/apps/converter/?embedded=1"
                ]);

            Assert.True(result.Success, result.Message);
            Assert.False(result.UsedFallback);
            Assert.Equal(2, result.TargetCount);
            Assert.Equal(1, result.RequestCount);
            var files = JsonNode.Parse(Assert.Single(handler.Bodies))!["files"]!.AsArray()
                .Select(node => node!.GetValue<string>())
                .ToArray();
            Assert.Equal(
                [
                    "https://example.test/project/apps/converter/",
                    "https://example.test/project/apps/converter/?embedded=1"
                ],
                files);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IncrementalPurge_ShouldDeduplicateChangedAndAlwaysPurgeUrlsBeforeApplyingLimit()
    {
        var root = NewTempDirectory();
        try
        {
            var previousPath = WriteManifest(root, "previous.json", [Entry("index.html", 'a')]);
            var currentPath = WriteManifest(root, "current.json", [Entry("index.html", 'b')]);
            var handler = new RecordingHandler(SuccessResponse());
            using var client = NewClient(handler);

            var result = CloudflareIncrementalCachePurger.Purge(
                ZoneId,
                "secret-token",
                "https://example.test/",
                currentPath,
                previousPath,
                dryRun: false,
                logger: null,
                client,
                alwaysPurgePaths: ["/index.html", "/?embedded=1"]);

            Assert.True(result.Success, result.Message);
            Assert.Equal(2, result.TargetCount);
            var files = JsonNode.Parse(Assert.Single(handler.Bodies))!["files"]!.AsArray();
            Assert.Equal(2, files.Count);
            Assert.Single(files, node => node!.GetValue<string>() == "https://example.test/index.html");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IncrementalPurge_ShouldUseHostnameFallbackWhenChangedAndAlwaysPurgeTargetsExceedLimit()
    {
        var root = NewTempDirectory();
        try
        {
            var previousPath = WriteManifest(root, "previous.json", Array.Empty<CloudflareDeploymentManifestEntry>());
            var currentPath = WriteManifest(root, "current.json", Enumerable.Range(0, CloudflareIncrementalCachePurger.MaxIncrementalTargets)
                .Select(index => Entry($"assets/{index}.js", 'a')).ToArray());
            var handler = new RecordingHandler(SuccessResponse());
            using var client = NewClient(handler);

            var result = CloudflareIncrementalCachePurger.Purge(
                ZoneId,
                "secret-token",
                "https://example.test/",
                currentPath,
                previousPath,
                dryRun: false,
                logger: null,
                client,
                alwaysPurgePaths: ["/?embedded=1"]);

            Assert.True(result.Success, result.Message);
            Assert.True(result.UsedFallback);
            Assert.Equal(CloudflareCachePurgeMode.Hostname, result.ActualMode);
            Assert.Contains("501 URL targets", result.FallbackReason, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("https://outside.test/app/")]
    [InlineData("//outside.test/app/")]
    [InlineData("/app/#fragment")]
    [InlineData("/../outside/")]
    [InlineData("/%2e%2e/outside/")]
    public void ResolveAlwaysPurgeUrl_ShouldRejectUnsafeTargets(string path)
    {
        Assert.Throws<InvalidDataException>(() =>
            CloudflareIncrementalCachePurger.ResolveAlwaysPurgeUrl("https://example.test/project/", path));
    }

    [Fact]
    public void RouteProfile_ShouldLoadAndNormalizeAlwaysPurgePaths()
    {
        var root = NewTempDirectory();
        try
        {
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath,
                """
                {
                  "Name": "Example",
                  "BaseUrl": "https://example.test/project/",
                  "Cloudflare": {
                    "PurgeMode": "incremental",
                    "AlwaysPurgePaths": [
                      " /apps/converter/ ",
                      "/apps/converter/?embedded=1",
                      "/apps/converter/"
                    ]
                  }
                }
                """);

            var profile = CloudflareRouteProfileResolver.Load(configPath);

            Assert.Equal(
                ["/apps/converter/", "/apps/converter/?embedded=1"],
                profile.Cloudflare!.AlwaysPurgePaths);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
