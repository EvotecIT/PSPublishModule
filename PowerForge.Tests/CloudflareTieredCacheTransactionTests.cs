using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class CloudflareTieredCacheTransactionTests
{
    private const string ZoneId = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void Enable_ShouldActivateParentWhenSmartTopologyIsAlreadyOn()
    {
        var handler = new SettingsHandler(parent: false, topology: true);
        using var client = NewClient(handler);

        var result = CloudflareSmartTieredCacheManager.Apply(ZoneId, "test-token", true, false, client);

        Assert.True(result.Success, result.Message);
        Assert.True(result.Changed);
        Assert.True(handler.Parent);
        Assert.True(handler.Topology);
        Assert.Equal(["argo/tiered_caching=on"], handler.Writes);
    }

    [Fact]
    public void DryRun_ShouldRejectLockedParentBeforeChangingTopology()
    {
        var handler = new SettingsHandler(parent: false, topology: false) { ParentEditable = false };
        using var client = NewClient(handler);

        var result = CloudflareSmartTieredCacheManager.Apply(ZoneId, "test-token", true, true, client);

        Assert.False(result.Success);
        Assert.Contains("not editable", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Writes);
    }

    [Fact]
    public void Disable_ShouldPreserveEnabledParent()
    {
        var handler = new SettingsHandler(parent: true, topology: true);
        using var client = NewClient(handler);

        var result = CloudflareSmartTieredCacheManager.Apply(ZoneId, "test-token", false, false, client);

        Assert.True(result.Success, result.Message);
        Assert.True(handler.Parent);
        Assert.False(handler.Topology);
        Assert.Equal(["cache/tiered_cache_smart_topology_enable=off"], handler.Writes);
    }

    [Fact]
    public void Enable_ShouldRestoreParentWhenTopologyWriteIsRejected()
    {
        var handler = new SettingsHandler(parent: false, topology: false) { RejectTopologyWrite = true };
        using var client = NewClient(handler);

        var result = CloudflareSmartTieredCacheManager.Apply(ZoneId, "test-token", true, false, client);

        Assert.False(result.Success);
        Assert.False(handler.Parent);
        Assert.False(handler.Topology);
        Assert.Contains("restored", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["argo/tiered_caching=on", "cache/tiered_cache_smart_topology_enable=on", "argo/tiered_caching=off"], handler.Writes);
    }

    private static HttpClient NewClient(SettingsHandler handler) => new(handler)
    {
        BaseAddress = new Uri("https://api.cloudflare.test/client/v4/")
    };

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Restore_ShouldRecoverTheExactOriginalPair(bool parent, bool topology)
    {
        var handler = new SettingsHandler(parent, topology);
        using var client = NewClient(handler);
        var applied = CloudflareSmartTieredCacheManager.Apply(ZoneId, "test-token", true, false, client);
        Assert.True(applied.Success, applied.Message);
        Assert.True(handler.Parent);
        Assert.True(handler.Topology);

        var restored = CloudflareSmartTieredCacheManager.Restore(ZoneId, "test-token", applied, client);

        Assert.True(restored.Success, restored.Message);
        Assert.Equal(parent, handler.Parent);
        Assert.Equal(topology, handler.Topology);
    }

    [Fact]
    public void Enable_ShouldAvoidWritesWhenBothSettingsAreAlreadyEnabled()
    {
        var handler = new SettingsHandler(parent: true, topology: true);
        using var client = NewClient(handler);

        var result = CloudflareSmartTieredCacheManager.Apply(ZoneId, "test-token", true, false, client);

        Assert.True(result.Success, result.Message);
        Assert.False(result.ChangesRequired);
        Assert.False(result.Changed);
        Assert.Empty(handler.Writes);
    }

    [Fact]
    public void SitePolicy_ShouldRestoreBothDisabledSettingsAfterHeaderFailure()
    {
        var handler = new SettingsHandler(parent: false, topology: false) { RejectHeaderWrite = true };
        using var client = NewClient(handler);

        var result = CloudflareSitePolicyManager.Apply(ZoneId, "test-token", "example.com", "Example",
            htmlPaths: null, securityHeaders: new AgentSecurityHeadersSpec { Hsts = false },
            dryRun: false, httpClient: client, cache: new CloudflareCacheSpec(), smartTieredCache: true);

        Assert.False(result.Success);
        Assert.Contains("previous site-policy state was restored", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(handler.Parent);
        Assert.False(handler.Topology);
        Assert.Equal(["argo/tiered_caching=on", "cache/tiered_cache_smart_topology_enable=on",
            "cache/tiered_cache_smart_topology_enable=off", "argo/tiered_caching=off"], handler.Writes);
    }

    private sealed class SettingsHandler(bool parent, bool topology) : HttpMessageHandler
    {
        internal bool Parent { get; private set; } = parent;
        internal bool Topology { get; private set; } = topology;
        internal bool ParentEditable { get; init; } = true;
        internal bool RejectTopologyWrite { get; init; }
        internal bool RejectHeaderWrite { get; init; }
        internal List<string> Writes { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.Contains("/rulesets/", StringComparison.Ordinal))
            {
                if (RejectHeaderWrite && request.Method == HttpMethod.Put &&
                    request.RequestUri.AbsolutePath.Contains("http_response_headers_transform", StringComparison.Ordinal))
                    return Response(HttpStatusCode.BadRequest, """{"success":false,"errors":[{"message":"invalid transform"}]}""");
                return Response(HttpStatusCode.OK, """{"success":true,"result":{"rules":[]}}""");
            }
            var isParent = request.RequestUri!.AbsolutePath.EndsWith("/argo/tiered_caching", StringComparison.Ordinal);
            var isTopology = request.RequestUri.AbsolutePath.EndsWith("/cache/tiered_cache_smart_topology_enable", StringComparison.Ordinal);
            Assert.True(isParent || isTopology, request.RequestUri.ToString());
            if (request.Method == HttpMethod.Patch)
            {
                var value = JsonNode.Parse(request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult())!["value"]!.GetValue<string>();
                Writes.Add($"{(isParent ? "argo/tiered_caching" : "cache/tiered_cache_smart_topology_enable")}={value}");
                if (isTopology && RejectTopologyWrite)
                    return Response(HttpStatusCode.Forbidden, """{"success":false,"errors":[{"message":"setting locked"}]}""");
                if (isParent) Parent = value == "on";
                else Topology = value == "on";
            }
            return Response(HttpStatusCode.OK, new JsonObject
            {
                ["success"] = true,
                ["result"] = new JsonObject
                {
                    ["value"] = (isParent ? Parent : Topology) ? "on" : "off",
                    ["editable"] = !isParent || ParentEditable
                }
            }.ToJsonString());
        }

        private static HttpResponseMessage Response(HttpStatusCode status, string json) => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
