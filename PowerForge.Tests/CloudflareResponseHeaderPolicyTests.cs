using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class CloudflareResponseHeaderPolicyTests
{
    [Fact]
    public void BuildManagedRules_ShouldEmitConfiguredBaselineWithoutHsts()
    {
        var rules = CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(
            "officeimo.com",
            "OfficeIMO",
            new AgentSecurityHeadersSpec
            {
                Hsts = false,
                ContentSecurityPolicyValue = "default-src 'self'; frame-ancestors 'self'",
                XFrameOptionsValue = "SAMEORIGIN"
            });

        var rule = Assert.IsType<JsonObject>(Assert.Single(rules));
        var headers = rule["action_parameters"]!["headers"]!.AsObject();
        Assert.False(headers.ContainsKey("Strict-Transport-Security"));
        Assert.Equal("nosniff", headers["X-Content-Type-Options"]!["value"]!.GetValue<string>());
        Assert.Equal("SAMEORIGIN", headers["X-Frame-Options"]!["value"]!.GetValue<string>());
        Assert.Equal("camera=(), geolocation=(), microphone=(), payment=(), usb=()", headers["Permissions-Policy"]!["value"]!.GetValue<string>());
        Assert.Equal("(http.host eq \"officeimo.com\")", rule["expression"]!.GetValue<string>());
    }

    [Fact]
    public void BuildManagedRules_ShouldOnlyEmitHstsWhenExplicitlyEnabled()
    {
        var rules = CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(
            "example.com",
            "Example",
            new AgentSecurityHeadersSpec { Hsts = true, HstsValue = "max-age=2592000" });

        var headers = rules[0]!["action_parameters"]!["headers"]!.AsObject();
        Assert.Equal("max-age=2592000", headers["Strict-Transport-Security"]!["value"]!.GetValue<string>());
    }

    [Fact]
    public void Apply_DryRun_ShouldPreserveUnrelatedTransformRules()
    {
        var existingRules = new JsonArray
        {
            ExistingRule("custom-id", "Operator custom header"),
            ExistingRule("managed-id", "PowerForge OfficeIMO: security headers")
        };
        var handler = new SequenceHandler(JsonResponse(HttpStatusCode.OK, ExistingEnvelope(existingRules)));
        using var client = NewClient(handler);

        var result = CloudflareResponseHeaderPolicyManager.Apply(
            "0123456789abcdef0123456789abcdef",
            "secret-token",
            "officeimo.com",
            "OfficeIMO",
            new AgentSecurityHeadersSpec { Hsts = false },
            dryRun: true,
            client);

        Assert.True(result.Success, result.Message);
        Assert.True(result.ChangesRequired);
        Assert.False(result.Changed);
        Assert.Equal(1, result.ManagedRuleCount);
        Assert.Equal(1, result.PreservedRuleCount);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void CompositeAction_ShouldApplyBothPoliciesWithoutPassingTokenAsAnArgument()
    {
        var action = ReadRepoFile(".github", "actions", "powerforge-cloudflare-site-policy", "action.yml");
        var script = ReadRepoFile(".github", "actions", "powerforge-cloudflare-site-policy", "Invoke-PowerForgeCloudflareSitePolicy.ps1");

        Assert.Contains("Reject pull request site-policy changes", action, StringComparison.Ordinal);
        Assert.Contains("Cache Rules Write", action, StringComparison.Ordinal);
        Assert.Contains("Transform Rules Write", action, StringComparison.Ordinal);
        Assert.Contains("Invoke-PowerForgeCloudflareSitePolicy.ps1", action, StringComparison.Ordinal);
        Assert.Contains("--token-env', 'POWERFORGE_CLOUDFLARE_API_TOKEN'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("--token', $env:POWERFORGE_CLOUDFLARE_API_TOKEN", script, StringComparison.Ordinal);
        Assert.Contains("'site-policy'", script, StringComparison.Ordinal);
        Assert.True(script.Split('\n').Length < 100, "The action entrypoint should remain a bounded adapter over the CLI.");
    }

    private static JsonObject ExistingRule(string id, string description) => new()
    {
        ["id"] = id,
        ["ref"] = id,
        ["version"] = "3",
        ["last_updated"] = "2026-08-12T00:00:00Z",
        ["description"] = description,
        ["expression"] = "true",
        ["action"] = "rewrite",
        ["action_parameters"] = new JsonObject { ["headers"] = new JsonObject() },
        ["enabled"] = true
    };

    private static string ExistingEnvelope(JsonArray rules) => new JsonObject
    {
        ["success"] = true,
        ["result"] = new JsonObject { ["rules"] = rules }
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

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) => CaptureAndRespond(request);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(CaptureAndRespond(request));

        private HttpResponseMessage CaptureAndRespond(HttpRequestMessage request)
        {
            var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, request.Headers.Authorization, body));
            return _responses.Dequeue();
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, AuthenticationHeaderValue? Authorization, string Body);
}
