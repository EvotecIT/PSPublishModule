namespace PowerForge.Web;

/// <summary>Agent-readiness discovery and policy output settings.</summary>
public sealed class AgentReadinessSpec
{
    /// <summary>Enable agent-readiness output generation and checks.</summary>
    public bool Enabled { get; set; }
    /// <summary>Optional output path for static host headers (default: _headers).</summary>
    public string? HeadersPath { get; set; }
    /// <summary>When true, write Link response header hints for agent discovery resources.</summary>
    public bool LinkHeaders { get; set; } = true;
    /// <summary>Security and trust response headers expected by agent-readiness scanners.</summary>
    public AgentSecurityHeadersSpec? SecurityHeaders { get; set; }
    /// <summary>When true, update robots.txt with sitemap and Content-Signal directives.</summary>
    public bool Robots { get; set; } = true;
    /// <summary>Content Signals preferences written to robots.txt.</summary>
    public AgentContentSignalsSpec? ContentSignals { get; set; }
    /// <summary>Optional AI crawler user-agent rules written to robots.txt.</summary>
    public AgentBotRuleSpec[] BotRules { get; set; } = Array.Empty<AgentBotRuleSpec>();
    /// <summary>API catalog output configuration.</summary>
    public AgentApiCatalogSpec? ApiCatalog { get; set; }
    /// <summary>Agent Skills discovery output configuration.</summary>
    public AgentSkillsDiscoverySpec? AgentSkills { get; set; }
    /// <summary>Generic agent discovery metadata output configuration.</summary>
    public AgentDiscoveryDocumentSpec? AgentsJson { get; set; }
    /// <summary>A2A Agent Card output configuration.</summary>
    public AgentA2ACardSpec? A2AAgentCard { get; set; }
    /// <summary>MCP server card output configuration.</summary>
    public AgentMcpServerCardSpec? McpServerCard { get; set; }
    /// <summary>OpenAPI discovery settings used for checks and API catalog links.</summary>
    public AgentOpenApiSpec? OpenApi { get; set; }
    /// <summary>When true, verify that rendered HTML exposes WebMCP browser tools.</summary>
    public bool WebMcp { get; set; }
    /// <summary>Optional static markdown artifacts generated from rendered HTML.</summary>
    public AgentMarkdownArtifactsSpec? MarkdownArtifacts { get; set; }
    /// <summary>When true, treat markdown negotiation as expected during remote scans.</summary>
    public bool MarkdownNegotiation { get; set; } = true;
    /// <summary>Optional Apache .htaccess support for Link headers and Markdown negotiation.</summary>
    public AgentApacheSupportSpec? Apache { get; set; }
}

/// <summary>Static markdown artifacts generated from rendered HTML for agent readers and edge negotiation.</summary>
public sealed class AgentMarkdownArtifactsSpec
{
    /// <summary>Enable markdown artifact generation during agent-ready prepare.</summary>
    public bool Enabled { get; set; }
    /// <summary>Markdown file extension. Defaults to .md.</summary>
    public string? Extension { get; set; } = ".md";
    /// <summary>Maximum number of HTML pages to convert. Zero means no explicit limit.</summary>
    public int MaxPages { get; set; }
    /// <summary>When true, prepend the page title as a top-level heading when one can be resolved.</summary>
    public bool IncludeTitle { get; set; } = true;
}

/// <summary>Apache/mod_headers and mod_rewrite integration for static agent-readiness output.</summary>
public sealed class AgentApacheSupportSpec
{
    /// <summary>Enable .htaccess generation for Apache-hosted static sites.</summary>
    public bool Enabled { get; set; }
    /// <summary>Output path relative to site root. Defaults to .htaccess.</summary>
    public string? OutputPath { get; set; } = ".htaccess";
    /// <summary>Output path with the default fallback applied.</summary>
    public string EffectiveOutputPath => string.IsNullOrWhiteSpace(OutputPath) ? ".htaccess" : OutputPath!;
    /// <summary>Emit homepage Link response headers through mod_headers.</summary>
    public bool LinkHeaders { get; set; } = true;
    /// <summary>Emit Content-Signal response headers when Content Signals are configured.</summary>
    public bool ContentSignalsHeader { get; set; } = true;
    /// <summary>Emit AddType and mod_rewrite rules for Accept: text/markdown requests.</summary>
    public bool MarkdownNegotiation { get; set; } = true;
    /// <summary>Emit Content-Type and CORS headers for generated discovery resources.</summary>
    public bool DiscoveryResourceHeaders { get; set; } = true;
}

