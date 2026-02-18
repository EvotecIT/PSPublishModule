namespace PowerForge.Web;

/// <summary>SEO configuration for site and collection metadata rendering.</summary>
public sealed class SeoSpec
{
    /// <summary>When false, SEO template resolution is disabled.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Title/description template settings.</summary>
    public SeoTemplatesSpec? Templates { get; set; }
    /// <summary>Crawl/indexing policy for robots metadata.</summary>
    public CrawlPolicySpec? CrawlPolicy { get; set; }
}

/// <summary>SEO title/description template strings.</summary>
public sealed class SeoTemplatesSpec
{
    /// <summary>Template for HTML title and default social title fallback.</summary>
    public string? Title { get; set; }
    /// <summary>Template for meta description.</summary>
    public string? Description { get; set; }
}

/// <summary>Route-scoped crawl directives and bot metadata rules.</summary>
public sealed class CrawlPolicySpec
{
    /// <summary>When false, crawl policy metadata is not emitted.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Default robots directives used when no rule matches (for example: index,follow).</summary>
    public string? DefaultRobots { get; set; }
    /// <summary>Default bot-specific directives applied to all pages unless overridden by rules/pages.</summary>
    public CrawlBotDirectiveSpec[] Bots { get; set; } = Array.Empty<CrawlBotDirectiveSpec>();
    /// <summary>Route matching rules; first matching rule wins.</summary>
    public CrawlRuleSpec[] Rules { get; set; } = Array.Empty<CrawlRuleSpec>();
}

/// <summary>Rule for matching routes and applying robots directives.</summary>
public sealed class CrawlRuleSpec
{
    /// <summary>Optional rule name used for diagnostics output.</summary>
    public string? Name { get; set; }
    /// <summary>Route pattern (for example: /search/*).</summary>
    public string Match { get; set; } = string.Empty;
    /// <summary>Match type: exact, prefix, or wildcard (default: wildcard).</summary>
    public string MatchType { get; set; } = "wildcard";
    /// <summary>Robots directives applied by this rule.</summary>
    public string? Robots { get; set; }
    /// <summary>Bot-specific directives applied by this rule.</summary>
    public CrawlBotDirectiveSpec[] Bots { get; set; } = Array.Empty<CrawlBotDirectiveSpec>();
}

/// <summary>Bot-specific robots directives (for example googlebot or bingbot).</summary>
public sealed class CrawlBotDirectiveSpec
{
    /// <summary>Bot name used as the meta tag name attribute.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Directives value for the bot meta tag.</summary>
    public string Directives { get; set; } = string.Empty;
}
