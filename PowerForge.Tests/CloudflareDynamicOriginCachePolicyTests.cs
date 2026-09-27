using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class CloudflareDynamicOriginCachePolicyTests
{
    [Fact]
    public void BuildManagedRules_CachesOnlyExplicitAnonymousPathsWhenOriginAllowsIt()
    {
        var rules = CloudflareDynamicOriginCachePolicyBuilder.BuildManagedRules(
            "changeintel.xyz", "ChangeIntel", ["/sources", "/sitemap.xml", "/sources"]);

        var rule = Assert.IsType<JsonObject>(Assert.Single(rules));
        var expression = rule["expression"]!.GetValue<string>();
        Assert.Contains("http.host eq \"changeintel.xyz\"", expression, StringComparison.Ordinal);
        Assert.Contains("http.request.uri.path eq \"/sources\"", expression, StringComparison.Ordinal);
        Assert.Contains("http.request.uri.path eq \"/sitemap.xml\"", expression, StringComparison.Ordinal);
        Assert.Equal(1, Count(expression, "http.request.uri.path eq \"/sources\""));
        Assert.DoesNotContain("http.request.uri.path wildcard", expression, StringComparison.Ordinal);
        Assert.DoesNotContain("http.request.uri.path eq \"/\"", expression, StringComparison.Ordinal);
        Assert.Contains("not http.request.headers.truncated", expression, StringComparison.Ordinal);
        Assert.Contains("not any(lower(http.request.headers.names[*])[*] eq \"cookie\")", expression, StringComparison.Ordinal);
        Assert.Contains("not any(lower(http.request.headers.names[*])[*] eq \"authorization\")", expression, StringComparison.Ordinal);
        Assert.Equal("bypass_by_default", rule["action_parameters"]!["edge_ttl"]!["mode"]!.GetValue<string>());
        Assert.Equal("respect_origin", rule["action_parameters"]!["browser_ttl"]!["mode"]!.GetValue<string>());
        Assert.Null(rule["action_parameters"]!["edge_ttl"]!["default"]);
        Assert.Null(rule["action_parameters"]!["cache_key"]);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/sources/*")]
    [InlineData("/sources?view=all")]
    [InlineData("/sources%2Fprivate")]
    [InlineData("/sources/../team")]
    [InlineData("/sources//team")]
    [InlineData("/sources#fragment")]
    public void BuildManagedRules_RejectsNonExactOrAmbiguousPaths(string path)
    {
        Assert.Throws<ArgumentException>(() =>
            CloudflareDynamicOriginCachePolicyBuilder.BuildManagedRules("example.com", "Example", [path]));
    }

    [Fact]
    public void BuildManagedRules_RejectsPathsOutsideBasePath()
    {
        Assert.Throws<ArgumentException>(() =>
            CloudflareDynamicOriginCachePolicyBuilder.BuildManagedRules("example.com", "Example", ["/sources"], "/product/"));
        var rule = Assert.Single(CloudflareDynamicOriginCachePolicyBuilder.BuildManagedRules(
            "example.com", "Example", ["/product/sources"], "/product/"));
        Assert.Contains("/product/sources", rule!["expression"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_DryRunPreservesOtherRulesAndNeverWrites()
    {
        var existing = new JsonObject
        {
            ["success"] = true,
            ["result"] = new JsonObject
            {
                ["rules"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = "operator-rule",
                        ["description"] = "Operator bypass",
                        ["expression"] = "true",
                        ["action"] = "set_cache_settings",
                        ["action_parameters"] = new JsonObject { ["cache"] = false },
                        ["enabled"] = true
                    }
                }
            }
        };
        using var handler = new CountingHandler(existing.ToJsonString());
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.cloudflare.test/client/v4/") };

        var result = CloudflareCachePolicyManager.Apply(
            "0123456789abcdef0123456789abcdef", "test-token", "example.com", "Example",
            htmlPaths: null, dryRun: true, logger: null, httpClient: client,
            originRespectingDynamic: true, publicPaths: ["/sources"]);

        Assert.True(result.Success, result.Message);
        Assert.True(result.ChangesRequired);
        Assert.False(result.Changed);
        Assert.Equal(1, result.ManagedRuleCount);
        Assert.Equal(1, result.PreservedRuleCount);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(HttpMethod.Get, handler.LastMethod);
    }

    [Fact]
    public void Apply_RejectsUnsafeCombinationBeforeNetwork()
    {
        using var handler = new CountingHandler("{}");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.cloudflare.test/client/v4/") };
        var result = CloudflareCachePolicyManager.Apply(
            "0123456789abcdef0123456789abcdef", "test-token", "example.com", "Example",
            htmlPaths: ["/docs"], dryRun: false, logger: null, httpClient: client,
            originRespectingDynamic: true, publicPaths: ["/sources"]);
        Assert.False(result.Success);
        Assert.Equal(0, handler.RequestCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Apply_RefusesUnownedCacheEnablingRuleBeforeWrite(bool cacheFlagPresent)
    {
        var parameters = new JsonObject();
        if (cacheFlagPresent)
            parameters["cache"] = true;
        else
            parameters["edge_ttl"] = new JsonObject { ["mode"] = "override_origin", ["default"] = 7200 };
        var existing = ExistingRules(new JsonObject
        {
            ["description"] = "Operator Cache Everything",
            ["expression"] = "true",
            ["action"] = "set_cache_settings",
            ["action_parameters"] = parameters,
            ["enabled"] = true
        });
        using var handler = new CountingHandler(existing);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.cloudflare.test/client/v4/") };

        var result = CloudflareCachePolicyManager.Apply(
            "0123456789abcdef0123456789abcdef", "test-token", "example.com", "Example",
            htmlPaths: null, dryRun: false, logger: null, httpClient: client,
            originRespectingDynamic: true, publicPaths: ["/sources"]);

        Assert.False(result.Success);
        Assert.Contains("could override", result.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(HttpMethod.Get, handler.LastMethod);
    }

    [Fact]
    public void Apply_RefusesImplicitStaticReplacementOfDynamicRule()
    {
        var dynamicRule = Assert.IsType<JsonObject>(Assert.Single(
            CloudflareDynamicOriginCachePolicyBuilder.BuildManagedRules("example.com", "Example", ["/sources"])));
        using var handler = new CountingHandler(ExistingRules(dynamicRule));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.cloudflare.test/client/v4/") };

        var result = CloudflareCachePolicyManager.Apply(
            "0123456789abcdef0123456789abcdef", "test-token", "example.com", "Example",
            htmlPaths: null, dryRun: false, logger: null, httpClient: client);

        Assert.False(result.Success);
        Assert.Contains("cannot replace it implicitly", result.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(HttpMethod.Get, handler.LastMethod);
    }

    [Fact]
    public void Apply_LeavesOperatorBypassAfterDynamicRuleWhenReplacingInterleavedStaticRules()
    {
        var staticRules = CloudflareCachePolicyBuilder.BuildManagedRules("example.com", "Example", null);
        var bypass = new JsonObject
        {
            ["id"] = "operator-bypass",
            ["description"] = "Operator bypass /sources",
            ["expression"] = "http.request.uri.path eq \"/sources\"",
            ["action"] = "set_cache_settings",
            ["action_parameters"] = new JsonObject { ["cache"] = false },
            ["enabled"] = true
        };
        var existing = new JsonArray(
            staticRules[0]!.DeepClone(), bypass, staticRules[1]!.DeepClone(), staticRules[2]!.DeepClone());
        using var handler = new CountingHandler(ExistingRules(existing), """{"success":true,"result":{}}""");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.cloudflare.test/client/v4/") };

        var result = CloudflareCachePolicyManager.Apply(
            "0123456789abcdef0123456789abcdef", "test-token", "example.com", "Example",
            htmlPaths: null, dryRun: false, logger: null, httpClient: client,
            originRespectingDynamic: true, publicPaths: ["/sources"]);

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.ManagedRuleCount);
        Assert.Equal(1, result.PreservedRuleCount);
        Assert.Equal(2, handler.RequestCount);
        var rules = JsonNode.Parse(handler.LastBody!)!["rules"]!.AsArray();
        Assert.Equal(2, rules.Count);
        Assert.Contains("anonymous public routes", rules[0]!["description"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("operator-bypass", rules[1]!["id"]!.GetValue<string>());
        Assert.False(rules[1]!["action_parameters"]!["cache"]!.GetValue<bool>());
    }

    private static int Count(string source, string value) =>
        (source.Length - source.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;

    private static string ExistingRules(JsonObject rule) => ExistingRules(new JsonArray(rule.DeepClone()));

    private static string ExistingRules(JsonArray rules) => new JsonObject
    {
        ["success"] = true,
        ["result"] = new JsonObject { ["rules"] = rules.DeepClone() }
    }.ToJsonString();

    private sealed class CountingHandler(params string[] responseBodies) : HttpMessageHandler
    {
        private readonly Queue<string> _responseBodies = new(responseBodies);
        internal int RequestCount { get; private set; }
        internal HttpMethod? LastMethod { get; private set; }
        internal string? LastBody { get; private set; }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastMethod = request.Method;
            LastBody = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBodies.Dequeue(), Encoding.UTF8, "application/json")
            };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }
}