/// <summary>Security headers that agent and AI-readiness scanners commonly expect.</summary>
public sealed class AgentSecurityHeadersSpec
{
    /// <summary>Enable generated security headers in the static host headers file.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Emit Strict-Transport-Security. Only meaningful when the deployed site is HTTPS.</summary>
    public bool Hsts { get; set; } = true;
    /// <summary>Strict-Transport-Security value.</summary>
    public string? HstsValue { get; set; } = "max-age=31536000; includeSubDomains; preload";
    /// <summary>Emit Content-Security-Policy.</summary>
    public bool ContentSecurityPolicy { get; set; } = true;
    /// <summary>Content-Security-Policy value. Override for sites that load external assets.</summary>
    public string? ContentSecurityPolicyValue { get; set; } =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https:; font-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    /// <summary>Emit X-Content-Type-Options.</summary>
    public bool XContentTypeOptions { get; set; } = true;
    /// <summary>Emit X-Frame-Options.</summary>
    public bool XFrameOptions { get; set; } = true;
    /// <summary>Emit Referrer-Policy.</summary>
    public bool ReferrerPolicy { get; set; } = true;
    /// <summary>Referrer-Policy value.</summary>
    public string? ReferrerPolicyValue { get; set; } = "strict-origin-when-cross-origin";
    /// <summary>Emit permissive CORS only for agent well-known JSON resources.</summary>
    public bool CorsForWellKnown { get; set; } = true;
    /// <summary>Access-Control-Allow-Origin value for agent discovery resources.</summary>
    public string? CorsAllowOrigin { get; set; } = "*";
}

/// <summary>Content Signals preferences for automated content usage.</summary>
public sealed class AgentContentSignalsSpec
{
    /// <summary>Enable Content-Signal directives.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Allow search indexing and excerpts.</summary>
    public bool Search { get; set; } = true;
    /// <summary>Allow query-time AI input or grounding.</summary>
    public bool AiInput { get; set; } = true;
    /// <summary>Allow AI model training or fine-tuning.</summary>
    public bool AiTrain { get; set; }
}

/// <summary>Optional robots.txt rule for a specific crawler.</summary>
public sealed class AgentBotRuleSpec
{
    /// <summary>User-agent token, for example GPTBot.</summary>
    public string UserAgent { get; set; } = string.Empty;
    /// <summary>Allow or disallow path. Defaults to Allow: /.</summary>
    public string? Allow { get; set; }
    /// <summary>Disallowed path. When set, Disallow is emitted instead of Allow.</summary>
    public string? Disallow { get; set; }
}

/// <summary>API catalog output settings.</summary>
public sealed class AgentApiCatalogSpec
{
    /// <summary>Enable /.well-known/api-catalog generation.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Output path relative to site root.</summary>
    public string? OutputPath { get; set; }
    /// <summary>API catalog entries.</summary>
    public AgentApiCatalogEntrySpec[] Entries { get; set; } = Array.Empty<AgentApiCatalogEntrySpec>();
}

/// <summary>Single API catalog entry.</summary>
public sealed class AgentApiCatalogEntrySpec
{
    /// <summary>API anchor URL or route.</summary>
    public string Anchor { get; set; } = string.Empty;
    /// <summary>Machine-readable service description URL, usually OpenAPI.</summary>
    public string? ServiceDesc { get; set; }
    /// <summary>Human-readable service documentation URL.</summary>
    public string? ServiceDoc { get; set; }
    /// <summary>Optional health/status URL.</summary>
    public string? Status { get; set; }
    /// <summary>Optional title.</summary>
    public string? Title { get; set; }
}

/// <summary>Agent Skills discovery output settings.</summary>
public sealed class AgentSkillsDiscoverySpec
{
    /// <summary>Enable /.well-known/agent-skills/index.json generation.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Index output path relative to site root.</summary>
    public string? IndexPath { get; set; }
    /// <summary>Skill entries.</summary>
    public AgentSkillSpec[] Skills { get; set; } = Array.Empty<AgentSkillSpec>();
}

