using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class CloudflareStaticCacheProfileTests
{
    private const string ZoneId = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void BuildManagedRules_StaticProfile_ShouldOverrideEverySuccessfulResponseForSevenDays()
    {
        var rules = CloudflareCachePolicyBuilder.BuildManagedRules(
            "officeimo.com",
            "OfficeIMO",
            htmlPaths: null,
            cache: new CloudflareCacheSpec
            {
                EdgeTtlSeconds = 604800
            });

        Assert.Equal(3, rules.Count);
        foreach (var rule in rules)
        {
            Assert.Contains("http.request.method eq \"GET\" or http.request.method eq \"PURGE\"", rule!["expression"]!.GetValue<string>(), StringComparison.Ordinal);
            var parameters = rule!["action_parameters"]!;
            Assert.Equal("override_origin", parameters["edge_ttl"]!["mode"]!.GetValue<string>());
            Assert.Equal(604800, parameters["edge_ttl"]!["default"]!.GetValue<int>());
            Assert.Equal("respect_origin", parameters["browser_ttl"]!["mode"]!.GetValue<string>());
            Assert.Null(parameters["browser_ttl"]!["default"]);

            var successRange = parameters["edge_ttl"]!["status_code_ttl"]!.AsArray()[1]!;
            Assert.Equal(200, successRange["status_code_range"]!["from"]!.GetValue<int>());
            Assert.Equal(299, successRange["status_code_range"]!["to"]!.GetValue<int>());
            Assert.Equal(604800, successRange["value"]!.GetValue<int>());
        }

        var fallback = rules[2]!;
        Assert.EndsWith("static assets", fallback["description"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(
            "(http.host eq \"officeimo.com\" and (http.request.method eq \"GET\" or http.request.method eq \"PURGE\"))",
            fallback["expression"]!.GetValue<string>());
    }

    [Fact]
    public void RouteProfile_ShouldLoadStaticCacheDeliveryPolicy()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-cloudflare-static-cache-" + Guid.NewGuid().ToString("N"));
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
                    "Cache": {
                      "EdgeTtlSeconds": 604800
                    },
                    "PurgeMode": "hostname",
                    "SmartTieredCache": true
                  }
                }
                """);

            var profile = CloudflareRouteProfileResolver.Load(configPath);

            Assert.NotNull(profile.Cloudflare);
            Assert.Equal("hostname", profile.Cloudflare.PurgeMode);
            Assert.Equal(604800, profile.Cloudflare.Cache?.EdgeTtlSeconds);
            Assert.True(profile.Cloudflare.SmartTieredCache);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RouteProfile_ShouldRejectPurgeModeThatTheSchemaRejects()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-cloudflare-purge-mode-" + Guid.NewGuid().ToString("N"));
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
                    "PurgeMode": "HOSTNAME"
                  }
                }
                """);

            var exception = Assert.Throws<InvalidOperationException>(() => CloudflareRouteProfileResolver.Load(configPath));

            Assert.Contains("files, incremental, hostname, or everything", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("HOSTNAME")]
    [InlineData("host")]
    [InlineData("hosts")]
    [InlineData("all")]
    [InlineData(" hostname ")]
    public void PipelinePurgeMode_ShouldRejectValuesOutsideItsSchema(string value)
    {
        Assert.False(CloudflareCachePurger.TryParseCanonicalMode(value, out _));
    }

    [Theory]
    [InlineData("files")]
    [InlineData("incremental")]
    [InlineData("hostname")]
    [InlineData("everything")]
    public void PipelinePurgeMode_ShouldAcceptEverySchemaValue(string value)
    {
        Assert.True(CloudflareCachePurger.TryParseCanonicalMode(value, out var actual));
        Assert.Equal(value, CloudflareCachePurger.FormatMode(actual));
    }

    [Fact]
    public void Schemas_ShouldExposeTheStaticCacheAndHostnamePurgeContracts()
    {
        using var siteSchema = JsonDocument.Parse(ReadRepoFile("Schemas", "powerforge.web.sitespec.schema.json"));
        var cloudflare = siteSchema.RootElement.GetProperty("$defs").GetProperty("CloudflareSitePolicySpec");
        Assert.True(cloudflare.GetProperty("properties").TryGetProperty("Cache", out _));
        Assert.True(cloudflare.GetProperty("properties").TryGetProperty("AlwaysPurgePaths", out _));
        Assert.Equal(
            ["files", "incremental", "hostname", "everything"],
            cloudflare.GetProperty("properties").GetProperty("PurgeMode").GetProperty("enum")
                .EnumerateArray().Select(value => value.GetString()!).ToArray());

        using var pipelineSchema = JsonDocument.Parse(ReadRepoFile("Schemas", "powerforge.web.pipelinespec.schema.json"));
        var pipelineText = pipelineSchema.RootElement.GetRawText();
        Assert.Contains("purgeMode", pipelineText, StringComparison.Ordinal);
        Assert.Contains("purgeHostname", pipelineText, StringComparison.Ordinal);
        Assert.Contains("hostnames", pipelineText, StringComparison.Ordinal);
        var cloudflareStep = pipelineSchema.RootElement.GetProperty("$defs").GetProperty("CloudflareStep");
        Assert.Equal(
            ["files", "incremental", "hostname", "everything"],
            cloudflareStep.GetProperty("properties").GetProperty("purgeMode").GetProperty("enum")
                .EnumerateArray().Select(value => value.GetString()!).ToArray());
        Assert.Equal(
            ["files", "incremental", "hostname", "everything"],
            cloudflareStep.GetProperty("properties").GetProperty("purge-mode").GetProperty("enum")
                .EnumerateArray().Select(value => value.GetString()!).ToArray());
    }

    [Fact]
    public void Purge_Hostname_ShouldSendTheZoneScopedHostnamePayload()
    {
        var handler = new SequenceHandler(JsonResponse(HttpStatusCode.OK, SuccessEnvelope()));
        using var client = NewClient(handler);

        var result = CloudflareCachePurger.Purge(
            ZoneId,
            "secret-token",
            CloudflareCachePurgeMode.Hostname,
            ["OfficeIMO.COM."],
            dryRun: false,
            logger: null,
            client);

        Assert.True(result.ok, result.message);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("Bearer", request.Authorization?.Scheme);
        Assert.Equal("secret-token", request.Authorization?.Parameter);
        Assert.EndsWith($"/zones/{ZoneId}/purge_cache", request.Uri.AbsolutePath, StringComparison.Ordinal);
        var payload = JsonNode.Parse(request.Body)!.AsObject();
        Assert.Equal("officeimo.com", Assert.Single(payload["hosts"]!.AsArray())!.GetValue<string>());
        Assert.Null(payload["files"]);
        Assert.Null(payload["purge_everything"]);
    }

    [Fact]
    public void Purge_Hostname_ShouldRejectMoreThanThirtyNormalizedTargets()
    {
        var handler = new SequenceHandler();
        using var client = NewClient(handler);
        var hostnames = Enumerable.Range(1, 31).Select(index => $"site-{index}.example.com").ToArray();

        var result = CloudflareCachePurger.Purge(
            ZoneId,
            "secret-token",
            CloudflareCachePurgeMode.Hostname,
            hostnames,
            dryRun: false,
            logger: null,
            client);

        Assert.False(result.ok);
        Assert.Contains("at most 30 hostnames", result.message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Purge_Hostname_ShouldApplyTheLimitAfterNormalization()
    {
        var handler = new SequenceHandler();
        using var client = NewClient(handler);
        var hostnames = Enumerable.Range(1, 30)
            .Select(index => $"site-{index}.example.com")
            .Append("SITE-1.EXAMPLE.COM.")
            .ToArray();

        var result = CloudflareCachePurger.Purge(
            ZoneId,
            "secret-token",
            CloudflareCachePurgeMode.Hostname,
            hostnames,
            dryRun: true,
            logger: null,
            client);

        Assert.True(result.ok, result.message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Purge_Files_ShouldRetainTheHundredUrlLimit()
    {
        var handler = new SequenceHandler();
        using var client = NewClient(handler);
        var urls = Enumerable.Range(1, 101).Select(index => $"https://example.com/assets/{index}.js").ToArray();

        var result = CloudflareCachePurger.Purge(
            ZoneId,
            "secret-token",
            CloudflareCachePurgeMode.Files,
            urls,
            dryRun: false,
            logger: null,
            client);

        Assert.False(result.ok);
        Assert.Contains("at most 100 URLs", result.message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Purge_Files_ShouldPreserveCaseDistinctUrls()
    {
        var handler = new SequenceHandler(JsonResponse(HttpStatusCode.OK, SuccessEnvelope()));
        using var client = NewClient(handler);

        var result = CloudflareCachePurger.Purge(
            ZoneId,
            "secret-token",
            CloudflareCachePurgeMode.Files,
            ["https://example.com/docs/A.html", "https://example.com/docs/a.html"],
            dryRun: false,
            logger: null,
            client);

        Assert.True(result.ok, result.message);
        var payload = JsonNode.Parse(Assert.Single(handler.Requests).Body)!.AsObject();
        Assert.Equal(
            ["https://example.com/docs/A.html", "https://example.com/docs/a.html"],
            payload["files"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray());
    }

    [Fact]
    public void PurgeJson_ShouldPreserveSchemaOneFieldsAlongsideTheNewModeContract()
    {
        var result = WebCliCommandHandlers.BuildCloudflarePurgeResult(
            "site.json",
            ZoneId,
            "https://officeimo.com/",
            CloudflareCachePurgeMode.Hostname,
            urlCount: 0,
            targetCount: 1,
            dryRun: false,
            message: "Purged 1 hostname.");

        Assert.False(result.GetProperty("purgeEverything").GetBoolean());
        Assert.Equal(0, result.GetProperty("urlCount").GetInt32());
        Assert.Equal("hostname", result.GetProperty("purgeMode").GetString());
        Assert.Equal(1, result.GetProperty("targetCount").GetInt32());
    }

    [Fact]
    public void SmartTieredCache_ShouldEnableAndVerifyTheRequestedState()
    {
        var handler = new SequenceHandler(
            SmartTieredResponse("off"),
            JsonResponse(HttpStatusCode.OK, SuccessEnvelope()),
            SmartTieredResponse("on"));
        using var client = NewClient(handler);

        var result = CloudflareSmartTieredCacheManager.Apply(
            ZoneId,
            "secret-token",
            enabled: true,
            dryRun: false,
            client);

        Assert.True(result.Success, result.Message);
        Assert.True(result.Changed);
        Assert.False(result.PreviousEnabled);
        Assert.True(result.Enabled);
        Assert.Equal([HttpMethod.Get, HttpMethod.Patch, HttpMethod.Get], handler.Requests.Select(request => request.Method).ToArray());
        Assert.Equal("on", JsonNode.Parse(handler.Requests[1].Body)!["value"]!.GetValue<string>());
    }

    [Fact]
    public void SmartTieredCache_DryRun_ShouldRejectAnExplicitlyNonEditableChange()
    {
        var handler = new SequenceHandler(SmartTieredResponse("off", editable: false));
        using var client = NewClient(handler);

        var result = CloudflareSmartTieredCacheManager.Apply(
            ZoneId,
            "secret-token",
            enabled: true,
            dryRun: true,
            client);

        Assert.False(result.Success);
        Assert.False(result.PreviousEnabled);
        Assert.Contains("not editable", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void SmartTieredCache_ShouldReconcileAnAmbiguousWriteResponse()
    {
        var handler = new SequenceHandler(
            SmartTieredResponse("off"),
            JsonResponse(HttpStatusCode.InternalServerError, """{"success":false,"errors":[{"message":"upstream timeout"}]}"""),
            SmartTieredResponse("on"));
        using var client = NewClient(handler);

        var result = CloudflareSmartTieredCacheManager.Apply(
            ZoneId,
            "secret-token",
            enabled: true,
            dryRun: false,
            client);

        Assert.True(result.Success, result.Message);
        Assert.True(result.Changed);
        Assert.Contains("ambiguous API response", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SmartTieredCache_ShouldNotOwnAChangeAfterDefinitiveWriteRejection()
    {
        var handler = new SequenceHandler(
            SmartTieredResponse("off"),
            JsonResponse(HttpStatusCode.Forbidden, """{"success":false,"errors":[{"message":"setting locked"}]}"""),
            SmartTieredResponse("on"));
        using var client = NewClient(handler);

        var result = CloudflareSmartTieredCacheManager.Apply(
            ZoneId,
            "secret-token",
            enabled: true,
            dryRun: false,
            client);

        Assert.False(result.Success);
        Assert.False(result.Changed);
        Assert.False(result.PreviousEnabled);
        Assert.Contains("HTTP 403", result.Message, StringComparison.Ordinal);
        Assert.Equal([HttpMethod.Get, HttpMethod.Patch], handler.Requests.Select(request => request.Method).ToArray());
    }

    [Fact]
    public void SmartTieredCache_ShouldRestorePreviousStateWhenWriteVerificationFails()
    {
        var handler = new SequenceHandler(
            SmartTieredResponse("off"),
            JsonResponse(HttpStatusCode.OK, SuccessEnvelope()),
            JsonResponse(HttpStatusCode.InternalServerError, """{"success":false,"errors":[{"message":"read timeout"}]}"""),
            JsonResponse(HttpStatusCode.OK, SuccessEnvelope()),
            SmartTieredResponse("off"));
        using var client = NewClient(handler);

        var result = CloudflareSmartTieredCacheManager.Apply(
            ZoneId,
            "secret-token",
            enabled: true,
            dryRun: false,
            client);

        Assert.False(result.Success);
        Assert.Contains("previous Smart Tiered Cache state was restored", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5, handler.Requests.Count);
        Assert.Equal("off", JsonNode.Parse(handler.Requests[3].Body)!["value"]!.GetValue<string>());
    }

    [Fact]
    public void SitePolicy_ShouldRollBackSmartTieredCacheWhenARequiredRulesetFails()
    {
        var oldCacheRules = new JsonArray { ExistingRule("cache-custom", "Operator cache rule", "set_cache_settings") };
        var oldHeaderRules = new JsonArray { ExistingRule("header-custom", "Operator header rule", "rewrite") };
        var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.OK, ExistingRulesEnvelope(oldCacheRules)),
            JsonResponse(HttpStatusCode.OK, ExistingRulesEnvelope(oldHeaderRules)),
            SmartTieredResponse("off"),
            SmartTieredResponse("off"),
            JsonResponse(HttpStatusCode.OK, SuccessEnvelope()),
            SmartTieredResponse("on"),
            JsonResponse(HttpStatusCode.OK, ExistingRulesEnvelope(oldCacheRules)),
            JsonResponse(HttpStatusCode.OK, SuccessEnvelope()),
            JsonResponse(HttpStatusCode.OK, ExistingRulesEnvelope(oldHeaderRules)),
            JsonResponse(HttpStatusCode.BadRequest, """{"success":false,"errors":[{"message":"invalid transform"}]}"""),
            JsonResponse(HttpStatusCode.OK, SuccessEnvelope()),
            SmartTieredResponse("on"),
            JsonResponse(HttpStatusCode.OK, SuccessEnvelope()),
            SmartTieredResponse("off"));
        using var client = NewClient(handler);

        var result = CloudflareSitePolicyManager.Apply(
            ZoneId,
            "secret-token",
            "officeimo.com",
            "OfficeIMO",
            htmlPaths: null,
            securityHeaders: new AgentSecurityHeadersSpec { Hsts = false },
            dryRun: false,
            httpClient: client,
            cache: new CloudflareCacheSpec(),
            smartTieredCache: true);

        Assert.False(result.Success);
        Assert.Contains("previous site-policy state was restored", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Smart Tiered Cache rollback", result.Message, StringComparison.Ordinal);
        Assert.Equal(14, handler.Requests.Count);
        Assert.Equal(HttpMethod.Patch, handler.Requests[12].Method);
        Assert.Equal("off", JsonNode.Parse(handler.Requests[12].Body)!["value"]!.GetValue<string>());
    }

    [Fact]
    public void SitePolicy_ShouldNotClaimFullRecoveryWhenSmartTieredRollbackFails()
    {
        var oldCacheRules = new JsonArray { ExistingRule("cache-custom", "Operator cache rule", "set_cache_settings") };
        var oldHeaderRules = new JsonArray { ExistingRule("header-custom", "Operator header rule", "rewrite") };
        var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.OK, ExistingRulesEnvelope(oldCacheRules)),
            JsonResponse(HttpStatusCode.OK, ExistingRulesEnvelope(oldHeaderRules)),
            SmartTieredResponse("off"),
            SmartTieredResponse("off"),
            JsonResponse(HttpStatusCode.OK, SuccessEnvelope()),
            SmartTieredResponse("on"),
            JsonResponse(HttpStatusCode.OK, ExistingRulesEnvelope(oldCacheRules)),
            JsonResponse(HttpStatusCode.OK, SuccessEnvelope()),
            JsonResponse(HttpStatusCode.OK, ExistingRulesEnvelope(oldHeaderRules)),
            JsonResponse(HttpStatusCode.BadRequest, """{"success":false,"errors":[{"message":"invalid transform"}]}"""),
            JsonResponse(HttpStatusCode.OK, SuccessEnvelope()),
            SmartTieredResponse("on"),
            JsonResponse(HttpStatusCode.Forbidden, """{"success":false,"errors":[{"message":"setting locked"}]}"""),
            SmartTieredResponse("on"));
        using var client = NewClient(handler);

        var result = CloudflareSitePolicyManager.Apply(
            ZoneId,
            "secret-token",
            "officeimo.com",
            "OfficeIMO",
            htmlPaths: null,
            securityHeaders: new AgentSecurityHeadersSpec { Hsts = false },
            dryRun: false,
            httpClient: client,
            cache: new CloudflareCacheSpec(),
            smartTieredCache: true);

        Assert.False(result.Success);
        Assert.Contains("Smart Tiered Cache rollback was incomplete", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Rollback was incomplete", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("previous site-policy state was restored", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(13, handler.Requests.Count);
    }

    private static JsonObject ExistingRule(string id, string description, string action) => new()
    {
        ["id"] = id,
        ["ref"] = id,
        ["description"] = description,
        ["expression"] = "true",
        ["action"] = action,
        ["action_parameters"] = action == "rewrite"
            ? new JsonObject { ["headers"] = new JsonObject() }
            : new JsonObject { ["cache"] = true },
        ["enabled"] = true
    };

    private static string ExistingRulesEnvelope(JsonArray rules) => new JsonObject
    {
        ["success"] = true,
        ["result"] = new JsonObject { ["rules"] = rules.DeepClone() }
    }.ToJsonString();

    private static string SuccessEnvelope() => new JsonObject
    {
        ["success"] = true,
        ["result"] = new JsonObject()
    }.ToJsonString();

    private static HttpResponseMessage SmartTieredResponse(string value, bool? editable = null)
    {
        var result = new JsonObject { ["value"] = value };
        if (editable.HasValue)
            result["editable"] = editable.Value;
        return JsonResponse(
            HttpStatusCode.OK,
            new JsonObject
            {
                ["success"] = true,
                ["result"] = result
            }.ToJsonString());
    }

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
