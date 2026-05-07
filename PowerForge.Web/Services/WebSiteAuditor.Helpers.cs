using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HtmlTinkerX;

namespace PowerForge.Web;

/// <summary>Audits generated HTML output using static checks.</summary>
public static partial class WebSiteAuditor
{
    private static readonly Regex HreflangTokenPattern = new(
        "^(x-default|[a-z]{2,3}(?:-[a-z0-9]{2,8})*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LanguagePathSegmentPattern = new(
        "^[a-z]{2,3}(?:-[a-z0-9]{2,8})*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> ReservedLanguagePathSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "api",
        "app",
        "rss",
        "www"
    };
    private static readonly Regex EscapedMediaTagPattern = new(
        "&lt;\\s*(img|iframe|video|source|picture)\\b(?=[^&]*?=)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex HtmlAttributePattern = new(
        "(?<name>[a-zA-Z_:][-a-zA-Z0-9_:.]*)\\s*=\\s*(?<quote>[\"'])(?<value>.*?)\\k<quote>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
    private const int LinkPurposeRedirectDepthLimit = 8;

    private static int CountAllFiles(string root, int stopAfter, string[] budgetExcludePatterns, bool useDefaultExcludes, out bool truncated)
    {
        // Best-effort: avoid full traversal when auditing just wants a budget check.
        var count = 0;
        truncated = false;
        var excludes = BuildExcludePatterns(budgetExcludePatterns ?? Array.Empty<string>(), useDefaultExcludes);
        var hasExcludes = excludes.Length > 0;
        try
        {
            foreach (var _ in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (hasExcludes)
                {
                    var relative = Path.GetRelativePath(root, _).Replace('\\', '/');
                    if (MatchesAny(excludes, relative))
                        continue;
                }
                count++;
                if (stopAfter > 0 && count > stopAfter)
                {
                    truncated = true;
                    break;
                }
            }
        }
        catch
        {
            // ignore IO errors; this is a best-effort metric
        }

        return count;
    }

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

            if (TryResolveLocalizedAliasTarget(siteRoot, baseDir, relative, isDirectoryHint: true, out var localizedIndexPath))
            {
                resolvedPath = localizedIndexPath;
                return true;
            }

            resolvedPath = indexPath;
            return true;
        }

        if (Path.HasExtension(candidate))
        {
            if (!File.Exists(candidate) &&
                TryResolveLocalizedAliasTarget(siteRoot, baseDir, relative, isDirectoryHint: false, out var localizedFilePath))
            {
                resolvedPath = localizedFilePath;
                return true;
            }

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
        if (!File.Exists(indexCandidate) &&
            TryResolveLocalizedAliasTarget(siteRoot, baseDir, relative, isDirectoryHint: true, out var localizedIndexFallback))
        {
            resolvedPath = localizedIndexFallback;
            return true;
        }

        resolvedPath = indexCandidate;
        return true;
    }

    private static bool TryResolveLocalizedAliasTarget(
        string siteRoot,
        string baseDir,
        string relative,
        bool isDirectoryHint,
        out string resolvedPath)
    {
        resolvedPath = string.Empty;
        var languagePrefix = TryResolveAuditLanguagePrefix(siteRoot, baseDir);
        if (string.IsNullOrWhiteSpace(languagePrefix) || string.IsNullOrWhiteSpace(relative))
            return false;

        var combinedRelative = Path.Combine(languagePrefix, relative.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var candidate = Path.GetFullPath(Path.Combine(siteRoot, combinedRelative));
        if (!candidate.StartsWith(siteRoot, StringComparison.OrdinalIgnoreCase))
            return false;

        if (isDirectoryHint)
        {
            var indexPath = Path.Combine(candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), "index.html");
            if (File.Exists(indexPath))
            {
                resolvedPath = indexPath;
                return true;
            }

            return false;
        }

        if (File.Exists(candidate))
        {
            resolvedPath = candidate;
            return true;
        }

        if (Path.HasExtension(candidate))
            return false;

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
        if (File.Exists(indexCandidate))
        {
            resolvedPath = indexCandidate;
            return true;
        }

        return false;
    }

    private static string? TryResolveAuditLanguagePrefix(string siteRoot, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(siteRoot) || string.IsNullOrWhiteSpace(baseDir))
            return null;

