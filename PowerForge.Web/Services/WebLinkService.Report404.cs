using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

public static partial class WebLinkService
{
    private static readonly TimeSpan LinkRegexTimeout = TimeSpan.FromSeconds(2);
    private static readonly Regex ApacheRequestRegex = new("\"[A-Z]+\\s+([^\\s\\\"]+)\\s+HTTP/[^\\\"]+\"\\s+(\\d{3})", RegexOptions.Compiled | RegexOptions.CultureInvariant, LinkRegexTimeout);

    /// <summary>Creates a reviewable 404 suggestion report from logs or observation CSVs.</summary>
    public static WebLink404ReportResult Generate404Report(WebLink404ReportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.SiteRoot))
            throw new ArgumentException("SiteRoot is required.", nameof(options));

        var siteRoot = Path.GetFullPath(options.SiteRoot);
        if (!Directory.Exists(siteRoot))
            throw new DirectoryNotFoundException($"Site root not found: {siteRoot}");

        var routes = DiscoverHtmlRoutes(siteRoot);
        var ignored = LoadIgnored404Rules(options.Ignored404Path, options.Ignored404Rules);
        var filteredObservations = Load404Observations(options, routes)
            .Where(item => !RouteExists(routes, item.Path))
            .Where(item => options.IncludeAsset404s || !LooksLikeAssetPath(item.Path))
            .ToArray();
        var observations = filteredObservations
            .Where(item => !IsIgnored404(item, ignored))
            .OrderByDescending(static item => item.Count)
            .ThenBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var maxSuggestions = options.MaxSuggestions <= 0 ? 3 : Math.Min(options.MaxSuggestions, 10);
        var minimumScore = options.MinimumScore <= 0 ? 0.35d : Math.Clamp(options.MinimumScore, 0.05d, 1d);
        var suggestions = new List<WebLink404Suggestion>();
        foreach (var observation in observations)
        {
            var matches = routes
                .Select(route => new { Route = route, Score = ScoreRoute(observation.Path, route) })
                .Where(item => item.Score >= minimumScore)
                .OrderByDescending(static item => item.Score)
                .ThenBy(static item => item.Route, StringComparer.OrdinalIgnoreCase)
                .Take(maxSuggestions)
                .Select(item => new WebLink404RouteSuggestion
                {
                    TargetPath = item.Route,
                    Score = Math.Round(item.Score, 3)
                })
                .ToArray();

            suggestions.Add(new WebLink404Suggestion
            {
                Path = observation.Path,
                Host = observation.Host,
                Count = observation.Count,
                Referrer = observation.Referrer,
                LastSeenAt = observation.LastSeenAt,
                Suggestions = matches
            });
        }

        return new WebLink404ReportResult
        {
            SiteRoot = siteRoot,
            SourcePath = string.IsNullOrWhiteSpace(options.SourcePath) ? null : Path.GetFullPath(options.SourcePath),
            RouteCount = routes.Length,
            ObservationCount = observations.Length,
            IgnoredObservationCount = filteredObservations.Length - observations.Length,
            SuggestedObservationCount = suggestions.Count(static item => item.Suggestions.Length > 0),
            Suggestions = suggestions.ToArray()
        };
    }

    private static string[] DiscoverHtmlRoutes(string siteRoot)
    {
        return Directory.EnumerateFiles(siteRoot, "*.html", SearchOption.AllDirectories)
            .Select(path => ToRoute(siteRoot, path))
            .Where(route => !string.IsNullOrWhiteSpace(route))
            .Where(route => !route.Equals("/404.html", StringComparison.OrdinalIgnoreCase))
            .Where(route => !route.Equals("/404/", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static route => route, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ToRoute(string siteRoot, string filePath)
    {
        var relative = Path.GetRelativePath(siteRoot, filePath).Replace('\\', '/');
        if (relative.Equals("index.html", StringComparison.OrdinalIgnoreCase))
            return "/";
        if (relative.EndsWith("/index.html", StringComparison.OrdinalIgnoreCase))
            return "/" + relative[..^"index.html".Length];
        return "/" + relative.TrimStart('/');
    }

    private static IEnumerable<WebLink404Observation> Load404Observations(WebLink404ReportOptions options, IReadOnlyCollection<string> routes)
    {
        if (string.IsNullOrWhiteSpace(options.SourcePath))
            return Array.Empty<WebLink404Observation>();

        var sourcePath = Path.GetFullPath(options.SourcePath);
        if (!File.Exists(sourcePath))
        {
            if (options.AllowMissingSource)
                return Array.Empty<WebLink404Observation>();
            throw new FileNotFoundException($"404 source file not found: {sourcePath}", sourcePath);
        }

        var lines = File.ReadAllLines(sourcePath);
        if (lines.Length == 0)
            return Array.Empty<WebLink404Observation>();

        var header = SplitCsvLine(lines[0]);
        var pathIndex = FindHeader(header, "path", "url", "request", "request_uri", "request uri", "uri");
        var countIndex = FindHeader(header, "count", "hits", "visits");
        var statusIndex = FindHeader(header, "status", "status_code", "status code");
        if (pathIndex >= 0)
            return Read404Csv(lines, pathIndex, countIndex, statusIndex);

        return ReadApache404Log(lines, routes);
    }

    private static IEnumerable<WebLink404Observation> Read404Csv(string[] lines, int pathIndex, int countIndex, int statusIndex)
    {
        var hostIndex = FindHeader(SplitCsvLine(lines[0]), "host", "domain");
        var referrerIndex = FindHeader(SplitCsvLine(lines[0]), "referrer", "referer");
        var lastSeenIndex = FindHeader(SplitCsvLine(lines[0]), "last_seen", "lastSeen", "last seen", "last_seen_at", "lastSeenAt");
        var aggregated = new Dictionary<string, WebLink404Observation>(StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;

            var parts = SplitCsvLine(lines[i]);
            if (statusIndex >= 0 && ReadPart(parts, statusIndex) is { Length: > 0 } statusText && statusText != "404")
                continue;

            var path = NormalizeObservationPath(ReadPart(parts, pathIndex));
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var count = ParsePositiveInt(ReadPart(parts, countIndex));
            if (count <= 0)
                count = 1;
            AddObservation(aggregated, path, ReadPart(parts, hostIndex), count, ReadPart(parts, referrerIndex), ReadPart(parts, lastSeenIndex));
        }

        return aggregated.Values;
    }

    private static IEnumerable<WebLink404Observation> ReadApache404Log(string[] lines, IReadOnlyCollection<string> routes)
    {
        var aggregated = new Dictionary<string, WebLink404Observation>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var match = ApacheRequestRegex.Match(line);
            if (!match.Success || !match.Groups[2].Value.Equals("404", StringComparison.Ordinal))
                continue;

            var path = NormalizeObservationPath(match.Groups[1].Value);
            if (string.IsNullOrWhiteSpace(path) || RouteExists(routes, path))
                continue;

            AddObservation(aggregated, path, host: null, count: 1, referrer: null, lastSeenAt: null);
        }

        return aggregated.Values;
    }

    private static void AddObservation(Dictionary<string, WebLink404Observation> target, string path, string? host, int count, string? referrer, string? lastSeenAt)
    {
        var key = (host ?? string.Empty) + "|" + path;
        if (target.TryGetValue(key, out var existing))
        {
            existing.Count += count;
            if (string.IsNullOrWhiteSpace(existing.Referrer))
                existing.Referrer = NullIfWhiteSpace(referrer);
            if (string.IsNullOrWhiteSpace(existing.LastSeenAt))
                existing.LastSeenAt = NullIfWhiteSpace(lastSeenAt);
            return;
        }

        target[key] = new WebLink404Observation
        {
            Path = path,
            Host = NullIfWhiteSpace(host),
            Count = count,
            Referrer = NullIfWhiteSpace(referrer),
            LastSeenAt = NullIfWhiteSpace(lastSeenAt)
        };
    }

    private static bool RouteExists(IReadOnlyCollection<string> routes, string path)
    {
        var normalized = NormalizeObservationPath(path);
        if (routes.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            return true;
        if (!normalized.EndsWith("/", StringComparison.Ordinal) &&
            routes.Contains(normalized + "/", StringComparer.OrdinalIgnoreCase))
            return true;
        return normalized.EndsWith("/", StringComparison.Ordinal) &&
               routes.Contains(normalized.TrimEnd('/') + ".html", StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeObservationPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            trimmed = uri.PathAndQuery;
        }

        var queryIndex = trimmed.IndexOf('?');
        if (queryIndex >= 0)
            trimmed = trimmed[..queryIndex];
        var hashIndex = trimmed.IndexOf('#');
        if (hashIndex >= 0)
            trimmed = trimmed[..hashIndex];
        if (!trimmed.StartsWith("/", StringComparison.Ordinal))
            trimmed = "/" + trimmed.TrimStart('/');
        return trimmed.Length > 1 ? trimmed.TrimEnd('/') : trimmed;
    }

    private static bool LooksLikeAssetPath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".css", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".js", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".map", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".svg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ico", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIgnored404(WebLink404Observation observation, IReadOnlyList<Ignored404Rule> ignored)
        => ignored.Any(rule => MatchesIgnored404Rule(observation, rule));

    private static bool MatchesIgnored404Rule(WebLink404Observation observation, Ignored404Rule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Path))
            return false;
        if (!string.IsNullOrWhiteSpace(rule.Host) &&
            !string.Equals(rule.Host.Trim(), observation.Host, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var path = NormalizeObservationPath(observation.Path);
        var pattern = NormalizeObservationPath(rule.Path);
        if (!pattern.Contains('*', StringComparison.Ordinal))
            return path.Equals(pattern, StringComparison.OrdinalIgnoreCase);

        var parts = pattern.Split('*', StringSplitOptions.None);
        var position = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0)
                continue;

            var index = path.IndexOf(part, position, StringComparison.OrdinalIgnoreCase);
            if (index < 0 || (i == 0 && !pattern.StartsWith('*') && index != 0))
                return false;
            position = index + part.Length;
        }

        return pattern.EndsWith('*') || position == path.Length;
    }

    private static Ignored404Rule[] LoadIgnored404Rules(string? path, IReadOnlyList<Ignored404Rule>? inlineRules = null)
    {
        var rules = new List<Ignored404Rule>();
        if (inlineRules is { Count: > 0 })
            rules.AddRange(inlineRules.Where(static item => item is not null));

        if (!string.IsNullOrWhiteSpace(path))
        {
            var resolved = Path.GetFullPath(path);
            if (File.Exists(resolved))
                rules.AddRange(ReadIgnored404Json(resolved));
        }

        return rules
            .Where(static item => !string.IsNullOrWhiteSpace(item.Path))
            .ToArray();
    }

    private static Ignored404Rule[] ReadIgnored404Json(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var source = document.RootElement;
        if (source.ValueKind == JsonValueKind.Object && source.TryGetProperty("ignored404", out var nested))
            source = nested;
        if (source.ValueKind != JsonValueKind.Array)
            return Array.Empty<Ignored404Rule>();

        return source.Deserialize<Ignored404Rule[]>(WebJson.Options) ?? Array.Empty<Ignored404Rule>();
    }

    private static double ScoreRoute(string missingPath, string route)
    {
        var missing = NormalizeForScore(missingPath);
        var candidate = NormalizeForScore(route);
        if (missing.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            return 1d;
        if (missing.Contains(candidate, StringComparison.OrdinalIgnoreCase) ||
            candidate.Contains(missing, StringComparison.OrdinalIgnoreCase))
            return 0.82d;

        var missingTokens = TokenizePath(missing);
        var candidateTokens = TokenizePath(candidate);
        var tokenScore = Jaccard(missingTokens, candidateTokens);
        var tailScore = SegmentSimilarity(missingTokens.LastOrDefault() ?? missing, candidateTokens.LastOrDefault() ?? candidate);
        return Math.Max(tokenScore, tailScore);
    }

    private static string NormalizeForScore(string path)
        => NormalizeObservationPath(path)
            .Trim('/')
            .Replace(".html", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace('-', ' ')
            .Replace('_', ' ')
            .ToLowerInvariant();

    private static string[] TokenizePath(string value)
        => value.Split(new[] { '/', ' ', '.', '+', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static token => token.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static double Jaccard(string[] left, string[] right)
    {
        if (left.Length == 0 || right.Length == 0)
            return 0d;

        var intersection = left.Intersect(right, StringComparer.OrdinalIgnoreCase).Count();
        var union = left.Union(right, StringComparer.OrdinalIgnoreCase).Count();
        return union == 0 ? 0d : (double)intersection / union;
    }

    private static double SegmentSimilarity(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return 0d;

        var distance = Levenshtein(left, right);
        var max = Math.Max(left.Length, right.Length);
        return max == 0 ? 0d : 1d - ((double)distance / max);
    }

    private static int Levenshtein(string left, string right)
    {
        var costs = new int[right.Length + 1];
        for (var j = 0; j < costs.Length; j++)
            costs[j] = j;

        for (var i = 1; i <= left.Length; i++)
        {
            var previous = costs[0];
            costs[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var current = costs[j];
                costs[j] = left[i - 1] == right[j - 1]
                    ? previous
                    : Math.Min(Math.Min(costs[j - 1], costs[j]), previous) + 1;
                previous = current;
            }
        }

        return costs[right.Length];
    }
}

/// <summary>Options for generating reviewable 404 redirect suggestions.</summary>
public sealed class WebLink404ReportOptions
{
    /// <summary>Generated site root used for known route discovery.</summary>
    public string SiteRoot { get; set; } = string.Empty;
    /// <summary>Apache access log or CSV file with 404 observations.</summary>
    public string? SourcePath { get; set; }
    /// <summary>Path to ignored 404 JSON rules.</summary>
    public string? Ignored404Path { get; set; }
    /// <summary>Inline ignored 404 rules.</summary>
    public Ignored404Rule[] Ignored404Rules { get; set; } = Array.Empty<Ignored404Rule>();
    /// <summary>When true, a missing source log produces an empty report instead of failing.</summary>
    public bool AllowMissingSource { get; set; }
    /// <summary>Maximum suggestions per missing path.</summary>
    public int MaxSuggestions { get; set; } = 3;
    /// <summary>Minimum match score from 0 to 1.</summary>
    public double MinimumScore { get; set; } = 0.35d;
    /// <summary>When true, include CSS/JS/image 404s in the report.</summary>
    public bool IncludeAsset404s { get; set; }
}

/// <summary>Generated 404 report with candidate redirect targets.</summary>
public sealed class WebLink404ReportResult
{
    /// <summary>Resolved generated site root used for route discovery.</summary>
    public string SiteRoot { get; set; } = string.Empty;
    /// <summary>Resolved source log or observation CSV path, when provided.</summary>
    public string? SourcePath { get; set; }
    /// <summary>Number of generated HTML routes discovered.</summary>
    public int RouteCount { get; set; }
    /// <summary>Number of missing URL observations included in the report.</summary>
    public int ObservationCount { get; set; }
    /// <summary>Number of observations suppressed by ignored 404 rules.</summary>
    public int IgnoredObservationCount { get; set; }
    /// <summary>Number of observations with at least one suggested target.</summary>
    public int SuggestedObservationCount { get; set; }
    /// <summary>Observed missing URLs and their candidate targets.</summary>
    public WebLink404Suggestion[] Suggestions { get; set; } = Array.Empty<WebLink404Suggestion>();
}

/// <summary>One observed missing URL and its suggested targets.</summary>
public sealed class WebLink404Suggestion
{
    /// <summary>Observed missing path.</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>Optional observed host.</summary>
    public string? Host { get; set; }
    /// <summary>Observation count for this missing path.</summary>
    public int Count { get; set; }
    /// <summary>Optional referrer recorded with the observation.</summary>
    public string? Referrer { get; set; }
    /// <summary>Optional last-seen timestamp from the observation source.</summary>
    public string? LastSeenAt { get; set; }
    /// <summary>Candidate generated routes that may be suitable redirect targets.</summary>
    public WebLink404RouteSuggestion[] Suggestions { get; set; } = Array.Empty<WebLink404RouteSuggestion>();
}

/// <summary>One candidate route for a missing URL.</summary>
public sealed class WebLink404RouteSuggestion
{
    /// <summary>Candidate generated route path.</summary>
    public string TargetPath { get; set; } = string.Empty;
    /// <summary>Similarity score from 0 to 1.</summary>
    public double Score { get; set; }
}

internal sealed class WebLink404Observation
{
    public string Path { get; set; } = string.Empty;
    public string? Host { get; set; }
    public int Count { get; set; }
    public string? Referrer { get; set; }
    public string? LastSeenAt { get; set; }
}