/// <summary>Generic agent discovery document settings.</summary>
public sealed class AgentDiscoveryDocumentSpec
{
    /// <summary>Enable /agents.json and /.well-known/agents.json generation.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Main output path relative to site root.</summary>
    public string? OutputPath { get; set; } = "agents.json";
    /// <summary>Optional well-known mirror path relative to site root.</summary>
    public string? WellKnownOutputPath { get; set; } = ".well-known/agents.json";
    /// <summary>Human-readable description of the agent-facing site surface.</summary>
    public string? Description { get; set; }
}

/// <summary>A2A Agent Card output settings.</summary>
public sealed class AgentA2ACardSpec
{
    /// <summary>Enable /.well-known/agent-card.json generation.</summary>
    public bool Enabled { get; set; }
    /// <summary>Output path relative to site root.</summary>
    public string? OutputPath { get; set; } = ".well-known/agent-card.json";
    /// <summary>Agent or site name.</summary>
    public string? Name { get; set; }
    /// <summary>Agent or site description.</summary>
    public string? Description { get; set; }
    /// <summary>Service endpoint URL. If omitted, the site base URL is used for a discoverability-only card.</summary>
    public string? Url { get; set; }
    /// <summary>Agent card version.</summary>
    public string? Version { get; set; } = "1.0.0";
    /// <summary>Documentation route or absolute URL.</summary>
    public string? DocumentationUrl { get; set; }
    /// <summary>Supported default input modes.</summary>
    public string[] DefaultInputModes { get; set; } = new[] { "text/plain", "text/markdown" };
    /// <summary>Supported default output modes.</summary>
    public string[] DefaultOutputModes { get; set; } = new[] { "text/plain", "text/markdown" };
    /// <summary>Advertised A2A skills.</summary>
    public AgentA2ASkillSpec[] Skills { get; set; } = Array.Empty<AgentA2ASkillSpec>();
}

/// <summary>A2A Agent Card skill entry.</summary>
public sealed class AgentA2ASkillSpec
{
    /// <summary>Skill identifier.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Skill name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Skill description.</summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>Skill tags.</summary>
    public string[] Tags { get; set; } = Array.Empty<string>();
}

/// <summary>Single Agent Skill discovery entry.</summary>
public sealed class AgentSkillSpec
{
    /// <summary>Skill identifier.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Skill type, usually skill-md.</summary>
    public string Type { get; set; } = "skill-md";
    /// <summary>Short description used for progressive disclosure.</summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>Public URL to the skill artifact. If omitted, a local skill file is generated.</summary>
    public string? Url { get; set; }
    /// <summary>Local file path to an existing SKILL.md source.</summary>
    public string? SourcePath { get; set; }
    /// <summary>Inline SKILL.md content used when SourcePath is not set.</summary>
    public string? Content { get; set; }
}

/// <summary>OpenAPI discovery settings.</summary>
public sealed class AgentOpenApiSpec
{
    /// <summary>Enable OpenAPI checks and API catalog linking.</summary>
    public bool Enabled { get; set; }
    /// <summary>OpenAPI document path or absolute URL. If omitted, common static paths are checked.</summary>
    public string? Path { get; set; }
}

/// <summary>MCP server card output settings.</summary>
public sealed class AgentMcpServerCardSpec
{
    /// <summary>Enable /.well-known/mcp/server-card.json generation.</summary>
    public bool Enabled { get; set; }
    /// <summary>Output path relative to site root.</summary>
    public string? OutputPath { get; set; }
    /// <summary>MCP server name.</summary>
    public string? Name { get; set; }
    /// <summary>MCP server version.</summary>
    public string? Version { get; set; }
    /// <summary>Transport endpoint URL or route.</summary>
    public string? Endpoint { get; set; }
    /// <summary>Transport type, for example streamable-http.</summary>
    public string? Transport { get; set; }
    /// <summary>Advertise tool support.</summary>
    public bool Tools { get; set; } = true;
    /// <summary>Advertise resource support.</summary>
    public bool Resources { get; set; }
    /// <summary>Advertise prompt support.</summary>
    public bool Prompts { get; set; }
}