        var relativeBase = Path.GetRelativePath(siteRoot, baseDir).Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(relativeBase) || relativeBase.StartsWith("..", StringComparison.Ordinal))
            return null;

        var slashIndex = relativeBase.IndexOf('/');
        var firstSegment = slashIndex >= 0 ? relativeBase.Substring(0, slashIndex) : relativeBase;
        return LooksLikeAuditLanguagePrefix(firstSegment) ? firstSegment : null;
    }

    private static bool LooksLikeAuditLanguagePrefix(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return false;

        return LooksLikeLanguagePathSegment(segment);
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
            var href = NormalizeNavHref(link.GetAttribute("href"));
            var text = link.TextContent?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                text = link.GetAttribute("aria-label") ?? link.GetAttribute("title") ?? string.Empty;
            tokens.Add($"{href}|{text.Trim()}");
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

    private static WebAuditMediaProfile[] NormalizeMediaProfiles(WebAuditMediaProfile[]? profiles)
    {
        if (profiles is null || profiles.Length == 0)
            return Array.Empty<WebAuditMediaProfile>();

        return profiles
            .Where(profile => profile is not null && !string.IsNullOrWhiteSpace(profile.Match))
            .Select(profile => new WebAuditMediaProfile
            {
                Match = profile.Match.Replace('\\', '/').Trim(),
                Ignore = profile.Ignore,
                AllowYoutubeStandardHost = profile.AllowYoutubeStandardHost,
                RequireIframeLazy = profile.RequireIframeLazy,
                RequireIframeTitle = profile.RequireIframeTitle,
                RequireIframeReferrerPolicy = profile.RequireIframeReferrerPolicy,
                RequireImageLoadingHint = profile.RequireImageLoadingHint,
                RequireImageDecodingHint = profile.RequireImageDecodingHint,
                RequireImageDimensions = profile.RequireImageDimensions,
                RequireImageSrcSetSizes = profile.RequireImageSrcSetSizes,
                MaxEagerImages = profile.MaxEagerImages
            })
            .OrderByDescending(profile => profile.Match.Length)
            .ToArray();
    }

    private static WebAuditMediaProfile? ResolveMediaProfile(string relativePath, WebAuditMediaProfile[] profiles)
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
        var headings = EnumerateHeadingCandidates(doc)
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

    private static void ValidateSeoMetadata(
        AngleSharp.Dom.IDocument doc,
        string rawHtml,
        string relativePath,
        Action<string, string, string?, string, string?> addIssue)
    {
        if (doc.Head is null)
            return;

        if (HasNoIndexRobots(doc, rawHtml))
            return;

        var canonicalLinks = GetHeadLinkValues(doc, "canonical", rawHtml);
        if (canonicalLinks.Length == 0)
        {
            addIssue("warning", "seo", relativePath,
                "missing canonical link (<link rel=\"canonical\" ...>).",
                "seo-missing-canonical");
        }
        else if (canonicalLinks.Length > 1)
        {
            addIssue("warning", "seo", relativePath,
                $"duplicate canonical links detected ({canonicalLinks.Length}).",
                "seo-duplicate-canonical");
        }
        else if (canonicalLinks.Length == 1 && !IsAbsoluteHttpUrl(canonicalLinks[0]))
        {
            addIssue("warning", "seo", relativePath,
                "canonical link should be an absolute http(s) URL.",
                "seo-canonical-absolute");
        }

        var ogTitle = GetMetaPropertyValues(doc, "og:title", rawHtml);
        if (ogTitle.Length == 0)
            addIssue("warning", "seo", relativePath, "missing og:title meta tag.", "seo-missing-og-title");
        if (ogTitle.Length > 1)
            addIssue("warning", "seo", relativePath, $"duplicate og:title tags detected ({ogTitle.Length}).", "seo-duplicate-og-title");

        var ogDescription = GetMetaPropertyValues(doc, "og:description", rawHtml);
        if (ogDescription.Length == 0)
            addIssue("warning", "seo", relativePath, "missing og:description meta tag.", "seo-missing-og-description");
        if (ogDescription.Length > 1)
            addIssue("warning", "seo", relativePath, $"duplicate og:description tags detected ({ogDescription.Length}).", "seo-duplicate-og-description");

        var ogUrl = GetMetaPropertyValues(doc, "og:url", rawHtml);
        if (ogUrl.Length == 0)
            addIssue("warning", "seo", relativePath, "missing og:url meta tag.", "seo-missing-og-url");
        if (ogUrl.Length > 1)
            addIssue("warning", "seo", relativePath, $"duplicate og:url tags detected ({ogUrl.Length}).", "seo-duplicate-og-url");
        foreach (var value in ogUrl.Where(value => !IsAbsoluteHttpUrl(value)))
        {
            addIssue("warning", "seo", relativePath,
                $"og:url should be absolute http(s) but was '{value}'.",
                "seo-og-url-absolute");
        }

        var ogImage = GetMetaPropertyValues(doc, "og:image", rawHtml);
        if (ogImage.Length == 0)
        {
            addIssue("warning", "seo", relativePath, "missing og:image meta tag.", "seo-missing-og-image");
        }
        else
        {
            if (ogImage.Length > 1)
                addIssue("warning", "seo", relativePath, $"duplicate og:image tags detected ({ogImage.Length}).", "seo-duplicate-og-image");
            foreach (var value in ogImage.Where(value => !IsAbsoluteHttpUrl(value)))
            {
                addIssue("warning", "seo", relativePath,
                    $"og:image should be absolute http(s) but was '{value}'.",
                    "seo-og-image-absolute");
            }
        }

        var twitterCard = GetMetaNameValues(doc, "twitter:card", rawHtml);
        if (twitterCard.Length == 0)
            addIssue("warning", "seo", relativePath, "missing twitter:card meta tag.", "seo-missing-twitter-card");
        if (twitterCard.Length > 1)
            addIssue("warning", "seo", relativePath, $"duplicate twitter:card tags detected ({twitterCard.Length}).", "seo-duplicate-twitter-card");

        var twitterTitle = GetMetaNameValues(doc, "twitter:title", rawHtml);
        if (twitterTitle.Length == 0)
            addIssue("warning", "seo", relativePath, "missing twitter:title meta tag.", "seo-missing-twitter-title");
        if (twitterTitle.Length > 1)
            addIssue("warning", "seo", relativePath, $"duplicate twitter:title tags detected ({twitterTitle.Length}).", "seo-duplicate-twitter-title");

        var twitterDescription = GetMetaNameValues(doc, "twitter:description", rawHtml);
        if (twitterDescription.Length == 0)
            addIssue("warning", "seo", relativePath, "missing twitter:description meta tag.", "seo-missing-twitter-description");
        if (twitterDescription.Length > 1)
            addIssue("warning", "seo", relativePath, $"duplicate twitter:description tags detected ({twitterDescription.Length}).", "seo-duplicate-twitter-description");

        var twitterUrl = GetMetaNameValues(doc, "twitter:url", rawHtml);
        if (twitterUrl.Length == 0)
            addIssue("warning", "seo", relativePath, "missing twitter:url meta tag.", "seo-missing-twitter-url");
        if (twitterUrl.Length > 1)
            addIssue("warning", "seo", relativePath, $"duplicate twitter:url tags detected ({twitterUrl.Length}).", "seo-duplicate-twitter-url");
        foreach (var value in twitterUrl.Where(value => !IsAbsoluteHttpUrl(value)))
        {
            addIssue("warning", "seo", relativePath,
                $"twitter:url should be absolute http(s) but was '{value}'.",
                "seo-twitter-url-absolute");
        }

        var twitterImage = GetMetaNameValues(doc, "twitter:image", rawHtml);
        if (twitterImage.Length == 0)
            addIssue("warning", "seo", relativePath, "missing twitter:image meta tag.", "seo-missing-twitter-image");
        if (twitterImage.Length > 1)
            addIssue("warning", "seo", relativePath, $"duplicate twitter:image tags detected ({twitterImage.Length}).", "seo-duplicate-twitter-image");
        foreach (var value in twitterImage.Where(value => !IsAbsoluteHttpUrl(value)))
        {
            addIssue("warning", "seo", relativePath,
                $"twitter:image should be absolute http(s) but was '{value}'.",
                "seo-twitter-image-absolute");
        }

        if (canonicalLinks.Length > 0 &&
            IsAbsoluteHttpUrl(canonicalLinks[0]) &&
            ogUrl.Length > 0 &&
            IsAbsoluteHttpUrl(ogUrl[0]) &&
            !SeoUrlsMatch(canonicalLinks[0], ogUrl[0]))
        {
            addIssue("warning", "seo", relativePath,
                $"canonical URL does not match og:url ('{canonicalLinks[0]}' vs '{ogUrl[0]}').",
                "seo-canonical-ogurl-mismatch");
        }

        if (canonicalLinks.Length > 0 &&
            IsAbsoluteHttpUrl(canonicalLinks[0]) &&
            twitterUrl.Length > 0 &&
            IsAbsoluteHttpUrl(twitterUrl[0]) &&
            !SeoUrlsMatch(canonicalLinks[0], twitterUrl[0]))
        {
            addIssue("warning", "seo", relativePath,
                $"canonical URL does not match twitter:url ('{canonicalLinks[0]}' vs '{twitterUrl[0]}').",
                "seo-canonical-twitterurl-mismatch");
        }

        var hreflangLinks = doc.Head.QuerySelectorAll("link[rel][hreflang][href]")
            .Where(link => ContainsRelToken(link.GetAttribute("rel"), "alternate"))
            .Select(link => new
            {
                HrefLang = (link.GetAttribute("hreflang") ?? string.Empty).Trim(),
                Href = (link.GetAttribute("href") ?? string.Empty).Trim()
            })
            .Where(link => !string.IsNullOrWhiteSpace(link.HrefLang) && !string.IsNullOrWhiteSpace(link.Href))
            .ToArray();

        if (hreflangLinks.Length > 0)
        {
            foreach (var duplicate in hreflangLinks
                         .GroupBy(link => link.HrefLang, StringComparer.OrdinalIgnoreCase)
                         .Where(group => group.Count() > 1))
            {
                addIssue("warning", "seo", relativePath,
                    $"duplicate hreflang '{duplicate.Key}' entries detected ({duplicate.Count()}).",
                    "seo-hreflang-duplicate");
            }

            foreach (var link in hreflangLinks)
            {
                if (!HreflangTokenPattern.IsMatch(link.HrefLang))
                {
                    addIssue("warning", "seo", relativePath,
                        $"invalid hreflang value '{link.HrefLang}'.",
                        "seo-hreflang-invalid");
                }

                if (!IsAbsoluteHttpUrl(link.Href))
                {
                    addIssue("warning", "seo", relativePath,
                        $"hreflang '{link.HrefLang}' should be absolute http(s) but was '{link.Href}'.",
                        "seo-hreflang-absolute");
                }
            }

            if (hreflangLinks.All(link => !link.HrefLang.Equals("x-default", StringComparison.OrdinalIgnoreCase)))
            {
                addIssue("warning", "seo", relativePath,
                    "hreflang alternates are present but x-default is missing.",
                    "seo-hreflang-x-default-missing");
            }
        }
    }

    private static bool SeoUrlsMatch(string left, string right)
    {
        var leftNormalized = NormalizeComparableSeoUrl(left);
        var rightNormalized = NormalizeComparableSeoUrl(right);
        if (string.IsNullOrWhiteSpace(leftNormalized) || string.IsNullOrWhiteSpace(rightNormalized))
            return false;
        return string.Equals(leftNormalized, rightNormalized, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeComparableSeoUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return string.Empty;

        var path = uri.AbsolutePath;
        if (string.IsNullOrWhiteSpace(path))
            path = "/";

        if (!path.Equals("/", StringComparison.Ordinal) && path.EndsWith("/", StringComparison.Ordinal))
            path = path.TrimEnd('/');

        return $"{uri.Scheme.ToLowerInvariant()}://{uri.Host.ToLowerInvariant()}{path}";
    }

    private static string[] BuildRouteCandidatesForSeoChecks(string relativePath, string routePath, AngleSharp.Dom.IDocument doc)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddGeneratedPageRouteCandidates(candidates, relativePath, routePath);

        if (doc.Head is not null)
        {
            var canonicalHref = doc.Head.QuerySelectorAll("link[rel][href]")
                .Where(link => ContainsRelToken(link.GetAttribute("rel"), "canonical"))
                .Select(link => (link.GetAttribute("href") ?? string.Empty).Trim())
                .FirstOrDefault(href => !string.IsNullOrWhiteSpace(href));
            if (!string.IsNullOrWhiteSpace(canonicalHref))
                AddRouteCandidates(candidates, canonicalHref);
        }

        return candidates.ToArray();
    }

    private static string[] BuildGeneratedPageRouteCandidates(string relativePath, string routePath)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddGeneratedPageRouteCandidates(candidates, relativePath, routePath);
        return candidates.ToArray();
    }

    private static void AddGeneratedPageRouteCandidates(HashSet<string> candidates, string relativePath, string routePath)
    {
        AddRouteCandidates(candidates, routePath);
        AddRouteCandidates(candidates, "/" + relativePath.Replace('\\', '/').TrimStart('/'));
    }

    private static void AddRouteCandidates(HashSet<string> routes, string? value)
    {
        var normalized = NormalizeRouteLikeValue(value);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        routes.Add(normalized);

        if (normalized.EndsWith("/index.html", StringComparison.OrdinalIgnoreCase))
        {
            var folder = normalized.Substring(0, normalized.Length - "index.html".Length);
            if (string.IsNullOrWhiteSpace(folder))
                folder = "/";
            routes.Add(folder);
        }
        else if (normalized.EndsWith("/", StringComparison.Ordinal))
        {
            routes.Add(normalized.Equals("/", StringComparison.Ordinal)
                ? "/index.html"
                : normalized + "index.html");
        }
    }

    private static string NormalizeRouteLikeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var raw = value.Trim();
        if (Uri.TryCreate(raw, UriKind.Absolute, out var absolute))
            raw = absolute.AbsolutePath;

        var queryIndex = raw.IndexOf('?');
        if (queryIndex >= 0)
            raw = raw.Substring(0, queryIndex);
        var fragmentIndex = raw.IndexOf('#');
        if (fragmentIndex >= 0)
            raw = raw.Substring(0, fragmentIndex);

        raw = raw.Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return "/";
        if (!raw.StartsWith("/", StringComparison.Ordinal))
            raw = "/" + raw;
        return raw;
    }

    private static SitemapSeoScan CollectSitemapSeoMetadata(
        string siteRoot,
        IReadOnlyCollection<string> htmlFiles,
        Action<string, string, string?, string, string?> addIssue)
    {
        const int sampleLimit = 5;
        var noIndexRoutes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pagesByRoute = new Dictionary<string, SitemapPageSeoMetadata>(StringComparer.OrdinalIgnoreCase);
        var readErrorSamples = new List<string>(sampleLimit);
        var parseErrorSamples = new List<string>(sampleLimit);
        var routeCollisionSamples = new List<string>(sampleLimit);
        var readErrorCount = 0;
        var parseErrorCount = 0;
        var routeCollisionCount = 0;

        foreach (var file in htmlFiles)
        {
            var relativePath = Path.GetRelativePath(siteRoot, file).Replace('\\', '/');
            string html;
            try
            {
                html = File.ReadAllText(file);
            }
            catch
            {
                readErrorCount++;
                AddSample(readErrorSamples, relativePath, sampleLimit);
                continue;
            }

            if (string.IsNullOrWhiteSpace(html))
                continue;

            AngleSharp.Dom.IDocument doc;
            try
            {
                doc = HtmlParser.ParseWithAngleSharp(html);
            }
            catch
            {
                parseErrorCount++;
                AddSample(parseErrorSamples, relativePath, sampleLimit);
                continue;
            }

            var routePath = ToRoutePath(relativePath);
            if (doc.Head is not null)
            {
                var canonicalLinks = GetHeadLinkValues(doc, "canonical", html);
                var canonicalHref = canonicalLinks.Length == 1 && IsAbsoluteHttpUrl(canonicalLinks[0])
                    ? canonicalLinks[0]
                    : string.Empty;
                if (!string.IsNullOrWhiteSpace(canonicalHref))
                {
                    var metadata = new SitemapPageSeoMetadata(relativePath, canonicalHref);
                    foreach (var candidate in BuildGeneratedPageRouteCandidates(relativePath, routePath))
                    {
                        if (!string.IsNullOrWhiteSpace(candidate))
                        {
                            if (pagesByRoute.TryGetValue(candidate, out var existing) &&
                                (!string.Equals(existing.RelativePath, metadata.RelativePath, StringComparison.OrdinalIgnoreCase) ||
                                 !string.Equals(existing.CanonicalUrl, metadata.CanonicalUrl, StringComparison.OrdinalIgnoreCase)))
                            {
                                routeCollisionCount++;
                                AddSample(routeCollisionSamples, $"{candidate} ({existing.RelativePath}, {metadata.RelativePath})", sampleLimit);
                                continue;
                            }

                            pagesByRoute[candidate] = metadata;
                        }
                    }
                }
            }

            if (!HasNoIndexRobots(doc, html))
                continue;

            foreach (var candidate in BuildRouteCandidatesForSeoChecks(relativePath, routePath, doc))
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                    noIndexRoutes.Add(candidate);
            }
        }

        AddSitemapSeoScanSummaryIssue(addIssue, "seo-sitemap-metadata-read-error",
            "sitemap SEO metadata scan skipped {0} HTML file(s) that could not be read.",
            readErrorCount, readErrorSamples);
        AddSitemapSeoScanSummaryIssue(addIssue, "seo-sitemap-metadata-parse-error",
            "sitemap SEO metadata scan skipped {0} HTML file(s) that could not be parsed.",
            parseErrorCount, parseErrorSamples);
        AddSitemapSeoScanSummaryIssue(addIssue, "seo-sitemap-route-collision",
            "sitemap SEO metadata scan found {0} generated route candidate collision(s).",
            routeCollisionCount, routeCollisionSamples);

        return new SitemapSeoScan(noIndexRoutes, pagesByRoute);
    }

    private static void AddSample(List<string> samples, string value, int sampleLimit)
    {
        if (samples.Count < sampleLimit && !string.IsNullOrWhiteSpace(value))
            samples.Add(value);
    }

    private static void AddSitemapSeoScanSummaryIssue(
        Action<string, string, string?, string, string?> addIssue,
        string hint,
        string messageFormat,
        int count,
        List<string> samples)
    {
        if (count <= 0)
            return;

        var sampleText = samples.Count > 0 ? $" Sample: {string.Join(", ", samples)}." : string.Empty;
        addIssue("warning", "seo", "sitemap.xml",
            string.Format(System.Globalization.CultureInfo.InvariantCulture, messageFormat, count) + sampleText,
            hint);
    }

    private static void ValidateSitemapSeoConsistency(
        string siteRoot,
        HashSet<string> noIndexRoutes,
        Dictionary<string, SitemapPageSeoMetadata> pageMetadataByRoute,
        Action<string, string, string?, string, string?> addIssue)
    {
        if (noIndexRoutes.Count == 0 && pageMetadataByRoute.Count == 0)
            return;

        var sitemapPath = Path.Combine(siteRoot, "sitemap.xml");
        if (!File.Exists(sitemapPath))
            return;

        try
        {
            var doc = XDocument.Load(sitemapPath);
            var locs = doc
                .Descendants()
                .Where(node => node.Name.LocalName.Equals("loc", StringComparison.OrdinalIgnoreCase))
                .Select(node => (Url: (node.Value ?? string.Empty).Trim(), ComparableUrl: NormalizeExactSeoUrl(node.Value), Route: NormalizeRouteLikeValue(node.Value)))
                .Where(loc => !string.IsNullOrWhiteSpace(loc.Url))
                .ToArray();

            var duplicateLocs = locs
                .Where(loc => !string.IsNullOrWhiteSpace(loc.ComparableUrl))
                .GroupBy(loc => loc.ComparableUrl, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .ToArray();
            foreach (var duplicate in duplicateLocs
                         .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                         .Take(50))
            {
                addIssue("warning", "seo", "sitemap.xml",
                    $"sitemap includes duplicate URL '{duplicate.First().Url}' ({duplicate.Count()} entries).",
                    $"seo-sitemap-duplicate-loc:{duplicate.First().Route}");
            }
            if (duplicateLocs.Length > 50)
            {
                addIssue("warning", "seo", "sitemap.xml",
                    $"sitemap includes additional duplicate URL groups ({duplicateLocs.Length - 50} more).",
                    "seo-sitemap-duplicate-loc-more");
            }

            if (noIndexRoutes.Count > 0)
            {
                var noIndexInSitemap = new List<string>();
                foreach (var loc in locs.Where(loc => !string.IsNullOrWhiteSpace(loc.Route)))
                {
                    var matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    AddRouteCandidates(matches, loc.Route);
                    if (matches.Any(candidate => noIndexRoutes.Contains(candidate)))
                        noIndexInSitemap.Add(loc.Route);
                }

                foreach (var route in noIndexInSitemap
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                             .Take(50))
                {
                    addIssue("warning", "seo", "sitemap.xml",
                        $"sitemap includes noindex route '{route}'.",
                        $"seo-sitemap-noindex:{route}");
                }

                if (noIndexInSitemap.Count > 50)
                {
                    addIssue("warning", "seo", "sitemap.xml",
                        $"sitemap includes additional noindex routes ({noIndexInSitemap.Count - 50} more).",
                        "seo-sitemap-noindex-more");
                }
            }

            if (pageMetadataByRoute.Count > 0)
            {
                var canonicalMismatches = new List<(string Url, string Route, SitemapPageSeoMetadata Metadata)>();
                foreach (var loc in locs
                             .Where(loc => !string.IsNullOrWhiteSpace(loc.Route) && !string.IsNullOrWhiteSpace(loc.ComparableUrl))
                             .GroupBy(loc => loc.ComparableUrl, StringComparer.OrdinalIgnoreCase)
                             .Select(group => group.First()))
                {
                    var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    AddRouteCandidates(candidates, loc.Route);
                    var metadata = candidates
                        .Select(candidate => pageMetadataByRoute.TryGetValue(candidate, out var value) ? value : null)
                        .FirstOrDefault(value => value is not null);
                    if (metadata is null)
                        continue;

                    var canonicalUrl = NormalizeExactSeoUrl(metadata.CanonicalUrl);
                    if (string.IsNullOrWhiteSpace(canonicalUrl))
                        continue;

                    if (!string.Equals(loc.ComparableUrl, canonicalUrl, StringComparison.OrdinalIgnoreCase))
                        canonicalMismatches.Add((loc.Url, loc.Route, metadata));
                }

                foreach (var mismatch in canonicalMismatches
                             .OrderBy(item => item.Route, StringComparer.OrdinalIgnoreCase)
                             .Take(50))
                {
                    addIssue("warning", "seo", "sitemap.xml",
                        $"sitemap URL '{mismatch.Url}' points to generated page '{mismatch.Metadata.RelativePath}' whose canonical is '{mismatch.Metadata.CanonicalUrl}'.",
                        $"seo-sitemap-canonical-mismatch:{mismatch.Route}");
                }

                if (canonicalMismatches.Count > 50)
                {
                    addIssue("warning", "seo", "sitemap.xml",
                        $"sitemap includes additional URLs whose page canonical points elsewhere ({canonicalMismatches.Count - 50} more).",
                        "seo-sitemap-canonical-mismatch-more");
                }
            }
        }
        catch (Exception ex)
        {
            addIssue("warning", "seo", "sitemap.xml",
                $"failed to parse sitemap.xml for noindex validation ({ex.Message}).",
                "seo-sitemap-parse");
        }
    }

    private static string NormalizeExactSeoUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return string.Empty;
        }

        var path = string.IsNullOrWhiteSpace(uri.AbsolutePath) ? "/" : uri.AbsolutePath;
        var authority = uri.IsDefaultPort
            ? uri.IdnHost.ToLowerInvariant()
            : uri.IdnHost.ToLowerInvariant() + ":" + uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        // Fragments are intentionally omitted because sitemap and canonical URLs identify documents, not anchors.
        return $"{uri.Scheme.ToLowerInvariant()}://{authority}{path}{uri.Query}";
    }

    private sealed record SitemapPageSeoMetadata(string RelativePath, string CanonicalUrl);

    private sealed record SitemapSeoScan(
        HashSet<string> NoIndexRoutes,
        Dictionary<string, SitemapPageSeoMetadata> PagesByRoute);

    private static string[] GetMetaPropertyValues(AngleSharp.Dom.IDocument doc, string propertyName, string? rawHtml = null)
    {
        var values = doc.Head?
            .QuerySelectorAll("meta[property]")
            .Where(meta => string.Equals(meta.GetAttribute("property"), propertyName, StringComparison.OrdinalIgnoreCase))
            .Select(meta => (meta.GetAttribute("content") ?? string.Empty).Trim())
            .Where(content => !string.IsNullOrWhiteSpace(content))
            .ToArray()
            ?? Array.Empty<string>();

        if (values.Length > 0 || string.IsNullOrWhiteSpace(rawHtml))
            return values;

        return GetTagAttributeValuesFromHtml(rawHtml, "meta", "property", propertyName, "content");
    }

    private static string[] GetMetaNameValues(AngleSharp.Dom.IDocument doc, string name, string? rawHtml = null)
    {
        var values = doc.Head?
            .QuerySelectorAll("meta[name]")
            .Where(meta => string.Equals(meta.GetAttribute("name"), name, StringComparison.OrdinalIgnoreCase))
            .Select(meta => (meta.GetAttribute("content") ?? string.Empty).Trim())
            .Where(content => !string.IsNullOrWhiteSpace(content))
            .ToArray()
            ?? Array.Empty<string>();

        if (values.Length > 0 || string.IsNullOrWhiteSpace(rawHtml))
            return values;

        return GetTagAttributeValuesFromHtml(rawHtml, "meta", "name", name, "content");
    }

    private static string[] GetHeadLinkValues(AngleSharp.Dom.IDocument doc, string relToken, string? rawHtml = null)
    {
        var values = doc.Head?
            .QuerySelectorAll("link[rel][href]")
            .Where(link => ContainsRelToken(link.GetAttribute("rel"), relToken))
            .Select(link => (link.GetAttribute("href") ?? string.Empty).Trim())
            .Where(href => !string.IsNullOrWhiteSpace(href))
            .ToArray()
            ?? Array.Empty<string>();

        if (values.Length > 0 || string.IsNullOrWhiteSpace(rawHtml))
            return values;

        return GetLinkHrefValuesFromHtml(rawHtml, relToken);
    }

    private static bool HasNoIndexRobots(AngleSharp.Dom.IDocument doc, string? rawHtml = null)
    {
        if (doc.Head is null)
        {
            if (!string.IsNullOrWhiteSpace(rawHtml))
                return HasNoIndexRobotsRaw(rawHtml);
            return false;
        }

        foreach (var meta in doc.Head.QuerySelectorAll("meta[name]"))
        {
            var name = meta.GetAttribute("name");
            if (!string.Equals(name, "robots", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(name, "googlebot", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(name, "bingbot", StringComparison.OrdinalIgnoreCase))
                continue;

            var content = meta.GetAttribute("content");
            if (!string.IsNullOrWhiteSpace(content) &&
                content.IndexOf("noindex", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return !string.IsNullOrWhiteSpace(rawHtml) && HasNoIndexRobotsRaw(rawHtml);
    }

    private static bool HasNoIndexRobotsRaw(string rawHtml)
    {
        var names = new[] { "robots", "googlebot", "bingbot" };
        foreach (var name in names)
        {
            foreach (var content in GetTagAttributeValuesFromHtml(rawHtml, "meta", "name", name, "content"))
            {
                if (content.IndexOf("noindex", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
        }

        return false;
    }

    private static string[] GetLinkHrefValuesFromHtml(string rawHtml, string relToken)
    {
        if (string.IsNullOrWhiteSpace(rawHtml) || string.IsNullOrWhiteSpace(relToken))
            return Array.Empty<string>();

        var values = new List<string>();
        foreach (Match match in Regex.Matches(rawHtml, "<link\\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline))
        {
            var attributes = ExtractTagAttributes(match.Value);
            if (!attributes.TryGetValue("rel", out var relValue) || !ContainsRelToken(relValue, relToken))
                continue;
            if (!attributes.TryGetValue("href", out var hrefValue) || string.IsNullOrWhiteSpace(hrefValue))
                continue;

            values.Add(hrefValue.Trim());
        }

        return values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private static string[] GetTagAttributeValuesFromHtml(string rawHtml, string tagName, string matchAttribute, string matchValue, string targetAttribute)
    {
        if (string.IsNullOrWhiteSpace(rawHtml) ||
            string.IsNullOrWhiteSpace(tagName) ||
            string.IsNullOrWhiteSpace(matchAttribute) ||
            string.IsNullOrWhiteSpace(matchValue) ||
            string.IsNullOrWhiteSpace(targetAttribute))
            return Array.Empty<string>();

        var values = new List<string>();
        var pattern = $"<{Regex.Escape(tagName)}\\b[^>]*>";
        foreach (Match match in Regex.Matches(rawHtml, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline))
        {
            var attributes = ExtractTagAttributes(match.Value);
            if (!attributes.TryGetValue(matchAttribute, out var attributeValue) ||
                !string.Equals(attributeValue, matchValue, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!attributes.TryGetValue(targetAttribute, out var targetValue) || string.IsNullOrWhiteSpace(targetValue))
                continue;

            values.Add(targetValue.Trim());
        }

        return values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private static Dictionary<string, string> ExtractTagAttributes(string tagHtml)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(tagHtml))
            return values;

        foreach (Match match in HtmlAttributePattern.Matches(tagHtml))
        {
            var name = match.Groups["name"].Value;
            var value = match.Groups["value"].Value;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            values[name] = value;
        }

        return values;
    }

    private static bool IsAbsoluteHttpUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;

        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }

    private static IEnumerable<AngleSharp.Dom.IElement> EnumerateHeadingCandidates(AngleSharp.Dom.IDocument doc)
    {
        if (doc.Body is null)
            return Enumerable.Empty<AngleSharp.Dom.IElement>();

        var mainScopes = doc.QuerySelectorAll("main,[role='main']")
            .Where(scope => !IsElementHidden(scope))
            .ToArray();
        if (mainScopes.Length > 0)
        {
            return mainScopes
                .SelectMany(scope => scope.QuerySelectorAll("h1,h2,h3,h4,h5,h6"))
                .Where(heading => !IsWithinExcludedHeadingScope(heading));
        }

        return doc.Body.QuerySelectorAll("h1,h2,h3,h4,h5,h6")
            .Where(heading => !IsWithinExcludedHeadingScope(heading));
    }

    private static bool IsWithinExcludedHeadingScope(AngleSharp.Dom.IElement heading)
    {
        if (heading is null)
            return true;

        if (IsElementHidden(heading))
            return true;

        var parent = heading.ParentElement;
        while (parent is not null)
        {
            if (IsElementHidden(parent))
                return true;

            var tagName = parent.TagName;
            if (tagName.Equals("HEADER", StringComparison.OrdinalIgnoreCase) ||
                tagName.Equals("FOOTER", StringComparison.OrdinalIgnoreCase) ||
                tagName.Equals("NAV", StringComparison.OrdinalIgnoreCase) ||
                tagName.Equals("ASIDE", StringComparison.OrdinalIgnoreCase) ||
                tagName.Equals("TEMPLATE", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            parent = parent.ParentElement;
        }

        return false;
    }

    private static void ValidateLinkPurposeConsistency(
        AngleSharp.Dom.IDocument doc,
        string relativePath,
        IReadOnlyDictionary<string, string[]> redirectMap,
        Action<string, string, string?, string, string?> addIssue)
    {
        var labelTargets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var labelDisplay = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var languagePrefix = ResolveAuditLanguagePrefix(relativePath);

        foreach (var anchor in doc.QuerySelectorAll("a[href]"))
        {
            var href = anchor.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href) || ShouldSkipLink(href))
                continue;

            var label = GetAccessibleLinkLabel(anchor);
            if (string.IsNullOrWhiteSpace(label))
                continue;

            var destination = NormalizeLinkPurposeDestination(href, redirectMap, languagePrefix);
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

    private static string NormalizeLinkPurposeDestination(string href, IReadOnlyDictionary<string, string[]> redirectMap, string languagePrefix)
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
            var normalizedPath = NormalizeNavHref(path);
            var redirectedPath = ResolveRedirectTarget(path, redirectMap, languagePrefix);
            if (!string.IsNullOrWhiteSpace(redirectedPath) &&
                !string.Equals(redirectedPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                return redirectedPath;
            }
            return $"{absolute.Scheme}://{absolute.Host}{(absolute.IsDefaultPort ? string.Empty : ":" + absolute.Port)}{path}";
        }

        return ResolveRedirectTarget(normalized, redirectMap, languagePrefix);
    }

    private static IReadOnlyDictionary<string, string[]> LoadAuditRedirectMap(string siteRoot)
    {
        if (string.IsNullOrWhiteSpace(siteRoot))
            return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        var redirectMetadataPath = Path.Combine(siteRoot, "_powerforge", "redirects.json");
        if (!File.Exists(redirectMetadataPath))
            return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(redirectMetadataPath));
            var redirectMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            if (document.RootElement.TryGetProperty("routeOverrides", out var routeOverrides) &&
                routeOverrides.ValueKind == JsonValueKind.Array)
            {
                AddRedirectEntries(routeOverrides, redirectMap);
            }

            if (document.RootElement.TryGetProperty("redirects", out var redirects) &&
                redirects.ValueKind == JsonValueKind.Array)
            {
                AddRedirectEntries(redirects, redirectMap);
            }

            return redirectMap.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void AddRedirectEntries(JsonElement redirects, Dictionary<string, List<string>> redirectMap)
    {
        foreach (var entry in redirects.EnumerateArray())
        {
            if (!entry.TryGetProperty("from", out var fromProperty) ||
                !entry.TryGetProperty("to", out var toProperty))
            {
                continue;
            }

            var from = NormalizeNavHref(fromProperty.GetString());
            var to = NormalizeNavHref(toProperty.GetString());
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                continue;

            if (!redirectMap.TryGetValue(from, out var targets))
            {
                targets = new List<string>();
                redirectMap[from] = targets;
            }

            if (!targets.Contains(to, StringComparer.OrdinalIgnoreCase))
                targets.Add(to);
        }
    }

    private static string ResolveRedirectTarget(string href, IReadOnlyDictionary<string, string[]> redirectMap, string languagePrefix)
    {
        var normalized = NormalizeNavHref(href);
        if (string.IsNullOrWhiteSpace(normalized) || redirectMap.Count == 0)
            return normalized;

        var current = normalized;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var depth = 0; depth < LinkPurposeRedirectDepthLimit; depth++)
        {
            if (!visited.Add(current))
                break;

            if (!redirectMap.TryGetValue(current, out var targets) || targets.Length == 0)
                break;

            current = SelectRedirectTargetForLanguage(targets, languagePrefix);
            if (string.IsNullOrWhiteSpace(current))
                return normalized;
        }

        return current;
    }

    private static string SelectRedirectTargetForLanguage(string[] targets, string languagePrefix)
    {
        if (targets is null || targets.Length == 0)
            return string.Empty;

        var normalizedLanguagePrefix = NormalizeNavHref(languagePrefix);
        if (string.IsNullOrWhiteSpace(normalizedLanguagePrefix) || normalizedLanguagePrefix.Equals("/", StringComparison.Ordinal))
        {
            var rootTarget = targets.FirstOrDefault(static target => !HasExplicitLanguagePrefix(target));
            if (!string.IsNullOrWhiteSpace(rootTarget))
                return NormalizeNavHref(rootTarget);
        }
        else
        {
            var prefixedTarget = targets.FirstOrDefault(target => TargetMatchesLanguagePrefix(target, normalizedLanguagePrefix));
            if (!string.IsNullOrWhiteSpace(prefixedTarget))
                return NormalizeNavHref(prefixedTarget);
        }

        return NormalizeNavHref(targets[0]);
    }

    private static string ResolveAuditLanguagePrefix(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return string.Empty;

        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        var slashIndex = normalized.IndexOf('/');
        var firstSegment = slashIndex >= 0 ? normalized.Substring(0, slashIndex) : normalized;
        if (!LooksLikeLanguagePathSegment(firstSegment))
            return string.Empty;

        return "/" + firstSegment.ToLowerInvariant();
    }

    private static bool TargetMatchesLanguagePrefix(string target, string languagePrefix)
    {
        var normalizedTarget = NormalizeNavHref(target);
        if (string.IsNullOrWhiteSpace(normalizedTarget) || string.IsNullOrWhiteSpace(languagePrefix))
            return false;

        return normalizedTarget.Equals(languagePrefix, StringComparison.OrdinalIgnoreCase) ||
               normalizedTarget.StartsWith(languagePrefix + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasExplicitLanguagePrefix(string target)
    {
        var normalized = NormalizeNavHref(target).TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        var slashIndex = normalized.IndexOf('/');
        var firstSegment = slashIndex >= 0 ? normalized.Substring(0, slashIndex) : normalized;
        return LooksLikeLanguagePathSegment(firstSegment);
    }

    private static bool LooksLikeLanguagePathSegment(string segment)
    {
        var trimmed = segment.Trim();
        return trimmed.Length > 0 &&
            !ReservedLanguagePathSegments.Contains(trimmed) &&
            LanguagePathSegmentPattern.IsMatch(trimmed);
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

    private static void ValidateMediaEmbeds(
        AngleSharp.Dom.IDocument doc,
        string rawHtml,
        string relativePath,
        WebAuditMediaProfile? profile,
        Action<string, string, string?, string, string?> addIssue)
    {
        ValidateEscapedMediaTags(doc, rawHtml, relativePath, addIssue);

        var requireIframeLazy = profile?.RequireIframeLazy ?? true;
        var requireIframeTitle = profile?.RequireIframeTitle ?? true;
        var requireIframeReferrerPolicy = profile?.RequireIframeReferrerPolicy ?? true;
        var requireYoutubeNoCookie = !(profile?.AllowYoutubeStandardHost ?? false);
        var requireImageLoadingHint = profile?.RequireImageLoadingHint ?? true;
        var requireImageDecodingHint = profile?.RequireImageDecodingHint ?? true;
        var requireImageDimensions = profile?.RequireImageDimensions ?? true;
        var requireImageSrcSetSizes = profile?.RequireImageSrcSetSizes ?? true;
        var maxEagerImages = profile?.MaxEagerImages ?? 1;
        if (maxEagerImages < 0)
            maxEagerImages = 0;

        var iframeMissingLazy = new List<string>();
        var iframeMissingTitle = new List<string>();
        var iframeMissingReferrerPolicy = new List<string>();
        var youtubeNonNoCookie = new List<string>();

        foreach (var iframe in doc.QuerySelectorAll("iframe[src]"))
        {
            if (IsElementHidden(iframe))
                continue;

            var src = (iframe.GetAttribute("src") ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(src) || ShouldSkipLink(src))
                continue;

            if (requireIframeLazy)
            {
                var loading = (iframe.GetAttribute("loading") ?? string.Empty).Trim();
                if (!loading.Equals("lazy", StringComparison.OrdinalIgnoreCase))
                    iframeMissingLazy.Add(src);
            }

            if (requireIframeTitle)
            {
                var title = (iframe.GetAttribute("title") ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(title))
                    iframeMissingTitle.Add(src);
            }

            if (requireIframeReferrerPolicy)
            {
                var referrerPolicy = (iframe.GetAttribute("referrerpolicy") ?? string.Empty).Trim();
                if (IsExternalLink(src) && string.IsNullOrWhiteSpace(referrerPolicy))
                    iframeMissingReferrerPolicy.Add(src);
            }

            if (requireYoutubeNoCookie &&
                IsYouTubeEmbedUrl(src) &&
                src.IndexOf("youtube-nocookie.com", StringComparison.OrdinalIgnoreCase) < 0)
            {
                youtubeNonNoCookie.Add(src);
            }
        }

        var imageMissingLoading = new List<string>();
        var imageMissingDecoding = new List<string>();
        var imageMissingDimensions = new List<string>();
        var imageSrcSetMissingSizes = new List<string>();
        var eagerImageCount = 0;

        foreach (var image in doc.QuerySelectorAll("img[src]"))
        {
            if (IsElementHidden(image))
                continue;

            var src = (image.GetAttribute("src") ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(src) || ShouldSkipLink(src))
                continue;

            var loading = (image.GetAttribute("loading") ?? string.Empty).Trim();
            var fetchPriority = (image.GetAttribute("fetchpriority") ?? string.Empty).Trim();
            if (loading.Equals("eager", StringComparison.OrdinalIgnoreCase))
                eagerImageCount++;

            if (requireImageLoadingHint &&
                string.IsNullOrWhiteSpace(loading) &&
                !fetchPriority.Equals("high", StringComparison.OrdinalIgnoreCase))
            {
                imageMissingLoading.Add(src);
            }

            if (requireImageDecodingHint)
            {
                var decoding = (image.GetAttribute("decoding") ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(decoding))
                    imageMissingDecoding.Add(src);
            }

            if (requireImageDimensions && !HasImageDimensionHints(image, src))
                imageMissingDimensions.Add(src);

            var srcset = (image.GetAttribute("srcset") ?? string.Empty).Trim();
            var sizes = (image.GetAttribute("sizes") ?? string.Empty).Trim();
            if (requireImageSrcSetSizes &&
                !string.IsNullOrWhiteSpace(srcset) &&
                string.IsNullOrWhiteSpace(sizes))
            {
                imageSrcSetMissingSizes.Add(src);
            }
        }

        if (iframeMissingLazy.Count > 0)
        {
            addIssue("warning", "media", relativePath,
                $"{iframeMissingLazy.Count} iframe embed(s) missing loading=\"lazy\".{BuildMediaSampleSuffix(iframeMissingLazy)}",
                "media-iframe-lazy");
        }

        if (iframeMissingTitle.Count > 0)
        {
            addIssue("warning", "media", relativePath,
                $"{iframeMissingTitle.Count} iframe embed(s) missing title attribute.{BuildMediaSampleSuffix(iframeMissingTitle)}",
                "media-iframe-title");
        }

        if (iframeMissingReferrerPolicy.Count > 0)
        {
            addIssue("warning", "media", relativePath,
                $"{iframeMissingReferrerPolicy.Count} external iframe embed(s) missing referrerpolicy.{BuildMediaSampleSuffix(iframeMissingReferrerPolicy)}",
                "media-iframe-referrerpolicy");
        }

        if (youtubeNonNoCookie.Count > 0)
        {
            addIssue("warning", "media", relativePath,
                $"{youtubeNonNoCookie.Count} YouTube embed(s) do not use youtube-nocookie.com.{BuildMediaSampleSuffix(youtubeNonNoCookie)}",
                "media-youtube-nocookie");
        }

        if (imageMissingLoading.Count > 0)
        {
            addIssue("warning", "media", relativePath,
                $"{imageMissingLoading.Count} image(s) missing loading hint (lazy/eager or fetchpriority=high).{BuildMediaSampleSuffix(imageMissingLoading)}",
                "media-img-loading");
        }

        if (imageMissingDecoding.Count > 0)
        {
            addIssue("warning", "media", relativePath,
                $"{imageMissingDecoding.Count} image(s) missing decoding hint.{BuildMediaSampleSuffix(imageMissingDecoding)}",
                "media-img-decoding");
        }

        if (imageMissingDimensions.Count > 0)
        {
            addIssue("warning", "media", relativePath,
                $"{imageMissingDimensions.Count} image(s) missing width/height or aspect-ratio hint (risk of layout shift).{BuildMediaSampleSuffix(imageMissingDimensions)}",
                "media-img-dimensions");
        }

        if (imageSrcSetMissingSizes.Count > 0)
        {
            addIssue("warning", "media", relativePath,
                $"{imageSrcSetMissingSizes.Count} responsive image(s) define srcset without sizes.{BuildMediaSampleSuffix(imageSrcSetMissingSizes)}",
                "media-img-srcset-sizes");
        }

        if (eagerImageCount > maxEagerImages)
        {
            addIssue("warning", "media", relativePath,
                $"page contains {eagerImageCount} eagerly loaded images; max allowed is {maxEagerImages}.",
                "media-img-eager");
        }
    }

    private static void ValidateEscapedMediaTags(
        AngleSharp.Dom.IDocument doc,
        string rawHtml,
        string relativePath,
        Action<string, string, string?, string, string?> addIssue)
    {
        if (string.IsNullOrWhiteSpace(rawHtml))
            return;

        var searchable = rawHtml;
        foreach (var codeElement in doc.QuerySelectorAll("code,pre"))
        {
            var encoded = codeElement.InnerHtml;
            if (string.IsNullOrWhiteSpace(encoded))
                continue;

            // Keep legitimate escaped HTML examples in code blocks from triggering media warnings.
            searchable = searchable.Replace(encoded, string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        var matches = EscapedMediaTagPattern.Matches(searchable);
        if (matches.Count == 0)
            return;

        var tags = matches
            .Select(match => match.Groups[1].Value.ToLowerInvariant())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        addIssue("warning", "media", relativePath,
            $"escaped media HTML tags detected in rendered content ({string.Join(", ", tags)}). " +
            "This usually means markdown HTML was not rendered as intended; prefer markdown image syntax or supported media shortcodes.",
            "media-escaped-html-tag");
    }

    private static string BuildMediaSampleSuffix(IEnumerable<string> values)
    {
        var samples = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value =>
            {
                var trimmed = value.Trim();
                return trimmed.Length <= 64 ? trimmed : trimmed.Substring(0, 61) + "...";
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();

        if (samples.Length == 0)
            return string.Empty;

        return $" Sample: {string.Join(", ", samples)}.";
    }

    private static bool HasImageDimensionHints(AngleSharp.Dom.IElement image, string src)
    {
        if (IsSvgImageSource(src))
            return true;

        var width = (image.GetAttribute("width") ?? string.Empty).Trim();
        var height = (image.GetAttribute("height") ?? string.Empty).Trim();
        if (int.TryParse(width, out var widthPx) && widthPx > 0 &&
            int.TryParse(height, out var heightPx) && heightPx > 0)
        {
            return true;
        }

        var style = image.GetAttribute("style") ?? string.Empty;
        if (style.IndexOf("aspect-ratio", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return false;
    }

    private static bool IsSvgImageSource(string src)
    {
        var normalized = StripQueryAndFragment(src).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        return normalized.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".svgz", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsYouTubeEmbedUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (value.IndexOf("youtube.com/embed/", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (value.IndexOf("youtube-nocookie.com/embed/", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (value.IndexOf("youtu.be/", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return false;
    }

    private static bool ContainsRelToken(string? relValue, string token)
    {
        if (string.IsNullOrWhiteSpace(relValue) || string.IsNullOrWhiteSpace(token))
            return false;

        var parts = relValue.Split(new[] { ' ', '\t', '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);
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
