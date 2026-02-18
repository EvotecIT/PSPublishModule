using System.Text.Json;

namespace PowerForge.Web;

/// <summary>Crawl policy resolution and metadata emitters.</summary>
public static partial class WebSiteBuilder
{
    private static readonly string[] DefaultCrawlerMetaNames =
    {
        "googlebot",
        "bingbot",
        "slurp",
        "duckduckbot",
        "baiduspider",
        "yandex"
    };

    private static string BuildCrawlMetaHtml(SiteSpec spec, ContentItem item)
    {
        var resolved = ResolveCrawlPolicy(spec, item);
        if (!resolved.Enabled)
            return string.Empty;

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(resolved.Robots))
        {
            lines.Add(
                $"<meta name=\"robots\" content=\"{System.Web.HttpUtility.HtmlEncode(resolved.Robots)}\" />");
        }

        foreach (var bot in resolved.Bots.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(bot.Key) || string.IsNullOrWhiteSpace(bot.Value))
                continue;

            lines.Add(
                $"<meta name=\"{System.Web.HttpUtility.HtmlEncode(bot.Key)}\" content=\"{System.Web.HttpUtility.HtmlEncode(bot.Value)}\" />");
        }

        return lines.Count == 0 ? string.Empty : string.Join(Environment.NewLine, lines);
    }

    private static void WriteCrawlPolicyReport(SiteSpec spec, IReadOnlyList<ContentItem> items, string metaDir)
    {
        if (spec?.Seo?.CrawlPolicy?.Enabled != true || items is null || string.IsNullOrWhiteSpace(metaDir))
            return;

        var pages = items
            .Where(static item => !item.Draft)
            .OrderBy(static item => NormalizeRouteForMatch(item.OutputPath), StringComparer.OrdinalIgnoreCase)
            .Select(item =>
            {
                var resolved = ResolveCrawlPolicy(spec, item);
                return new
                {
                    sourcePath = item.SourcePath,
                    outputPath = NormalizeRouteForMatch(item.OutputPath),
                    rule = resolved.RuleName,
                    robots = resolved.Robots,
                    bots = resolved.Bots
                };
            })
            .ToArray();

        var payload = new
        {
            generatedAtUtc = DateTime.UtcNow,
            enabled = true,
            defaults = new
            {
                robots = spec.Seo.CrawlPolicy.DefaultRobots,
                bots = spec.Seo.CrawlPolicy.Bots
                    .Where(static bot => bot is not null && !string.IsNullOrWhiteSpace(bot.Name))
                    .Select(static bot => new
                    {
                        name = bot.Name,
                        directives = bot.Directives
                    })
                    .ToArray()
            },
            rules = spec.Seo.CrawlPolicy.Rules
                .Where(static rule => rule is not null && !string.IsNullOrWhiteSpace(rule.Match))
                .Select(static rule => new
                {
                    name = string.IsNullOrWhiteSpace(rule.Name) ? null : rule.Name.Trim(),
                    match = rule.Match,
                    matchType = string.IsNullOrWhiteSpace(rule.MatchType) ? "wildcard" : rule.MatchType,
                    robots = rule.Robots,
                    bots = rule.Bots
                        .Where(static bot => bot is not null && !string.IsNullOrWhiteSpace(bot.Name))
                        .Select(static bot => new
                        {
                            name = bot.Name,
                            directives = bot.Directives
                        })
                        .ToArray()
                })
                .ToArray(),
            pages
        };

        var reportPath = Path.Combine(metaDir, "crawl-policy.json");
        WriteAllTextIfChanged(reportPath, JsonSerializer.Serialize(payload, WebJson.Options));
    }

    private static ResolvedCrawlPolicy ResolveCrawlPolicy(SiteSpec spec, ContentItem item)
    {
        var policy = spec?.Seo?.CrawlPolicy;
        if (policy?.Enabled != true)
            return ResolvedCrawlPolicy.Disabled;

        var normalizedRoute = NormalizeRouteForMatch(item.OutputPath);
        var matchedRule = ResolveMatchingCrawlRule(policy, normalizedRoute);

        var robots = FirstNonEmpty(
            GetMetaString(item.Meta, "robots"),
            matchedRule?.Robots,
            policy.DefaultRobots);

        var bots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AppendBotDirectives(bots, policy.Bots);
        if (matchedRule is not null)
            AppendBotDirectives(bots, matchedRule.Bots);

        foreach (var metaName in DefaultCrawlerMetaNames)
        {
            var overrideValue = GetPageBotDirective(item, metaName);
            if (string.IsNullOrWhiteSpace(overrideValue))
                continue;
            bots[metaName] = NormalizeCrawlDirectiveValue(overrideValue)!;
        }

        foreach (var existing in bots.Keys.ToArray())
        {
            var overrideValue = GetPageBotDirective(item, existing);
            if (string.IsNullOrWhiteSpace(overrideValue))
                continue;
            bots[existing] = NormalizeCrawlDirectiveValue(overrideValue)!;
        }

        var normalizedBots = bots
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(
                static pair => pair.Key.Trim(),
                static pair => pair.Value.Trim(),
                StringComparer.OrdinalIgnoreCase);

        return new ResolvedCrawlPolicy
        {
            Enabled = true,
            RuleName = string.IsNullOrWhiteSpace(matchedRule?.Name) ? null : matchedRule.Name.Trim(),
            Robots = NormalizeCrawlDirectiveValue(robots),
            Bots = normalizedBots
        };
    }

    private static CrawlRuleSpec? ResolveMatchingCrawlRule(CrawlPolicySpec policy, string normalizedRoute)
    {
        if (policy.Rules is null || policy.Rules.Length == 0)
            return null;

        foreach (var rule in policy.Rules)
        {
            if (rule is null || string.IsNullOrWhiteSpace(rule.Match))
                continue;
            if (CrawlRuleMatches(rule, normalizedRoute))
                return rule;
        }

        return null;
    }

    private static bool CrawlRuleMatches(CrawlRuleSpec rule, string normalizedRoute)
    {
        var matchType = string.IsNullOrWhiteSpace(rule.MatchType)
            ? "wildcard"
            : rule.MatchType.Trim().ToLowerInvariant();
        return matchType switch
        {
            "exact" => CrawlExactMatch(normalizedRoute, rule.Match),
            "prefix" => CrawlPrefixMatch(normalizedRoute, rule.Match),
            "wildcard" => CrawlWildcardMatch(normalizedRoute, rule.Match),
            _ => CrawlWildcardMatch(normalizedRoute, rule.Match)
        };
    }

    private static bool CrawlExactMatch(string normalizedRoute, string? rawMatch)
    {
        var normalizedMatch = NormalizeRouteForMatch(rawMatch);
        if (string.IsNullOrWhiteSpace(normalizedMatch))
            return false;
        return string.Equals(normalizedRoute, normalizedMatch, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CrawlPrefixMatch(string normalizedRoute, string? rawMatch)
    {
        var normalizedMatch = NormalizeRouteForMatch(rawMatch);
        if (string.IsNullOrWhiteSpace(normalizedMatch))
            return false;
        return RouteStartsWith(normalizedRoute, normalizedMatch);
    }

    private static bool CrawlWildcardMatch(string normalizedRoute, string? rawMatch)
    {
        var normalizedPattern = NormalizeCrawlWildcardPattern(rawMatch);
        if (string.IsNullOrWhiteSpace(normalizedPattern))
            return false;
        return WildcardRouteMatches(normalizedRoute, normalizedPattern);
    }

    private static string NormalizeCrawlWildcardPattern(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim();
        var hashIndex = normalized.IndexOf('#');
        if (hashIndex >= 0)
            normalized = normalized.Substring(0, hashIndex);
        var queryIndex = normalized.IndexOf('?');
        if (queryIndex >= 0)
            normalized = normalized.Substring(0, queryIndex);

        if (!normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = "/" + normalized;

        return normalized;
    }

    private static bool WildcardRouteMatches(string route, string pattern)
    {
        if (GlobMatch(pattern, route))
            return true;

        if (pattern.EndsWith("/*", StringComparison.Ordinal))
        {
            var prefix = pattern.Substring(0, pattern.Length - 2);
            return RouteStartsWith(route, prefix);
        }

        return false;
    }

    private static bool RouteStartsWith(string route, string prefix)
    {
        if (string.IsNullOrWhiteSpace(route) || string.IsNullOrWhiteSpace(prefix))
            return false;
        if (string.Equals(route, prefix, StringComparison.OrdinalIgnoreCase))
            return true;

        var normalizedPrefix = prefix.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalizedPrefix))
            normalizedPrefix = "/";
        return route.StartsWith(normalizedPrefix + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendBotDirectives(IDictionary<string, string> destination, IEnumerable<CrawlBotDirectiveSpec>? directives)
    {
        if (destination is null || directives is null)
            return;

        foreach (var directive in directives)
        {
            if (directive is null || string.IsNullOrWhiteSpace(directive.Name))
                continue;
            var normalizedValue = NormalizeCrawlDirectiveValue(directive.Directives);
            if (string.IsNullOrWhiteSpace(normalizedValue))
                continue;
            destination[directive.Name.Trim()] = normalizedValue;
        }
    }

    private static string? GetPageBotDirective(ContentItem item, string botName)
    {
        if (string.IsNullOrWhiteSpace(botName))
            return null;

        return FirstNonEmpty(
            GetMetaString(item.Meta, botName),
            GetMetaString(item.Meta, "robots." + botName),
            GetMetaString(item.Meta, "robots_" + botName));
    }

    private static string? NormalizeCrawlDirectiveValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return WhitespaceRegex.Replace(value.Trim(), " ");
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private sealed class ResolvedCrawlPolicy
    {
        public static readonly ResolvedCrawlPolicy Disabled = new();

        public bool Enabled { get; init; }
        public string? RuleName { get; init; }
        public string? Robots { get; init; }
        public IReadOnlyDictionary<string, string> Bots { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
