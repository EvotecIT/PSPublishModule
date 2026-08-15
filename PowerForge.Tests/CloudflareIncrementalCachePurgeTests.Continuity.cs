using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed partial class CloudflareIncrementalCachePurgeTests
{
    [Fact]
    public void IncrementalPurge_ShouldForceHostnameFallbackAfterManagedPolicyReconciliation()
    {
        var root = NewTempDirectory();
        try
        {
            var entries = new[] { Entry(string.Empty, 'a'), Entry("index.html", 'a') };
            var previousPath = WriteManifest(root, "previous.json", entries);
            var currentPath = WriteManifest(root, "current.json", entries);
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
                forcedHostnameFallbackReason: "the managed site policy was reconciled");

            Assert.True(result.Success, result.Message);
            Assert.True(result.UsedFallback);
            Assert.Contains("site policy was reconciled", result.FallbackReason, StringComparison.OrdinalIgnoreCase);
            var hosts = JsonNode.Parse(Assert.Single(handler.Bodies))!["hosts"]!.AsArray();
            Assert.Equal(["example.test"], hosts.Select(node => node!.GetValue<string>()).ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IncrementalPurge_ShouldPurgePreviousAndCurrentHostnamesAfterBaseUrlMigration()
    {
        var root = NewTempDirectory();
        try
        {
            var entries = new[] { Entry(string.Empty, 'a'), Entry("index.html", 'a') };
            var previousPath = WriteManifest(root, "previous.json", entries, baseUrl: "https://old.example.test/");
            var currentPath = WriteManifest(root, "current.json", entries, baseUrl: "https://new.example.test/");
            var handler = new RecordingHandler(SuccessResponse());
            using var client = NewClient(handler);

            var result = CloudflareIncrementalCachePurger.Purge(
                ZoneId,
                "secret-token",
                "https://new.example.test/",
                currentPath,
                previousPath,
                dryRun: false,
                logger: null,
                client);

            Assert.True(result.Success, result.Message);
            Assert.True(result.UsedFallback);
            Assert.Equal(2, result.TargetCount);
            Assert.Contains("BaseUrl", result.FallbackReason, StringComparison.Ordinal);
            var hosts = JsonNode.Parse(Assert.Single(handler.Bodies))!["hosts"]!.AsArray()
                .Select(node => node!.GetValue<string>())
                .ToArray();
            Assert.Equal(["new.example.test", "old.example.test"], hosts);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IncrementalPurge_ShouldRetainPreviousHostnameWhenDeploymentContinuityIsUnproven()
    {
        var root = NewTempDirectory();
        try
        {
            var entries = new[] { Entry(string.Empty, 'a'), Entry("index.html", 'a') };
            var previousPath = WriteManifest(root, "previous.json", entries, baseUrl: "https://old.example.test/");
            var currentPath = WriteManifest(root, "current.json", entries, baseUrl: "https://new.example.test/");
            var handler = new RecordingHandler(SuccessResponse());
            using var client = NewClient(handler);

            var result = CloudflareIncrementalCachePurger.Purge(
                ZoneId,
                "secret-token",
                "https://new.example.test/",
                currentPath,
                previousPath,
                dryRun: false,
                logger: null,
                client,
                forcedHostnameFallbackReason: "an intervening deployment could not be correlated");

            Assert.True(result.Success, result.Message);
            Assert.True(result.UsedFallback);
            Assert.Contains("intervening deployment", result.FallbackReason, StringComparison.OrdinalIgnoreCase);
            var hosts = JsonNode.Parse(Assert.Single(handler.Bodies))!["hosts"]!.AsArray()
                .Select(node => node!.GetValue<string>())
                .ToArray();
            Assert.Equal(["new.example.test", "old.example.test"], hosts);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CloudflareInspect_ShouldResolveIncrementalModeThroughSiteInheritance()
    {
        var root = NewTempDirectory();
        try
        {
            var baseConfig = Path.Combine(root, "base.json");
            var siteConfig = Path.Combine(root, "site.json");
            File.WriteAllText(baseConfig,
                """
                {
                  "Name": "Inherited Cloudflare profile",
                  "BaseUrl": "https://example.test/",
                  "Cloudflare": {
                    "PurgeMode": "incremental",
                    "Cache": { "EdgeTtlSeconds": 604800 }
                  }
                }
                """);
            File.WriteAllText(siteConfig,
                """
                {
                  "extends": "./base.json",
                  "Name": "Child site"
                }
                """);

            var profile = CloudflareRouteProfileResolver.Load(siteConfig);
            var result = WebCliCommandHandlers.BuildCloudflareInspectResult(profile);
            var exitCode = WebCliCommandHandlers.HandleSubCommand(
                "cloudflare",
                ["inspect", "--site-config", siteConfig],
                outputJson: false,
                new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(0, exitCode);
            Assert.Equal("https://example.test/", result.GetProperty("baseUrl").GetString());
            Assert.Equal("incremental", result.GetProperty("purgeMode").GetString());
            Assert.Equal(Path.GetFullPath(siteConfig), result.GetProperty("siteConfig").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CompositeAction_ShouldForceHostnameFallbackWhenSitePolicyRequiresChanges()
    {
        var action = ReadRepoFile(".github", "actions", "powerforge-cloudflare-site-policy", "action.yml");
        var policyScript = ReadRepoFile(".github", "actions", "powerforge-cloudflare-site-policy", "Invoke-PowerForgeCloudflareSitePolicy.ps1");
        var purgeScript = ReadRepoFile(".github", "actions", "powerforge-cloudflare-site-policy", "Invoke-PowerForgeCloudflareIncrementalPurge.ps1");

        Assert.Contains("id: site_policy", action, StringComparison.Ordinal);
        Assert.Contains("steps.site_policy.outputs.changes_required", action, StringComparison.Ordinal);
        Assert.Contains("result.result.changesRequired", policyScript, StringComparison.Ordinal);
        Assert.Contains("changes_required=", policyScript, StringComparison.Ordinal);
        Assert.Contains("--force-hostname-fallback-reason", purgeScript, StringComparison.Ordinal);
        Assert.Contains("POWERFORGE_CLOUDFLARE_BASELINE_REASON: ${{ steps.baseline_order.outputs.reason }}", action, StringComparison.Ordinal);
        Assert.Contains("cloudflare inspect", action, StringComparison.Ordinal);
        Assert.DoesNotContain("site.Cloudflare.PurgeMode", action, StringComparison.Ordinal);
        Assert.Contains("$previousManifestAvailable", purgeScript, StringComparison.Ordinal);
        Assert.DoesNotContain("POWERFORGE_CLOUDFLARE_USE_PREVIOUS -eq 'true'", purgeScript, StringComparison.Ordinal);
    }

    [Fact]
    public void ReusableWebsiteWorkflow_ShouldResolveInheritedIncrementalMode()
    {
        var workflow = ReadRepoFile(".github", "workflows", "powerforge-website-run.yml");

        Assert.Contains("cloudflare inspect", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("site.Cloudflare.PurgeMode", workflow, StringComparison.Ordinal);
        Assert.Contains("profile.result.purgeMode", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void IncrementalPurge_ShouldCountFailedFirstFilePurgeRequest()
    {
        var root = NewTempDirectory();
        try
        {
            var previousPath = WriteManifest(root, "previous.json", []);
            var currentPath = WriteManifest(root, "current.json", [Entry("index.html", 'a')]);
            var handler = new RecordingHandler(FailureResponse());
            using var client = NewClient(handler);

            var result = CloudflareIncrementalCachePurger.Purge(
                ZoneId,
                "secret-token",
                "https://example.test/",
                currentPath,
                previousPath,
                dryRun: false,
                logger: null,
                client);

            Assert.False(result.Success);
            Assert.Equal(1, result.RequestCount);
            Assert.Single(handler.Bodies);
            Assert.Contains("1 request attempt", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IncrementalPurge_ShouldCountFailedRequestAfterSuccessfulBatch()
    {
        var root = NewTempDirectory();
        try
        {
            var previousPath = WriteManifest(root, "previous.json", Enumerable.Range(0, 101)
                .Select(index => Entry($"docs/{index}.html", 'a')).ToArray());
            var currentPath = WriteManifest(root, "current.json", Enumerable.Range(0, 101)
                .Select(index => Entry($"docs/{index}.html", 'b')).ToArray());
            var handler = new RecordingHandler(SuccessResponse(), FailureResponse());
            using var client = NewClient(handler);

            var result = CloudflareIncrementalCachePurger.Purge(
                ZoneId,
                "secret-token",
                "https://example.test/",
                currentPath,
                previousPath,
                dryRun: false,
                logger: null,
                client);

            Assert.False(result.Success);
            Assert.Equal(2, result.RequestCount);
            Assert.Equal(2, handler.Bodies.Count);
            Assert.Contains("2 request attempt", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IncrementalPurge_ShouldCountFailedHostnameFallbackRequest()
    {
        var root = NewTempDirectory();
        try
        {
            var currentPath = WriteManifest(root, "current.json", [Entry("index.html", 'a')]);
            var handler = new RecordingHandler(FailureResponse());
            using var client = NewClient(handler);

            var result = CloudflareIncrementalCachePurger.Purge(
                ZoneId,
                "secret-token",
                "https://example.test/",
                currentPath,
                previousManifestPath: null,
                dryRun: false,
                logger: null,
                client);

            Assert.False(result.Success);
            Assert.True(result.UsedFallback);
            Assert.Equal(1, result.RequestCount);
            Assert.Single(handler.Bodies);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IncrementalPurge_ShouldNotCountLocallyRejectedRequest()
    {
        var root = NewTempDirectory();
        try
        {
            var previousPath = WriteManifest(root, "previous.json", []);
            var currentPath = WriteManifest(root, "current.json", [Entry("index.html", 'a')]);
            var handler = new RecordingHandler();
            using var client = NewClient(handler);

            var result = CloudflareIncrementalCachePurger.Purge(
                "invalid-zone",
                "secret-token",
                "https://example.test/",
                currentPath,
                previousPath,
                dryRun: false,
                logger: null,
                client);

            Assert.False(result.Success);
            Assert.Equal(0, result.RequestCount);
            Assert.Empty(handler.Bodies);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static HttpResponseMessage FailureResponse() => new(HttpStatusCode.BadRequest)
    {
        Content = new StringContent(
            "{\"success\":false,\"errors\":[{\"code\":1000,\"message\":\"denied\"}],\"messages\":[],\"result\":null}",
            Encoding.UTF8,
            "application/json")
    };
}
