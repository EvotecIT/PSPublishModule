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
    public void SecurityHeaderDefaults_ShouldRemainBackwardCompatible()
    {
        var security = new AgentSecurityHeadersSpec();

        Assert.True(security.Hsts);
        Assert.False(security.PermissionsPolicy);
    }

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
                XFrameOptionsValue = "SAMEORIGIN",
                PermissionsPolicy = true
            });

        var rule = Assert.IsType<JsonObject>(Assert.Single(rules));
        var headers = rule["action_parameters"]!["headers"]!.AsObject();
        Assert.False(headers.ContainsKey("Strict-Transport-Security"));
        Assert.Equal("nosniff", headers["X-Content-Type-Options"]!["value"]!.GetValue<string>());
        Assert.Equal("SAMEORIGIN", headers["X-Frame-Options"]!["value"]!.GetValue<string>());
        Assert.Equal("camera=(), geolocation=(), microphone=(), payment=(), usb=()", headers["Permissions-Policy"]!["value"]!.GetValue<string>());
        Assert.Equal("(http.host eq \"officeimo.com\")", rule["expression"]!.GetValue<string>());
        Assert.Equal("PowerForge [officeimo.com/]: OfficeIMO: security headers", rule["description"]!.GetValue<string>());
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
    public void BuildManagedRules_ShouldScopeHeadersToBasePathAndUseBlankValueDefaults()
    {
        var rules = CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(
            "example.com",
            "Docs",
            new AgentSecurityHeadersSpec
            {
                Hsts = false,
                XFrameOptionsValue = " ",
                ReferrerPolicyValue = ""
            },
            "/docs/");

        var rule = Assert.IsType<JsonObject>(Assert.Single(rules));
        Assert.Equal(
            "(http.host eq \"example.com\" and (http.request.uri.path eq \"/docs\" or starts_with(http.request.uri.path, \"/docs/\")))",
            rule["expression"]!.GetValue<string>());
        var headers = rule["action_parameters"]!["headers"]!.AsObject();
        Assert.Equal("DENY", headers["X-Frame-Options"]!["value"]!.GetValue<string>());
        Assert.Equal("strict-origin-when-cross-origin", headers["Referrer-Policy"]!["value"]!.GetValue<string>());
    }

    [Fact]
    public void BuildManagedRules_ShouldScopeConfiguredDiscoveryCorsToEnabledResourcePaths()
    {
        var readiness = new AgentReadinessSpec
        {
            Enabled = true,
            ApiCatalog = new AgentApiCatalogSpec { Enabled = true, OutputPath = "discovery/catalog.json" },
            AgentSkills = new AgentSkillsDiscoverySpec { Enabled = true, IndexPath = "skills/index.json" },
            AgentsJson = new AgentDiscoveryDocumentSpec { Enabled = false },
            A2AAgentCard = new AgentA2ACardSpec { Enabled = true },
            McpServerCard = new AgentMcpServerCardSpec { Enabled = true, OutputPath = "cards/mcp.json" }
        };
        var security = new AgentSecurityHeadersSpec
        {
            Hsts = false,
            CorsForWellKnown = true,
            CorsAllowOrigin = "https://client.example"
        };

        var rules = CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(
            "example.com", "Docs", security, "/project/", readiness);

        Assert.Equal(4, rules.Count);
        var apiRule = Assert.IsType<JsonObject>(rules.Single(rule =>
            rule!["description"]!.GetValue<string>().EndsWith("discovery API catalog headers", StringComparison.Ordinal)));
        var jsonRule = Assert.IsType<JsonObject>(rules.Single(rule =>
            rule!["description"]!.GetValue<string>().EndsWith("discovery JSON headers", StringComparison.Ordinal)));
        var expression = apiRule["expression"]!.GetValue<string>() + jsonRule["expression"]!.GetValue<string>();
        Assert.Contains("/project/discovery/catalog.json", expression, StringComparison.Ordinal);
        Assert.Contains("/project/skills/index.json", expression, StringComparison.Ordinal);
        Assert.Contains("/project/.well-known/agent-card.json", expression, StringComparison.Ordinal);
        Assert.Contains("/project/cards/mcp.json", expression, StringComparison.Ordinal);
        Assert.DoesNotContain("agents.json", expression, StringComparison.Ordinal);
        Assert.Equal("https://client.example",
            jsonRule["action_parameters"]!["headers"]!["Access-Control-Allow-Origin"]!["value"]!.GetValue<string>());
        Assert.Equal("application/linkset+json; profile=\"https://www.rfc-editor.org/info/rfc9727\"",
            apiRule["action_parameters"]!["headers"]!["Content-Type"]!["value"]!.GetValue<string>());
        Assert.Equal("application/json",
            jsonRule["action_parameters"]!["headers"]!["Content-Type"]!["value"]!.GetValue<string>());
        var linkRule = Assert.IsType<JsonObject>(rules.Single(rule =>
            rule!["description"]!.GetValue<string>().EndsWith("discovery Link headers", StringComparison.Ordinal)));
        Assert.Equal(
            "(http.host eq \"example.com\" and (http.request.uri.path eq \"/project\" or http.request.uri.path eq \"/project/\"))",
            linkRule["expression"]!.GetValue<string>());
        var link = linkRule["action_parameters"]!["headers"]!["Link"]!["value"]!.GetValue<string>();
        Assert.Equal("add", linkRule["action_parameters"]!["headers"]!["Link"]!["operation"]!.GetValue<string>());
        Assert.Contains("</project/discovery/catalog.json>; rel=\"api-catalog\"; type=\"application/linkset+json\"", link, StringComparison.Ordinal);
        Assert.Contains("</project/skills/index.json>; rel=\"describedby\"; type=\"application/json\"", link, StringComparison.Ordinal);
        Assert.Contains("</project/.well-known/agent-card.json>; rel=\"service-desc\"; type=\"application/json\"", link, StringComparison.Ordinal);
        Assert.Contains("</project/cards/mcp.json>; rel=\"service-desc\"; type=\"application/json\"", link, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildManagedRules_ShouldNotAdvertiseLlmsFallbackWhenDiscoveryResourcesAreDisabled()
    {
        var rules = CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(
            "example.com",
            "Docs",
            new AgentSecurityHeadersSpec { Enabled = false, Hsts = false },
            agentReadiness: new AgentReadinessSpec
            {
                Enabled = true,
                LinkHeaders = true,
                ApiCatalog = new AgentApiCatalogSpec { Enabled = false },
                AgentSkills = new AgentSkillsDiscoverySpec { Enabled = false },
                AgentsJson = new AgentDiscoveryDocumentSpec { Enabled = false },
                A2AAgentCard = new AgentA2ACardSpec { Enabled = false },
                McpServerCard = new AgentMcpServerCardSpec { Enabled = false },
                OpenApi = new AgentOpenApiSpec { Enabled = false },
                MarkdownArtifacts = new AgentMarkdownArtifactsSpec { Enabled = false }
            });

        Assert.Empty(rules);
    }

    [Fact]
    public void BuildManagedRules_ShouldPercentEncodeDiscoveryLinkPaths()
    {
        var rules = CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(
            "example.com",
            "Docs",
            new AgentSecurityHeadersSpec { Enabled = true, Hsts = false },
            basePath: "/product docs/",
            agentReadiness: new AgentReadinessSpec
            {
                Enabled = true,
                ApiCatalog = new AgentApiCatalogSpec { Enabled = true, OutputPath = "discovery/api catalog.json" },
                AgentSkills = new AgentSkillsDiscoverySpec { Enabled = false },
                AgentsJson = new AgentDiscoveryDocumentSpec { Enabled = false }
            });

        var linkRule = Assert.IsType<JsonObject>(rules.Single(rule =>
            rule!["description"]!.GetValue<string>().EndsWith("discovery Link headers", StringComparison.Ordinal)));
        var link = linkRule["action_parameters"]!["headers"]!["Link"]!["value"]!.GetValue<string>();
        Assert.Contains("</product%20docs/discovery/api%20catalog.json>", link, StringComparison.Ordinal);
        Assert.DoesNotContain("api catalog.json", link, StringComparison.Ordinal);
        Assert.Contains("uri.path eq \"/product%20docs\"", linkRule["expression"]!.GetValue<string>(), StringComparison.Ordinal);
        var apiRule = Assert.IsType<JsonObject>(rules.Single(rule =>
            rule!["description"]!.GetValue<string>().EndsWith("discovery API catalog headers", StringComparison.Ordinal)));
        Assert.Contains("uri.path eq \"/product%20docs/discovery/api%20catalog.json\"",
            apiRule["expression"]!.GetValue<string>(), StringComparison.Ordinal);
        var securityRule = Assert.IsType<JsonObject>(rules.Single(rule =>
            rule!["description"]!.GetValue<string>().EndsWith("security headers", StringComparison.Ordinal)));
        Assert.Contains("starts_with(http.request.uri.path, \"/product%20docs/\")",
            securityRule["expression"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildManagedRules_ShouldPreserveRootRelativeDiscoveryLinksAcrossPlatforms()
    {
        var rules = CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(
            "example.com",
            "Root",
            new AgentSecurityHeadersSpec { Enabled = true, Hsts = false },
            basePath: "/",
            agentReadiness: new AgentReadinessSpec
            {
                Enabled = true,
                ApiCatalog = new AgentApiCatalogSpec
                {
                    Enabled = true,
                    OutputPath = ".well-known/api-catalog"
                },
                AgentSkills = new AgentSkillsDiscoverySpec { Enabled = false },
                AgentsJson = new AgentDiscoveryDocumentSpec
                {
                    Enabled = true,
                    OutputPath = "agents.json"
                }
            });

        var linkRule = Assert.IsType<JsonObject>(rules.Single(rule =>
            rule!["description"]!.GetValue<string>().EndsWith("discovery Link headers", StringComparison.Ordinal)));
        var link = linkRule["action_parameters"]!["headers"]!["Link"]!["value"]!.GetValue<string>();

        Assert.Contains("</.well-known/api-catalog>; rel=\"api-catalog\"", link, StringComparison.Ordinal);
        Assert.Contains("</agents.json>; rel=\"describedby\"", link, StringComparison.Ordinal);
        Assert.DoesNotContain("file:", link, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildManagedRules_ShouldApplyMarkdownHeadersAcrossConfiguredSubpath()
    {
        var rules = CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(
            "example.com",
            "Docs",
            new AgentSecurityHeadersSpec
            {
                Enabled = true,
                Hsts = false,
                CorsForWellKnown = true,
                CorsAllowOrigin = "https://agent.example"
            },
            "/project/",
            new AgentReadinessSpec
            {
                Enabled = true,
                ApiCatalog = new AgentApiCatalogSpec { Enabled = false },
                AgentSkills = new AgentSkillsDiscoverySpec { Enabled = false },
                AgentsJson = new AgentDiscoveryDocumentSpec { Enabled = false },
                MarkdownArtifacts = new AgentMarkdownArtifactsSpec { Enabled = true, Extension = ".markdown" }
            });

        var markdownRule = Assert.IsType<JsonObject>(rules.Single(rule =>
            rule!["description"]!.GetValue<string>().EndsWith("discovery Markdown headers", StringComparison.Ordinal)));
        Assert.Equal(
            "(http.host eq \"example.com\" and starts_with(http.request.uri.path, \"/project/\") and ends_with(http.request.uri.path, \".markdown\"))",
            markdownRule["expression"]!.GetValue<string>());
        var headers = markdownRule["action_parameters"]!["headers"]!.AsObject();
        Assert.Equal("text/markdown; charset=utf-8", headers["Content-Type"]!["value"]!.GetValue<string>());
        Assert.Equal("https://agent.example", headers["Access-Control-Allow-Origin"]!["value"]!.GetValue<string>());

        var linkRule = Assert.IsType<JsonObject>(rules.Single(rule =>
            rule!["description"]!.GetValue<string>().EndsWith("discovery Link headers", StringComparison.Ordinal)));
        Assert.Contains("</project/index.markdown>; rel=\"alternate\"; type=\"text/markdown\"",
            linkRule["action_parameters"]!["headers"]!["Link"]!["value"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildManagedRules_ShouldNotEmitDiscoveryCorsWhenDisabled()
    {
        var rules = CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(
            "example.com",
            "Docs",
            new AgentSecurityHeadersSpec { Hsts = false, CorsForWellKnown = false },
            agentReadiness: new AgentReadinessSpec
            {
                Enabled = true,
                ApiCatalog = new AgentApiCatalogSpec { Enabled = true }
            });

        Assert.Equal(4, rules.Count);
        foreach (var discoveryRule in rules.Where(rule =>
                     !rule!["description"]!.GetValue<string>().EndsWith("discovery Link headers", StringComparison.Ordinal)).Skip(1))
        {
            var discoveryHeaders = discoveryRule!["action_parameters"]!["headers"]!.AsObject();
            Assert.True(discoveryHeaders.ContainsKey("Content-Type"));
            Assert.False(discoveryHeaders.ContainsKey("Access-Control-Allow-Origin"));
        }
        Assert.Contains(rules, rule =>
            rule!["description"]!.GetValue<string>().EndsWith("discovery Link headers", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildManagedRules_ShouldRequireOpenApiPathForManagedLinkHeaders()
    {
        var readiness = new AgentReadinessSpec
        {
            Enabled = true,
            ApiCatalog = new AgentApiCatalogSpec { Enabled = false },
            AgentSkills = new AgentSkillsDiscoverySpec { Enabled = false },
            AgentsJson = new AgentDiscoveryDocumentSpec { Enabled = false },
            A2AAgentCard = new AgentA2ACardSpec { Enabled = false },
            McpServerCard = new AgentMcpServerCardSpec { Enabled = false },
            OpenApi = new AgentOpenApiSpec { Enabled = true }
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(
                "example.com", "API", new AgentSecurityHeadersSpec { Hsts = false }, "/docs/", readiness));

        Assert.Contains("AgentReadiness.OpenApi.Path", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildManagedRules_ShouldKeepDiscoveryContentTypeWhenSecurityHeadersAreDisabled()
    {
        var rules = CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(
            "example.com",
            "Docs",
            new AgentSecurityHeadersSpec { Enabled = false, Hsts = false },
            agentReadiness: new AgentReadinessSpec
            {
                Enabled = true,
                ApiCatalog = new AgentApiCatalogSpec { Enabled = true }
            });

        Assert.Equal(3, rules.Count);
        var rule = Assert.IsType<JsonObject>(rules.Single(candidate =>
            candidate!["description"]!.GetValue<string>().EndsWith("discovery API catalog headers", StringComparison.Ordinal)));
        var headers = rule["action_parameters"]!["headers"]!.AsObject();
        Assert.Equal("application/linkset+json; profile=\"https://www.rfc-editor.org/info/rfc9727\"",
            headers["Content-Type"]!["value"]!.GetValue<string>());
        Assert.False(headers.ContainsKey("Access-Control-Allow-Origin"));
    }

    [Fact]
    public void BuildManagedRules_ShouldRejectOversizedDiscoveryCorsExpression()
    {
        var readiness = new AgentReadinessSpec
        {
            Enabled = true,
            LinkHeaders = false,
            ApiCatalog = new AgentApiCatalogSpec
            {
                Enabled = true,
                OutputPath = "discovery/" + new string('a', 4096) + ".json"
            }
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(
                "example.com",
                "Docs",
                new AgentSecurityHeadersSpec { Hsts = false },
                agentReadiness: readiness));

        Assert.Contains("discovery API catalog headers expression", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildManagedRules_ShouldRejectCollidingDiscoveryMediaTypePaths()
    {
        var readiness = new AgentReadinessSpec
        {
            Enabled = true,
            ApiCatalog = new AgentApiCatalogSpec { Enabled = true, OutputPath = "custom/discovery.json" },
            AgentSkills = new AgentSkillsDiscoverySpec { Enabled = true, IndexPath = "custom/discovery.json" },
            AgentsJson = new AgentDiscoveryDocumentSpec { Enabled = false }
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(
                "example.com",
                "Docs",
                new AgentSecurityHeadersSpec { Hsts = false },
                agentReadiness: readiness));

        Assert.Contains("configured for both API Catalog and Agent Skills", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RouteProfile_ShouldDisableCloudflareHeadersWhenAgentReadinessIsDisabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-cloudflare-disabled-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath,
                """
                {
                  "Name": "Disabled",
                  "BaseUrl": "https://disabled.example.com",
                  "AgentReadiness": { "Enabled": false }
                }
                """);

            var profile = CloudflareRouteProfileResolver.Load(configPath);

            Assert.NotNull(profile.SecurityHeaders);
            Assert.False(profile.SecurityHeaders.Enabled);
            Assert.False(profile.SecurityHeaders.Hsts);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RouteProfile_ShouldMaterializeEffectiveDiscoveryDefaults()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-cloudflare-default-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath,
                """
                {
                  "Name": "Defaults",
                  "BaseUrl": "https://example.com/project/",
                  "AgentReadiness": {
                    "Enabled": true,
                    "SecurityHeaders": { "Hsts": false }
                  }
                }
                """);

            var profile = CloudflareRouteProfileResolver.Load(configPath);
            var rules = CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(
                "example.com", "Defaults", profile.SecurityHeaders, "/project/", profile.AgentReadiness);

            Assert.NotNull(profile.AgentReadiness?.ApiCatalog);
            Assert.NotNull(profile.AgentReadiness?.AgentSkills);
            Assert.NotNull(profile.AgentReadiness?.AgentsJson);
            Assert.Equal(4, rules.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Apply_DryRun_ShouldPreserveUnrelatedTransformRules()
    {
        var existingRules = new JsonArray
        {
            ExistingRule("custom-id", "Operator custom header"),
            ExistingRule("managed-id", "PowerForge OfficeIMO [officeimo.com]: security headers")
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
    public void Apply_DefinitiveUpdateRejection_ShouldNotExposeRollbackSnapshot()
    {
        var existingRules = new JsonArray { ExistingRule("custom-id", "Operator custom header") };
        var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.OK, ExistingEnvelope(existingRules)),
            JsonResponse(HttpStatusCode.Forbidden, """{"success":false,"errors":[{"message":"write denied"}]}"""));
        using var client = NewClient(handler);

        var result = CloudflareResponseHeaderPolicyManager.Apply(
            "0123456789abcdef0123456789abcdef",
            "secret-token",
            "officeimo.com",
            "OfficeIMO",
            new AgentSecurityHeadersSpec { Hsts = false },
            dryRun: false,
            client);

        Assert.False(result.Success);
        Assert.Null(result.Snapshot);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public void CompositeAction_ShouldApplyBothPoliciesWithoutPassingTokenAsAnArgument()
    {
        var action = ReadRepoFile(".github", "actions", "powerforge-cloudflare-site-policy", "action.yml");
        var script = ReadRepoFile(".github", "actions", "powerforge-cloudflare-site-policy", "Invoke-PowerForgeCloudflareSitePolicy.ps1");

        Assert.Contains("Reject pull request site-policy changes", action, StringComparison.Ordinal);
        Assert.Contains("Cache Settings Write", action, StringComparison.Ordinal);
        Assert.Contains("Zone Transform Rules Write", action, StringComparison.Ordinal);
        Assert.Contains("Zone Settings Read and Write", action, StringComparison.Ordinal);
        Assert.DoesNotContain("add Cache Settings Write when SmartTieredCache", action, StringComparison.Ordinal);
        Assert.Contains("Invoke-PowerForgeCloudflareSitePolicy.ps1", action, StringComparison.Ordinal);
        Assert.Contains("--token-env', 'POWERFORGE_CLOUDFLARE_API_TOKEN'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("--token', $env:POWERFORGE_CLOUDFLARE_API_TOKEN", script, StringComparison.Ordinal);
        Assert.Contains("$cliExitCode = $LASTEXITCODE", script, StringComparison.Ordinal);
        Assert.Contains("Write-Host $jsonText", script, StringComparison.Ordinal);
        Assert.Contains("'site-policy'", script, StringComparison.Ordinal);
        Assert.True(script.Split('\n').Length < 100, "The action entrypoint should remain a bounded adapter over the CLI.");
    }

    [Fact]
    public void SitePolicy_ShouldPreflightBothRulesetsBeforeWriting()
    {
        var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.OK, ExistingEnvelope(new JsonArray())),
            JsonResponse(HttpStatusCode.Forbidden, """{"success":false,"errors":[{"message":"missing transform permission"}]}"""));
        using var client = NewClient(handler);

        var result = CloudflareSitePolicyManager.Apply(
            "0123456789abcdef0123456789abcdef",
            "secret-token",
            "officeimo.com",
            "OfficeIMO",
            htmlPaths: null,
            securityHeaders: new AgentSecurityHeadersSpec { Hsts = false },
            dryRun: false,
            httpClient: client);

        Assert.False(result.Success);
        Assert.Contains("No changes were made", result.Message, StringComparison.Ordinal);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
    }

    [Fact]
    public void SitePolicy_ShouldRestoreCacheWhenHeaderApplyFails()
    {
        var oldCacheRules = new JsonArray { ExistingRule("cache-custom", "Operator cache rule", "set_cache_settings") };
        var oldHeaderRules = new JsonArray { ExistingRule("header-custom", "Operator header rule") };
        var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.OK, ExistingEnvelope(oldCacheRules)),
            JsonResponse(HttpStatusCode.OK, ExistingEnvelope(oldHeaderRules)),
            JsonResponse(HttpStatusCode.OK, ExistingEnvelope(oldCacheRules)),
            JsonResponse(HttpStatusCode.OK, SuccessEnvelope()),
            JsonResponse(HttpStatusCode.OK, ExistingEnvelope(oldHeaderRules)),
            JsonResponse(HttpStatusCode.BadRequest, """{"success":false,"errors":[{"message":"invalid transform"}]}"""),
            JsonResponse(HttpStatusCode.OK, SuccessEnvelope()));
        using var client = NewClient(handler);

        var result = CloudflareSitePolicyManager.Apply(
            "0123456789abcdef0123456789abcdef",
            "secret-token",
            "officeimo.com",
            "OfficeIMO",
            htmlPaths: null,
            securityHeaders: new AgentSecurityHeadersSpec { Hsts = false },
            dryRun: false,
            httpClient: client);

        Assert.False(result.Success);
        Assert.Contains("previous site-policy state was restored", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(7, handler.Requests.Count);
        Assert.Equal([HttpMethod.Get, HttpMethod.Get, HttpMethod.Get, HttpMethod.Put, HttpMethod.Get, HttpMethod.Put, HttpMethod.Put],
            handler.Requests.Select(request => request.Method).ToArray());

        var restoredCache = JsonNode.Parse(handler.Requests[6].Body)!["rules"]!.AsArray();
        Assert.Equal("Operator cache rule", Assert.Single(restoredCache)!["description"]!.GetValue<string>());
    }

    [Fact]
    public void SitePolicy_ShouldDeleteNewCacheRulesetWhenHeaderApplyFails()
    {
        var oldHeaderRules = new JsonArray { ExistingRule("header-custom", "Operator header rule") };
        var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.NotFound, """{"success":false,"errors":[{"message":"not found"}]}"""),
            JsonResponse(HttpStatusCode.OK, ExistingEnvelope(oldHeaderRules)),
            JsonResponse(HttpStatusCode.NotFound, """{"success":false,"errors":[{"message":"not found"}]}"""),
            JsonResponse(HttpStatusCode.OK, SuccessEnvelope("new-cache-ruleset")),
            JsonResponse(HttpStatusCode.OK, ExistingEnvelope(oldHeaderRules)),
            JsonResponse(HttpStatusCode.BadRequest, """{"success":false,"errors":[{"message":"invalid transform"}]}"""),
            JsonResponse(HttpStatusCode.OK, SuccessEnvelope()));
        using var client = NewClient(handler);

        var result = CloudflareSitePolicyManager.Apply(
            "0123456789abcdef0123456789abcdef",
            "secret-token",
            "officeimo.com",
            "OfficeIMO",
            htmlPaths: null,
            securityHeaders: new AgentSecurityHeadersSpec { Hsts = false },
            dryRun: false,
            httpClient: client);

        Assert.False(result.Success);
        Assert.Contains("previous site-policy state was restored", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(7, handler.Requests.Count);
        Assert.Equal(HttpMethod.Delete, handler.Requests[6].Method);
        Assert.EndsWith("/rulesets/new-cache-ruleset", handler.Requests[6].Uri.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public void SitePolicy_ShouldReportIncompleteRollbackWhenCreatedHeaderRulesetHasNoIdentifier()
    {
        var oldCacheRules = new JsonArray { ExistingRule("cache-custom", "Operator cache rule", "set_cache_settings") };
        var desiredHeaderRules = CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(
            "officeimo.com", "OfficeIMO", new AgentSecurityHeadersSpec { Hsts = false });
        var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.OK, ExistingEnvelope(oldCacheRules)),
            JsonResponse(HttpStatusCode.NotFound, """{"success":false,"errors":[{"message":"not found"}]}"""),
            JsonResponse(HttpStatusCode.OK, ExistingEnvelope(oldCacheRules)),
            JsonResponse(HttpStatusCode.OK, SuccessEnvelope()),
            JsonResponse(HttpStatusCode.NotFound, """{"success":false,"errors":[{"message":"not found"}]}"""),
            JsonResponse(HttpStatusCode.OK, SuccessEnvelope()),
            JsonResponse(HttpStatusCode.OK, ExistingEnvelope(desiredHeaderRules)),
            JsonResponse(HttpStatusCode.OK, SuccessEnvelope()));
        using var client = NewClient(handler);

        var result = CloudflareSitePolicyManager.Apply(
            "0123456789abcdef0123456789abcdef",
            "secret-token",
            "officeimo.com",
            "OfficeIMO",
            htmlPaths: null,
            securityHeaders: new AgentSecurityHeadersSpec { Hsts = false },
            dryRun: false,
            httpClient: client);

        Assert.False(result.Success);
        Assert.Contains("Response-header rollback", result.Message, StringComparison.Ordinal);
        Assert.Contains("identifier was not returned", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Rollback was incomplete", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("previous site-policy state was restored", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(8, handler.Requests.Count);
        Assert.Equal(HttpMethod.Put, handler.Requests[7].Method);
    }

    [Fact]
    public void Apply_ShouldPreserveSameNamedLegacyHeaderRuleForAnotherHost()
    {
        var existingRules = new JsonArray
        {
            ExistingRule("target-id", "PowerForge Shared: security headers", expression: "(http.host eq \"one.example.com\")"),
            ExistingRule("other-id", "PowerForge Shared: security headers", expression: "(http.host eq \"two.example.com\")")
        };
        var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.OK, ExistingEnvelope(existingRules)),
            JsonResponse(HttpStatusCode.OK, SuccessEnvelope()));
        using var client = NewClient(handler);

        var result = CloudflareResponseHeaderPolicyManager.Apply(
            "0123456789abcdef0123456789abcdef",
            "secret-token",
            "one.example.com",
            "Shared",
            new AgentSecurityHeadersSpec { Hsts = false },
            dryRun: false,
            client);

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.PreservedRuleCount);
        var rules = JsonNode.Parse(handler.Requests[1].Body)!["rules"]!.AsArray();
        Assert.Contains(rules, rule => rule!["description"]!.GetValue<string>() == "PowerForge [one.example.com/]: Shared: security headers");
        Assert.Contains(rules, rule => rule!["id"]?.GetValue<string>() == "other-id");
        Assert.Contains(rules, rule => rule!["id"]?.GetValue<string>() == "target-id");
    }

    [Fact]
    public void Apply_ShouldReplaceRenamedHostScopedSecurityRuleAndRemoveDisabledHsts()
    {
        var previousRules = CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(
            "example.com", "Previous", new AgentSecurityHeadersSpec { Hsts = true });
        var previousRule = previousRules[0]!.AsObject();
        previousRule["id"] = "managed-security-id";
        previousRule["description"] = "PowerForge Previous [example.com]: security headers";
        var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.OK, ExistingEnvelope(previousRules)),
            JsonResponse(HttpStatusCode.OK, SuccessEnvelope()));
        using var client = NewClient(handler);

        var result = CloudflareResponseHeaderPolicyManager.Apply(
            "0123456789abcdef0123456789abcdef",
            "secret-token",
            "example.com",
            "Renamed",
            new AgentSecurityHeadersSpec { Hsts = false },
            dryRun: false,
            client);

        Assert.True(result.Success, result.Message);
        Assert.Equal(0, result.PreservedRuleCount);
        var rules = JsonNode.Parse(handler.Requests[1].Body)!["rules"]!.AsArray();
        var rule = Assert.Single(rules)!.AsObject();
        Assert.Equal("managed-security-id", rule["id"]!.GetValue<string>());
        Assert.Equal("PowerForge [example.com/]: Renamed: security headers", rule["description"]!.GetValue<string>());
        Assert.Null(rule["action_parameters"]!["headers"]!["Strict-Transport-Security"]);
    }

    [Fact]
    public void Apply_ShouldUseBasePathIdentityWhenPolicyNameChanges()
    {
        var firstSiteRules = CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(
            "example.com", "First", new AgentSecurityHeadersSpec { Hsts = false }, "/first/");
        firstSiteRules[0]!["id"] = "first-id";
        var secondSiteRules = CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(
            "example.com", "Second", new AgentSecurityHeadersSpec { Hsts = false }, "/second/");
        secondSiteRules[0]!["id"] = "second-id";
        var existingRules = new JsonArray(firstSiteRules[0]!.DeepClone(), secondSiteRules[0]!.DeepClone());
        var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.OK, ExistingEnvelope(existingRules)),
            JsonResponse(HttpStatusCode.OK, SuccessEnvelope()));
        using var client = NewClient(handler);

        var result = CloudflareResponseHeaderPolicyManager.Apply(
            "0123456789abcdef0123456789abcdef",
            "secret-token",
            "example.com",
            "First Renamed",
            new AgentSecurityHeadersSpec { Hsts = false },
            dryRun: false,
            client,
            basePath: "/first/");

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.PreservedRuleCount);
        var rules = JsonNode.Parse(handler.Requests[1].Body)!["rules"]!.AsArray();
        Assert.Contains(rules, rule => rule!["description"]!.GetValue<string>() == "PowerForge [example.com/first/]: First Renamed: security headers");
        Assert.Contains(rules, rule => rule!["id"]?.GetValue<string>() == "first-id");
        Assert.Contains(rules, rule => rule!["id"]?.GetValue<string>() == "second-id");
    }

    [Fact]
    public void Apply_ShouldScopePreviousHostFormatRulesByBasePath()
    {
        var docsRules = CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(
            "example.com", "Shared", new AgentSecurityHeadersSpec { Hsts = false }, "/docs/");
        docsRules[0]!["id"] = "docs-id";
        docsRules[0]!["description"] = "PowerForge Shared [example.com]: security headers";
        var appRules = CloudflareResponseHeaderPolicyBuilder.BuildManagedRules(
            "example.com", "Shared", new AgentSecurityHeadersSpec { Hsts = false }, "/app/");
        appRules[0]!["id"] = "app-id";
        appRules[0]!["description"] = "PowerForge Shared [example.com]: security headers";
        var existingRules = new JsonArray(docsRules[0]!.DeepClone(), appRules[0]!.DeepClone());
        var handler = new SequenceHandler(
            JsonResponse(HttpStatusCode.OK, ExistingEnvelope(existingRules)),
            JsonResponse(HttpStatusCode.OK, SuccessEnvelope()));
        using var client = NewClient(handler);

        var result = CloudflareResponseHeaderPolicyManager.Apply(
            "0123456789abcdef0123456789abcdef",
            "secret-token",
            "example.com",
            "Shared",
            new AgentSecurityHeadersSpec { Hsts = false },
            dryRun: false,
            client,
            basePath: "/docs/");

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.PreservedRuleCount);
        var rules = JsonNode.Parse(handler.Requests[1].Body)!["rules"]!.AsArray();
        Assert.Contains(rules, rule => rule!["description"]!.GetValue<string>() == "PowerForge [example.com/docs/]: Shared: security headers");
        Assert.Contains(rules, rule => rule!["id"]?.GetValue<string>() == "docs-id");
        Assert.Contains(rules, rule => rule!["id"]?.GetValue<string>() == "app-id");
    }

    [Fact]
    public void SitePolicy_ShouldRestoreCacheAfterAmbiguousTransportFailure()
    {
        var oldCacheRules = new JsonArray { ExistingRule("cache-custom", "Operator cache rule", "set_cache_settings") };
        var oldHeaderRules = new JsonArray { ExistingRule("header-custom", "Operator header rule") };
        var handler = new TransportFailureHandler(
            throwOnRequest: 4,
            JsonResponse(HttpStatusCode.OK, ExistingEnvelope(oldCacheRules)),
            JsonResponse(HttpStatusCode.OK, ExistingEnvelope(oldHeaderRules)),
            JsonResponse(HttpStatusCode.OK, ExistingEnvelope(oldCacheRules)),
            JsonResponse(HttpStatusCode.OK, SuccessEnvelope()));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.cloudflare.test/client/v4/") };

        var result = CloudflareSitePolicyManager.Apply(
            "0123456789abcdef0123456789abcdef",
            "secret-token",
            "officeimo.com",
            "OfficeIMO",
            htmlPaths: null,
            securityHeaders: new AgentSecurityHeadersSpec { Hsts = false },
            dryRun: false,
            httpClient: client);

        Assert.False(result.Success);
        Assert.Contains("previous cache-policy state was restored", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5, handler.Requests.Count);
        Assert.Equal(HttpMethod.Put, handler.Requests[4].Method);
        var restoredCache = JsonNode.Parse(handler.Requests[4].Body)!["rules"]!.AsArray();
        Assert.Equal("Operator cache rule", Assert.Single(restoredCache)!["description"]!.GetValue<string>());
    }

    [Fact]
    public void SitePolicy_ShouldDeleteNewCacheRulesetAfterAmbiguousCreateResponseLoss()
    {
        var oldHeaderRules = new JsonArray { ExistingRule("header-custom", "Operator header rule") };
        var desiredCacheRules = CloudflareCachePolicyBuilder.BuildManagedRules(
            "officeimo.com",
            "OfficeIMO",
            htmlPaths: null);
        var handler = new TransportFailureHandler(
            throwOnRequest: 4,
            JsonResponse(HttpStatusCode.NotFound, """{"success":false,"errors":[{"message":"not found"}]}"""),
            JsonResponse(HttpStatusCode.OK, ExistingEnvelope(oldHeaderRules)),
            JsonResponse(HttpStatusCode.NotFound, """{"success":false,"errors":[{"message":"not found"}]}"""),
            JsonResponse(HttpStatusCode.OK, ExistingEnvelope(desiredCacheRules, "new-cache-ruleset")),
            JsonResponse(HttpStatusCode.OK, SuccessEnvelope()));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.cloudflare.test/client/v4/") };

        var result = CloudflareSitePolicyManager.Apply(
            "0123456789abcdef0123456789abcdef",
            "secret-token",
            "officeimo.com",
            "OfficeIMO",
            htmlPaths: null,
            securityHeaders: new AgentSecurityHeadersSpec { Hsts = false },
            dryRun: false,
            httpClient: client);

        Assert.False(result.Success);
        Assert.Contains("previous cache-policy state was restored", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(6, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[4].Method);
        Assert.Equal(HttpMethod.Delete, handler.Requests[5].Method);
        Assert.EndsWith("/rulesets/new-cache-ruleset", handler.Requests[5].Uri.AbsolutePath, StringComparison.Ordinal);
    }

    private static JsonObject ExistingRule(string id, string description, string action = "rewrite", string expression = "true") => new()
    {
        ["id"] = id,
        ["ref"] = id,
        ["version"] = "3",
        ["last_updated"] = "2026-08-12T00:00:00Z",
        ["description"] = description,
        ["expression"] = expression,
        ["action"] = action,
        ["action_parameters"] = action == "rewrite"
            ? new JsonObject { ["headers"] = new JsonObject() }
            : new JsonObject { ["cache"] = true },
        ["enabled"] = true
    };

    private static string ExistingEnvelope(JsonArray rules, string? id = null)
    {
        var result = new JsonObject { ["rules"] = rules.DeepClone() };
        if (!string.IsNullOrWhiteSpace(id))
            result["id"] = id;
        return new JsonObject
        {
            ["success"] = true,
            ["result"] = result
        }.ToJsonString();
    }

    private static string SuccessEnvelope(string? id = null) => new JsonObject
    {
        ["success"] = true,
        ["result"] = string.IsNullOrWhiteSpace(id)
            ? new JsonObject()
            : new JsonObject { ["id"] = id }
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

    private sealed class TransportFailureHandler(int throwOnRequest, params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        internal List<CapturedRequest> Requests { get; } = [];

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) => CaptureAndRespond(request);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(CaptureAndRespond(request));

        private HttpResponseMessage CaptureAndRespond(HttpRequestMessage request)
        {
            var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, request.Headers.Authorization, body));
            if (Requests.Count == throwOnRequest)
                throw new HttpRequestException("simulated response loss");
            return _responses.Dequeue();
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, AuthenticationHeaderValue? Authorization, string Body);
}
