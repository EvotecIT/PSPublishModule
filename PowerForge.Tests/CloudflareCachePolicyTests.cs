using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class CloudflareCachePolicyTests
{
    [Fact]
    public void BuildManagedRules_ShouldScopePolicyAndIncludeSiteRoutes()
    {
        var rules = CloudflareCachePolicyBuilder.BuildManagedRules(
            "tactra.dev",
            "Tactra",
            ["/privacy/", "/support/", "/sitemap.xml"]);

        Assert.Equal(3, rules.Count);
        var staticRule = Assert.IsType<JsonObject>(rules[0]);
        var htmlRule = Assert.IsType<JsonObject>(rules[2]);
        Assert.Equal("PowerForge Tactra: static assets", staticRule["description"]!.GetValue<string>());
        Assert.Contains("http.host eq \"tactra.dev\"", staticRule["expression"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.True(staticRule["action_parameters"]!["cache_key"]!["custom_key"]!["query_string"]!["exclude"]!["all"]!.GetValue<bool>());

        var htmlExpression = htmlRule["expression"]!.GetValue<string>();
        Assert.Contains("/privacy/*", htmlExpression, StringComparison.Ordinal);
        Assert.Contains("/support/*", htmlExpression, StringComparison.Ordinal);
        Assert.DoesNotContain("sitemap.xml", htmlExpression, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_ShouldReplaceManagedRulesAndPreserveUnrelatedRules()
    {
        var existingRules = new JsonArray
        {
            ExistingRule("managed-static-id", "PowerForge CodeGlyphX: static assets", "old-static"),
            ExistingRule("managed-data-id", "PowerForge CodeGlyphX: data files", "old-data"),
            ExistingRule("managed-html-id", "PowerForge CodeGlyphX: HTML docs and API", "old-html"),
            ExistingRule("custom-id", "Operator custom bypass", "custom", action: "set_cache_settings")
        };
        var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.OK, ExistingEnvelope(existingRules)),
            JsonResponse(HttpStatusCode.OK, SuccessEnvelope()));
        using var client = NewClient(handler);

        var result = CloudflareCachePolicyManager.Apply(
            "0123456789abcdef0123456789abcdef",
            "secret-token",
            "codeglyphx.com",
            "CodeGlyphX",
            ["/downloads/"],
            dryRun: false,
            logger: null,
            client);

        Assert.True(result.Success, result.Message);
        Assert.True(result.Changed);
        Assert.Equal(3, result.ManagedRuleCount);
        Assert.Equal(1, result.PreservedRuleCount);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Put, handler.Requests[1].Method);
        Assert.Equal("Bearer", handler.Requests[1].Authorization?.Scheme);
        Assert.Equal("secret-token", handler.Requests[1].Authorization?.Parameter);

        var payload = JsonNode.Parse(handler.Requests[1].Body)!.AsObject();
        var rules = payload["rules"]!.AsArray();
        Assert.Equal(4, rules.Count);
        Assert.Equal("managed-static-id", rules[0]!["id"]!.GetValue<string>());
        Assert.Contains("/downloads/*", rules[2]!["expression"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("Operator custom bypass", rules[3]!["description"]!.GetValue<string>());
        Assert.Equal("custom-id", rules[3]!["id"]!.GetValue<string>());
        Assert.Null(rules[3]!["version"]);
        Assert.Null(rules[3]!["last_updated"]);
    }

    [Fact]
    public void Apply_ShouldCreateMissingEntryPointRuleset()
    {
        var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.NotFound, """{"success":false,"errors":[{"message":"not found"}]}"""),
            JsonResponse(HttpStatusCode.OK, SuccessEnvelope()));
        using var client = NewClient(handler);

        var result = CloudflareCachePolicyManager.Apply(
            "0123456789abcdef0123456789abcdef",
            "secret-token",
            "example.com",
            "Example",
            htmlPaths: null,
            dryRun: false,
            logger: null,
            client);

        Assert.True(result.Success, result.Message);
        Assert.True(result.Changed);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.EndsWith("/zones/0123456789abcdef0123456789abcdef/rulesets", handler.Requests[1].Uri.AbsolutePath, StringComparison.Ordinal);
        var payload = JsonNode.Parse(handler.Requests[1].Body)!.AsObject();
        Assert.Equal("zone", payload["kind"]!.GetValue<string>());
        Assert.Equal("http_request_cache_settings", payload["phase"]!.GetValue<string>());
        Assert.Equal(3, payload["rules"]!.AsArray().Count);
    }

    [Fact]
    public void Apply_DryRun_ShouldReadWithoutWriting()
    {
        var existingRules = new JsonArray
        {
            ExistingRule("custom-id", "Operator custom rule", "custom")
        };
        var handler = new SequenceHandler(JsonResponse(HttpStatusCode.OK, ExistingEnvelope(existingRules)));
        using var client = NewClient(handler);

        var result = CloudflareCachePolicyManager.Apply(
            "0123456789abcdef0123456789abcdef",
            "secret-token",
            "example.com",
            "Example",
            htmlPaths: null,
            dryRun: true,
            logger: null,
            client);

        Assert.True(result.Success, result.Message);
        Assert.True(result.DryRun);
        Assert.True(result.ChangesRequired);
        Assert.False(result.Changed);
        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
    }

    [Fact]
    public void Apply_ShouldFailClosedWhenExistingRulesAreMissing()
    {
        var handler = new SequenceHandler(JsonResponse(HttpStatusCode.OK, SuccessEnvelope()));
        using var client = NewClient(handler);

        var result = CloudflareCachePolicyManager.Apply(
            "0123456789abcdef0123456789abcdef",
            "secret-token",
            "example.com",
            "Example",
            htmlPaths: null,
            dryRun: false,
            logger: null,
            client);

        Assert.False(result.Success);
        Assert.Contains("refusing to replace", result.Message, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
    }

    [Fact]
    public void Apply_ShouldFailClosedWhenExistingRuleIsMalformed()
    {
        var rules = new JsonArray { ExistingRule("custom-id", "Operator custom rule", "custom"), "not-a-rule" };
        var handler = new SequenceHandler(JsonResponse(HttpStatusCode.OK, ExistingEnvelope(rules)));
        using var client = NewClient(handler);

        var result = CloudflareCachePolicyManager.Apply(
            "0123456789abcdef0123456789abcdef",
            "secret-token",
            "example.com",
            "Example",
            htmlPaths: null,
            dryRun: false,
            logger: null,
            client);

        Assert.False(result.Success);
        Assert.Contains("malformed rule", result.Message, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void Apply_ShouldRejectInvalidZoneBeforeCallingCloudflare()
    {
        var handler = new SequenceHandler();
        using var client = NewClient(handler);

        var result = CloudflareCachePolicyManager.Apply(
            "not-a-zone-id",
            "secret-token",
            "example.com",
            "Example",
            htmlPaths: null,
            dryRun: false,
            logger: null,
            client);

        Assert.False(result.Success);
        Assert.Contains("32-character hexadecimal", result.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Apply_ShouldRejectUnsafeRoutesBeforeCallingCloudflare()
    {
        var handler = new SequenceHandler();
        using var client = NewClient(handler);

        var result = CloudflareCachePolicyManager.Apply(
            "0123456789abcdef0123456789abcdef",
            "secret-token",
            "example.com",
            "Example",
            ["/docs/*"],
            dryRun: false,
            logger: null,
            client);

        Assert.False(result.Success);
        Assert.Contains("Invalid Cloudflare HTML route", result.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Apply_ShouldAvoidRulesetVersionChurnWhenPolicyIsCurrent()
    {
        var currentRules = CloudflareCachePolicyBuilder.BuildManagedRules(
            "example.com",
            "Example",
            ["/support/"]);
        for (var i = 0; i < currentRules.Count; i++)
        {
            currentRules[i]!["id"] = $"managed-{i}";
            currentRules[i]!["ref"] = $"managed-{i}";
            currentRules[i]!["version"] = "7";
            currentRules[i]!["last_updated"] = "2026-07-16T00:00:00Z";
        }
        currentRules.Add(ExistingRule("custom-id", "Operator custom rule", "custom"));

        var handler = new SequenceHandler(JsonResponse(HttpStatusCode.OK, ExistingEnvelope(currentRules)));
        using var client = NewClient(handler);

        var result = CloudflareCachePolicyManager.Apply(
            "0123456789abcdef0123456789abcdef",
            "secret-token",
            "example.com",
            "Example",
            ["/support/"],
            dryRun: false,
            logger: null,
            client);

        Assert.True(result.Success, result.Message);
        Assert.False(result.ChangesRequired);
        Assert.False(result.Changed);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void CompositeAction_ShouldKeepCallerWorkflowDeclarativeAndTokenOutOfArguments()
    {
        var action = ReadRepoFile(".github", "actions", "powerforge-cloudflare-cache-policy", "action.yml");
        var script = ReadRepoFile(".github", "actions", "powerforge-cloudflare-cache-policy", "Invoke-PowerForgeCloudflareCachePolicy.ps1");

        Assert.Contains("Reject pull request cache changes", action, StringComparison.Ordinal);
        Assert.Contains("actions/checkout@de0fac2e4500dabe0009e67214ff5f5447ce83dd", action, StringComparison.Ordinal);
        Assert.Contains("actions/setup-dotnet@c2fa09f4bde5ebb9d1777cf28262a3eb3db3ced7", action, StringComparison.Ordinal);
        Assert.Contains("Invoke-PowerForgeCloudflareCachePolicy.ps1", action, StringComparison.Ordinal);
        Assert.Contains("--token-env', 'POWERFORGE_CLOUDFLARE_API_TOKEN'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("--token', $env:POWERFORGE_CLOUDFLARE_API_TOKEN", script, StringComparison.Ordinal);
        Assert.Contains("site-config must identify a file inside the caller repository", script, StringComparison.Ordinal);
        Assert.True(script.Split('\n').Length < 100, "The action entrypoint should remain a bounded adapter over the CLI.");
    }

    private static JsonObject ExistingRule(string id, string description, string expression, string action = "set_cache_settings") => new()
    {
        ["id"] = id,
        ["ref"] = id,
        ["version"] = "3",
        ["last_updated"] = "2026-07-15T00:00:00Z",
        ["description"] = description,
        ["expression"] = expression,
        ["action"] = action,
        ["action_parameters"] = new JsonObject { ["cache"] = true },
        ["enabled"] = true
    };

    private static string ExistingEnvelope(JsonArray rules) => new JsonObject
    {
        ["success"] = true,
        ["result"] = new JsonObject { ["rules"] = rules }
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

    private static string ReadRepoFile(params string[] relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && current is not null; i++)
        {
            if (File.Exists(Path.Combine(current.FullName, "PowerForge", "PowerForge.csproj")))
                return File.ReadAllText(Path.Combine([current.FullName, .. relativePath]));
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }

    private sealed class SequenceHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        internal List<CapturedRequest> Requests { get; } = [];

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) =>
            CaptureAndRespond(request);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(CaptureAndRespond(request));

        private HttpResponseMessage CaptureAndRespond(HttpRequestMessage request)
        {
            var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, request.Headers.Authorization, body));
            return _responses.Dequeue();
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, AuthenticationHeaderValue? Authorization, string Body);
}
