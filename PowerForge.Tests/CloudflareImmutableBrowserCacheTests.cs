using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class CloudflareImmutableBrowserCacheTests
{
    private static readonly string[] FrameworkPatterns =
    [
        "/apps/converter/_framework/*.*.wasm",
        "/apps/converter/_framework/*.*.js",
        "/apps/converter/_framework/*.*.dat"
    ];

    [Fact]
    public void BuildManagedRules_ShouldEmitStatusGatedImmutableCacheControlRule()
    {
        var rules = CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(
            "officeimo.com",
            "OfficeIMO",
            new AgentSecurityHeadersSpec { Enabled = false },
            immutablePaths: FrameworkPatterns);

        var rule = Assert.IsType<JsonObject>(Assert.Single(rules));
        Assert.Equal("PowerForge [officeimo.com/]: OfficeIMO: immutable browser cache", rule["description"]!.GetValue<string>());
        Assert.Equal("rewrite", rule["action"]!.GetValue<string>());
        Assert.True(rule["enabled"]!.GetValue<bool>());
        Assert.Equal(
            "(http.host eq \"officeimo.com\" and http.response.code eq 200 and (" +
            "http.request.uri.path wildcard \"/apps/converter/_framework/*.*.wasm\" or " +
            "http.request.uri.path wildcard \"/apps/converter/_framework/*.*.js\" or " +
            "http.request.uri.path wildcard \"/apps/converter/_framework/*.*.dat\"))",
            rule["expression"]!.GetValue<string>());
        var header = rule["action_parameters"]!["headers"]!["Cache-Control"]!;
        Assert.Equal("set", header["operation"]!.GetValue<string>());
        Assert.Equal("public, max-age=31536000, immutable", header["value"]!.GetValue<string>());
    }

    [Fact]
    public void BuildManagedRules_ShouldNotEmitImmutableRuleWithoutExplicitPatterns()
    {
        var withoutPatterns = CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(
            "example.com", "Example", new AgentSecurityHeadersSpec { Hsts = false });
        var withBlankPatterns = CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(
            "example.com", "Example", new AgentSecurityHeadersSpec { Hsts = false }, immutablePaths: [" ", ""]);

        Assert.True(JsonNode.DeepEquals(withoutPatterns, withBlankPatterns));
        Assert.DoesNotContain(withoutPatterns, rule =>
            rule!["description"]!.GetValue<string>().EndsWith("immutable browser cache", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildManagedRules_ShouldScopeImmutablePatternsToBasePath()
    {
        var rules = CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(
            "example.com",
            "Project",
            new AgentSecurityHeadersSpec { Enabled = false },
            "/project/",
            immutablePaths: ["/_framework/*.*.wasm", "/project/assets/*.*.css", "/_framework/*.*.wasm"]);

        var expression = Assert.Single(rules)!["expression"]!.GetValue<string>();
        Assert.Equal(
            "(http.host eq \"example.com\" and http.response.code eq 200 and (" +
            "http.request.uri.path wildcard \"/project/_framework/*.*.wasm\" or " +
            "http.request.uri.path wildcard \"/project/assets/*.*.css\"))",
            expression);
    }

    [Theory]
    [InlineData("app/_framework/x.wasm")]
    [InlineData("/app/_framework/*")]
    [InlineData("/app/_framework/")]
    [InlineData("/app/_framework/*.*")]
    [InlineData("/app/index.html")]
    [InlineData("/app/*.HTM")]
    [InlineData("/app/../secret.js")]
    [InlineData("//cdn.example/app.js")]
    [InlineData("/app/app.js?v=1")]
    [InlineData("/app/app.js#hash")]
    [InlineData("/app/app name.js")]
    [InlineData("/app/app.\"js")]
    [InlineData("/app\\app.js")]
    public void BuildManagedRules_ShouldRejectBroadOrMalformedImmutablePatterns(string pattern)
    {
        var exception = Assert.Throws<ArgumentException>(() => CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(
            "example.com", "Example", new AgentSecurityHeadersSpec { Enabled = false }, immutablePaths: [pattern]));

        Assert.Contains("Invalid Cloudflare immutable path", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/app/**/*.js")]
    [InlineData("/app/**.*.wasm")]
    [InlineData("/app/%2A%2a.js")]
    [InlineData("/app/*%2A.js")]
    [InlineData("/app/%2a*.js")]
    public void Apply_ShouldRejectConsecutiveWildcardsBeforeRequestingRules(string pattern)
    {
        var handler = new SequenceHandler();
        using var client = NewClient(handler);
        var result = CloudflareResponseHeaderPolicyManager.Apply(
            "0123456789abcdef0123456789abcdef", "synthetic-token", "example.com", "Example",
            new AgentSecurityHeadersSpec { Enabled = false }, dryRun: false, client, immutablePaths: [pattern]);

        Assert.False(result.Success);
        Assert.Contains("consecutive wildcard", result.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void BuildManagedRules_ShouldBoundImmutablePatternCount()
    {
        var patterns = Enumerable.Range(0, 33).Select(index => $"/assets/file{index}.*.js").ToArray();

        var exception = Assert.Throws<ArgumentException>(() => CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(
            "example.com", "Example", new AgentSecurityHeadersSpec { Enabled = false }, immutablePaths: patterns));

        Assert.Contains("at most 32", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FrameworkPatterns_ShouldMatchFingerprintedFilesButNotTheDotnetLoader()
    {
        // Mirrors Cloudflare's case-insensitive wildcard operator, where '*' matches any characters.
        bool Matches(string path) => FrameworkPatterns.Any(pattern =>
            Regex.IsMatch(path, "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal) + "$", RegexOptions.IgnoreCase));

        Assert.True(Matches("/apps/converter/_framework/dotnet.native.9l2nig3v99.wasm"));
        Assert.True(Matches("/apps/converter/_framework/dotnet.native.a86z4uitf0.js"));
        Assert.True(Matches("/apps/converter/_framework/dotnet.runtime.v06hirbjsv.js"));
        Assert.True(Matches("/apps/converter/_framework/DocumentFormat.OpenXml.0j9cmps5ul.wasm"));
        Assert.True(Matches("/apps/converter/_framework/icudt_CJK.tjcz0u77k5.dat"));
        Assert.False(Matches("/apps/converter/_framework/dotnet.js"));
        Assert.False(Matches("/apps/converter/engine-worker.js"));
        Assert.False(Matches("/apps/converter/index.html"));
    }

    [Fact]
    public void Apply_ShouldUpdateExistingImmutableRuleInPlace()
    {
        var previous = CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(
            "example.com", "Example", new AgentSecurityHeadersSpec { Hsts = false }, immutablePaths: ["/app/_framework/*.*.wasm"]);
        previous[0]!["id"] = "security-id";
        previous[1]!["id"] = "immutable-id";
        var operatorRule = new JsonObject
        {
            ["id"] = "operator-id",
            ["description"] = "Operator header rule",
            ["expression"] = "true",
            ["action"] = "rewrite",
            ["action_parameters"] = new JsonObject { ["headers"] = new JsonObject() },
            ["enabled"] = true
        };
        var existing = new JsonArray(previous[0]!.DeepClone(), previous[1]!.DeepClone(), operatorRule);
        var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.OK, ExistingEnvelope(existing)),
            JsonResponse(HttpStatusCode.OK, SuccessEnvelope()));
        using var client = NewClient(handler);

        var result = CloudflareResponseHeaderPolicyManager.Apply(
            "0123456789abcdef0123456789abcdef",
            "secret-token",
            "example.com",
            "Example",
            new AgentSecurityHeadersSpec { Hsts = false },
            dryRun: false,
            client,
            immutablePaths: FrameworkPatterns);

        Assert.True(result.Success, result.Message);
        Assert.True(result.Changed);
        Assert.Equal(2, result.ManagedRuleCount);
        Assert.Equal(1, result.PreservedRuleCount);
        var rules = JsonNode.Parse(handler.Requests[1].Body)!["rules"]!.AsArray();
        Assert.Equal(3, rules.Count);
        var immutable = Assert.Single(rules, rule =>
            rule!["description"]!.GetValue<string>().EndsWith("immutable browser cache", StringComparison.Ordinal))!;
        Assert.Equal("immutable-id", immutable["id"]!.GetValue<string>());
        Assert.Contains("/apps/converter/_framework/*.*.dat", immutable["expression"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("operator-id", rules[2]!["id"]!.GetValue<string>());
    }

    [Fact]
    public void Apply_ShouldNotWriteWhenImmutableRuleIsCurrent()
    {
        var current = CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(
            "example.com", "Example", new AgentSecurityHeadersSpec { Hsts = false }, immutablePaths: FrameworkPatterns);
        var handler = new SequenceHandler(JsonResponse(HttpStatusCode.OK, ExistingEnvelope(current)));
        using var client = NewClient(handler);

        var result = CloudflareResponseHeaderPolicyManager.Apply(
            "0123456789abcdef0123456789abcdef",
            "secret-token",
            "example.com",
            "Example",
            new AgentSecurityHeadersSpec { Hsts = false },
            dryRun: false,
            client,
            immutablePaths: FrameworkPatterns);

        Assert.True(result.Success, result.Message);
        Assert.False(result.ChangesRequired);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void Apply_ShouldRemoveImmutableRuleWhenPatternsAreCleared()
    {
        var previous = CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(
            "example.com", "Example", new AgentSecurityHeadersSpec { Enabled = false }, immutablePaths: FrameworkPatterns);
        var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.OK, ExistingEnvelope(previous)),
            JsonResponse(HttpStatusCode.OK, SuccessEnvelope()));
        using var client = NewClient(handler);

        var result = CloudflareResponseHeaderPolicyManager.Apply(
            "0123456789abcdef0123456789abcdef",
            "secret-token",
            "example.com",
            "Example",
            new AgentSecurityHeadersSpec { Enabled = false },
            dryRun: false,
            client);

        Assert.True(result.Success, result.Message);
        Assert.Empty(JsonNode.Parse(handler.Requests[1].Body)!["rules"]!.AsArray());
    }

    [Fact]
    public void SitePolicyDryRun_ShouldIncludeImmutableRuleWithoutWriting()
    {
        var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.OK, ExistingEnvelope(new JsonArray())),
            JsonResponse(HttpStatusCode.OK, ExistingEnvelope(new JsonArray())));
        using var client = NewClient(handler);

        var result = CloudflareSitePolicyManager.Apply(
            "0123456789abcdef0123456789abcdef",
            "secret-token",
            "officeimo.com",
            "OfficeIMO",
            htmlPaths: null,
            securityHeaders: new AgentSecurityHeadersSpec { Hsts = false },
            dryRun: true,
            httpClient: client,
            cache: new CloudflareCacheSpec(),
            immutablePaths: FrameworkPatterns);

        Assert.True(result.Success, result.Message);
        Assert.True(result.ChangesRequired);
        Assert.Equal(2, result.ResponseHeaderManagedRuleCount);
        Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
        Assert.Contains("http_response_headers_transform", handler.Requests[1].Uri.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public void ImmutablePaths_ShouldNotChangeTheEdgeCachePolicyFingerprint()
    {
        var withoutPaths = CloudflareDeploymentManifestStore.ComputeCachePolicyFingerprint(
            "https://officeimo.com/", null, new CloudflareSitePolicySpec { Cache = new CloudflareCacheSpec() });
        var withPaths = CloudflareDeploymentManifestStore.ComputeCachePolicyFingerprint(
            "https://officeimo.com/", null, new CloudflareSitePolicySpec { Cache = new CloudflareCacheSpec(), ImmutablePaths = FrameworkPatterns });

        Assert.Equal(withoutPaths, withPaths);
    }

    [Fact]
    public void RouteProfile_ShouldLoadAndValidateImmutablePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-cloudflare-immutable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath,
                """
                {
                  "Name": "OfficeIMO",
                  "BaseUrl": "https://officeimo.com/",
                  "Cloudflare": {
                    "ImmutablePaths": [ " /apps/converter/_framework/*.*.wasm ", "/apps/converter/_framework/*.*.wasm", "" ]
                  }
                }
                """);

            var profile = CloudflareRouteProfileResolver.Load(configPath);

            Assert.Equal(new[] { "/apps/converter/_framework/*.*.wasm" }, profile.Cloudflare!.ImmutablePaths);

            File.WriteAllText(configPath,
                """
                {
                  "Name": "OfficeIMO",
                  "BaseUrl": "https://officeimo.com/",
                  "Cloudflare": { "ImmutablePaths": [ "/apps/converter/*" ] }
                }
                """);

            var exception = Assert.Throws<InvalidOperationException>(() => CloudflareRouteProfileResolver.Load(configPath));
            Assert.Contains("Invalid Cloudflare immutable path", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string ExistingEnvelope(JsonArray rules) => new JsonObject
    {
        ["success"] = true,
        ["result"] = new JsonObject { ["rules"] = rules.DeepClone() }
    }.ToJsonString();

    private static string SuccessEnvelope() => new JsonObject
    {
        ["success"] = true,
        ["result"] = new JsonObject()
    }.ToJsonString();

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) => new(statusCode)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static HttpClient NewClient(SequenceHandler handler) => new(handler)
    {
        BaseAddress = new Uri("https://api.cloudflare.test/client/v4/")
    };

    private sealed class SequenceHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        internal List<CapturedRequest> Requests { get; } = [];

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) => CaptureAndRespond(request);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(CaptureAndRespond(request));

        private HttpResponseMessage CaptureAndRespond(HttpRequestMessage request)
        {
            var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, body));
            return _responses.Dequeue();
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, string Body);
}
