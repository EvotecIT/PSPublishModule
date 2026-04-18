using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

/// <summary>Loads, validates, and exports PowerForge.Web redirect and shortlink data.</summary>
public static partial class WebLinkService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly Regex SafeSlugRegex = new("^[a-z0-9][a-z0-9._-]*$", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LegacyPostIdRegex = new(@"^\s*/\?p=(\d+)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LegacyPageIdRegex = new(@"^\s*/\?page_id=(\d+)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Loads redirect and shortlink data from configured JSON and compatibility CSV files.</summary>
    public static WebLinkDataSet Load(WebLinkLoadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var redirects = new List<LinkRedirectRule>();
        var shortlinks = new List<LinkShortlinkRule>();
        var usedSources = new List<string>();
        var missingSources = new List<string>();

        LoadRedirectJson(options.RedirectsPath, redirects, usedSources, missingSources);
        LoadShortlinkJson(options.ShortlinksPath, shortlinks, usedSources, missingSources);

        foreach (var csvPath in options.RedirectCsvPaths ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(csvPath))
                continue;

            var resolved = Path.GetFullPath(csvPath);
            if (!File.Exists(resolved))
            {
                missingSources.Add(resolved);
                continue;
            }

            usedSources.Add(resolved);
            redirects.AddRange(ReadRedirectCsv(resolved, options.Hosts));
        }

        return new WebLinkDataSet
        {
            Redirects = redirects.ToArray(),
            Shortlinks = shortlinks.ToArray(),
            UsedSources = usedSources.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            MissingSources = missingSources.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Hosts = NormalizeHostMap(options.Hosts),
            LanguageRootHosts = NormalizeLanguageRootHosts(options.LanguageRootHosts)
        };
    }

    /// <summary>Validates redirect and shortlink rules for duplicates, unsafe targets, loops, and hygiene issues.</summary>
    public static LinkValidationResult Validate(WebLinkDataSet dataSet)
    {
        ArgumentNullException.ThrowIfNull(dataSet);

        var issues = new List<LinkValidationIssue>();
        var enabledRedirects = dataSet.Redirects
            .Where(static redirect => redirect is not null && redirect.Enabled)
            .ToArray();
        var enabledShortlinks = dataSet.Shortlinks
            .Where(static shortlink => shortlink is not null && shortlink.Enabled)
            .ToArray();

        ValidateRedirects(enabledRedirects, issues, dataSet.LanguageRootHosts);
        ValidateShortlinks(enabledShortlinks, issues, dataSet.Hosts);
        ValidateRedirectGraph(enabledRedirects, issues);

        var errorCount = issues.Count(static issue => issue.Severity == LinkValidationSeverity.Error);
        var warningCount = issues.Count(static issue => issue.Severity == LinkValidationSeverity.Warning);
        return new LinkValidationResult
        {
            Issues = issues.ToArray(),
            RedirectCount = enabledRedirects.Length,
            ShortlinkCount = enabledShortlinks.Length,
            ErrorCount = errorCount,
            WarningCount = warningCount
        };
    }

    private static void LoadRedirectJson(string? path, List<LinkRedirectRule> redirects, List<string> usedSources, List<string> missingSources)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var resolved = Path.GetFullPath(path);
        if (!File.Exists(resolved))
        {
            missingSources.Add(resolved);
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(resolved), new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        usedSources.Add(resolved);
        JsonElement source = document.RootElement;
        if (source.ValueKind == JsonValueKind.Object && source.TryGetProperty("redirects", out var nested))
            source = nested;
        if (source.ValueKind != JsonValueKind.Array)
            return;

        var parsed = source.Deserialize<LinkRedirectRule[]>(WebJson.Options) ?? Array.Empty<LinkRedirectRule>();
        foreach (var redirect in parsed)
        {
            if (string.IsNullOrWhiteSpace(redirect.OriginPath))
                redirect.OriginPath = resolved;
        }
        redirects.AddRange(parsed);
    }

    private static void LoadShortlinkJson(string? path, List<LinkShortlinkRule> shortlinks, List<string> usedSources, List<string> missingSources)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var resolved = Path.GetFullPath(path);
        if (!File.Exists(resolved))
        {
            missingSources.Add(resolved);
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(resolved), new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        usedSources.Add(resolved);
        JsonElement source = document.RootElement;
        if (source.ValueKind == JsonValueKind.Object && source.TryGetProperty("shortlinks", out var nested))
            source = nested;
        if (source.ValueKind != JsonValueKind.Array)
            return;

        var parsed = source.Deserialize<LinkShortlinkRule[]>(WebJson.Options) ?? Array.Empty<LinkShortlinkRule>();
        foreach (var shortlink in parsed)
        {
            if (string.IsNullOrWhiteSpace(shortlink.OriginPath))
                shortlink.OriginPath = resolved;
        }
        shortlinks.AddRange(parsed);
    }

    private static IEnumerable<LinkRedirectRule> ReadRedirectCsv(string csvPath, IReadOnlyDictionary<string, string>? hosts)
    {
        var rows = new List<LinkRedirectRule>();
        var lines = File.ReadAllLines(csvPath);
        if (lines.Length <= 1)
            return rows;

        var header = SplitCsvLine(lines[0]);
        var legacyIndex = FindHeader(header, "legacy_url", "source", "from", "redirect_from", "redirect from");
        var targetIndex = FindHeader(header, "target_url", "target", "to", "redirect_to", "redirect to");
        var statusIndex = FindHeader(header, "status", "redirect_type", "redirect type", "status_code", "status code");
        var languageIndex = FindHeader(header, "language", "lang");
        var sourceTypeIndex = FindHeader(header, "source_type", "sourceType", "group", "redirect_from_type", "redirect from type");
        var sourceIdIndex = FindHeader(header, "source_id", "sourceId", "id");
        var notesIndex = FindHeader(header, "notes", "note");
        var regexIndex = FindHeader(header, "regex", "pattern");

        if (legacyIndex < 0 || targetIndex < 0)
            return rows;

        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;

            var parts = SplitCsvLine(lines[i]);
            if (parts.Length <= legacyIndex || parts.Length <= targetIndex)
                continue;

            var legacy = parts[legacyIndex].Trim();
            var target = parts[targetIndex].Trim();
            if (string.IsNullOrWhiteSpace(legacy) || string.IsNullOrWhiteSpace(target))
                continue;

            var language = ReadPart(parts, languageIndex);
            var source = ParseLegacySource(legacy, language, hosts);
            var status = 301;
            if (statusIndex >= 0 && statusIndex < parts.Length && int.TryParse(parts[statusIndex], out var parsedStatus))
                status = parsedStatus;
            var sourceType = ReadPart(parts, sourceTypeIndex);
            var regex = ReadPart(parts, regexIndex);
            var matchType = ParseRedirectMatchType(sourceType, regex, source.Path);
            var sourcePath = matchType == LinkRedirectMatchType.Regex
                ? NormalizeRedirectRegexSource(regex, legacy)
                : source.Path;

            rows.Add(new LinkRedirectRule
            {
                Id = BuildImportedId(source, target, i),
                Enabled = true,
                SourceHost = source.Host,
                SourcePath = sourcePath,
                SourceQuery = matchType == LinkRedirectMatchType.Regex ? null : source.Query,
                MatchType = matchType == LinkRedirectMatchType.Exact && !string.IsNullOrWhiteSpace(source.Query)
                    ? LinkRedirectMatchType.Query
                    : matchType,
                TargetUrl = target,
                Status = status,
                PreserveQuery = false,
                Group = sourceType,
                Source = "imported-csv",
                Notes = ReadPart(parts, notesIndex),
                AllowExternal = IsHttpUrl(target) || target.StartsWith("/", StringComparison.Ordinal),
                CreatedBy = ReadPart(parts, sourceIdIndex),
                OriginPath = Path.GetFullPath(csvPath),
                OriginLine = i + 1
            });
        }

        return rows;
    }

    private static LinkRedirectMatchType ParseRedirectMatchType(string sourceType, string regex, string sourcePath)
    {
        if (!string.IsNullOrWhiteSpace(regex) ||
            sourceType.Equals("regex", StringComparison.OrdinalIgnoreCase))
        {
            if (LooksLikeSimpleWildcard(sourcePath))
                return LinkRedirectMatchType.Prefix;
            return LinkRedirectMatchType.Regex;
        }

        return LooksLikeSimpleWildcard(sourcePath)
            ? LinkRedirectMatchType.Prefix
            : LinkRedirectMatchType.Exact;
    }

    private static bool LooksLikeSimpleWildcard(string value)
        => !string.IsNullOrWhiteSpace(value) &&
           value.EndsWith('*') &&
           !value.Contains(".*", StringComparison.Ordinal) &&
           !value.StartsWith("?", StringComparison.Ordinal);

    private static string NormalizeRedirectRegexSource(string regex, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(regex) ? fallback : regex;
        value = value.Trim();
        if (value.StartsWith("/", StringComparison.Ordinal) && value.Length > 1)
            value = value.TrimStart('/');
        return value;
    }

    private static LinkLegacySource ParseLegacySource(string value, string? language, IReadOnlyDictionary<string, string>? hosts)
    {
        var trimmed = value.Trim();
        string? host = null;
        string path = trimmed;
        string? query = null;

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            host = uri.Host;
            path = string.IsNullOrWhiteSpace(uri.AbsolutePath) ? "/" : uri.AbsolutePath;
            query = uri.Query.TrimStart('?');
        }
        else if (!string.IsNullOrWhiteSpace(language) &&
                 hosts is not null &&
                 hosts.TryGetValue(language, out var mappedHost) &&
                 !string.IsNullOrWhiteSpace(mappedHost))
        {
            host = mappedHost;
        }

        var hashIndex = path.IndexOf('#');
        if (hashIndex >= 0)
            path = path.Substring(0, hashIndex);

        var queryIndex = path.IndexOf('?');
        if (queryIndex >= 0)
        {
            query = path[(queryIndex + 1)..];
            path = path.Substring(0, queryIndex);
        }

        if (string.IsNullOrWhiteSpace(path))
            path = "/";
        if (!path.StartsWith("/", StringComparison.Ordinal))
            path = "/" + path.TrimStart('/');

        return new LinkLegacySource(host, NormalizeSourcePath(path), string.IsNullOrWhiteSpace(query) ? null : query);
    }

    private static void ValidateRedirects(
        LinkRedirectRule[] redirects,
        List<LinkValidationIssue> issues,
        IReadOnlyDictionary<string, string>? languageRootHosts)
    {
        var seen = new Dictionary<string, LinkRedirectRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var redirect in redirects)
        {
            var label = string.IsNullOrWhiteSpace(redirect.Id) ? redirect.SourcePath : redirect.Id;
            if (string.IsNullOrWhiteSpace(redirect.SourcePath))
            {
                AddIssue(issues, LinkValidationSeverity.Error, "PFLINK.REDIRECT.SOURCE_MISSING", "Redirect source path is required.", "redirect", label);
                continue;
            }

            if (!IsAllowedStatus(redirect.Status))
                AddIssue(issues, LinkValidationSeverity.Error, "PFLINK.REDIRECT.STATUS", $"Redirect status {redirect.Status} is not supported.", "redirect", label);

            if (redirect.Status != 410 && string.IsNullOrWhiteSpace(redirect.TargetUrl))
                AddIssue(issues, LinkValidationSeverity.Error, "PFLINK.REDIRECT.TARGET_MISSING", "Redirect target URL is required unless status is 410.", "redirect", label);

            if (!string.IsNullOrWhiteSpace(redirect.TargetUrl))
                ValidateTarget(redirect.TargetUrl, redirect.AllowExternal, issues, "redirect", label, "PFLINK.REDIRECT");

            if (redirect.MatchType == LinkRedirectMatchType.Regex && IsBroadRegex(redirect.SourcePath))
                AddIssue(issues, LinkValidationSeverity.Warning, "PFLINK.REDIRECT.REGEX_BROAD", "Regex redirect looks very broad and should be reviewed.", "redirect", label);

            var key = BuildRedirectKey(redirect);
            if (seen.TryGetValue(key, out var existing))
            {
                var existingLabel = string.IsNullOrWhiteSpace(existing.Id) ? existing.SourcePath : existing.Id;
                var existingNormalizedTarget = NormalizeTargetForCompare(existing.TargetUrl, existing.SourceHost, languageRootHosts);
                var normalizedTarget = NormalizeTargetForCompare(redirect.TargetUrl, redirect.SourceHost, languageRootHosts);
                if (string.Equals(
                        existingNormalizedTarget,
                        normalizedTarget,
                        StringComparison.OrdinalIgnoreCase) &&
                    ResolveStatus(existing.Status, 301) == ResolveStatus(redirect.Status, 301))
                {
                    AddRedirectIssue(
                        issues,
                        LinkValidationSeverity.Warning,
                        "PFLINK.REDIRECT.DUPLICATE_SAME_TARGET",
                        $"Duplicate redirect source repeats '{existingLabel}' for {BuildDisplaySource(redirect)} -> {redirect.TargetUrl}.",
                        redirect,
                        existing,
                        normalizedTarget,
                        existingNormalizedTarget);
                }
                else
                {
                    AddRedirectIssue(
                        issues,
                        LinkValidationSeverity.Error,
                        "PFLINK.REDIRECT.DUPLICATE",
                        $"Duplicate redirect source conflicts with '{existingLabel}' for {BuildDisplaySource(redirect)}: '{existing.TargetUrl}' vs '{redirect.TargetUrl}'.",
                        redirect,
                        existing,
                        normalizedTarget,
                        existingNormalizedTarget);
                }
            }
            else
            {
                seen[key] = redirect;
            }
        }
    }

    private static void ValidateShortlinks(
        LinkShortlinkRule[] shortlinks,
        List<LinkValidationIssue> issues,
        IReadOnlyDictionary<string, string>? hosts)
    {
        var seen = new Dictionary<string, LinkShortlinkRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var shortlink in shortlinks)
        {
            var label = shortlink.Slug;
            if (string.IsNullOrWhiteSpace(shortlink.Slug) || !SafeSlugRegex.IsMatch(shortlink.Slug.Trim()))
            {
                AddIssue(issues, LinkValidationSeverity.Error, "PFLINK.SHORTLINK.SLUG", "Shortlink slug must be URL-safe.", "shortlink", label);
                continue;
            }

            if (!IsAllowedStatus(shortlink.Status))
                AddIssue(issues, LinkValidationSeverity.Error, "PFLINK.SHORTLINK.STATUS", $"Shortlink status {shortlink.Status} is not supported.", "shortlink", label);

            if (string.IsNullOrWhiteSpace(shortlink.TargetUrl))
                AddIssue(issues, LinkValidationSeverity.Error, "PFLINK.SHORTLINK.TARGET_MISSING", "Shortlink target URL is required.", "shortlink", label);
            else
                ValidateTarget(AppendUtm(shortlink.TargetUrl, shortlink.Utm), shortlink.AllowExternal, issues, "shortlink", label, "PFLINK.SHORTLINK");

            var key = $"{shortlink.Host ?? string.Empty}|{NormalizeShortlinkPath(shortlink, hosts)}";
            if (seen.TryGetValue(key, out var existing))
            {
                AddIssue(issues, LinkValidationSeverity.Error, "PFLINK.SHORTLINK.DUPLICATE", $"Duplicate shortlink conflicts with '{existing.Slug}'.", "shortlink", label);
            }
            else
            {
                seen[key] = shortlink;
            }

            if (string.IsNullOrWhiteSpace(shortlink.Owner))
                AddIssue(issues, LinkValidationSeverity.Warning, "PFLINK.SHORTLINK.OWNER", "Shortlink is missing an owner.", "shortlink", label);
        }
    }

    private static void ValidateRedirectGraph(LinkRedirectRule[] redirects, List<LinkValidationIssue> issues)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var redirect in redirects)
        {
            if (redirect.MatchType != LinkRedirectMatchType.Exact && redirect.MatchType != LinkRedirectMatchType.Query)
                continue;
            if (!IsLocalPath(redirect.TargetUrl))
                continue;

            var source = NormalizeSourcePath(redirect.SourcePath);
            var target = NormalizeSourcePath(redirect.TargetUrl);
            if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(target))
                map[BuildRedirectGraphKey(redirect.SourceHost, source, redirect.SourceQuery)] = target;
        }

        foreach (var redirect in redirects)
        {
            if (redirect.MatchType != LinkRedirectMatchType.Exact && redirect.MatchType != LinkRedirectMatchType.Query)
                continue;

            var host = NormalizeRedirectGraphHost(redirect.SourceHost);
            var current = NormalizeSourcePath(redirect.SourcePath);
            var currentQuery = redirect.SourceQuery;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var depth = 0;
            while (TryGetRedirectGraphTarget(map, host, current, currentQuery, out var next))
            {
                if (!visited.Add(BuildRedirectGraphKey(host, current, currentQuery)))
                {
                    AddRedirectIssue(
                        issues,
                        LinkValidationSeverity.Error,
                        "PFLINK.REDIRECT.LOOP",
                        $"Redirect loop detected starting at {BuildDisplaySource(redirect)}.",
                        redirect,
                        normalizedTarget: NormalizeSourcePath(redirect.TargetUrl));
                    break;
                }

                current = next;
                currentQuery = string.Empty;
                depth++;
                if (depth > 5)
                {
                    AddRedirectIssue(
                        issues,
                        LinkValidationSeverity.Error,
                        "PFLINK.REDIRECT.CHAIN",
                        $"Redirect chain is longer than 5 hops starting at {BuildDisplaySource(redirect)}.",
                        redirect,
                        normalizedTarget: NormalizeSourcePath(redirect.TargetUrl));
                    break;
                }
            }
        }
    }

    private static bool TryGetRedirectGraphTarget(
        IReadOnlyDictionary<string, string> map,
        string host,
        string path,
        string? query,
        out string target)
    {
        if (!string.IsNullOrWhiteSpace(host) &&
            map.TryGetValue(BuildRedirectGraphKey(host, path, query), out target!))
        {
            return true;
        }

        return map.TryGetValue(BuildRedirectGraphKey(null, path, query), out target!);
    }

    private static void ValidateTarget(string targetUrl, bool allowExternal, List<LinkValidationIssue> issues, string source, string? id, string codePrefix)
    {
        var trimmed = targetUrl.Trim();
        if (trimmed.StartsWith("/", StringComparison.Ordinal))
            return;

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            AddIssue(issues, LinkValidationSeverity.Error, codePrefix + ".TARGET_INVALID", "Target URL must be absolute or a local root-relative path.", source, id);
            return;
        }

        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            AddIssue(issues, LinkValidationSeverity.Error, codePrefix + ".TARGET_PROTOCOL", "Target URL must use http or https.", source, id);
            return;
        }

        if (!allowExternal)
            AddIssue(issues, LinkValidationSeverity.Error, codePrefix + ".TARGET_EXTERNAL", "External target requires allowExternal: true.", source, id);
    }

    private static void AddIssue(List<LinkValidationIssue> issues, LinkValidationSeverity severity, string code, string message, string source, string? id)
        => issues.Add(new LinkValidationIssue
        {
            Severity = severity,
            Code = code,
            Message = message,
            Source = source,
            Id = id
        });

    private static void AddRedirectIssue(
        List<LinkValidationIssue> issues,
        LinkValidationSeverity severity,
        string code,
        string message,
        LinkRedirectRule redirect,
        LinkRedirectRule? related = null,
        string? normalizedTarget = null,
        string? relatedNormalizedTarget = null)
        => issues.Add(new LinkValidationIssue
        {
            Severity = severity,
            Code = code,
            Message = message,
            Source = "redirect",
            Id = string.IsNullOrWhiteSpace(redirect.Id) ? redirect.SourcePath : redirect.Id,
            RelatedId = related is null ? null : (string.IsNullOrWhiteSpace(related.Id) ? related.SourcePath : related.Id),
            SourceHost = redirect.SourceHost,
            SourcePath = redirect.SourcePath,
            SourceQuery = redirect.SourceQuery,
            TargetUrl = redirect.TargetUrl,
            RelatedTargetUrl = related?.TargetUrl,
            NormalizedTargetUrl = normalizedTarget,
            RelatedNormalizedTargetUrl = relatedNormalizedTarget,
            Status = ResolveStatus(redirect.Status, 301),
            RelatedStatus = related is null ? 0 : ResolveStatus(related.Status, 301),
            OriginPath = redirect.OriginPath,
            OriginLine = redirect.OriginLine,
            RelatedOriginPath = related?.OriginPath,
            RelatedOriginLine = related?.OriginLine ?? 0
        });

    private static string BuildRedirectKey(LinkRedirectRule redirect)
        => string.Join("|",
            redirect.SourceHost ?? string.Empty,
            redirect.MatchType.ToString(),
            NormalizeSourcePath(redirect.SourcePath),
            redirect.SourceQuery ?? string.Empty);

    private static string BuildRedirectGraphKey(string? host, string path, string? query)
        => string.Join("|", NormalizeRedirectGraphHost(host), NormalizeSourcePath(path), query?.Trim().ToLowerInvariant() ?? string.Empty);

    private static string NormalizeRedirectGraphHost(string? host)
        => string.IsNullOrWhiteSpace(host) ? string.Empty : host.Trim().ToLowerInvariant();

    private static bool IsAllowedStatus(int status)
        => status is 301 or 302 or 307 or 308 or 410;

    private static bool IsBroadRegex(string pattern)
    {
        var trimmed = pattern.Trim();
        return trimmed is ".*" or "^.*" or "^.*$" or "(.*)" or "^(.*)$" || trimmed.Length < 3;
    }

    private static int ResolveStatus(int status, int defaultStatus)
        => status <= 0 ? defaultStatus : status;

    private static int MatchTypeOrder(LinkRedirectMatchType matchType)
        => matchType switch
        {
            LinkRedirectMatchType.Exact => 0,
            LinkRedirectMatchType.Query => 1,
            LinkRedirectMatchType.Prefix => 2,
            LinkRedirectMatchType.Regex => 3,
            _ => 10
        };

    private static int SourceRank(string? source)
    {
        var normalized = source?.Trim() ?? string.Empty;
        if (normalized.Equals("manual", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (normalized.Equals("shortlink", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (normalized.StartsWith("generated", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (normalized.StartsWith("imported", StringComparison.OrdinalIgnoreCase))
            return 3;
        return 5;
    }

    private static string NormalizeSourcePath(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return string.Empty;

        var trimmed = source.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            trimmed = uri.AbsolutePath;
        }

        var queryIndex = trimmed.IndexOf('?');
        if (queryIndex >= 0)
            trimmed = trimmed.Substring(0, queryIndex);
        var hashIndex = trimmed.IndexOf('#');
        if (hashIndex >= 0)
            trimmed = trimmed.Substring(0, hashIndex);

        if (!trimmed.StartsWith("/", StringComparison.Ordinal))
            trimmed = "/" + trimmed.TrimStart('/');
        return trimmed.Length > 1 ? trimmed.TrimEnd('/') : trimmed;
    }

    private static string NormalizeDestination(string? destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
            return string.Empty;

        var trimmed = destination.Trim();
        if (IsHttpUrl(trimmed))
            return trimmed;
        return trimmed.StartsWith("/", StringComparison.Ordinal) ? trimmed : "/" + trimmed.TrimStart('/');
    }

    private static string NormalizeTargetForCompare(
        string? targetUrl,
        string? sourceHost,
        IReadOnlyDictionary<string, string>? languageRootHosts = null)
    {
        if (string.IsNullOrWhiteSpace(targetUrl))
            return string.Empty;

        var normalized = NormalizeDestination(targetUrl);
        if (!IsHttpUrl(normalized))
            return NormalizeLanguageRootPath(NormalizeSourcePath(normalized), sourceHost, languageRootHosts);

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            return normalized;

        if (!string.IsNullOrWhiteSpace(sourceHost) &&
            uri.Host.Equals(sourceHost.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeLanguageRootPath(NormalizeSourcePath(uri.AbsolutePath), sourceHost, languageRootHosts);
        }

        return normalized.TrimEnd('/');
    }

    private static string NormalizeLanguageRootPath(
        string path,
        string? sourceHost,
        IReadOnlyDictionary<string, string>? languageRootHosts)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            string.IsNullOrWhiteSpace(sourceHost) ||
            languageRootHosts is null ||
            !languageRootHosts.TryGetValue(sourceHost.Trim(), out var language) ||
            string.IsNullOrWhiteSpace(language))
        {
            return path;
        }

        return NormalizeSourcePath(StripLanguageRootPrefix(path, language));
    }

    private static string StripLanguageRootPrefix(string path, string language)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(language))
            return path;

        var prefix = "/" + language.Trim().Trim('/') + "/";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return path.Equals(prefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) ? "/" : path;

        var rebased = "/" + path[prefix.Length..].TrimStart('/');
        return string.IsNullOrWhiteSpace(rebased) ? "/" : rebased;
    }

    private static IReadOnlyDictionary<string, string> NormalizeHostMap(IReadOnlyDictionary<string, string>? hosts)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (hosts is null)
            return normalized;

        foreach (var pair in hosts)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                continue;

            normalized[pair.Key.Trim()] = pair.Value.Trim();
        }

        return normalized;
    }

    private static IReadOnlyDictionary<string, string> NormalizeLanguageRootHosts(IReadOnlyDictionary<string, string>? hosts)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (hosts is null)
            return normalized;

        foreach (var pair in hosts)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                continue;

            normalized[pair.Key.Trim()] = pair.Value.Trim().Trim('/');
        }

        return normalized;
    }

    private static string BuildDisplaySource(LinkRedirectRule redirect)
    {
        var host = string.IsNullOrWhiteSpace(redirect.SourceHost) ? "*" : redirect.SourceHost.Trim();
        var path = string.IsNullOrWhiteSpace(redirect.SourcePath) ? "/" : redirect.SourcePath.Trim();
        var query = string.IsNullOrWhiteSpace(redirect.SourceQuery) ? string.Empty : "?" + redirect.SourceQuery.Trim().TrimStart('?');
        return $"{host}{path}{query}";
    }

    private static bool IsLocalPath(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Trim().StartsWith("/", StringComparison.Ordinal);

    private static bool IsHttpUrl(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    private static string NormalizeShortlinkPath(LinkShortlinkRule shortlink, IReadOnlyDictionary<string, string>? hosts)
    {
        var slug = shortlink.Slug.Trim().Trim('/');
        var prefix = shortlink.PathPrefix;
        if (string.IsNullOrWhiteSpace(prefix))
        {
            var shortHost = ResolveShortHost(hosts);
            prefix = !string.IsNullOrWhiteSpace(shortlink.Host) &&
                     !string.IsNullOrWhiteSpace(shortHost) &&
                     shortlink.Host.Equals(shortHost, StringComparison.OrdinalIgnoreCase)
                ? "/"
                : "/go";
        }

        prefix = prefix.Trim();
        if (!prefix.StartsWith("/", StringComparison.Ordinal))
            prefix = "/" + prefix.TrimStart('/');
        prefix = prefix.TrimEnd('/');
        return string.IsNullOrWhiteSpace(prefix) ? "/" + slug : prefix + "/" + slug;
    }

    private static string? ResolveShortHost(IReadOnlyDictionary<string, string>? hosts)
    {
        if (hosts is null || hosts.Count == 0)
            return null;

        return hosts.TryGetValue("short", out var shortHost) && !string.IsNullOrWhiteSpace(shortHost)
            ? shortHost
            : null;
    }

    private static string AppendUtm(string targetUrl, string? utm)
    {
        if (string.IsNullOrWhiteSpace(targetUrl) || string.IsNullOrWhiteSpace(utm))
            return targetUrl;

        var separator = targetUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return targetUrl + separator + utm.Trim().TrimStart('?').TrimStart('&');
    }

    private static string BuildImportedId(LinkLegacySource source, string target, int row)
    {
        var raw = $"{source.Host}|{source.Path}|{source.Query}|{target}|{row}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw.ToLowerInvariant()));
        return "imported-" + Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
    }

    private static int FindHeader(string[] header, params string[] names)
    {
        for (var i = 0; i < header.Length; i++)
        {
            foreach (var name in names)
            {
                if (header[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }

        return -1;
    }

    private static string ReadPart(string[] parts, int index)
        => index >= 0 && index < parts.Length ? parts[index].Trim() : string.Empty;

    private static string[] SplitCsvLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return Array.Empty<string>();

        var values = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"' && (i + 1 >= line.Length || line[i + 1] != '"'))
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
            {
                sb.Append('"');
                i++;
                continue;
            }

            if (c == ',' && !inQuotes)
            {
                values.Add(sb.ToString().Trim());
                sb.Clear();
                continue;
            }

            sb.Append(c);
        }

        values.Add(sb.ToString().Trim());
        return values.ToArray();
    }
}

internal readonly record struct LinkLegacySource(string? Host, string Path, string? Query);
