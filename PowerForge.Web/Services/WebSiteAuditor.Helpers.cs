using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlTinkerX;

namespace PowerForge.Web;

/// <summary>Audits generated HTML output using static checks.</summary>
public static partial class WebSiteAuditor
{
    private static IEnumerable<string> EnumerateHtmlFiles(string root, string[] includePatterns, string[] excludePatterns, bool useDefaultExcludes)
    {
        var includes = NormalizePatterns(includePatterns);
        var excludes = BuildExcludePatterns(excludePatterns, useDefaultExcludes);
        var files = Directory.EnumerateFiles(root, "*.html", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(root, "*.htm", SearchOption.AllDirectories));

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (excludes.Length > 0 && MatchesAny(excludes, relative))
                continue;
            if (includes.Length > 0 && !MatchesAny(includes, relative))
                continue;
            yield return file;
        }
    }

    private static string[] NormalizePatterns(string[] patterns)
    {
        return patterns
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Replace('\\', '/').Trim())
            .ToArray();
    }

    private static string[] BuildExcludePatterns(string[] patterns, bool useDefaults)
    {
        var list = NormalizePatterns(patterns).ToList();
        if (useDefaults)
        {
            list.AddRange(DefaultExcludePatterns);
        }
        return list
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool MatchesAny(string[] patterns, string value)
    {
        foreach (var pattern in patterns)
        {
            if (GlobMatch(pattern, value))
                return true;
        }
        return false;
    }

    private static bool GlobMatch(string pattern, string value)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        var regex = "^" + Regex.Escape(pattern)
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*") + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase);
    }

    private static bool ShouldSkipLink(string href)
    {
        if (string.IsNullOrWhiteSpace(href)) return true;
        foreach (var prefix in IgnoreLinkPrefixes)
        {
            if (href.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsExternalLink(string href)
    {
        if (string.IsNullOrWhiteSpace(href)) return false;
        if (href.StartsWith("//")) return true;
        return href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               href.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripQueryAndFragment(string href)
    {
        var trimmed = href.Trim();
        var queryIndex = trimmed.IndexOf('?');
        if (queryIndex >= 0)
            trimmed = trimmed.Substring(0, queryIndex);
        var hashIndex = trimmed.IndexOf('#');
        if (hashIndex >= 0)
            trimmed = trimmed.Substring(0, hashIndex);
        return trimmed;
    }

    private static bool TryResolveLocalTarget(string siteRoot, string baseDir, string href, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(href)) return false;

        var cleaned = StripQueryAndFragment(href);
        if (string.IsNullOrWhiteSpace(cleaned)) return false;

        var isRooted = cleaned.StartsWith("/");
        var isExplicitDir = cleaned.EndsWith("/");
        var relative = isRooted ? cleaned.TrimStart('/') : cleaned;
        relative = relative.Replace('/', Path.DirectorySeparatorChar);

        var candidateBase = isRooted ? siteRoot : baseDir;
        var candidate = Path.GetFullPath(Path.Combine(candidateBase, relative));
        if (!candidate.StartsWith(siteRoot, StringComparison.OrdinalIgnoreCase))
            return false;

        if (isExplicitDir)
        {
            var dir = candidate.TrimEnd(Path.DirectorySeparatorChar);
            var indexPath = Path.Combine(dir, "index.html");
            if (File.Exists(indexPath))
            {
                resolvedPath = indexPath;
                return true;
            }

            resolvedPath = indexPath;
            return true;
        }

        if (Path.HasExtension(candidate))
        {
            resolvedPath = candidate;
            return true;
        }

        foreach (var ext in DefaultHtmlExtensions)
        {
            var htmlCandidate = candidate + ext;
            if (File.Exists(htmlCandidate))
            {
                resolvedPath = htmlCandidate;
                return true;
            }
        }

        var indexCandidate = Path.Combine(candidate, "index.html");
        resolvedPath = indexCandidate;
        return true;
    }

    private static string? ResolveBaseHref(AngleSharp.Dom.IDocument doc)
    {
        var baseElement = doc.QuerySelector("base[href]");
        if (baseElement is null) return null;
        var href = baseElement.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(href)) return null;
        if (IsExternalLink(href)) return null;
        if (!href.StartsWith("/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return href.TrimEnd('/');
    }

    private static string BuildNavSignature(AngleSharp.Dom.IElement nav)
    {
        var tokens = new List<string>();
        foreach (var link in nav.QuerySelectorAll("a"))
        {
            var href = link.GetAttribute("href") ?? string.Empty;
            var text = link.TextContent?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                text = link.GetAttribute("aria-label") ?? link.GetAttribute("title") ?? string.Empty;
            tokens.Add($"{href.Trim()}|{text.Trim()}");
        }
        return string.Join("||", tokens);
    }

    private static WebAuditNavProfile[] NormalizeNavProfiles(WebAuditNavProfile[]? profiles)
    {
        if (profiles is null || profiles.Length == 0)
            return Array.Empty<WebAuditNavProfile>();

        return profiles
            .Where(profile => profile is not null && !string.IsNullOrWhiteSpace(profile.Match))
            .Select(profile => new WebAuditNavProfile
            {
                Match = profile.Match.Replace('\\', '/').Trim(),
                Selector = string.IsNullOrWhiteSpace(profile.Selector) ? null : profile.Selector.Trim(),
                Required = profile.Required,
                RequiredLinks = (profile.RequiredLinks ?? Array.Empty<string>())
                    .Where(link => !string.IsNullOrWhiteSpace(link))
                    .Select(NormalizeNavHref)
                    .Where(link => !string.IsNullOrWhiteSpace(link))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Ignore = profile.Ignore
            })
            .OrderByDescending(profile => profile.Match.Length)
            .ToArray();
    }

    private static WebAuditNavProfile? ResolveNavProfile(string relativePath, WebAuditNavProfile[] profiles)
    {
        if (profiles.Length == 0 || string.IsNullOrWhiteSpace(relativePath))
            return null;

        var normalizedPath = relativePath.Replace('\\', '/').TrimStart('/');
        foreach (var profile in profiles)
        {
            if (GlobMatch(profile.Match, normalizedPath))
                return profile;
        }

        return null;
    }

    private static string[] MergeRequiredNavLinks(string[] defaultRequiredLinks, WebAuditNavProfile? profile)
    {
        if (profile is null || profile.RequiredLinks.Length == 0)
            return defaultRequiredLinks;

        return defaultRequiredLinks
            .Concat(profile.RequiredLinks)
            .Where(link => !string.IsNullOrWhiteSpace(link))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildNavScopeKey(WebAuditNavProfile? profile, string selector)
    {
        var profileMatch = profile is null || string.IsNullOrWhiteSpace(profile.Match)
            ? "__default__"
            : profile.Match;
        var navSelector = string.IsNullOrWhiteSpace(selector)
            ? "nav"
            : selector.Trim();
        return profileMatch + "|" + navSelector;
    }

    private static void ValidateHeadingOrder(
        AngleSharp.Dom.IDocument doc,
        string relativePath,
        Action<string, string, string?, string, string?> addIssue)
    {
        var headings = doc.QuerySelectorAll("h1,h2,h3,h4,h5,h6")
            .Select(heading => new
            {
                Element = heading,
                Level = ParseHeadingLevel(heading.TagName)
            })
            .Where(entry => entry.Level > 0 && !IsElementHidden(entry.Element))
            .ToList();

        if (headings.Count < 2)
            return;

        var previousLevel = headings[0].Level;
        for (var index = 1; index < headings.Count; index++)
        {
            var current = headings[index];
            if (current.Level <= previousLevel + 1)
            {
                previousLevel = current.Level;
                continue;
            }

            var text = NormalizeHeadingText(current.Element.TextContent);
            var label = string.IsNullOrWhiteSpace(text)
                ? $"h{current.Level}"
                : $"h{current.Level} \"{text}\"";
            addIssue("warning", "heading-order", relativePath,
                $"heading order skips levels (h{previousLevel} -> h{current.Level}) near {label}.",
                $"heading-order:{index}:{previousLevel}:{current.Level}");
            previousLevel = current.Level;
        }
    }

    private static void ValidateLinkPurposeConsistency(
        AngleSharp.Dom.IDocument doc,
        string relativePath,
        Action<string, string, string?, string, string?> addIssue)
    {
        var labelTargets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var labelDisplay = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var anchor in doc.QuerySelectorAll("a[href]"))
        {
            var href = anchor.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href) || ShouldSkipLink(href))
                continue;

            var label = GetAccessibleLinkLabel(anchor);
            if (string.IsNullOrWhiteSpace(label))
                continue;

            var destination = NormalizeLinkPurposeDestination(href);
            if (string.IsNullOrWhiteSpace(destination))
                continue;

            var normalizedLabel = label.Trim();
            if (!labelTargets.TryGetValue(normalizedLabel, out var targets))
            {
                targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                labelTargets[normalizedLabel] = targets;
                labelDisplay[normalizedLabel] = normalizedLabel;
            }
            targets.Add(destination);
        }

        foreach (var pair in labelTargets)
        {
            if (pair.Value.Count <= 1)
                continue;

            var targets = pair.Value
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var label = labelDisplay.TryGetValue(pair.Key, out var value)
                ? value
                : pair.Key;

            addIssue("warning", "link-purpose", relativePath,
                $"link label '{label}' points to multiple destinations: {string.Join(", ", targets)}.",
                $"link-purpose:{label}:{string.Join("|", targets)}");
        }
    }

    private static int ParseHeadingLevel(string? tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName) || tagName.Length != 2)
            return 0;
        if (tagName[0] != 'H' && tagName[0] != 'h')
            return 0;
        return tagName[1] switch
        {
            '1' => 1,
            '2' => 2,
            '3' => 3,
            '4' => 4,
            '5' => 5,
            '6' => 6,
            _ => 0
        };
    }

    private static bool IsElementHidden(AngleSharp.Dom.IElement element)
    {
        if (element.HasAttribute("hidden"))
            return true;

        var ariaHidden = element.GetAttribute("aria-hidden");
        return !string.IsNullOrWhiteSpace(ariaHidden) &&
               ariaHidden.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeHeadingText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return Regex.Replace(value, "\\s+", " ").Trim();
    }

    private static string GetAccessibleLinkLabel(AngleSharp.Dom.IElement anchor)
    {
        var ariaLabel = anchor.GetAttribute("aria-label");
        if (!string.IsNullOrWhiteSpace(ariaLabel))
            return Regex.Replace(ariaLabel, "\\s+", " ").Trim();

        var text = anchor.TextContent;
        if (!string.IsNullOrWhiteSpace(text))
            return Regex.Replace(text, "\\s+", " ").Trim();

        var title = anchor.GetAttribute("title");
        if (!string.IsNullOrWhiteSpace(title))
            return Regex.Replace(title, "\\s+", " ").Trim();

        return string.Empty;
    }

    private static string NormalizeLinkPurposeDestination(string href)
    {
        var normalized = StripQueryAndFragment(href).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        if (normalized.StartsWith("//", StringComparison.Ordinal))
            normalized = "https:" + normalized;

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             absolute.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            var path = string.IsNullOrWhiteSpace(absolute.AbsolutePath) ? "/" : absolute.AbsolutePath;
            if (path.Length > 1 && path.EndsWith("/", StringComparison.Ordinal))
                path = path.TrimEnd('/');
            return $"{absolute.Scheme}://{absolute.Host}{(absolute.IsDefaultPort ? string.Empty : ":" + absolute.Port)}{path}";
        }

        return NormalizeNavHref(normalized);
    }

    private static void ValidateNetworkHints(
        AngleSharp.Dom.IDocument doc,
        string relativePath,
        Action<string, string, string?, string, string?> addIssue)
    {
        var head = doc.Head;
        if (head is null)
            return;

        var requiredOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in head.QuerySelectorAll("link[href]"))
        {
            var rel = (link.GetAttribute("rel") ?? string.Empty).Trim();
            if (!ContainsRelToken(rel, "stylesheet"))
                continue;

            var href = link.GetAttribute("href");
            if (TryGetExternalOrigin(href, out var origin))
                requiredOrigins.Add(origin);
        }

        foreach (var script in head.QuerySelectorAll("script[src]"))
        {
            var src = script.GetAttribute("src");
            if (TryGetExternalOrigin(src, out var origin))
                requiredOrigins.Add(origin);
        }

        var externalImageOriginCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var img in doc.QuerySelectorAll("img[src]"))
        {
            var src = img.GetAttribute("src");
            if (!TryGetExternalOrigin(src, out var origin))
                continue;
            externalImageOriginCounts.TryGetValue(origin, out var count);
            externalImageOriginCounts[origin] = count + 1;
        }
        foreach (var srcset in doc.QuerySelectorAll("img[srcset],source[srcset]")
                     .Select(element => element.GetAttribute("srcset"))
                     .Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            foreach (var candidate in ParseSrcSet(srcset!))
            {
                if (!TryGetExternalOrigin(candidate, out var origin))
                    continue;
                externalImageOriginCounts.TryGetValue(origin, out var count);
                externalImageOriginCounts[origin] = count + 1;
            }
        }
        foreach (var pair in externalImageOriginCounts)
        {
            if (pair.Value >= 2 || pair.Key.Contains("img.shields.io", StringComparison.OrdinalIgnoreCase))
                requiredOrigins.Add(pair.Key);
        }

        if (requiredOrigins.Contains("https://fonts.googleapis.com"))
            requiredOrigins.Add("https://fonts.gstatic.com");

        if (requiredOrigins.Count == 0)
            return;

        var hintedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in head.QuerySelectorAll("link[rel][href]"))
        {
            var rel = (link.GetAttribute("rel") ?? string.Empty).Trim();
            if (!ContainsRelToken(rel, "preconnect") && !ContainsRelToken(rel, "dns-prefetch"))
                continue;

            var href = link.GetAttribute("href");
            if (TryGetExternalOrigin(href, out var origin))
                hintedOrigins.Add(origin);
        }

        var missing = requiredOrigins
            .Where(origin => !hintedOrigins.Contains(origin))
            .OrderBy(origin => origin, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (missing.Length == 0)
            return;

        addIssue("warning", "network-hint", relativePath,
            $"missing preconnect/dns-prefetch for external origins: {string.Join(", ", missing)}.",
            "network-hints");
    }

    private static void ValidateHeadRenderBlocking(
        AngleSharp.Dom.IDocument doc,
        string relativePath,
        int maxHeadBlockingResources,
        Action<string, string, string?, string, string?> addIssue)
    {
        if (maxHeadBlockingResources <= 0)
            return;

        var head = doc.Head;
        if (head is null)
            return;

        var blockingStyles = head.QuerySelectorAll("link[rel][href]")
            .Count(link =>
            {
                var rel = (link.GetAttribute("rel") ?? string.Empty).Trim();
                if (!ContainsRelToken(rel, "stylesheet"))
                    return false;

                var media = (link.GetAttribute("media") ?? string.Empty).Trim();
                return string.IsNullOrWhiteSpace(media) ||
                       media.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                       media.Equals("screen", StringComparison.OrdinalIgnoreCase);
            });

        var blockingScripts = head.QuerySelectorAll("script[src]")
            .Count(script =>
            {
                var hasAsync = script.HasAttribute("async");
                var hasDefer = script.HasAttribute("defer");
                var type = (script.GetAttribute("type") ?? string.Empty).Trim();
                var isModule = type.Equals("module", StringComparison.OrdinalIgnoreCase);
                return !hasAsync && !hasDefer && !isModule;
            });

        var totalBlocking = blockingStyles + blockingScripts;
        if (totalBlocking <= maxHeadBlockingResources)
            return;

        addIssue("warning", "render-blocking", relativePath,
            $"head includes {totalBlocking} render-blocking resources (styles {blockingStyles}, scripts {blockingScripts}); max is {maxHeadBlockingResources}.",
            "head-render-blocking");
    }

    private static bool ContainsRelToken(string relValue, string token)
    {
        if (string.IsNullOrWhiteSpace(relValue) || string.IsNullOrWhiteSpace(token))
            return false;

        var parts = relValue.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.Equals(token, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool TryGetExternalOrigin(string? value, out string origin)
    {
        origin = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var candidate = value.Trim();
        if (candidate.StartsWith("//", StringComparison.Ordinal))
            candidate = "https:" + candidate;

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            return false;
        if (string.IsNullOrWhiteSpace(uri.Host))
            return false;
        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        origin = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return !string.IsNullOrWhiteSpace(origin);
    }

    private static string NormalizeNavHref(string? href)
    {
        if (string.IsNullOrWhiteSpace(href)) return string.Empty;
        var normalized = StripQueryAndFragment(href).Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return string.Empty;

        if (IsExternalLink(normalized))
            return normalized;

        if (!normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = "/" + normalized;

        if (normalized.Length > 1 && normalized.EndsWith("/", StringComparison.Ordinal))
            normalized = normalized.TrimEnd('/');

        return normalized;
    }

    private static string[] ResolveRequiredRouteCandidates(string route)
    {
        var normalized = StripQueryAndFragment(route).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return Array.Empty<string>();
        if (IsExternalLink(normalized))
            return Array.Empty<string>();

        normalized = normalized.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = normalized.TrimStart('/');

        if (string.IsNullOrWhiteSpace(normalized))
            return new[] { "index.html" };

        var candidates = new List<string>();
        if (normalized.EndsWith("/", StringComparison.Ordinal))
        {
            var basePath = normalized.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(basePath))
            {
                candidates.Add("index.html");
            }
            else
            {
                candidates.Add(basePath + "/index.html");
                candidates.Add(basePath + ".html");
            }
        }
        else if (normalized.EndsWith("index.html", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(normalized);
        }
        else if (Path.HasExtension(normalized))
        {
            candidates.Add(normalized);
        }
        else
        {
            candidates.Add(normalized + "/index.html");
            candidates.Add(normalized + ".html");
        }

        return candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate => candidate.TrimStart('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
