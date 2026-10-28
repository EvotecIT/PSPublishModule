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
    public void EnabledSmartShield_ShouldIgnoreLockedLegacyArgoSetting()
    {
        var handler = new SettingsHandler(enabled: true);
        using var client = NewClient(handler);

        var result = CloudflareSmartTieredCacheManager.Apply(ZoneId, "test-token", true, false, client);

        Assert.True(result.Success, result.Message);
        Assert.False(result.ChangesRequired);
        Assert.False(result.Changed);
        Assert.Empty(handler.Writes);
        Assert.Equal([$"/client/v4/zones/{ZoneId}/smart_shield"], handler.Paths);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Apply_ShouldChangeOnlySmartTieredCacheAndVerifyItsValue(bool enabled)
    {
        var handler = new SettingsHandler(!enabled);
        using var client = NewClient(handler);

        var result = CloudflareSmartTieredCacheManager.Apply(ZoneId, "test-token", enabled, false, client);

        Assert.True(result.Success, result.Message);
        Assert.True(result.Changed);
        Assert.Equal(enabled, handler.Enabled);
        var body = JsonNode.Parse(Assert.Single(handler.Writes))!.AsObject();
        Assert.Equal("smart_tiered_cache", Assert.Single(body).Key);
        Assert.Equal(enabled ? "on" : "off", body["smart_tiered_cache"]!["value"]!.GetValue<string>());
        Assert.Equal(3, handler.Paths.Count);
    }

    [Fact]
    public void DryRun_ShouldPreflightEditableSmartShieldWithoutWriting()
    {
        var handler = new SettingsHandler(false);
        using var client = NewClient(handler);

        var result = CloudflareSmartTieredCacheManager.Apply(ZoneId, "test-token", true, true, client);

        Assert.True(result.Success, result.Message);
        Assert.True(result.ChangesRequired);
        Assert.False(handler.Enabled);
        Assert.Empty(handler.Writes);
    }

    [Fact]
    public void DryRun_ShouldRejectLockedSmartTieredCache()
    {
        var handler = new SettingsHandler(false) { Editable = false };
        using var client = NewClient(handler);

        var result = CloudflareSmartTieredCacheManager.Apply(ZoneId, "test-token", true, true, client);

        Assert.False(result.Success);
        Assert.Contains("not editable", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Restore_ShouldRecoverTheOriginalSmartTieredCacheValue(bool previous)
    {
        var handler = new SettingsHandler(previous);
        using var client = NewClient(handler);
        var applied = CloudflareSmartTieredCacheManager.Apply(ZoneId, "test-token", !previous, false, client);
        Assert.True(applied.Success, applied.Message);

        var restored = CloudflareSmartTieredCacheManager.Restore(ZoneId, "test-token", applied, client);

        Assert.True(restored.Success, restored.Message);
        Assert.Equal(previous, handler.Enabled);
        Assert.Equal(2, handler.Writes.Count);
    }

    [Fact]
    public void RejectedWrite_ShouldLeavePreviousStateWithoutSpeculativeRollback()
    {
        var handler = new SettingsHandler(false) { RejectWrites = true };
        using var client = NewClient(handler);

        var result = CloudflareSmartTieredCacheManager.Apply(ZoneId, "test-token", true, false, client);

        Assert.False(result.Success);
        Assert.False(handler.Enabled);
        Assert.Single(handler.Writes);
        Assert.Equal(2, handler.Paths.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LostWriteResponse_ShouldReconcileTheObservedState(bool committed)
    {
        var handler = new SettingsHandler(false) { LoseWriteResponse = true, CommitLostWrite = committed };
        using var client = NewClient(handler);

        var result = CloudflareSmartTieredCacheManager.Apply(ZoneId, "test-token", true, false, client);

        Assert.Equal(committed, result.Success);
        Assert.Equal(committed, handler.Enabled);
        Assert.Single(handler.Writes);
        Assert.Equal(3, handler.Paths.Count);
    }

    [Fact]
    public void UnreadableWriteVerification_ShouldRestoreTheOriginalValue()
    {
        var handler = new SettingsHandler(false) { LoseFirstVerification = true };
        using var client = NewClient(handler);

        var result = CloudflareSmartTieredCacheManager.Apply(ZoneId, "test-token", true, false, client);

        Assert.False(result.Success);
        Assert.Contains("was restored", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(handler.Enabled);
        Assert.Equal(2, handler.Writes.Count);
    }

    [Fact]
    public void Restore_ShouldReportASettingThatBecomesLocked()
    {
        var handler = new SettingsHandler(false);
        using var client = NewClient(handler);
        var applied = CloudflareSmartTieredCacheManager.Apply(ZoneId, "test-token", true, false, client);
        handler.Editable = false;

        var restored = CloudflareSmartTieredCacheManager.Restore(ZoneId, "test-token", applied, client);

        Assert.False(restored.Success);
        Assert.True(handler.Enabled);
        Assert.Single(handler.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Restore_ShouldKeepOriginalTargetWhenVerificationIsUnavailable(bool loseWriteResponse)
    {
        var handler = new SettingsHandler(false)
        {
            LoseRestoreVerification = true,
            LoseRestoreWriteResponse = loseWriteResponse
        };
        using var client = NewClient(handler);
        var applied = CloudflareSmartTieredCacheManager.Apply(ZoneId, "test-token", true, false, client);
        Assert.True(applied.Success, applied.Message);

        var restored = CloudflareSmartTieredCacheManager.Restore(ZoneId, "test-token", applied, client);

        Assert.False(restored.Success);
        Assert.False(handler.Enabled);
        Assert.Equal(2, handler.Writes.Count);
        Assert.Equal("off", JsonNode.Parse(handler.Writes[1])!["smart_tiered_cache"]!["value"]!.GetValue<string>());
    }

    [Fact]
    public void SitePolicy_ShouldNotUndoRestorationAfterLostVerification()
    {
        var handler = new SettingsHandler(false) { RejectHeaderWrite = true, LoseRestoreVerification = true };
        using var client = NewClient(handler);

        var result = CloudflareSitePolicyManager.Apply(ZoneId, "test-token", "example.com", "Example",
            htmlPaths: null, securityHeaders: new AgentSecurityHeadersSpec { Hsts = false },
            dryRun: false, httpClient: client, cache: new CloudflareCacheSpec(), smartTieredCache: true);

        Assert.False(result.Success);
        Assert.Contains("Rollback was incomplete", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("previous site-policy state was restored", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(handler.Enabled);
        Assert.Equal(2, handler.Writes.Count);
    }

    [Fact]
    public void SitePolicy_ShouldRestoreSmartTieredCacheAfterHeaderFailure()
    {
        var handler = new SettingsHandler(false) { RejectHeaderWrite = true };
        using var client = NewClient(handler);

        var result = CloudflareSitePolicyManager.Apply(ZoneId, "test-token", "example.com", "Example",
            htmlPaths: null, securityHeaders: new AgentSecurityHeadersSpec { Hsts = false },
            dryRun: false, httpClient: client, cache: new CloudflareCacheSpec(), smartTieredCache: true);

        Assert.False(result.Success);
        Assert.Contains("previous site-policy state was restored", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(handler.Enabled);
        Assert.Equal(2, handler.Writes.Count);
    }

    private static HttpClient NewClient(SettingsHandler handler) => new(handler)
    {
        BaseAddress = new Uri("https://api.cloudflare.test/client/v4/")
    };

    private sealed class SettingsHandler(bool enabled) : HttpMessageHandler
    {
        internal bool Enabled { get; private set; } = enabled;
        internal bool Editable { get; set; } = true;
        internal bool RejectWrites { get; init; }
        internal bool RejectHeaderWrite { get; init; }
        internal bool LoseWriteResponse { get; init; }
        internal bool CommitLostWrite { get; init; }
        internal bool LoseFirstVerification { get; init; }
        internal bool LoseRestoreVerification { get; init; }
        internal bool LoseRestoreWriteResponse { get; init; }
        internal List<string> Writes { get; } = [];
        internal List<string> Paths { get; } = [];
        private bool verificationLost;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            if (path.Contains("/rulesets/", StringComparison.Ordinal))
            {
                if (RejectHeaderWrite && request.Method == HttpMethod.Put &&
                    path.Contains("http_response_headers_transform", StringComparison.Ordinal))
                    return Response(HttpStatusCode.BadRequest, """{"success":false,"errors":[{"message":"invalid transform"}]}""");
                return Response(HttpStatusCode.OK, """{"success":true,"result":{"rules":[]}}""");
            }
            // This is the real legacy shape seen on Free and Pro zones whose Smart Shield value is on.
            if (path.EndsWith("/argo/tiered_caching", StringComparison.Ordinal))
                return Response(HttpStatusCode.OK, """{"success":true,"result":{"value":"off","editable":false}}""");
            Assert.EndsWith("/smart_shield", path, StringComparison.Ordinal);
            if (request.Method == HttpMethod.Patch)
            {
                var body = request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
                Writes.Add(body);
                if (RejectWrites)
                    return Response(HttpStatusCode.Forbidden, """{"success":false,"errors":[{"message":"setting locked"}]}""");
                if (!LoseWriteResponse || CommitLostWrite)
                    Enabled = JsonNode.Parse(body)!["smart_tiered_cache"]!["value"]!.GetValue<string>() == "on";
                if (LoseWriteResponse || (LoseRestoreWriteResponse && Writes.Count == 2))
                    throw new HttpRequestException("response lost");
            }
            else if (LoseRestoreVerification && Writes.Count == 2)
                return Response(HttpStatusCode.InternalServerError, """{"success":false,"errors":[{"message":"verification unavailable"}]}""");
            else if (LoseFirstVerification && Writes.Count == 1 && !verificationLost)
            {
                verificationLost = true;
                return Response(HttpStatusCode.OK, """{"success":true,"result":{}}""");
            }
            return Response(HttpStatusCode.OK, new JsonObject
            {
                ["success"] = true,
                ["result"] = new JsonObject
                {
                    ["smart_tiered_cache"] = new JsonObject { ["value"] = Enabled ? "on" : "off", ["editable"] = Editable },
                    ["smart_routing"] = new JsonObject { ["value"] = "off" }
                }
            }.ToJsonString());
        }

        private static HttpResponseMessage Response(HttpStatusCode status, string json) => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
