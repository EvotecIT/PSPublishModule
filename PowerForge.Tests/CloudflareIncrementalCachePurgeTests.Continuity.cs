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
    public void CompositeAction_ShouldForceHostnameFallbackWhenSitePolicyRequiresChanges()
    {
        var action = ReadRepoFile(".github", "actions", "powerforge-cloudflare-site-policy", "action.yml");
        var policyScript = ReadRepoFile(".github", "actions", "powerforge-cloudflare-site-policy", "Invoke-PowerForgeCloudflareSitePolicy.ps1");
        var purgeScript = ReadRepoFile(".github", "actions", "powerforge-cloudflare-site-policy", "Invoke-PowerForgeCloudflareIncrementalPurge.ps1");

        Assert.Contains("id: site_policy", action, StringComparison.Ordinal);
        Assert.Contains("steps.site_policy.outputs.changes_required", action, StringComparison.Ordinal);
        Assert.Contains("result.result.changesRequired", policyScript, StringComparison.Ordinal);
        Assert.Contains("changes_required=", policyScript, StringComparison.Ordinal);
        Assert.Contains("--force-hostname-fallback", purgeScript, StringComparison.Ordinal);
    }
}
