using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AngleSharp.Dom;
using HtmlTinkerX;

namespace PowerForge.Web;

/// <summary>Prepares and checks static-site agent-readiness discovery signals.</summary>
public static class WebAgentReadiness
{
    private const string AgentBlockStart = "# BEGIN PowerForge Agent Readiness";
    private const string AgentBlockEnd = "# END PowerForge Agent Readiness";
    private const string AgentSkillsSchema = "https://schemas.agentskills.io/discovery/0.2.0/schema.json";
    private static readonly StringComparison FileSystemPathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private static readonly Regex GeneratedBlockRegex = new(
        @"(?ms)^\s*# BEGIN PowerForge Agent Readiness\s*$.*?^\s*# END PowerForge Agent Readiness\s*$\r?\n?",
        RegexOptions.Compiled);
    private static readonly Regex MarkdownWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex MarkdownTrailingWhitespaceRegex = new(@"[ \t]+\n", RegexOptions.Compiled);
    private static readonly Regex MarkdownBlankLinesRegex = new(@"\n{3,}", RegexOptions.Compiled);
    private static readonly Regex MarkdownRepeatedSpacesRegex = new(@"[ \t]{2,}", RegexOptions.Compiled);

    /// <summary>Writes configured agent-readiness files under the site root.</summary>
    public static WebAgentReadinessResult Prepare(WebAgentReadinessPrepareOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.SiteRoot))
            throw new ArgumentException("SiteRoot is required.", nameof(options));

        var siteRoot = Path.GetFullPath(options.SiteRoot.Trim().Trim('"'));
        Directory.CreateDirectory(siteRoot);

        var spec = ResolveSpec(options.AgentReadiness);
        var baseUrl = NormalizeBaseUrl(options.BaseUrl);
        var siteName = string.IsNullOrWhiteSpace(options.SiteName) ? "Site" : options.SiteName!.Trim();
        var written = new List<string>();
        var warnings = new List<string>();
        var linkTargets = new List<HeaderLinkTarget>();
        var markdownArtifactPaths = Array.Empty<string>();

        if (!spec.Enabled)
        {
            return new WebAgentReadinessResult
            {
                Operation = "prepare",
                SiteRoot = siteRoot,
                BaseUrl = baseUrl,
                Success = true,
                Warnings = new[] { "AgentReadiness is disabled." }
            };
        }

        if (spec.Robots)
            written.Add(UpdateRobots(siteRoot, baseUrl, spec));

        if (spec.ApiCatalog?.Enabled == true)
        {
            var apiCatalogPath = WriteApiCatalog(siteRoot, baseUrl, spec.ApiCatalog, warnings);
            if (!string.IsNullOrWhiteSpace(apiCatalogPath))
            {
                written.Add(apiCatalogPath!);
                linkTargets.Add(new HeaderLinkTarget(ToSiteRoute(siteRoot, apiCatalogPath!), "api-catalog", "application/linkset+json"));
            }
        }

        if (spec.AgentSkills?.Enabled == true)
        {
            var agentSkillsPath = WriteAgentSkills(siteRoot, baseUrl, siteName, spec.AgentSkills, warnings);
            if (!string.IsNullOrWhiteSpace(agentSkillsPath))
            {
                written.Add(agentSkillsPath!);
                linkTargets.Add(new HeaderLinkTarget(ToSiteRoute(siteRoot, agentSkillsPath!), "describedby", "application/json"));
            }
        }

        if (spec.AgentsJson?.Enabled == true)
        {
            var agentsPaths = WriteAgentsJson(siteRoot, baseUrl, siteName, spec, warnings);
            written.AddRange(agentsPaths);
            if (agentsPaths.Length > 0)
                linkTargets.Add(new HeaderLinkTarget(ToSiteRoute(siteRoot, agentsPaths[0]), "describedby", "application/json"));
        }

        if (spec.A2AAgentCard?.Enabled == true)
        {
            var agentCardPath = WriteA2AAgentCard(siteRoot, baseUrl, siteName, spec.A2AAgentCard, warnings);
            if (!string.IsNullOrWhiteSpace(agentCardPath))
            {
                written.Add(agentCardPath!);
                linkTargets.Add(new HeaderLinkTarget(ToSiteRoute(siteRoot, agentCardPath!), "service-desc", "application/json"));
            }
        }

        if (spec.McpServerCard?.Enabled == true)
        {
            var serverCardPath = WriteMcpServerCard(siteRoot, baseUrl, siteName, spec.McpServerCard, warnings);
            if (!string.IsNullOrWhiteSpace(serverCardPath))
            {
                written.Add(serverCardPath!);
                linkTargets.Add(new HeaderLinkTarget(ToSiteRoute(siteRoot, serverCardPath!), "service-desc", "application/json"));
            }
        }

        if (spec.MarkdownArtifacts?.Enabled == true)
        {
            var markdownArtifacts = WriteMarkdownArtifacts(siteRoot, spec.MarkdownArtifacts, warnings);
            markdownArtifactPaths = markdownArtifacts.Paths;
            written.AddRange(markdownArtifacts.Paths);
            if (!string.IsNullOrWhiteSpace(markdownArtifacts.RootRoute))
                linkTargets.Add(new HeaderLinkTarget(markdownArtifacts.RootRoute!, "alternate", "text/markdown"));
        }

        if (File.Exists(Path.Combine(siteRoot, "llms.txt")))
            linkTargets.Add(new HeaderLinkTarget("/llms.txt", "service-doc", "text/plain"));

        var openApiPath = ResolveOpenApiRoute(siteRoot, spec.OpenApi);
        if (!string.IsNullOrWhiteSpace(openApiPath))
            linkTargets.Add(new HeaderLinkTarget(openApiPath!, "service-desc", "application/openapi+json"));

        if (ShouldWriteHeaders(spec, linkTargets))
            written.Add(UpdateHeaders(siteRoot, spec, linkTargets, markdownArtifactPaths));

        if (spec.Apache?.Enabled == true)
            written.Add(UpdateApacheConfig(siteRoot, spec, linkTargets, markdownArtifactPaths));

        var verify = Verify(new WebAgentReadinessVerifyOptions
        {
            SiteRoot = siteRoot,
            BaseUrl = baseUrl,
            AgentReadiness = spec
        });

        return new WebAgentReadinessResult
        {
            Operation = "prepare",
            SiteRoot = siteRoot,
            BaseUrl = baseUrl,
            Success = verify.Success,
            WrittenFiles = written
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Checks = verify.Checks,
            Warnings = warnings.Concat(verify.Warnings).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    /// <summary>Verifies agent-readiness files in a local static site output.</summary>
    public static WebAgentReadinessResult Verify(WebAgentReadinessVerifyOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.SiteRoot))
            throw new ArgumentException("SiteRoot is required.", nameof(options));

        var siteRoot = Path.GetFullPath(options.SiteRoot.Trim().Trim('"'));
        var spec = ResolveSpec(options.AgentReadiness);
        var checks = new List<WebAgentReadinessCheck>();
        var warnings = new List<string>();

        if (!spec.Enabled)
        {
            return new WebAgentReadinessResult
            {
                Operation = "verify",
                SiteRoot = siteRoot,
                BaseUrl = NormalizeBaseUrl(options.BaseUrl),
                Success = true,
                Warnings = new[] { "AgentReadiness is disabled." }
            };
        }

        var robotsPath = Path.Combine(siteRoot, "robots.txt");
        var robotsText = File.Exists(robotsPath) ? File.ReadAllText(robotsPath) : null;
        var robotsExists = File.Exists(robotsPath);
        AddCheck(checks, "robots-txt", "discoverability", "robots.txt", robotsExists ? "pass" : (spec.Robots ? "fail" : "info"),
            robotsExists ? "robots.txt exists." : (spec.Robots ? "robots.txt is missing." : "robots.txt generation is disabled."), robotsPath);
        if (!string.IsNullOrWhiteSpace(robotsText))
        {
            var botRulesExpected = spec.Robots;
            AddCheck(checks, "ai-bot-rules", "bot-access-control", "AI bot rules in robots.txt",
                HasRobotsUserAgent(robotsText!) ? "pass" : (botRulesExpected ? "fail" : "info"),
                HasRobotsUserAgent(robotsText!) ? "robots.txt declares crawler rules." : (botRulesExpected ? "robots.txt has no User-agent rules." : "robots.txt generation is disabled."),
                robotsPath);
            var contentSignalsExpected = spec.Robots && spec.ContentSignals?.Enabled == true;
            AddCheck(checks, "content-signals", "bot-access-control", "Content Signals in robots.txt",
                HasContentSignals(robotsText!) ? "pass" : (contentSignalsExpected ? "fail" : "info"),
                HasContentSignals(robotsText!) ? "robots.txt declares Content-Signal preferences." : (contentSignalsExpected ? "No Content-Signal directive found." : "Content Signals are disabled."),
                robotsPath);
        }

        var sitemapPath = Path.Combine(siteRoot, "sitemap.xml");
        AddCheck(checks, "sitemap", "discoverability", "sitemap.xml", ValidateSitemap(sitemapPath, out var sitemapMessage) ? "pass" : "fail",
            sitemapMessage, sitemapPath);

        var headersPath = ResolveSitePath(siteRoot, string.IsNullOrWhiteSpace(spec.HeadersPath) ? "_headers" : spec.HeadersPath!);
        var headersText = File.Exists(headersPath) ? File.ReadAllText(headersPath) : string.Empty;
        var apacheEnabled = spec.Apache?.Enabled == true;
        var apachePath = apacheEnabled ? ResolveSitePath(siteRoot, spec.Apache!.EffectiveOutputPath) : null;
        var apacheText = apachePath is not null && File.Exists(apachePath) ? File.ReadAllText(apachePath) : string.Empty;
        var effectiveHeadersText = string.Join(Environment.NewLine, headersText, apacheText);
        var fallbackHeadersPath = File.Exists(headersPath) ? headersPath : (apachePath ?? headersPath);
        var linkHeaderPath = ResolveHeaderSourcePath("Link", headersText, headersPath, apacheText, apachePath ?? headersPath, fallbackHeadersPath);
        var linkHeadersPresent = linkHeaderPath != fallbackHeadersPath || HeaderDirectiveExists(effectiveHeadersText, "Link");
        AddCheck(checks, "link-headers", "discoverability", "Link headers (RFC 8288)",
            linkHeadersPresent ? "pass" : (spec.LinkHeaders ? "fail" : "info"),
            linkHeadersPresent
                ? "Static host headers include Link discovery hints."
                : (spec.LinkHeaders ? "No static host Link headers found. Add _headers output or configure host-level response headers." : "Link header generation is disabled."),
            linkHeadersPresent ? linkHeaderPath : fallbackHeadersPath);

        AddSecurityHeaderChecks(checks, headersText, headersPath, apacheText, apachePath ?? headersPath, fallbackHeadersPath, spec.SecurityHeaders);

        var rootHtml = ReadFirstHtml(siteRoot);
        AddHtmlSemanticsChecks(checks, rootHtml.Text, rootHtml.Path);
        AddMarkdownArtifactChecks(checks, siteRoot, spec.MarkdownArtifacts);

        var apiCatalogPath = ResolveSitePath(siteRoot, string.IsNullOrWhiteSpace(spec.ApiCatalog?.OutputPath) ? ".well-known/api-catalog" : spec.ApiCatalog!.OutputPath!);
        var apiCatalogValid = ValidateApiCatalog(apiCatalogPath, out var apiCatalogMessage);
        var apiCatalogExpected = spec.ApiCatalog?.Enabled == true;
        AddCheck(checks, "api-catalog", "api-auth-mcp-skill-discovery", "API Catalog (RFC 9727)",
            apiCatalogValid ? "pass" : (apiCatalogExpected ? "fail" : "info"),
            apiCatalogValid ? apiCatalogMessage : (apiCatalogExpected ? apiCatalogMessage : "API catalog generation is disabled."),
            apiCatalogPath);

        var openApi = ResolveOpenApiRoute(siteRoot, spec.OpenApi);
        AddCheck(checks, "openapi", "agent-protocols", "OpenAPI",
            !string.IsNullOrWhiteSpace(openApi) ? "pass" : (spec.OpenApi?.Enabled == true ? "fail" : "info"),
            !string.IsNullOrWhiteSpace(openApi)
                ? $"OpenAPI document detected at {openApi}."
                : "No OpenAPI document configured or detected. Skip this for pure documentation sites.",
            siteRoot);

        var skillsIndexPath = ResolveSitePath(siteRoot, string.IsNullOrWhiteSpace(spec.AgentSkills?.IndexPath) ? ".well-known/agent-skills/index.json" : spec.AgentSkills!.IndexPath!);
        var skillsValid = ValidateAgentSkillsIndex(skillsIndexPath, siteRoot, out var skillsMessage);
        var skillsExpected = spec.AgentSkills?.Enabled == true;
        AddCheck(checks, "agent-skills", "api-auth-mcp-skill-discovery", "Agent Skills index",
            skillsValid ? "pass" : (skillsExpected ? "fail" : "info"),
            skillsValid ? skillsMessage : (skillsExpected ? skillsMessage : "Agent Skills index generation is disabled."),
            skillsIndexPath);

        var agentsJsonPath = ResolveSitePath(siteRoot, string.IsNullOrWhiteSpace(spec.AgentsJson?.OutputPath) ? "agents.json" : spec.AgentsJson!.OutputPath!);
        var agentsJsonValid = ValidateAgentsJson(agentsJsonPath, out var agentsMessage);
        var agentsJsonExpected = spec.AgentsJson?.Enabled == true;
        AddCheck(checks, "agents-json", "agent-protocols", "agents.json",
            agentsJsonValid ? "pass" : (agentsJsonExpected ? "fail" : "info"),
            agentsJsonValid ? agentsMessage : (agentsJsonExpected ? agentsMessage : "agents.json generation is disabled."),
            agentsJsonPath);

        var a2aPath = ResolveSitePath(siteRoot, string.IsNullOrWhiteSpace(spec.A2AAgentCard?.OutputPath) ? ".well-known/agent-card.json" : spec.A2AAgentCard!.OutputPath!);
        var a2aExpected = spec.A2AAgentCard?.Enabled == true;
        var a2aStatus = ValidateA2AAgentCard(a2aPath, out var a2aMessage) ? "pass" : (a2aExpected ? "fail" : "info");
        AddCheck(checks, "a2a-agent-card", "agent-protocols", "A2A Agent Card", a2aStatus, a2aMessage, a2aPath);

        var mcpCardPath = ResolveSitePath(siteRoot, string.IsNullOrWhiteSpace(spec.McpServerCard?.OutputPath) ? ".well-known/mcp/server-card.json" : spec.McpServerCard!.OutputPath!);
        var mcpExpected = spec.McpServerCard?.Enabled == true;
        var mcpStatus = ValidateMcpServerCard(mcpCardPath, out var mcpMessage) ? "pass" : (mcpExpected ? "fail" : "info");
        AddCheck(checks, "mcp-server-card", "api-auth-mcp-skill-discovery", "MCP Server Card", mcpStatus, mcpMessage, mcpCardPath);

        AddCheck(checks, "markdown-negotiation", "content", "Markdown for Agents",
            spec.MarkdownNegotiation ? "warn" : "info",
            spec.MarkdownNegotiation
                ? "Local static output cannot prove Accept: text/markdown negotiation. Use remote scan or host-level rules that serve markdown artifacts for markdown requests."
                : "Markdown negotiation is not expected for this site.",
            options.BaseUrl);

        if (spec.WebMcp)
        {
            var hasWebMcp = Directory.Exists(siteRoot) &&
                            Directory.EnumerateFiles(siteRoot, "*.html", SearchOption.AllDirectories)
                                .Take(500)
                                .Any(file => HtmlContainsWebMcpSignal(File.ReadAllText(file)));
            AddCheck(checks, "webmcp", "api-auth-mcp-skill-discovery", "WebMCP", hasWebMcp ? "pass" : "fail",
                hasWebMcp ? "Rendered HTML includes WebMCP tool registration or declarative tool annotations." : "No WebMCP browser tool registration or declarative tool annotations found.",
                siteRoot);
        }

        return new WebAgentReadinessResult
        {
            Operation = "verify",
            SiteRoot = siteRoot,
            BaseUrl = options.BaseUrl,
            Success = checks.All(static c => !string.Equals(c.Status, "fail", StringComparison.OrdinalIgnoreCase)),
            Checks = checks.ToArray(),
            Warnings = warnings.ToArray()
        };
    }

    /// <summary>Scans a live URL for the same agent-readiness signals PowerForge can prepare locally.</summary>
    public static async Task<WebAgentReadinessResult> ScanAsync(WebAgentReadinessScanOptions options, CancellationToken cancellationToken = default)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            throw new ArgumentException("BaseUrl is required.", nameof(options));

        var baseUrl = NormalizeBaseUrl(options.BaseUrl);
        var checks = new List<WebAgentReadinessCheck>();
        var warnings = new List<string>();
        using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(options.TimeoutMs <= 0 ? 15000 : options.TimeoutMs) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PowerForge.Web.AgentReady/1.0");

        var root = await TrySendAsync(http, HttpMethod.Get, baseUrl, null, cancellationToken).ConfigureAwait(false);
        var linkHeader = root.Response?.Headers.TryGetValues("Link", out var links) == true ? string.Join(", ", links) : string.Empty;
        AddCheck(checks, "link-headers", "discoverability", "Link headers (RFC 8288)",
            root.Success && linkHeader.Contains("api-catalog", StringComparison.OrdinalIgnoreCase) ? "pass" : "fail",
            root.Success && linkHeader.Contains("api-catalog", StringComparison.OrdinalIgnoreCase)
                ? "Homepage response includes agent discovery Link headers."
                : "Homepage response does not include Link headers pointing to agent discovery resources.",
            baseUrl);
        AddRemoteSecurityHeaderChecks(checks, root.Response, baseUrl);

        var rootText = await TryGetTextAsync(http, baseUrl, null, cancellationToken).ConfigureAwait(false);
        if (rootText.Success)
            AddHtmlSemanticsChecks(checks, rootText.Text, baseUrl);

        var robots = await TryGetTextAsync(http, CombineUrl(baseUrl, "/robots.txt"), null, cancellationToken).ConfigureAwait(false);
        AddCheck(checks, "robots-txt", "discoverability", "robots.txt", robots.Success ? "pass" : "fail", robots.Message, CombineUrl(baseUrl, "/robots.txt"));
        if (robots.Success)
        {
            AddCheck(checks, "ai-bot-rules", "bot-access-control", "AI bot rules in robots.txt",
                HasRobotsUserAgent(robots.Text) ? "pass" : "fail",
                HasRobotsUserAgent(robots.Text) ? "robots.txt declares crawler rules." : "robots.txt has no User-agent rules.",
                CombineUrl(baseUrl, "/robots.txt"));
            AddCheck(checks, "content-signals", "bot-access-control", "Content Signals in robots.txt",
                HasContentSignals(robots.Text) ? "pass" : "fail",
                HasContentSignals(robots.Text) ? "robots.txt declares Content-Signal preferences." : "No Content-Signal directive found.",
                CombineUrl(baseUrl, "/robots.txt"));
        }

        var sitemap = await TryGetTextAsync(http, CombineUrl(baseUrl, "/sitemap.xml"), null, cancellationToken).ConfigureAwait(false);
        AddCheck(checks, "sitemap", "discoverability", "sitemap.xml", sitemap.Success && LooksLikeSitemap(sitemap.Text) ? "pass" : "fail",
            sitemap.Success && LooksLikeSitemap(sitemap.Text) ? "sitemap.xml exists with valid structure." : sitemap.Message,
            CombineUrl(baseUrl, "/sitemap.xml"));

        var markdown = await TryGetTextAsync(http, baseUrl, "text/markdown", cancellationToken).ConfigureAwait(false);
        var markdownContentType = markdown.Response?.Content.Headers.ContentType?.MediaType ?? string.Empty;
        AddCheck(checks, "markdown-negotiation", "content", "Markdown for Agents",
            markdown.Success && markdownContentType.Contains("markdown", StringComparison.OrdinalIgnoreCase) ? "pass" : "fail",
            markdown.Success && markdownContentType.Contains("markdown", StringComparison.OrdinalIgnoreCase)
                ? "Accept: text/markdown returned markdown content."
                : "Accept: text/markdown did not return Content-Type text/markdown.",
            baseUrl);

        var apiCatalog = await TryGetTextAsync(http, CombineUrl(baseUrl, "/.well-known/api-catalog"), null, cancellationToken).ConfigureAwait(false);
        AddCheck(checks, "api-catalog", "api-auth-mcp-skill-discovery", "API Catalog (RFC 9727)",
            apiCatalog.Success && ValidateApiCatalogText(apiCatalog.Text) ? "pass" : "fail",
            apiCatalog.Success && ValidateApiCatalogText(apiCatalog.Text) ? "API catalog returned a Linkset document." : apiCatalog.Message,
            CombineUrl(baseUrl, "/.well-known/api-catalog"));

        var openApi = await TryFindOpenApiAsync(http, baseUrl, cancellationToken).ConfigureAwait(false);
        AddCheck(checks, "openapi", "agent-protocols", "OpenAPI",
            openApi.Success ? "pass" : "info",
            openApi.Success ? $"OpenAPI document detected at {openApi.Url}." : "No OpenAPI document found at common static paths.",
            openApi.Url ?? baseUrl);

        var agentSkills = await TryGetTextAsync(http, CombineUrl(baseUrl, "/.well-known/agent-skills/index.json"), null, cancellationToken).ConfigureAwait(false);
        var agentSkillsValid = agentSkills.Success && ValidateAgentSkillsIndexText(agentSkills.Text);
        AddCheck(checks, "agent-skills", "api-auth-mcp-skill-discovery", "Agent Skills index",
            agentSkillsValid ? "pass" : "info",
            agentSkillsValid ? "Agent Skills discovery index is valid." : "Agent Skills index was not found or is not valid.",
            CombineUrl(baseUrl, "/.well-known/agent-skills/index.json"));

        var agentsJson = await TryGetTextAsync(http, CombineUrl(baseUrl, "/agents.json"), null, cancellationToken).ConfigureAwait(false);
        var agentsJsonValid = agentsJson.Success && ValidateAgentsJsonText(agentsJson.Text);
        AddCheck(checks, "agents-json", "agent-protocols", "agents.json",
            agentsJsonValid ? "pass" : "info",
            agentsJsonValid ? "agents.json discovery document is valid." : "agents.json was not found or is not valid.",
            CombineUrl(baseUrl, "/agents.json"));

        var a2a = await TryGetTextAsync(http, CombineUrl(baseUrl, "/.well-known/agent-card.json"), null, cancellationToken).ConfigureAwait(false);
        AddCheck(checks, "a2a-agent-card", "agent-protocols", "A2A Agent Card",
            a2a.Success && ValidateA2AAgentCardText(a2a.Text) ? "pass" : "info",
            a2a.Success && ValidateA2AAgentCardText(a2a.Text) ? "A2A Agent Card is valid." : "A2A Agent Card was not found or is not valid.",
            CombineUrl(baseUrl, "/.well-known/agent-card.json"));

        var mcp = await TryGetTextAsync(http, CombineUrl(baseUrl, "/.well-known/mcp/server-card.json"), null, cancellationToken).ConfigureAwait(false);
        AddCheck(checks, "mcp-server-card", "api-auth-mcp-skill-discovery", "MCP Server Card",
            mcp.Success && ValidateMcpServerCardText(mcp.Text) ? "pass" : "info",
            mcp.Success && ValidateMcpServerCardText(mcp.Text) ? "MCP Server Card is valid." : "MCP Server Card was not found or is not valid.",
            CombineUrl(baseUrl, "/.well-known/mcp/server-card.json"));

        return new WebAgentReadinessResult
        {
            Operation = "scan",
            BaseUrl = baseUrl,
            Success = checks.All(static c => !string.Equals(c.Status, "fail", StringComparison.OrdinalIgnoreCase)),
            Checks = checks.ToArray(),
            Warnings = warnings.ToArray()
        };
    }

    private static AgentReadinessSpec ResolveSpec(AgentReadinessSpec? spec)
    {
        if (spec is not null)
        {
            var resolved = new AgentReadinessSpec
            {
                Enabled = spec.Enabled,
                HeadersPath = spec.HeadersPath,
                LinkHeaders = spec.LinkHeaders,
                SecurityHeaders = spec.SecurityHeaders,
                Robots = spec.Robots,
                ContentSignals = spec.ContentSignals,
                BotRules = spec.BotRules,
                ApiCatalog = spec.ApiCatalog,
                AgentSkills = spec.AgentSkills,
                AgentsJson = spec.AgentsJson,
                A2AAgentCard = spec.A2AAgentCard,
                McpServerCard = spec.McpServerCard,
                OpenApi = spec.OpenApi,
                WebMcp = spec.WebMcp,
                MarkdownArtifacts = spec.MarkdownArtifacts,
                MarkdownNegotiation = spec.MarkdownNegotiation,
                Apache = spec.Apache
            };

            if (resolved.Enabled)
            {
                resolved.SecurityHeaders ??= new AgentSecurityHeadersSpec();
                resolved.ContentSignals ??= new AgentContentSignalsSpec();
                resolved.ApiCatalog ??= new AgentApiCatalogSpec();
                resolved.AgentSkills ??= new AgentSkillsDiscoverySpec();
                resolved.AgentsJson ??= new AgentDiscoveryDocumentSpec();
            }

            return resolved;
        }

        return new AgentReadinessSpec
        {
            Enabled = true,
            SecurityHeaders = new AgentSecurityHeadersSpec(),
            ContentSignals = new AgentContentSignalsSpec(),
            ApiCatalog = new AgentApiCatalogSpec(),
            AgentSkills = new AgentSkillsDiscoverySpec(),
            AgentsJson = new AgentDiscoveryDocumentSpec(),
            Apache = new AgentApacheSupportSpec { Enabled = false }
        };
    }

    private static string UpdateRobots(string siteRoot, string? baseUrl, AgentReadinessSpec spec)
    {
        var path = Path.Combine(siteRoot, "robots.txt");
        var existing = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        var cleaned = RemoveGeneratedBlock(existing).TrimEnd();
        var block = BuildRobotsBlock(siteRoot, baseUrl, spec);
        var next = string.IsNullOrWhiteSpace(cleaned)
            ? block
            : cleaned + Environment.NewLine + Environment.NewLine + block;
        File.WriteAllText(path, next);
        return path;
    }

    private static string BuildRobotsBlock(string siteRoot, string? baseUrl, AgentReadinessSpec spec)
    {
        var sb = new StringBuilder();
        sb.AppendLine(AgentBlockStart);
        sb.AppendLine("User-agent: *");
        var signals = spec.ContentSignals ?? new AgentContentSignalsSpec();
        if (signals.Enabled)
        {
            sb.Append("Content-Signal: ");
            sb.Append("search=").Append(signals.Search ? "yes" : "no");
            sb.Append(", ai-input=").Append(signals.AiInput ? "yes" : "no");
            sb.Append(", ai-train=").Append(signals.AiTrain ? "yes" : "no");
            sb.AppendLine();
        }

        sb.AppendLine("Allow: /");
        if (!string.IsNullOrWhiteSpace(baseUrl) && File.Exists(Path.Combine(siteRoot, "sitemap.xml")))
            sb.Append("Sitemap: ").Append(CombineUrl(baseUrl!, "/sitemap.xml")).AppendLine();

        foreach (var rule in spec.BotRules.Where(static r => !string.IsNullOrWhiteSpace(r.UserAgent)))
        {
            sb.AppendLine();
            sb.Append("User-agent: ").Append(rule.UserAgent.Trim()).AppendLine();
            if (!string.IsNullOrWhiteSpace(rule.Disallow))
                sb.Append("Disallow: ").Append(NormalizeRoute(rule.Disallow!)).AppendLine();
            else
                sb.Append("Allow: ").Append(NormalizeRoute(rule.Allow ?? "/")).AppendLine();
        }

        sb.AppendLine(AgentBlockEnd);
        return sb.ToString();
    }

    private static string? WriteApiCatalog(string siteRoot, string? baseUrl, AgentApiCatalogSpec spec, List<string> warnings)
    {
        var output = string.IsNullOrWhiteSpace(spec.OutputPath) ? ".well-known/api-catalog" : spec.OutputPath!;
        var outputPath = ResolveSitePath(siteRoot, output);
        var entries = spec.Entries?.Where(static e => !string.IsNullOrWhiteSpace(e.Anchor)).ToList() ?? new List<AgentApiCatalogEntrySpec>();
        if (entries.Count == 0)
        {
            var apiIndex = Path.Combine(siteRoot, "api", "index.json");
            if (File.Exists(apiIndex))
            {
                entries.Add(new AgentApiCatalogEntrySpec
                {
                    Anchor = "/api/",
                    ServiceDesc = "/api/index.json",
                    ServiceDoc = "/api/",
                    Title = "API Reference"
                });
            }
        }

        if (entries.Count == 0)
        {
            warnings.Add("API catalog enabled but no entries were configured or inferred.");
            return null;
        }

        var linkset = new JsonArray();
        foreach (var entry in entries)
        {
            var item = new JsonObject
            {
                ["anchor"] = ToAbsoluteUrl(baseUrl, entry.Anchor)
            };
            AddLinkArray(item, "service-desc", baseUrl, entry.ServiceDesc, "application/json", entry.Title);
            AddLinkArray(item, "service-doc", baseUrl, entry.ServiceDoc, "text/html", entry.Title);
            AddLinkArray(item, "status", baseUrl, entry.Status, "application/json", "Status");
            linkset.Add(item);
        }

        var root = new JsonObject { ["linkset"] = linkset };
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, root.ToJsonString(WebJson.Options));
        return outputPath;
    }

    private static string? WriteAgentSkills(string siteRoot, string? baseUrl, string siteName, AgentSkillsDiscoverySpec spec, List<string> warnings)
    {
        var indexPath = ResolveSitePath(siteRoot, string.IsNullOrWhiteSpace(spec.IndexPath) ? ".well-known/agent-skills/index.json" : spec.IndexPath!);
        var skillSpecs = spec.Skills?.ToList() ?? new List<AgentSkillSpec>();
        if (skillSpecs.Count == 0)
        {
            skillSpecs.Add(new AgentSkillSpec
            {
                Name = "site-assistant",
                Type = "skill-md",
                Description = $"Use {siteName} documentation and API discovery resources.",
                Content = BuildDefaultSkill(siteName, baseUrl)
            });
        }

        var skills = new JsonArray();
        foreach (var skill in skillSpecs)
        {
            var name = Slugify(string.IsNullOrWhiteSpace(skill.Name) ? "site-assistant" : skill.Name);
            var content = ResolveSkillContent(skill);
            if (string.IsNullOrWhiteSpace(content))
            {
                warnings.Add($"Agent skill '{name}' has no content or source file.");
                continue;
            }

            var url = string.IsNullOrWhiteSpace(skill.Url)
                ? $"/.well-known/agent-skills/{name}/SKILL.md"
                : skill.Url!.Trim();

            if (url.StartsWith("/", StringComparison.Ordinal))
            {
                var skillPath = ResolveSitePath(siteRoot, url);
                Directory.CreateDirectory(Path.GetDirectoryName(skillPath)!);
                File.WriteAllText(skillPath, content);
            }

            skills.Add(new JsonObject
            {
                ["name"] = name,
                ["type"] = string.IsNullOrWhiteSpace(skill.Type) ? "skill-md" : skill.Type.Trim(),
                ["description"] = string.IsNullOrWhiteSpace(skill.Description) ? $"Use {siteName}." : skill.Description.Trim(),
                ["url"] = url,
                ["digest"] = "sha256:" + Sha256Hex(content)
            });
        }

        var root = new JsonObject
        {
            ["$schema"] = AgentSkillsSchema,
            ["skills"] = skills
        };
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        File.WriteAllText(indexPath, root.ToJsonString(WebJson.Options));
        return indexPath;
    }

    private static string[] WriteAgentsJson(string siteRoot, string? baseUrl, string siteName, AgentReadinessSpec spec, List<string> warnings)
    {
        var config = spec.AgentsJson ?? new AgentDiscoveryDocumentSpec();
        var output = string.IsNullOrWhiteSpace(config.OutputPath) ? "agents.json" : config.OutputPath!;
        var wellKnown = string.IsNullOrWhiteSpace(config.WellKnownOutputPath) ? ".well-known/agents.json" : config.WellKnownOutputPath!;
        var paths = new[] { output, wellKnown }
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var resources = new JsonObject
        {
            ["robots"] = ToAbsoluteUrl(baseUrl, "/robots.txt"),
            ["sitemap"] = ToAbsoluteUrl(baseUrl, "/sitemap.xml"),
            ["llms"] = File.Exists(Path.Combine(siteRoot, "llms.txt")) ? ToAbsoluteUrl(baseUrl, "/llms.txt") : null,
            ["apiCatalog"] = spec.ApiCatalog?.Enabled == true ? ToAbsoluteUrl(baseUrl, ResolveSiteRoute(siteRoot, spec.ApiCatalog.OutputPath, ".well-known/api-catalog")) : null,
            ["agentSkills"] = spec.AgentSkills?.Enabled == true ? ToAbsoluteUrl(baseUrl, ResolveSiteRoute(siteRoot, spec.AgentSkills.IndexPath, ".well-known/agent-skills/index.json")) : null,
            ["mcpServerCard"] = spec.McpServerCard?.Enabled == true ? ToAbsoluteUrl(baseUrl, ResolveSiteRoute(siteRoot, spec.McpServerCard.OutputPath, ".well-known/mcp/server-card.json")) : null,
            ["a2aAgentCard"] = spec.A2AAgentCard?.Enabled == true ? ToAbsoluteUrl(baseUrl, ResolveSiteRoute(siteRoot, spec.A2AAgentCard.OutputPath, ".well-known/agent-card.json")) : null
        };

        var root = new JsonObject
        {
            ["name"] = siteName,
            ["description"] = string.IsNullOrWhiteSpace(config.Description)
                ? $"Agent-facing discovery metadata for {siteName}."
                : config.Description!.Trim(),
            ["url"] = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl,
            ["resources"] = resources,
            ["capabilities"] = new JsonObject
            {
                ["contentNegotiation"] = spec.MarkdownNegotiation ? "text/markdown" : null,
                ["markdownArtifacts"] = spec.MarkdownArtifacts?.Enabled == true,
                ["contentSignals"] = spec.ContentSignals?.Enabled == true,
                ["webMcp"] = spec.WebMcp
            }
        };

        var written = new List<string>();
        foreach (var path in paths)
        {
            var outputPath = ResolveSitePath(siteRoot, path);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, root.ToJsonString(WebJson.Options));
            written.Add(outputPath);
        }

        if (written.Count == 0)
            warnings.Add("agents.json enabled but no output paths were configured.");

        return written.ToArray();
    }

    private static string? WriteA2AAgentCard(string siteRoot, string? baseUrl, string siteName, AgentA2ACardSpec spec, List<string> warnings)
    {
        var outputPath = ResolveSitePath(siteRoot, string.IsNullOrWhiteSpace(spec.OutputPath) ? ".well-known/agent-card.json" : spec.OutputPath!);
        var skills = new JsonArray();
        var skillSpecs = spec.Skills?.Where(static skill => !string.IsNullOrWhiteSpace(skill.Name) || !string.IsNullOrWhiteSpace(skill.Id)).ToArray() ?? Array.Empty<AgentA2ASkillSpec>();
        if (skillSpecs.Length == 0)
        {
            warnings.Add("A2A Agent Card enabled without explicit skills; writing a documentation discovery skill.");
            skillSpecs = new[]
            {
                new AgentA2ASkillSpec
                {
                    Id = "documentation-discovery",
                    Name = "Documentation discovery",
                    Description = $"Find public documentation, sitemap, llms.txt, and API reference resources for {siteName}.",
                    Tags = new[] { "documentation", "discovery" }
                }
            };
        }

        foreach (var skill in skillSpecs)
        {
            skills.Add(new JsonObject
            {
                ["id"] = Slugify(string.IsNullOrWhiteSpace(skill.Id) ? skill.Name : skill.Id),
                ["name"] = string.IsNullOrWhiteSpace(skill.Name) ? "Documentation discovery" : skill.Name.Trim(),
                ["description"] = string.IsNullOrWhiteSpace(skill.Description) ? $"Use {siteName} public documentation." : skill.Description.Trim(),
                ["tags"] = new JsonArray((skill.Tags ?? Array.Empty<string>()).Select(tag => JsonValue.Create(tag)).ToArray())
            });
        }

        var root = new JsonObject
        {
            ["name"] = string.IsNullOrWhiteSpace(spec.Name) ? siteName : spec.Name!.Trim(),
            ["description"] = string.IsNullOrWhiteSpace(spec.Description)
                ? $"Public documentation and discovery surface for {siteName}."
                : spec.Description!.Trim(),
            ["url"] = ToAbsoluteUrl(baseUrl, string.IsNullOrWhiteSpace(spec.Url) ? "/" : spec.Url!),
            ["version"] = string.IsNullOrWhiteSpace(spec.Version) ? "1.0.0" : spec.Version!.Trim(),
            ["documentationUrl"] = string.IsNullOrWhiteSpace(spec.DocumentationUrl) ? ToAbsoluteUrl(baseUrl, "/docs/") : ToAbsoluteUrl(baseUrl, spec.DocumentationUrl!),
            ["capabilities"] = new JsonObject
            {
                ["streaming"] = false,
                ["pushNotifications"] = false
            },
            ["defaultInputModes"] = new JsonArray((spec.DefaultInputModes ?? Array.Empty<string>()).Select(mode => JsonValue.Create(mode)).ToArray()),
            ["defaultOutputModes"] = new JsonArray((spec.DefaultOutputModes ?? Array.Empty<string>()).Select(mode => JsonValue.Create(mode)).ToArray()),
            ["skills"] = skills
        };
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, root.ToJsonString(WebJson.Options));
        return outputPath;
    }

    private static string? WriteMcpServerCard(string siteRoot, string? baseUrl, string siteName, AgentMcpServerCardSpec spec, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(spec.Endpoint))
        {
            warnings.Add("MCP Server Card enabled but no endpoint was configured.");
            return null;
        }

        var outputPath = ResolveSitePath(siteRoot, string.IsNullOrWhiteSpace(spec.OutputPath) ? ".well-known/mcp/server-card.json" : spec.OutputPath!);
        var root = new JsonObject
        {
            ["serverInfo"] = new JsonObject
            {
                ["name"] = string.IsNullOrWhiteSpace(spec.Name) ? siteName : spec.Name!.Trim(),
                ["version"] = string.IsNullOrWhiteSpace(spec.Version) ? "1.0.0" : spec.Version!.Trim()
            },
            ["transport"] = new JsonObject
            {
                ["type"] = string.IsNullOrWhiteSpace(spec.Transport) ? "streamable-http" : spec.Transport!.Trim(),
                ["endpoint"] = ToAbsoluteUrl(baseUrl, spec.Endpoint!)
            },
            ["capabilities"] = new JsonObject
            {
                ["tools"] = spec.Tools,
                ["resources"] = spec.Resources,
                ["prompts"] = spec.Prompts
            }
        };
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, root.ToJsonString(WebJson.Options));
        return outputPath;
    }

    private static MarkdownArtifactWriteResult WriteMarkdownArtifacts(string siteRoot, AgentMarkdownArtifactsSpec spec, List<string> warnings)
    {
        if (!Directory.Exists(siteRoot))
            return new MarkdownArtifactWriteResult(Array.Empty<string>(), null);

        var extension = NormalizeMarkdownExtension(spec.Extension);
        var candidates = Directory.EnumerateFiles(siteRoot, "*.html", SearchOption.AllDirectories)
            .Where(path => !ShouldSkipMarkdownArtifactCandidate(siteRoot, path))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var maxPages = spec.MaxPages;
        if (maxPages > 0 && candidates.Length > maxPages)
        {
            warnings.Add($"Markdown artifact generation limited to {maxPages} of {candidates.Length} HTML pages.");
            candidates = candidates.Take(maxPages).ToArray();
        }

        var written = new List<string>();
        foreach (var htmlPath in candidates)
        {
            var markdown = ConvertHtmlDocumentToMarkdown(File.ReadAllText(htmlPath, Encoding.UTF8), spec);
            if (string.IsNullOrWhiteSpace(markdown))
                continue;

            var outputPath = Path.ChangeExtension(htmlPath, extension);
            File.WriteAllText(outputPath, markdown, Encoding.UTF8);
            written.Add(outputPath);
        }

        var rootMarkdown = Path.Combine(siteRoot, "index" + extension);
        var rootRoute = File.Exists(rootMarkdown) ? ToSiteRoute(siteRoot, rootMarkdown) : null;
        return new MarkdownArtifactWriteResult(written.ToArray(), rootRoute);
    }

    private static bool ShouldSkipMarkdownArtifactCandidate(string siteRoot, string path)
    {
        var relative = Path.GetRelativePath(siteRoot, path).Replace('\\', '/');
        return relative.EndsWith(".head.html", StringComparison.OrdinalIgnoreCase) ||
               relative.EndsWith(".scripts.html", StringComparison.OrdinalIgnoreCase) ||
               relative.Contains("/api-fragments/", StringComparison.OrdinalIgnoreCase) ||
               relative.StartsWith("api-fragments/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeMarkdownExtension(string? extension)
    {
        var value = string.IsNullOrWhiteSpace(extension) ? ".md" : extension!.Trim();
        if (!value.StartsWith(".", StringComparison.Ordinal))
            value = "." + value;
        return value.Equals(".", StringComparison.Ordinal) ? ".md" : value;
    }

    private static string ConvertHtmlDocumentToMarkdown(string html, AgentMarkdownArtifactsSpec spec)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var doc = HtmlParser.ParseWithAngleSharp(html);
        var contentRoot = doc.QuerySelector("main,[role='main'],article") ?? doc.Body ?? doc.DocumentElement;
        if (contentRoot is null)
            return string.Empty;

        var body = NormalizeMarkdownDocument(RenderMarkdownChildren(contentRoot));
        var title = doc.Title?.Trim();
        if (spec.IncludeTitle &&
            !string.IsNullOrWhiteSpace(title) &&
            !body.StartsWith("# " + title, StringComparison.OrdinalIgnoreCase))
        {
            body = "# " + title + Environment.NewLine + Environment.NewLine + body.TrimStart();
        }

        return body.Trim() + Environment.NewLine;
    }

    private static string RenderMarkdownChildren(INode node)
    {
        var sb = new StringBuilder();
        foreach (var child in node.ChildNodes)
            sb.Append(RenderMarkdownNode(child));
        return sb.ToString();
    }

    private static string RenderMarkdownNode(INode node)
    {
        if (node.NodeType == NodeType.Text)
            return NormalizeInlineWhitespace(node.TextContent);

        if (node is not IElement element)
            return string.Empty;

        var tag = element.TagName.ToLowerInvariant();
        return tag switch
        {
            "h1" => RenderHeading(element, 1),
            "h2" => RenderHeading(element, 2),
            "h3" => RenderHeading(element, 3),
            "h4" => RenderHeading(element, 4),
            "h5" => RenderHeading(element, 5),
            "h6" => RenderHeading(element, 6),
            "p" => Block(RenderMarkdownChildren(element)),
            "br" => Environment.NewLine,
            "strong" or "b" => WrapInline("**", RenderMarkdownChildren(element)),
            "em" or "i" => WrapInline("*", RenderMarkdownChildren(element)),
            "code" when !string.Equals(element.ParentElement?.TagName, "pre", StringComparison.OrdinalIgnoreCase) => "`" + element.TextContent.Trim() + "`",
            "pre" => RenderCodeBlock(element),
            "a" => RenderLink(element),
            "img" => RenderImage(element),
            "ul" => RenderList(element, ordered: false),
            "ol" => RenderList(element, ordered: true),
            "li" => RenderMarkdownChildren(element),
            "blockquote" => RenderBlockquote(element),
            "hr" => Environment.NewLine + Environment.NewLine + "---" + Environment.NewLine + Environment.NewLine,
            "script" or "style" or "noscript" or "svg" => string.Empty,
            _ => IsMarkdownBlockElement(tag) ? Block(RenderMarkdownChildren(element)) : RenderMarkdownChildren(element)
        };
    }

    private static string RenderHeading(IElement element, int level)
    {
        var text = CollapseMarkdownInline(RenderMarkdownChildren(element));
        return string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : Environment.NewLine + Environment.NewLine + new string('#', level) + " " + text + Environment.NewLine + Environment.NewLine;
    }

    private static string RenderCodeBlock(IElement element)
    {
        var text = element.TextContent.Trim('\r', '\n');
        return string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : Environment.NewLine + Environment.NewLine + "```" + Environment.NewLine + text + Environment.NewLine + "```" + Environment.NewLine + Environment.NewLine;
    }

    private static string RenderLink(IElement element)
    {
        var text = CollapseMarkdownInline(RenderMarkdownChildren(element));
        var href = element.GetAttribute("href")?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            text = href ?? string.Empty;
        return string.IsNullOrWhiteSpace(href) ? text : $"[{EscapeMarkdownLinkText(text)}]({href})";
    }

    private static string RenderImage(IElement element)
    {
        var src = element.GetAttribute("src")?.Trim();
        if (string.IsNullOrWhiteSpace(src))
            return string.Empty;
        var alt = element.GetAttribute("alt")?.Trim() ?? string.Empty;
        return $"![{EscapeMarkdownLinkText(alt)}]({src})";
    }

    private static string RenderList(IElement element, bool ordered)
    {
        var index = 1;
        var lines = new List<string>();
        foreach (var item in element.Children.Where(static child => child.TagName.Equals("li", StringComparison.OrdinalIgnoreCase)))
        {
            var text = NormalizeMarkdownDocument(RenderMarkdownChildren(item)).Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;
            var prefix = ordered ? $"{index}. " : "- ";
            lines.Add(prefix + text.Replace(Environment.NewLine, Environment.NewLine + "  ", StringComparison.Ordinal));
            index++;
        }

        return lines.Count == 0
            ? string.Empty
            : Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, lines) + Environment.NewLine + Environment.NewLine;
    }

    private static string RenderBlockquote(IElement element)
    {
        var text = NormalizeMarkdownDocument(RenderMarkdownChildren(element)).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var quoted = string.Join(Environment.NewLine, text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Select(line => "> " + line));
        return Environment.NewLine + Environment.NewLine + quoted + Environment.NewLine + Environment.NewLine;
    }

    private static string Block(string text)
    {
        var normalized = NormalizeMarkdownDocument(text).Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? string.Empty
            : Environment.NewLine + Environment.NewLine + normalized + Environment.NewLine + Environment.NewLine;
    }

    private static string WrapInline(string marker, string text)
    {
        var normalized = CollapseMarkdownInline(text);
        return string.IsNullOrWhiteSpace(normalized) ? string.Empty : marker + normalized + marker;
    }

    private static bool IsMarkdownBlockElement(string tag)
        => tag is "address" or "article" or "aside" or "div" or "dl" or "fieldset" or "figcaption" or "figure" or
            "footer" or "form" or "header" or "main" or "nav" or "section" or "table";

    private static string NormalizeInlineWhitespace(string text)
        => string.IsNullOrWhiteSpace(text) ? string.Empty : MarkdownWhitespaceRegex.Replace(text, " ");

    private static string CollapseMarkdownInline(string text)
        => MarkdownWhitespaceRegex.Replace(NormalizeMarkdownDocument(text).Replace(Environment.NewLine, " ", StringComparison.Ordinal), " ").Trim();

    private static string NormalizeMarkdownDocument(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        normalized = MarkdownTrailingWhitespaceRegex.Replace(normalized, "\n");
        normalized = MarkdownBlankLinesRegex.Replace(normalized, "\n\n");
        normalized = MarkdownRepeatedSpacesRegex.Replace(normalized, " ");
        return normalized.Replace("\n", Environment.NewLine, StringComparison.Ordinal).Trim();
    }

    private static string EscapeMarkdownLinkText(string text)
        => text.Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal);

    private static bool HtmlContainsWebMcpSignal(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return false;

        if (html.Contains("navigator.modelContext.provideContext", StringComparison.Ordinal))
            return true;

        if (!html.Contains("tool-name", StringComparison.OrdinalIgnoreCase) &&
            !html.Contains("tool-description", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var doc = HtmlParser.ParseWithAngleSharp(html);
            return doc.QuerySelector("[tool-name],[tool-description]") is not null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    private static bool ShouldWriteHeaders(AgentReadinessSpec spec, List<HeaderLinkTarget> linkTargets)
        => spec.SecurityHeaders?.Enabled == true ||
           (spec.LinkHeaders && linkTargets.Count > 0) ||
           spec.ApiCatalog?.Enabled == true ||
           spec.AgentSkills?.Enabled == true ||
           spec.AgentsJson?.Enabled == true ||
           spec.A2AAgentCard?.Enabled == true ||
           spec.McpServerCard?.Enabled == true ||
           spec.MarkdownArtifacts?.Enabled == true;

    private static string UpdateHeaders(string siteRoot, AgentReadinessSpec spec, List<HeaderLinkTarget> linkTargets, IReadOnlyList<string> markdownArtifactPaths)
    {
        var headersPath = spec.HeadersPath;
        var path = ResolveSitePath(siteRoot, string.IsNullOrWhiteSpace(headersPath) ? "_headers" : headersPath!);
        var existing = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        var cleaned = RemoveGeneratedBlock(existing).TrimEnd();
        var sb = new StringBuilder();
        sb.AppendLine(AgentBlockStart);
        var security = spec.SecurityHeaders ?? new AgentSecurityHeadersSpec();
        var writeLinkHeaders = spec.LinkHeaders && linkTargets.Count > 0;
        if (security.Enabled || writeLinkHeaders)
        {
            sb.AppendLine("/");
            AppendSecurityHeaders(sb, security);
            if (writeLinkHeaders)
            {
                foreach (var target in linkTargets.DistinctBy(static t => t.Href, StringComparer.OrdinalIgnoreCase))
                {
                    sb.Append("  Link: ").AppendLine(BuildLinkHeaderValue(target));
                }
            }
        }

        AppendDiscoveryResourceHeaders(sb, siteRoot, spec, security, markdownArtifactPaths);
        sb.AppendLine(AgentBlockEnd);

        var next = string.IsNullOrWhiteSpace(cleaned)
            ? sb.ToString()
            : cleaned + Environment.NewLine + Environment.NewLine + sb;
        CreateParentDirectory(path);
        File.WriteAllText(path, next);
        return path;
    }

    private static string UpdateApacheConfig(string siteRoot, AgentReadinessSpec spec, List<HeaderLinkTarget> linkTargets, IReadOnlyList<string> markdownArtifactPaths)
    {
        var apache = spec.Apache ?? new AgentApacheSupportSpec();
        var path = ResolveSitePath(siteRoot, apache.EffectiveOutputPath);
        var existing = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        var cleaned = RemoveGeneratedBlock(existing).TrimEnd();
        var sb = new StringBuilder();
        var security = spec.SecurityHeaders ?? new AgentSecurityHeadersSpec();
        var contentSignals = spec.ContentSignals ?? new AgentContentSignalsSpec();
        var writeLinkHeaders = apache.LinkHeaders && spec.LinkHeaders && linkTargets.Count > 0;
        var writeContentSignals = apache.ContentSignalsHeader && contentSignals.Enabled;
        var writeMarkdownNegotiation = apache.MarkdownNegotiation && spec.MarkdownNegotiation && spec.MarkdownArtifacts?.Enabled == true && markdownArtifactPaths.Count > 0;
        var writeDiscoveryHeaders = apache.DiscoveryResourceHeaders && HasApacheDiscoveryResources(spec);
        var markdownExtension = NormalizeMarkdownExtension(spec.MarkdownArtifacts?.Extension);

        sb.AppendLine(AgentBlockStart);

        if (writeLinkHeaders || writeContentSignals || security.Enabled || writeDiscoveryHeaders || writeMarkdownNegotiation)
        {
            sb.AppendLine("<IfModule mod_headers.c>");
            if (writeMarkdownNegotiation)
                sb.AppendLine("  Header merge Vary \"Accept\"");

            if (security.Enabled)
            {
                AppendApacheHeaderSet(sb, "Strict-Transport-Security", security.Hsts ? security.HstsValue : null);
                AppendApacheHeaderSet(sb, "Content-Security-Policy", security.ContentSecurityPolicy ? security.ContentSecurityPolicyValue : null);
                if (security.XContentTypeOptions)
                    AppendApacheHeaderSet(sb, "X-Content-Type-Options", "nosniff");
                if (security.XFrameOptions)
                    AppendApacheHeaderSet(sb, "X-Frame-Options", "DENY");
                if (security.ReferrerPolicy)
                    AppendApacheHeaderSet(sb, "Referrer-Policy", string.IsNullOrWhiteSpace(security.ReferrerPolicyValue) ? "strict-origin-when-cross-origin" : security.ReferrerPolicyValue);
            }

            if (writeContentSignals)
                AppendApacheHeaderSet(sb, "Content-Signal", BuildContentSignalValue(contentSignals));

            if (writeLinkHeaders)
            {
                foreach (var target in linkTargets.DistinctBy(static t => t.Href, StringComparer.OrdinalIgnoreCase))
                    AppendApacheHeaderAdd(sb, "Link", BuildLinkHeaderValue(target));
            }

            if (writeDiscoveryHeaders)
                AppendApacheDiscoveryHeaders(sb, siteRoot, spec, security, markdownArtifactPaths);

            sb.AppendLine("</IfModule>");
        }

        if (writeMarkdownNegotiation)
        {
            sb.AppendLine("<IfModule mod_mime.c>");
            sb.Append("  AddType text/markdown ").Append(markdownExtension).AppendLine();
            sb.AppendLine("</IfModule>");
            sb.AppendLine("<IfModule mod_rewrite.c>");
            sb.AppendLine("  RewriteEngine On");
            sb.AppendLine("  # Markdown negotiation covers the root and directory-style routes generated by PowerForge.");
            sb.AppendLine("  # Configure canonical trailing-slash redirects before this block for extensionless deep paths.");
            sb.AppendLine("  RewriteCond %{HTTP_ACCEPT} \"(^|,|;)[[:space:]]*text/markdown\" [NC]");
            sb.Append("  RewriteCond %{DOCUMENT_ROOT}/index").Append(markdownExtension).AppendLine(" -f");
            sb.Append("  RewriteRule ^$ /index").Append(markdownExtension).AppendLine(" [L,T=text/markdown]");
            sb.AppendLine("  RewriteCond %{HTTP_ACCEPT} \"(^|,|;)[[:space:]]*text/markdown\" [NC]");
            sb.Append("  RewriteCond %{DOCUMENT_ROOT}/$1/index").Append(markdownExtension).AppendLine(" -f");
            sb.Append("  RewriteRule ^(.+)/$ /$1/index").Append(markdownExtension).AppendLine(" [L,T=text/markdown]");
            sb.AppendLine("</IfModule>");
        }

        sb.AppendLine(AgentBlockEnd);

        var next = string.IsNullOrWhiteSpace(cleaned)
            ? sb.ToString()
            : cleaned + Environment.NewLine + Environment.NewLine + sb;
        CreateParentDirectory(path);
        File.WriteAllText(path, next);
        return path;
    }

    private static void AppendDiscoveryResourceHeaders(StringBuilder sb, string siteRoot, AgentReadinessSpec spec, AgentSecurityHeadersSpec security, IReadOnlyList<string> markdownArtifactPaths)
    {
        if (spec.ApiCatalog?.Enabled == true)
        {
            AppendResourceHeaders(sb, ResolveSiteRoute(siteRoot, spec.ApiCatalog.OutputPath, ".well-known/api-catalog"),
                "application/linkset+json; profile=\"https://www.rfc-editor.org/info/rfc9727\"", security);
        }

        if (spec.AgentSkills?.Enabled == true)
        {
            AppendResourceHeaders(sb, ResolveSiteRoute(siteRoot, spec.AgentSkills.IndexPath, ".well-known/agent-skills/index.json"),
                "application/json", security);
        }

        if (spec.AgentsJson?.Enabled == true)
        {
            var routes = new[]
                {
                    ResolveSiteRoute(siteRoot, spec.AgentsJson.OutputPath, "agents.json"),
                    ResolveSiteRoute(siteRoot, spec.AgentsJson.WellKnownOutputPath, ".well-known/agents.json")
                }
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var route in routes)
                AppendResourceHeaders(sb, route, "application/json", security);
        }

        if (spec.A2AAgentCard?.Enabled == true)
        {
            AppendResourceHeaders(sb, ResolveSiteRoute(siteRoot, spec.A2AAgentCard.OutputPath, ".well-known/agent-card.json"),
                "application/json", security);
        }

        if (spec.McpServerCard?.Enabled == true)
        {
            AppendResourceHeaders(sb, ResolveSiteRoute(siteRoot, spec.McpServerCard.OutputPath, ".well-known/mcp/server-card.json"),
                "application/json", security);
        }

        if (spec.MarkdownArtifacts?.Enabled == true)
        {
            var extension = NormalizeMarkdownExtension(spec.MarkdownArtifacts.Extension);
            foreach (var route in markdownArtifactPaths
                         .Select(path => ToSiteRoute(siteRoot, path))
                         .Where(route => route.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                AppendResourceHeaders(sb, route, "text/markdown; charset=utf-8", security);
            }
        }
    }

    private static void AppendApacheDiscoveryHeaders(StringBuilder sb, string siteRoot, AgentReadinessSpec spec, AgentSecurityHeadersSpec security, IReadOnlyList<string> markdownArtifactPaths)
    {
        if (spec.ApiCatalog?.Enabled == true)
        {
            AppendApacheResourceHeaders(sb, ResolveSiteRoute(siteRoot, spec.ApiCatalog.OutputPath, ".well-known/api-catalog"),
                "application/linkset+json; profile=\"https://www.rfc-editor.org/info/rfc9727\"", security);
        }

        if (spec.AgentSkills?.Enabled == true)
        {
            AppendApacheResourceHeaders(sb, ResolveSiteRoute(siteRoot, spec.AgentSkills.IndexPath, ".well-known/agent-skills/index.json"),
                "application/json", security);
        }

        if (spec.AgentsJson?.Enabled == true)
        {
            var routes = new[]
                {
                    ResolveSiteRoute(siteRoot, spec.AgentsJson.OutputPath, "agents.json"),
                    ResolveSiteRoute(siteRoot, spec.AgentsJson.WellKnownOutputPath, ".well-known/agents.json")
                }
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var route in routes)
                AppendApacheResourceHeaders(sb, route, "application/json", security);
        }

        if (spec.A2AAgentCard?.Enabled == true)
        {
            AppendApacheResourceHeaders(sb, ResolveSiteRoute(siteRoot, spec.A2AAgentCard.OutputPath, ".well-known/agent-card.json"),
                "application/json", security);
        }

        if (spec.McpServerCard?.Enabled == true)
        {
            AppendApacheResourceHeaders(sb, ResolveSiteRoute(siteRoot, spec.McpServerCard.OutputPath, ".well-known/mcp/server-card.json"),
                "application/json", security);
        }

        // Markdown artifacts use AddType/RewriteRule in Apache; per-file Header
        // blocks would make .htaccess enormous on documentation/blog sites.
    }

    private static void AppendApacheResourceHeaders(StringBuilder sb, string route, string contentType, AgentSecurityHeadersSpec security)
    {
        if (string.IsNullOrWhiteSpace(route))
            return;

        sb.Append("  <If \"%{REQUEST_URI} == '").Append(EscapeApacheExpressionString(NormalizeRoute(route))).AppendLine("'\">");
        AppendApacheHeaderSet(sb, "Content-Type", contentType, indent: "    ", always: true);
        if (security.Enabled && security.CorsForWellKnown && !string.IsNullOrWhiteSpace(security.CorsAllowOrigin))
            AppendApacheHeaderSet(sb, "Access-Control-Allow-Origin", security.CorsAllowOrigin, indent: "    ", always: true);
        sb.AppendLine("  </If>");
    }

    private static bool HasApacheDiscoveryResources(AgentReadinessSpec spec)
        => spec.ApiCatalog?.Enabled == true ||
           spec.AgentSkills?.Enabled == true ||
           spec.AgentsJson?.Enabled == true ||
           spec.A2AAgentCard?.Enabled == true ||
           spec.McpServerCard?.Enabled == true;

    private static void AppendResourceHeaders(StringBuilder sb, string route, string contentType, AgentSecurityHeadersSpec security)
    {
        if (string.IsNullOrWhiteSpace(route))
            return;

        sb.AppendLine(NormalizeRoute(route));
        sb.Append("  Content-Type: ").Append(contentType).AppendLine();
        AppendCorsHeaders(sb, security);
    }

    private static void AppendSecurityHeaders(StringBuilder sb, AgentSecurityHeadersSpec security)
    {
        if (!security.Enabled)
            return;

        if (security.Hsts && !string.IsNullOrWhiteSpace(security.HstsValue))
            sb.Append("  Strict-Transport-Security: ").Append(security.HstsValue!.Trim()).AppendLine();
        if (security.ContentSecurityPolicy && !string.IsNullOrWhiteSpace(security.ContentSecurityPolicyValue))
            sb.Append("  Content-Security-Policy: ").Append(security.ContentSecurityPolicyValue!.Trim()).AppendLine();
        if (security.XContentTypeOptions)
            sb.AppendLine("  X-Content-Type-Options: nosniff");
        if (security.XFrameOptions)
            sb.AppendLine("  X-Frame-Options: DENY");
        if (security.ReferrerPolicy)
            sb.Append("  Referrer-Policy: ").Append(string.IsNullOrWhiteSpace(security.ReferrerPolicyValue) ? "strict-origin-when-cross-origin" : security.ReferrerPolicyValue!.Trim()).AppendLine();
    }

    private static void AppendCorsHeaders(StringBuilder sb, AgentSecurityHeadersSpec security)
    {
        if (security.Enabled && security.CorsForWellKnown && !string.IsNullOrWhiteSpace(security.CorsAllowOrigin))
            sb.Append("  Access-Control-Allow-Origin: ").Append(security.CorsAllowOrigin!.Trim()).AppendLine();
    }

    private static void AppendApacheHeaderSet(StringBuilder sb, string name, string? value, string indent = "  ", bool always = false)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        sb.Append(indent)
            .Append(always ? "Header always set " : "Header set ")
            .Append(name)
            .Append(" \"")
            .Append(EscapeApacheQuotedValue(value!))
            .AppendLine("\"");
    }

    private static void AppendApacheHeaderAdd(StringBuilder sb, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        sb.Append("  Header always add ")
            .Append(name)
            .Append(" \"")
            .Append(EscapeApacheQuotedValue(value))
            .AppendLine("\"");
    }

    private static void CreateParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
    }

    private static string BuildLinkHeaderValue(HeaderLinkTarget target)
    {
        var sb = new StringBuilder();
        sb.Append('<').Append(EscapeLinkUriReference(target.Href)).Append(">; rel=\"").Append(EscapeLinkParameterValue(target.Rel)).Append('"');
        if (!string.IsNullOrWhiteSpace(target.Type))
            sb.Append("; type=\"").Append(EscapeLinkParameterValue(target.Type!)).Append('"');
        return sb.ToString();
    }

    private static string EscapeLinkUriReference(string value)
        => StripHeaderControlCharacters(value)
            .Replace("<", "%3C", StringComparison.Ordinal)
            .Replace(">", "%3E", StringComparison.Ordinal)
            .Replace("\"", "%22", StringComparison.Ordinal);

    private static string EscapeLinkParameterValue(string value)
        => StripHeaderControlCharacters(value)
            .Replace("\\", "%5C", StringComparison.Ordinal)
            .Replace("\"", "%22", StringComparison.Ordinal);

    private static string BuildContentSignalValue(AgentContentSignalsSpec signals)
        => $"search={(signals.Search ? "yes" : "no")}, ai-input={(signals.AiInput ? "yes" : "no")}, ai-train={(signals.AiTrain ? "yes" : "no")}";

    private static string StripHeaderControlCharacters(string value)
        => value.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal);

    private static string EscapeApacheQuotedValue(string value)
        => StripHeaderControlCharacters(value)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string EscapeApacheExpressionString(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);

    private static string RemoveGeneratedBlock(string text)
        => string.IsNullOrWhiteSpace(text) ? string.Empty : GeneratedBlockRegex.Replace(text, string.Empty);

    private static bool ValidateSitemap(string path, out string message)
    {
        if (!File.Exists(path))
        {
            message = "sitemap.xml is missing.";
            return false;
        }

        try
        {
            var doc = XDocument.Load(path);
            var hasLoc = doc.Descendants().Any(static element => element.Name.LocalName.Equals("loc", StringComparison.OrdinalIgnoreCase));
            message = hasLoc ? "sitemap.xml exists with URL entries." : "sitemap.xml has no loc entries.";
            return hasLoc;
        }
        catch (Exception ex)
        {
            message = $"sitemap.xml could not be parsed: {ex.Message}";
            return false;
        }
    }

    private static bool ValidateApiCatalog(string path, out string message)
    {
        if (!File.Exists(path))
        {
            message = "API catalog not found.";
            return false;
        }

        var valid = ValidateApiCatalogText(File.ReadAllText(path));
        message = valid ? "API catalog contains a linkset array." : "API catalog must contain a linkset array.";
        return valid;
    }

    private static bool ValidateApiCatalogText(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.TryGetProperty("linkset", out var linkset) &&
                   linkset.ValueKind == JsonValueKind.Array &&
                   linkset.GetArrayLength() > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool ValidateAgentSkillsIndex(string path, string siteRoot, out string message)
    {
        if (!File.Exists(path))
        {
            message = "Agent Skills discovery index not found.";
            return false;
        }

        var text = File.ReadAllText(path);
        if (!ValidateAgentSkillsIndexText(text))
        {
            message = "Agent Skills index must include schema and skills array.";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            var checkedDigest = false;
            foreach (var skill in doc.RootElement.GetProperty("skills").EnumerateArray())
            {
                var url = skill.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;
                var digest = skill.TryGetProperty("digest", out var digestElement) ? digestElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(digest) || !url.StartsWith("/", StringComparison.Ordinal))
                    continue;

                var skillPath = ResolveSitePath(siteRoot, url);
                if (!File.Exists(skillPath))
                    continue;

                checkedDigest = true;
                var actual = "sha256:" + Sha256Hex(File.ReadAllText(skillPath));
                if (!string.Equals(actual, digest, StringComparison.OrdinalIgnoreCase))
                {
                    message = $"Agent skill digest mismatch for {url}.";
                    return false;
                }
            }

            message = checkedDigest
                ? "Agent Skills index is valid and local digests match."
                : "Agent Skills index is valid.";
            return true;
        }
        catch
        {
            message = "Agent Skills index could not be parsed.";
            return false;
        }
    }

    private static bool ValidateAgentSkillsIndexText(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.TryGetProperty("$schema", out var schema) &&
                   string.Equals(schema.GetString(), AgentSkillsSchema, StringComparison.OrdinalIgnoreCase) &&
                   doc.RootElement.TryGetProperty("skills", out var skills) &&
                   skills.ValueKind == JsonValueKind.Array &&
                   skills.GetArrayLength() > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool ValidateAgentsJson(string path, out string message)
    {
        if (!File.Exists(path))
        {
            message = "agents.json not found.";
            return false;
        }

        var valid = ValidateAgentsJsonText(File.ReadAllText(path));
        message = valid ? "agents.json includes name and resources." : "agents.json must include name and resources.";
        return valid;
    }

    private static bool ValidateAgentsJsonText(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return JsonLdObjectHasNonEmptyValue(doc.RootElement, "name") &&
                   doc.RootElement.TryGetProperty("resources", out var resources) &&
                   resources.ValueKind == JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }

    private static bool ValidateA2AAgentCard(string path, out string message)
    {
        if (!File.Exists(path))
        {
            message = "A2A Agent Card not found.";
            return false;
        }

        var valid = ValidateA2AAgentCardText(File.ReadAllText(path));
        message = valid ? "A2A Agent Card includes name, description, url, version, and skills." : "A2A Agent Card is missing required fields.";
        return valid;
    }

    private static bool ValidateA2AAgentCardText(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return JsonLdObjectHasNonEmptyValue(doc.RootElement, "name") &&
                   JsonLdObjectHasNonEmptyValue(doc.RootElement, "description") &&
                   JsonLdObjectHasNonEmptyValue(doc.RootElement, "url") &&
                   JsonLdObjectHasNonEmptyValue(doc.RootElement, "version") &&
                   doc.RootElement.TryGetProperty("skills", out var skills) &&
                   skills.ValueKind == JsonValueKind.Array;
        }
        catch
        {
            return false;
        }
    }

    private static bool ValidateMcpServerCard(string path, out string message)
    {
        if (!File.Exists(path))
        {
            message = "MCP Server Card not found.";
            return false;
        }

        var valid = ValidateMcpServerCardText(File.ReadAllText(path));
        message = valid ? "MCP Server Card includes serverInfo and transport endpoint." : "MCP Server Card must include serverInfo and transport.endpoint.";
        return valid;
    }

    private static bool ValidateMcpServerCardText(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.TryGetProperty("serverInfo", out var serverInfo) &&
                   serverInfo.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("transport", out var transport) &&
                   transport.ValueKind == JsonValueKind.Object &&
                   transport.TryGetProperty("endpoint", out var endpoint) &&
                   !string.IsNullOrWhiteSpace(endpoint.GetString());
        }
        catch
        {
            return false;
        }
    }

    private static bool HasRobotsUserAgent(string text)
        => text.Contains("User-agent:", StringComparison.OrdinalIgnoreCase) ||
           text.Contains("User-Agent:", StringComparison.OrdinalIgnoreCase);

    private static bool HasContentSignals(string text)
        => text.Contains("Content-Signal:", StringComparison.OrdinalIgnoreCase) ||
           text.Contains("Content-signal:", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeSitemap(string text)
        => text.Contains("<urlset", StringComparison.OrdinalIgnoreCase) ||
           text.Contains("<sitemapindex", StringComparison.OrdinalIgnoreCase);

    private static void AddSecurityHeaderChecks(
        List<WebAgentReadinessCheck> checks,
        string headersText,
        string headersPath,
        string apacheText,
        string apachePath,
        string fallbackTarget,
        AgentSecurityHeadersSpec? spec)
    {
        var expected = spec?.Enabled == true;
        AddConfiguredHeaderCheck(checks, "security-hsts", "HSTS", headersText, headersPath, apacheText, apachePath, fallbackTarget, expected && spec!.Hsts, "Strict-Transport-Security",
            "Static host headers include HSTS.", "Static host headers do not include Strict-Transport-Security.");
        AddConfiguredHeaderCheck(checks, "security-csp", "CSP", headersText, headersPath, apacheText, apachePath, fallbackTarget, expected && spec!.ContentSecurityPolicy, "Content-Security-Policy",
            "Static host headers include Content-Security-Policy.", "Static host headers do not include Content-Security-Policy.");
        AddConfiguredHeaderCheck(checks, "security-xcto", "X-Content-Type-Options", headersText, headersPath, apacheText, apachePath, fallbackTarget, expected && spec!.XContentTypeOptions, "X-Content-Type-Options",
            "Static host headers include X-Content-Type-Options.", "Static host headers do not include X-Content-Type-Options.");

        var hasFrameProtection = HeaderDirectiveExists(headersText, "X-Frame-Options") ||
                                 HeaderDirectiveExists(apacheText, "X-Frame-Options") ||
                                 headersText.Contains("frame-ancestors", StringComparison.OrdinalIgnoreCase) ||
                                 apacheText.Contains("frame-ancestors", StringComparison.OrdinalIgnoreCase);
        var frameTarget = ResolveHeaderSourcePath("X-Frame-Options", headersText, headersPath, apacheText, apachePath, fallbackTarget);
        if (frameTarget == fallbackTarget && headersText.Contains("frame-ancestors", StringComparison.OrdinalIgnoreCase))
            frameTarget = headersPath;
        else if (frameTarget == fallbackTarget && apacheText.Contains("frame-ancestors", StringComparison.OrdinalIgnoreCase))
            frameTarget = apachePath;
        AddCheck(checks, "security-xfo", "security-trust", "X-Frame-Options",
            hasFrameProtection ? "pass" : (expected && spec!.XFrameOptions ? "fail" : "info"),
            hasFrameProtection
                ? "Static host headers include clickjacking protection."
                : (expected && spec!.XFrameOptions ? "Static host headers do not include X-Frame-Options or CSP frame-ancestors." : "Clickjacking protection header generation is disabled."),
            hasFrameProtection ? frameTarget : fallbackTarget);

        AddConfiguredHeaderCheck(checks, "security-referrer-policy", "Referrer-Policy", headersText, headersPath, apacheText, apachePath, fallbackTarget, expected && spec!.ReferrerPolicy, "Referrer-Policy",
            "Static host headers include Referrer-Policy.", "Static host headers do not include Referrer-Policy.");
        AddConfiguredHeaderCheck(checks, "security-cors", "CORS", headersText, headersPath, apacheText, apachePath, fallbackTarget, expected && spec!.CorsForWellKnown, "Access-Control-Allow-Origin",
            "Agent discovery resources include CORS headers.", "No Access-Control-Allow-Origin header configured for agent discovery resources.");
    }

    private static void AddConfiguredHeaderCheck(
        List<WebAgentReadinessCheck> checks,
        string id,
        string name,
        string headersText,
        string headersPath,
        string apacheText,
        string apachePath,
        string fallbackTarget,
        bool expected,
        string headerName,
        string passMessage,
        string failMessage)
    {
        var target = ResolveHeaderSourcePath(headerName, headersText, headersPath, apacheText, apachePath, fallbackTarget);
        var present = target != fallbackTarget || HeaderDirectiveExists(headersText + Environment.NewLine + apacheText, headerName);
        AddCheck(checks, id, "security-trust", name,
            present ? "pass" : (expected ? "fail" : "info"),
            present ? passMessage : (expected ? failMessage : $"{name} header generation is disabled."),
            present ? target : fallbackTarget);
    }

    private static string ResolveHeaderSourcePath(string headerName, string headersText, string headersPath, string apacheText, string apachePath, string fallbackTarget)
    {
        if (HeaderDirectiveExists(headersText, headerName))
            return headersPath;
        if (HeaderDirectiveExists(apacheText, headerName))
            return apachePath;
        return fallbackTarget;
    }

    private static bool HeaderDirectiveExists(string headersText, string headerName)
    {
        if (string.IsNullOrWhiteSpace(headersText) || string.IsNullOrWhiteSpace(headerName))
            return false;

        foreach (var rawLine in headersText.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
        {
            var line = rawLine.TrimStart();
            if (line.Length == 0 || line[0] == '#')
                continue;

            if (line.StartsWith(headerName, StringComparison.OrdinalIgnoreCase) &&
                line.Length > headerName.Length &&
                line[headerName.Length] == ':')
            {
                return true;
            }

            if (!line.StartsWith("Header", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var headerIndex = parts.Length > 2 && parts[1].Equals("always", StringComparison.OrdinalIgnoreCase)
                ? 3
                : 2;
            if (parts.Length > headerIndex &&
                IsApacheHeaderAction(parts[headerIndex - 1]) &&
                parts[headerIndex].Equals(headerName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsApacheHeaderAction(string value)
        => value.Equals("set", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("add", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("append", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("merge", StringComparison.OrdinalIgnoreCase);

    private static void AddRemoteSecurityHeaderChecks(List<WebAgentReadinessCheck> checks, HttpResponseMessage? response, string target)
    {
        AddCheck(checks, "security-hsts", "security-trust", "HSTS",
            HeaderExists(response, "Strict-Transport-Security") ? "pass" : "fail",
            HeaderExists(response, "Strict-Transport-Security") ? "Homepage response includes HSTS." : "Homepage response does not include HSTS.",
            target);
        AddCheck(checks, "security-csp", "security-trust", "CSP",
            HeaderExists(response, "Content-Security-Policy") ? "pass" : "fail",
            HeaderExists(response, "Content-Security-Policy") ? "Homepage response includes CSP." : "Homepage response does not include CSP.",
            target);
        AddCheck(checks, "security-xcto", "security-trust", "X-Content-Type-Options",
            HeaderExists(response, "X-Content-Type-Options") ? "pass" : "fail",
            HeaderExists(response, "X-Content-Type-Options") ? "Homepage response includes X-Content-Type-Options." : "Homepage response does not include X-Content-Type-Options.",
            target);
        AddCheck(checks, "security-xfo", "security-trust", "X-Frame-Options",
            HeaderExists(response, "X-Frame-Options") || HeaderContains(response, "Content-Security-Policy", "frame-ancestors") ? "pass" : "fail",
            HeaderExists(response, "X-Frame-Options") || HeaderContains(response, "Content-Security-Policy", "frame-ancestors") ? "Homepage response includes clickjacking protection." : "Homepage response does not include X-Frame-Options or CSP frame-ancestors.",
            target);
        AddCheck(checks, "security-referrer-policy", "security-trust", "Referrer-Policy",
            HeaderExists(response, "Referrer-Policy") ? "pass" : "fail",
            HeaderExists(response, "Referrer-Policy") ? "Homepage response includes Referrer-Policy." : "Homepage response does not include Referrer-Policy.",
            target);
        AddCheck(checks, "security-cors", "security-trust", "CORS",
            HeaderExists(response, "Access-Control-Allow-Origin") ? "pass" : "warn",
            HeaderExists(response, "Access-Control-Allow-Origin") ? "Homepage response includes CORS." : "Homepage response does not include CORS; this is usually only required on API or discovery resources.",
            target);
    }

    private static void AddHtmlSemanticsChecks(List<WebAgentReadinessCheck> checks, string html, string? target)
    {
        var hasHtml = !string.IsNullOrWhiteSpace(html);
        AddCheck(checks, "ssr-detection", "content-semantics", "SSR Detection",
            hasHtml && Regex.IsMatch(html, @"<body\b[^>]*>[\s\S]*\w", RegexOptions.IgnoreCase) ? "pass" : "fail",
            hasHtml ? "Rendered HTML contains body content." : "No rendered HTML content was available.",
            target);
        AddCheck(checks, "language", "content-semantics", "Language",
            hasHtml && Regex.IsMatch(html, @"<html\b[^>]*\blang\s*=", RegexOptions.IgnoreCase) ? "pass" : "fail",
            "HTML document should declare a lang attribute.",
            target);
        AddCheck(checks, "meta-robots", "ai-content-discovery", "Meta Robots",
            hasHtml && Regex.IsMatch(html, @"<meta\b[^>]*name\s*=\s*([""']?)robots\1(?:\s|/|>)", RegexOptions.IgnoreCase) ? "pass" : "warn",
            hasHtml && Regex.IsMatch(html, @"<meta\b[^>]*name\s*=\s*([""']?)robots\1(?:\s|/|>)", RegexOptions.IgnoreCase)
                ? "HTML includes a meta robots directive."
                : "No meta robots directive found on the homepage.",
            target);
        AddCheck(checks, "semantic-html", "content-semantics", "Semantic HTML",
            hasHtml && Regex.IsMatch(html, @"<(main|article|section|header|footer|nav)\b", RegexOptions.IgnoreCase) ? "pass" : "fail",
            "HTML should expose semantic landmarks for agents and accessibility trees.",
            target);
        AddCheck(checks, "aria-landmarks", "content-semantics", "ARIA Landmarks",
            hasHtml && (Regex.IsMatch(html, @"<(main|nav|header|footer)\b", RegexOptions.IgnoreCase) ||
                        Regex.IsMatch(html, @"\brole\s*=\s*[""'](main|navigation|banner|contentinfo)[""']", RegexOptions.IgnoreCase)) ? "pass" : "fail",
            "HTML should expose navigation/main/footer landmarks.",
            target);
        AddCheck(checks, "heading-hierarchy", "content-semantics", "Heading Hierarchy",
            HasReasonableHeadingHierarchy(html) ? "pass" : "warn",
            HasReasonableHeadingHierarchy(html) ? "Headings include an h1 and do not skip the first level." : "Could not confirm a clean heading hierarchy from homepage HTML.",
            target);
        AddCheck(checks, "link-text", "content-semantics", "Link Text",
            hasHtml && !Regex.IsMatch(html, @"<a\b(?:(?!</a>).)*>\s*(click here|read more|more)\s*</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline) ? "pass" : "warn",
            "Avoid generic link text so agents can infer navigation intent.",
            target);
        AddCheck(checks, "alt-text", "content-semantics", "Alt Text",
            hasHtml && !Regex.IsMatch(html, @"<img\b(?![^>]*\balt\s*=)", RegexOptions.IgnoreCase) ? "pass" : "warn",
            "Images should include alt text or an explicit empty alt.",
            target);
        AddCheck(checks, "question-headings", "content-semantics", "Question Headings",
            hasHtml && Regex.IsMatch(html, @"<h[1-6]\b[^>]*>[^<]*\?", RegexOptions.IgnoreCase) ? "pass" : "info",
            "Question-style headings are useful for AI answer extraction when the page has FAQ content.",
            target);
        AddStructuredDataChecks(checks, html, target);
    }

    private static void AddStructuredDataChecks(List<WebAgentReadinessCheck> checks, string html, string? target)
    {
        var jsonLd = ExtractJsonLd(html).ToArray();
        AddCheck(checks, "json-ld", "ai-search-signals", "JSON-LD",
            jsonLd.Length > 0 ? "pass" : "fail",
            jsonLd.Length > 0 ? "HTML includes JSON-LD." : "No JSON-LD structured data found.",
            target);
        AddCheck(checks, "schema-types", "ai-search-signals", "Schema.org Types",
            jsonLd.Any(static item => item.Contains("\"@type\"", StringComparison.OrdinalIgnoreCase)) ? "pass" : "fail",
            "JSON-LD should include Schema.org @type values.",
            target);
        AddCheck(checks, "entity-linking", "ai-search-signals", "Entity Linking",
            jsonLd.Any(static item => item.Contains("\"sameAs\"", StringComparison.OrdinalIgnoreCase) ||
                                      item.Contains("\"@id\"", StringComparison.OrdinalIgnoreCase)) ||
            html.Contains("sameAs", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("\"@id\"", StringComparison.OrdinalIgnoreCase) ? "pass" : "warn",
            "Organization/content entities should include sameAs or @id links where possible.",
            target);
        AddCheck(checks, "breadcrumb-list", "ai-search-signals", "BreadcrumbList",
            jsonLd.Any(static item => item.Contains("BreadcrumbList", StringComparison.OrdinalIgnoreCase)) ||
            html.Contains("BreadcrumbList", StringComparison.OrdinalIgnoreCase) ? "pass" : "warn",
            "BreadcrumbList structured data helps agents resolve page context.",
            target);
        AddCheck(checks, "organization-schema", "ai-search-signals", "Organization Schema",
            jsonLd.Any(static item => item.Contains("Organization", StringComparison.OrdinalIgnoreCase)) ? "pass" : "warn",
            "Organization schema helps identify the publisher.",
            target);
        AddCheck(checks, "faqpage-schema", "ai-search-signals", "FAQPage Schema",
            jsonLd.Any(static item => item.Contains("FAQPage", StringComparison.OrdinalIgnoreCase)) ? "pass" : "info",
            "FAQPage schema is useful when a page contains FAQ content.",
            target);
        AddCheck(checks, "author-attribution", "ai-search-signals", "Author Attribution",
            jsonLd.Any(static item => item.Contains("\"author\"", StringComparison.OrdinalIgnoreCase) ||
                                      item.Contains("\"publisher\"", StringComparison.OrdinalIgnoreCase) ||
                                      item.Contains("Organization", StringComparison.OrdinalIgnoreCase)) ? "pass" : "warn",
            "Content should expose author or publisher attribution.",
            target);
        AddCheck(checks, "content-freshness", "ai-content-discovery", "Content Freshness",
            jsonLd.Any(static item => item.Contains("dateModified", StringComparison.OrdinalIgnoreCase) ||
                                      item.Contains("datePublished", StringComparison.OrdinalIgnoreCase)) ||
            Regex.IsMatch(html, @"<time\b[^>]*datetime\s*=", RegexOptions.IgnoreCase) ? "pass" : "warn",
            "dateModified, datePublished, or time[datetime] helps agents judge freshness.",
            target);
    }

    private static HtmlReadResult ReadFirstHtml(string siteRoot)
    {
        var preferred = new[] { Path.Combine(siteRoot, "index.html"), Path.Combine(siteRoot, "index.htm") };
        var path = preferred.FirstOrDefault(File.Exists) ??
                   (Directory.Exists(siteRoot)
                       ? Directory.EnumerateFiles(siteRoot, "*.html", SearchOption.AllDirectories).FirstOrDefault()
                       : null);
        return string.IsNullOrWhiteSpace(path)
            ? new HtmlReadResult(string.Empty, siteRoot)
            : new HtmlReadResult(File.ReadAllText(path), path);
    }

    private static void AddMarkdownArtifactChecks(List<WebAgentReadinessCheck> checks, string siteRoot, AgentMarkdownArtifactsSpec? spec)
    {
        var expected = spec?.Enabled == true;
        var extension = NormalizeMarkdownExtension(spec?.Extension);
        var rootMarkdown = Path.Combine(siteRoot, "index" + extension);
        var markdownFiles = Directory.Exists(siteRoot)
            ? Directory.EnumerateFiles(siteRoot, "*" + extension, SearchOption.AllDirectories)
                .Where(path => !ShouldSkipMarkdownArtifactCandidate(siteRoot, path))
                .Take(2)
                .ToArray()
            : Array.Empty<string>();
        var rootMarkdownExists = File.Exists(rootMarkdown);

        AddCheck(checks, "markdown-artifacts", "content", "Markdown artifacts",
            markdownFiles.Length > 0 ? "pass" : (expected ? "fail" : "info"),
            markdownFiles.Length > 0
                ? "Static markdown artifacts exist for agent-readable content."
                : (expected ? "Markdown artifact generation is enabled, but no markdown artifacts were found." : "Markdown artifact generation is disabled."),
            siteRoot);

        AddCheck(checks, "markdown-root-artifact", "content", "Root markdown artifact",
            rootMarkdownExists ? "pass" : (expected ? "fail" : "info"),
            rootMarkdownExists
                ? "Root page has a markdown artifact for host-level Accept negotiation."
                : (expected ? "Root markdown artifact is missing." : "Root markdown artifact generation is disabled."),
            rootMarkdown);
    }

    private static IEnumerable<string> ExtractJsonLd(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            yield break;

        foreach (Match match in Regex.Matches(html, @"<script\b[^>]*type\s*=\s*([""']?)application/ld\+json\1(?:\s[^>]*)?>(?<json>[\s\S]*?)</script>", RegexOptions.IgnoreCase))
            yield return match.Groups["json"].Value;
    }

    private static bool HasReasonableHeadingHierarchy(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return false;

        var levels = Regex.Matches(html, @"<h(?<level>[1-6])\b", RegexOptions.IgnoreCase)
            .Select(match => int.TryParse(match.Groups["level"].Value, out var level) ? level : 0)
            .Where(static level => level > 0)
            .ToArray();
        return levels.Length > 0 && levels[0] == 1 && levels.Contains(1);
    }

    private static bool JsonLdObjectHasNonEmptyValue(JsonElement obj, string propertyName)
        => obj.ValueKind == JsonValueKind.Object &&
           obj.TryGetProperty(propertyName, out var value) &&
           value.ValueKind != JsonValueKind.Null &&
           value.ValueKind != JsonValueKind.Undefined &&
           !string.IsNullOrWhiteSpace(value.ToString());

    private static bool HeaderExists(HttpResponseMessage? response, string name)
    {
        if (response is null)
            return false;

        return response.Headers.TryGetValues(name, out _) ||
               response.Content.Headers.TryGetValues(name, out _);
    }

    private static bool HeaderContains(HttpResponseMessage? response, string name, string value)
    {
        if (response is null)
            return false;

        return (response.Headers.TryGetValues(name, out var values) ||
                response.Content.Headers.TryGetValues(name, out values)) &&
               values.Any(header => header.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveOpenApiRoute(string siteRoot, AgentOpenApiSpec? spec)
    {
        if (!string.IsNullOrWhiteSpace(spec?.Path))
        {
            var configured = spec.Path!.Trim();
            if (Uri.TryCreate(configured, UriKind.Absolute, out _))
                return configured;
            return File.Exists(ResolveSitePath(siteRoot, configured)) ? NormalizeRoute(configured) : null;
        }

        foreach (var candidate in new[] { "/openapi.json", "/api/openapi.json", "/swagger.json", "/api/swagger.json", "/.well-known/openapi.json" })
        {
            if (File.Exists(ResolveSitePath(siteRoot, candidate)))
                return candidate;
        }

        return null;
    }

    private static async Task<OpenApiScanResult> TryFindOpenApiAsync(HttpClient http, string baseUrl, CancellationToken cancellationToken)
    {
        foreach (var candidate in new[] { "/openapi.json", "/api/openapi.json", "/swagger.json", "/api/swagger.json", "/.well-known/openapi.json" })
        {
            var url = CombineUrl(baseUrl, candidate);
            var result = await TryGetTextAsync(http, url, null, cancellationToken).ConfigureAwait(false);
            if (result.Success && LooksLikeOpenApi(result.Text))
                return new OpenApiScanResult(true, url);
        }

        return new OpenApiScanResult(false, null);
    }

    private static bool LooksLikeOpenApi(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.TryGetProperty("openapi", out _) ||
                   doc.RootElement.TryGetProperty("swagger", out _);
        }
        catch
        {
            return false;
        }
    }

    private static void AddCheck(List<WebAgentReadinessCheck> checks, string id, string category, string name, string status, string message, string? target)
        => checks.Add(new WebAgentReadinessCheck
        {
            Id = id,
            Category = category,
            Name = name,
            Status = status,
            Message = message,
            Target = target
        });

    private static void AddLinkArray(JsonObject item, string rel, string? baseUrl, string? href, string type, string? title)
    {
        if (string.IsNullOrWhiteSpace(href))
            return;

        var link = new JsonObject
        {
            ["href"] = ToAbsoluteUrl(baseUrl, href),
            ["type"] = type
        };
        if (!string.IsNullOrWhiteSpace(title))
            link["title"] = title.Trim();
        item[rel] = new JsonArray(link);
    }

    private static string? ResolveSkillContent(AgentSkillSpec skill)
    {
        if (!string.IsNullOrWhiteSpace(skill.SourcePath))
        {
            var sourcePath = Path.GetFullPath(skill.SourcePath.Trim().Trim('"'));
            if (File.Exists(sourcePath))
                return File.ReadAllText(sourcePath);
        }

        return skill.Content;
    }

    private static string BuildDefaultSkill(string siteName, string? baseUrl)
    {
        var url = string.IsNullOrWhiteSpace(baseUrl) ? "the site" : baseUrl!.TrimEnd('/');
        return $"""
        ---
        name: site-assistant
        description: Use {siteName} documentation, API catalog, sitemap, and llms resources.
        ---

        # {siteName} Site Assistant

        Use this skill when answering questions about {siteName}.

        - Prefer canonical pages from {url}.
        - Check `/sitemap.xml` for public routes.
        - Check `/llms.txt`, `/llms.json`, and `/llms-full.txt` when present for agent-readable summaries.
        - Check `/.well-known/api-catalog` before assuming API documentation paths.
        - Respect the site's `robots.txt` and Content-Signal preferences.
        """;
    }

    private static async Task<HttpTextResult> TryGetTextAsync(HttpClient http, string url, string? accept, CancellationToken cancellationToken)
    {
        var result = await TrySendAsync(http, HttpMethod.Get, url, accept, cancellationToken).ConfigureAwait(false);
        if (result.Response is null)
            return new HttpTextResult(false, result.Message, string.Empty, null);

        var text = string.Empty;
        try
        {
            text = await result.Response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new HttpTextResult(false, ex.Message, string.Empty, result.Response);
        }

        var success = (int)result.Response.StatusCode >= 200 && (int)result.Response.StatusCode < 300;
        return new HttpTextResult(success, success ? $"HTTP {(int)result.Response.StatusCode}" : $"HTTP {(int)result.Response.StatusCode}", text, result.Response);
    }

    private static async Task<HttpResponseResult> TrySendAsync(HttpClient http, HttpMethod method, string url, string? accept, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, url);
            if (!string.IsNullOrWhiteSpace(accept))
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            var success = (int)response.StatusCode >= 200 && (int)response.StatusCode < 400;
            return new HttpResponseResult(success, $"HTTP {(int)response.StatusCode}", response);
        }
        catch (Exception ex)
        {
            return new HttpResponseResult(false, ex.Message, null);
        }
    }

    private static string NormalizeBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Trim().TrimEnd('/');
    }

    private static string CombineUrl(string baseUrl, string route)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return NormalizeRoute(route);
        return baseUrl.TrimEnd('/') + "/" + NormalizeRoute(route).TrimStart('/');
    }

    private static string ToAbsoluteUrl(string? baseUrl, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        if (Uri.TryCreate(value, UriKind.Absolute, out _))
            return value.Trim();
        return string.IsNullOrWhiteSpace(baseUrl) ? NormalizeRoute(value) : CombineUrl(baseUrl!, value);
    }

    private static string NormalizeRoute(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "/";
        var trimmed = value.Trim().Replace('\\', '/');
        return trimmed.StartsWith("/", StringComparison.Ordinal) ? trimmed : "/" + trimmed;
    }

    private static string ResolveSitePath(string siteRoot, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));

        var root = Path.GetFullPath(siteRoot.Trim().Trim('"'));
        var normalized = path.Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        var resolved = Path.GetFullPath(Path.Combine(root, normalized));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!resolved.Equals(root, FileSystemPathComparison) &&
            !resolved.StartsWith(rootWithSeparator, FileSystemPathComparison))
        {
            throw new ArgumentException($"Path '{path}' resolves outside the site root.", nameof(path));
        }

        return resolved;
    }

    private static string ResolveSiteRoute(string siteRoot, string? path, string defaultPath)
        => ToSiteRoute(siteRoot, ResolveSitePath(siteRoot, string.IsNullOrWhiteSpace(path) ? defaultPath : path!));

    private static string ToSiteRoute(string siteRoot, string path)
    {
        var root = Path.GetFullPath(siteRoot.Trim().Trim('"'));
        var resolved = Path.GetFullPath(path);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!resolved.Equals(root, FileSystemPathComparison) &&
            !resolved.StartsWith(rootWithSeparator, FileSystemPathComparison))
        {
            throw new ArgumentException($"Path '{path}' resolves outside the site root.", nameof(path));
        }

        var relative = Path.GetRelativePath(root, resolved).Replace(Path.DirectorySeparatorChar, '/');
        return NormalizeRoute(relative);
    }

    private static string Slugify(string value)
    {
        var sb = new StringBuilder();
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
            else if (ch == '-' || ch == '_' || char.IsWhiteSpace(ch))
                sb.Append('-');
        }

        var slug = Regex.Replace(sb.ToString(), "-{2,}", "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "site-assistant" : slug;
    }

    private static string Sha256Hex(string content)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed record HeaderLinkTarget(string Href, string Rel, string Type);
    private sealed record MarkdownArtifactWriteResult(string[] Paths, string? RootRoute);
    private sealed record HtmlReadResult(string Text, string Path);
    private sealed record OpenApiScanResult(bool Success, string? Url);
    private sealed record HttpTextResult(bool Success, string Message, string Text, HttpResponseMessage? Response);
    private sealed record HttpResponseResult(bool Success, string Message, HttpResponseMessage? Response);
}

/// <summary>Options for preparing local agent-readiness artifacts.</summary>
public sealed class WebAgentReadinessPrepareOptions
{
    /// <summary>Static site output directory.</summary>
    public string SiteRoot { get; set; } = string.Empty;
    /// <summary>Public base URL.</summary>
    public string? BaseUrl { get; set; }
    /// <summary>Site name.</summary>
    public string? SiteName { get; set; }
    /// <summary>Agent-readiness settings.</summary>
    public AgentReadinessSpec? AgentReadiness { get; set; }
}

/// <summary>Options for verifying local agent-readiness artifacts.</summary>
public sealed class WebAgentReadinessVerifyOptions
{
    /// <summary>Static site output directory.</summary>
    public string SiteRoot { get; set; } = string.Empty;
    /// <summary>Public base URL.</summary>
    public string? BaseUrl { get; set; }
    /// <summary>Agent-readiness settings.</summary>
    public AgentReadinessSpec? AgentReadiness { get; set; }
}

/// <summary>Options for scanning a live site.</summary>
public sealed class WebAgentReadinessScanOptions
{
    /// <summary>Public base URL to scan.</summary>
    public string BaseUrl { get; set; } = string.Empty;
    /// <summary>HTTP timeout in milliseconds.</summary>
    public int TimeoutMs { get; set; } = 15000;
}
