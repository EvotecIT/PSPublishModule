using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace PowerForge.Web;

/// <summary>Validates and imports contributor-friendly website post bundles.</summary>
public static partial class WebContributionProcessor
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex MarkdownImageRegex = new(
        @"!\[(?<alt>[^\]]*)\]\((?<target>[^)\s]+)(?:\s+""[^""]*"")?\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);
    // Match only the canonical front matter image key; image_alt/image_url are intentionally excluded.
    private static readonly Regex FrontMatterImageRegex = new(
        @"(?m)^(?<prefix>image\s*:\s*[""']?)(?<target>[^""'\r\n]+)(?<suffix>[""']?\s*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);
    private static readonly Regex DraftRegex = new(
        @"(?m)^draft\s*:\s*true\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);
    private static readonly Regex FencedCodeBlockRegex = new(
        @"(?ms)^[ \t]*(```|~~~).*?^[ \t]*\1[ \t]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex FrontMatterDelimiterRegex = new(
        @"(?m)^---\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex DecorativeSeparatorRegex = new(
        @"(?m)^[ \t]*(?<marker>_{5,}|-{5,}|={5,})[ \t]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex BareFenceLanguageLabelRegex = new(
        @"(?im)^[ \t]*(?<label>bash|csharp|html|json|powershell|ps1|text|xml|yaml|yml)[ \t]*\r?\n[ \t]*(```|~~~)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex SlugLikeAltTextRegex = new(
        @"^[a-z0-9]+(?:[-_][a-z0-9]+)+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex SlugRegex = new(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex SlugNormalizerRegex = new(
        @"[^a-z0-9]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex XHandleRegex = new(
        "^[a-zA-Z0-9_]{1,50}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly IDeserializer AuthorDeserializer = new DeserializerBuilder().Build();
    private static readonly HashSet<string> AllowedAssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".webp",
        ".gif"
    };

    /// <summary>Validate contribution bundles and optionally import them into a website repository.</summary>
    public static WebContributionResult Process(WebContributionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();
        var warnings = new List<string>();
        var sourceRoot = FullPathRequired(options.SourceRoot, "SourceRoot");
        var result = new WebContributionResult
        {
            SourceRoot = sourceRoot,
            SiteRoot = string.IsNullOrWhiteSpace(options.SiteRoot) ? null : Path.GetFullPath(options.SiteRoot)
        };

        if (!Directory.Exists(sourceRoot))
        {
            errors.Add($"Contribution source root does not exist: {sourceRoot}");
            result.Errors = errors.ToArray();
            result.Warnings = warnings.ToArray();
            result.Success = false;
            return result;
        }

        if (!TryResolveInside(sourceRoot, options.PostsPath, "PostsPath", errors, out var postsRoot) ||
            !TryResolveInside(sourceRoot, options.AuthorsPath, "AuthorsPath", errors, out var authorsRoot))
        {
            result.Errors = errors.ToArray();
            result.Warnings = warnings.ToArray();
            result.Success = false;
            return result;
        }

        var authors = LoadAuthors(authorsRoot, warnings);
        result.AuthorCount = authors.Count;
        ValidateAuthorProfiles(authors, options, errors);

        if (!Directory.Exists(postsRoot))
            errors.Add($"Posts directory does not exist: {postsRoot}");

        var posts = Directory.Exists(postsRoot)
            ? Directory.GetFiles(postsRoot, "index.md", SearchOption.AllDirectories)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => ValidatePost(path, postsRoot, authors, options, errors, warnings))
                .ToArray()
            : Array.Empty<WebContributionPostResult>();

        result.Posts = posts;
        result.PostCount = posts.Length;
        if (posts.Length == 0 && Directory.Exists(postsRoot))
            warnings.Add($"No post bundles found under {postsRoot}. Expected posts/<language>/<slug>/index.md.");

        if (options.Import)
        {
            if (string.IsNullOrWhiteSpace(options.SiteRoot))
                errors.Add("Import requires SiteRoot.");
            else if (!Directory.Exists(result.SiteRoot))
                errors.Add($"Website root does not exist: {result.SiteRoot}");
            else
            {
                TryResolveInside(result.SiteRoot, options.TargetAuthorsPath, "TargetAuthorsPath", errors, out _);
                TryResolveInside(result.SiteRoot, options.TargetAuthorAssetsPath, "TargetAuthorAssetsPath", errors, out _);
                TryResolveInside(result.SiteRoot, options.ContentBlogPath, "ContentBlogPath", errors, out _);
                TryResolveInside(result.SiteRoot, options.StaticBlogAssetsPath, "StaticBlogAssetsPath", errors, out _);
            }
        }

        if (errors.Count == 0 && options.Import && result.SiteRoot is not null)
            Import(options, result.SiteRoot, authorsRoot, authors, posts, result, errors, warnings);

        result.Errors = errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        result.Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        result.Success = result.Errors.Length == 0;
        return result;
    }

    private static void Import(
        WebContributionOptions options,
        string siteRoot,
        string authorsRoot,
        IReadOnlyDictionary<string, WebContributionAuthorProfile> authors,
        WebContributionPostResult[] posts,
        WebContributionResult result,
        List<string> errors,
        List<string> warnings)
    {
        var targetAuthorsRoot = ResolveInside(siteRoot, options.TargetAuthorsPath);
        if (Directory.Exists(authorsRoot))
        {
            try
            {
                Directory.CreateDirectory(targetAuthorsRoot);
                var profiles = authors.Values
                    .OrderBy(static profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static profile => profile.Slug, StringComparer.OrdinalIgnoreCase)
                    .Select(profile => PrepareAuthorProfileForImport(profile, options, siteRoot, errors, result))
                    .ToArray();
                if (errors.Count > 0)
                    return;

                var catalog = new WebContributionAuthorCatalog { Authors = profiles };
                var catalogPath = Path.Combine(targetAuthorsRoot, "catalog.json");
                var json = JsonSerializer.Serialize(catalog, WebContributionJsonContext.Default.WebContributionAuthorCatalog);
                File.WriteAllText(catalogPath, json.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n", new UTF8Encoding(false));
                result.CopiedAuthorCount = profiles.Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                errors.Add($"Author catalog could not be written: {ex.Message}");
                return;
            }
        }

        foreach (var post in posts)
        {
            if (string.IsNullOrWhiteSpace(post.Language) || string.IsNullOrWhiteSpace(post.Slug))
                continue;

            var postErrorStart = errors.Count;
            var targetContentRoot = ResolveInside(siteRoot, Path.Combine(options.ContentBlogPath, post.Language));
            var targetContentPath = Path.Combine(targetContentRoot, post.Slug + ".md");
            var targetAssetRoot = ResolveInside(siteRoot, Path.Combine(options.StaticBlogAssetsPath, post.Year.ToString(CultureInfo.InvariantCulture), post.Slug));
            post.TargetContentPath = targetContentPath;
            post.TargetAssetPath = targetAssetRoot;

            if (!options.Force && File.Exists(targetContentPath))
                errors.Add($"Target post already exists: {targetContentPath}. Use --force to overwrite.");

            var assetCopies = post.Assets
                .Select(asset => new
                {
                    Source = Path.Combine(post.BundlePath, FromSlash(asset)),
                    Target = Path.Combine(targetAssetRoot, FromSlash(asset))
                })
                .ToArray();

            if (!options.Force)
            {
                foreach (var copy in assetCopies.Where(static copy => File.Exists(copy.Target)))
                    errors.Add($"Target file already exists: {copy.Target}. Use --force to overwrite.");
            }

            if (errors.Count > postErrorStart)
                continue;

            Directory.CreateDirectory(targetContentRoot);
            if (assetCopies.Length > 0)
                Directory.CreateDirectory(targetAssetRoot);

            foreach (var copy in assetCopies)
            {
                if (CopyFile(copy.Source, copy.Target, options.Force, errors))
                    result.CopiedAssetCount++;
            }

            if (errors.Count > postErrorStart)
                continue;

            var markdown = File.ReadAllText(post.SourcePath, Encoding.UTF8);
            var imported = RewritePostMarkdown(markdown, post, authors, options);

            File.WriteAllText(targetContentPath, imported, new UTF8Encoding(false));
            post.Imported = true;
            result.ImportedPostCount++;
        }

        if (errors.Count == 0 && result.ImportedPostCount == 0 && posts.Length > 0)
            warnings.Add("No posts were imported.");
    }

    private static WebContributionAuthorProfile PrepareAuthorProfileForImport(
        WebContributionAuthorProfile profile,
        WebContributionOptions options,
        string siteRoot,
        List<string> errors,
        WebContributionResult result)
    {
        var imported = new WebContributionAuthorProfile
        {
            Name = profile.Name,
            Slug = profile.Slug,
            Title = profile.Title,
            Bio = profile.Bio,
            Avatar = profile.Avatar,
            X = profile.X,
            LinkedIn = profile.LinkedIn,
            GitHub = profile.GitHub,
            Website = profile.Website
        };

        if (string.IsNullOrWhiteSpace(profile.Avatar) ||
            IsExternalOrRootedWebPath(profile.Avatar) ||
            string.IsNullOrWhiteSpace(profile.AvatarSourcePath))
        {
            return imported;
        }

        var avatarFileName = Path.GetFileName(profile.AvatarSourcePath);
        var targetRoot = ResolveInside(siteRoot, Path.Combine(options.TargetAuthorAssetsPath, profile.Slug));
        var target = Path.Combine(targetRoot, avatarFileName);
        if (CopyFile(profile.AvatarSourcePath, target, options.Force, errors))
        {
            result.CopiedAssetCount++;
            var publicRoot = options.TargetAuthorAssetsPath.Replace('\\', '/').Trim('/');
            if (publicRoot.StartsWith("static/", StringComparison.OrdinalIgnoreCase))
                publicRoot = publicRoot["static/".Length..];
            imported.Avatar = "/" + ToSlash(Path.Combine(publicRoot, profile.Slug, avatarFileName));
        }

        return imported;
    }

    private static string RewritePostMarkdown(
        string markdown,
        WebContributionPostResult post,
        IReadOnlyDictionary<string, WebContributionAuthorProfile> authors,
        WebContributionOptions options)
    {
        var assetRoutePrefix = $"/assets/blog/{post.Year.ToString(CultureInfo.InvariantCulture)}/{post.Slug}/";
        var rewritten = RewriteFrontMatter(markdown, frontMatter => FrontMatterImageRegex.Replace(frontMatter, match =>
        {
            var target = match.Groups["target"].Value.Trim();
            if (IsExternalOrRootedWebPath(target))
                return match.Value;

            return match.Groups["prefix"].Value + assetRoutePrefix + NormalizeRelativeAssetRoute(target) + match.Groups["suffix"].Value;
        }));

        rewritten = ReplaceOutsideFencedCodeBlocks(rewritten, MarkdownImageRegex, match =>
        {
            var target = UnescapeMarkdownTarget(match.Groups["target"].Value);
            if (IsExternalOrRootedWebPath(target))
                return match.Value;

            var alt = match.Groups["alt"].Value;
            return $"![{alt}]({assetRoutePrefix}{NormalizeRelativeAssetRoute(target)})";
        });

        if (options.Publish)
            rewritten = RewriteFrontMatter(rewritten, frontMatter => DraftRegex.Replace(frontMatter, "draft: false"));

        rewritten = EnsureImportedAuthorMetadata(rewritten, post, authors);
        return rewritten.Replace("\r\n", "\n");
    }

    private static string EnsureImportedAuthorMetadata(
        string markdown,
        WebContributionPostResult post,
        IReadOnlyDictionary<string, WebContributionAuthorProfile> authors)
    {
        var profiles = post.Authors
            .Select(author => authors.TryGetValue(author, out var profile) ? profile : null)
            .Where(static profile => profile is not null)
            .Cast<WebContributionAuthorProfile>()
            .ToArray();
        if (profiles.Length == 0)
            return markdown;

        var insert = new StringBuilder();
        insert.AppendLine("author: \"" + EscapeYamlScalar(profiles[0].Name) + "\"");
        AppendYamlStringArray(insert, "author_names", profiles.Select(static profile => profile.Name));
        AppendYamlStringArray(insert, "author_urls", profiles.Select(ResolveAuthorPublicUrl).Where(static value => !string.IsNullOrWhiteSpace(value)));

        var firstCreator = profiles
            .Select(static profile => NormalizeXHandle(profile.X))
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        if (!string.IsNullOrWhiteSpace(firstCreator))
            insert.AppendLine("social_twitter_creator: \"" + EscapeYamlScalar(firstCreator) + "\"");

        return RewriteFrontMatter(
            markdown,
            frontMatter => InsertFrontMatterFields(
                RemoveFrontMatterKeys(frontMatter, "author", "author_names", "author_urls", "social_twitter_creator"),
                insert.ToString()));
    }

    private static string MaskFencedCodeBlocks(string markdown) =>
        FencedCodeBlockRegex.Replace(markdown, match => new string('\n', match.Value.Count(static ch => ch == '\n')));

    private static string ReplaceOutsideFencedCodeBlocks(string markdown, Regex regex, MatchEvaluator evaluator)
    {
        var builder = new StringBuilder(markdown.Length);
        var offset = 0;
        foreach (Match fence in FencedCodeBlockRegex.Matches(markdown))
        {
            if (fence.Index > offset)
                builder.Append(regex.Replace(markdown[offset..fence.Index], evaluator));

            builder.Append(fence.Value);
            offset = fence.Index + fence.Length;
        }

        if (offset < markdown.Length)
            builder.Append(regex.Replace(markdown[offset..], evaluator));

        return builder.ToString();
    }

    private static string ResolveLanguage(FrontMatter matter, string postsRoot, string indexPath)
    {
        if (!string.IsNullOrWhiteSpace(matter.Language))
            return matter.Language.Trim().ToLowerInvariant();

        var relative = ToSlash(Path.GetRelativePath(postsRoot, indexPath));
        var first = relative.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first?.Trim().ToLowerInvariant() ?? string.Empty;
    }

    private static int ResolveYear(DateTime? date)
    {
        if (date is { Year: >= 2000 and <= 2100 } value)
            return value.Year;

        // Missing or invalid dates are validation errors; keep result paths deterministic until import is blocked.
        return 2000;
    }

    private static void ValidateBundleLayout(
        string relative,
        WebContributionPostResult result,
        List<string> errors)
    {
        var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 ||
            !string.Equals(parts[2], "index.md", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{relative}: expected posts/<language>/<slug>/index.md.");
            return;
        }

        var languageFolder = parts[0].Trim().ToLowerInvariant();
        var slugFolder = parts[1].Trim().ToLowerInvariant();

        if (!string.Equals(result.Language, languageFolder, StringComparison.OrdinalIgnoreCase))
            errors.Add($"{relative}: language '{result.Language}' must match folder '{languageFolder}'.");

        if (!string.Equals(result.Slug, slugFolder, StringComparison.Ordinal))
            errors.Add($"{relative}: slug '{result.Slug}' must match folder '{slugFolder}'.");
    }

    private static string? NormalizeSlug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim().ToLowerInvariant();
        normalized = SlugNormalizerRegex.Replace(normalized, "-").Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string[] ReadStringList(Dictionary<string, object?> meta, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!TryReadValue(meta, key, out var raw) || raw is null)
                continue;

            if (raw is IEnumerable<object?> items)
                return items.Select(static item => item?.ToString() ?? string.Empty).Where(static item => !string.IsNullOrWhiteSpace(item)).Select(static item => item.Trim()).ToArray();

            var text = raw.ToString();
            if (!string.IsNullOrWhiteSpace(text))
                return text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        return Array.Empty<string>();
    }

    private static bool TryReadString(Dictionary<string, object?> meta, string key, out string value)
    {
        value = string.Empty;
        if (!TryReadValue(meta, key, out var raw) || raw is null)
            return false;

        value = raw.ToString() ?? string.Empty;
        return true;
    }

    private static bool TryReadValue(Dictionary<string, object?> meta, string key, out object? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(key))
            return false;

        object? current = meta;
        foreach (var part in key.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current is IReadOnlyDictionary<string, object?> ro && ro.TryGetValue(part, out var next))
            {
                current = next;
                continue;
            }

            return false;
        }

        value = current;
        return true;
    }

    private static bool IsExternalOrRootedWebPath(string target) =>
        target.StartsWith("/", StringComparison.Ordinal) ||
        target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        target.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
        target.StartsWith("#", StringComparison.Ordinal);

    private static bool IsRootedWebPath(string? target) =>
        !string.IsNullOrWhiteSpace(target) && target.Trim().StartsWith("/", StringComparison.Ordinal);

    private static bool TryResolveBundleAsset(string bundleRoot, string target, out string fullPath)
    {
        fullPath = string.Empty;
        if (!TryNormalizeRelativeAssetPath(target, out var normalized))
            return false;

        var candidate = Path.GetFullPath(Path.Combine(bundleRoot, FromSlash(normalized)));
        var rootPrefix = Path.GetFullPath(bundleRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootPrefix, PathComparison))
            return false;

        fullPath = candidate;
        return true;
    }

    private static bool TryResolveAuthorAsset(string authorsRoot, string target, out string fullPath)
    {
        fullPath = string.Empty;
        if (!TryNormalizeRelativeAssetPath(target, out var normalized))
            return false;

        var candidate = Path.GetFullPath(Path.Combine(authorsRoot, FromSlash(normalized)));
        var rootPrefix = Path.GetFullPath(authorsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootPrefix, PathComparison))
            return false;

        fullPath = candidate;
        return true;
    }

    private static string NormalizeRelativeAssetRoute(string target)
    {
        if (!TryNormalizeRelativeAssetPath(target, out var normalized))
            return string.Empty;

        return string.Join("/", normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));
    }

    private static bool TryNormalizeRelativeAssetPath(string target, out string normalized)
    {
        normalized = UnescapeMarkdownTarget(target).Split('#')[0].Split('?')[0].Trim().Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];

        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.StartsWith("/", StringComparison.Ordinal) ||
            Path.IsPathRooted(normalized))
        {
            normalized = string.Empty;
            return false;
        }

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(static part => part == "." || part == ".."))
        {
            normalized = string.Empty;
            return false;
        }

        normalized = string.Join("/", parts);
        return true;
    }

    private static string UnescapeMarkdownTarget(string target) =>
        target.Trim().Trim('<', '>');

    private static string ReadMapString(Dictionary<string, object?> map, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!map.TryGetValue(key, out var value) || value is null)
                continue;

            var text = value.ToString();
            if (!string.IsNullOrWhiteSpace(text))
                return text.Trim();
        }

        return string.Empty;
    }

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsEmptyOrValidHttpUrl(string? value, params string[] allowedHosts)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        return allowedHosts.Length == 0 ||
               allowedHosts.Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
                                        uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsEmptyOrValidSocialValue(string? value, bool allowUnderscoreHandle, params string[] allowedHosts)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var trimmed = value.Trim();
        if (trimmed.StartsWith("@", StringComparison.Ordinal))
            trimmed = trimmed[1..];

        if (!trimmed.Contains("://", StringComparison.Ordinal))
            return allowUnderscoreHandle
                ? XHandleRegex.IsMatch(trimmed)
                : SlugRegex.IsMatch(trimmed.ToLowerInvariant());

        return IsEmptyOrValidHttpUrl(trimmed, allowedHosts);
    }

    private static string EscapeYamlScalar(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

    private static void AppendYamlStringArray(StringBuilder builder, string key, IEnumerable<string?> values)
    {
        var clean = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (clean.Length == 0)
            return;

        builder.AppendLine(key + ":");
        foreach (var value in clean)
            builder.AppendLine("  - \"" + EscapeYamlScalar(value) + "\"");
    }

    private static string? ResolveAuthorPublicUrl(WebContributionAuthorProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.Website))
            return profile.Website!.Trim();
        if (!string.IsNullOrWhiteSpace(profile.LinkedIn))
            return profile.LinkedIn!.Trim();
        if (!string.IsNullOrWhiteSpace(profile.X) && TryNormalizeSocialUrl(profile.X, "https://x.com/", out var xUrl))
            return xUrl;
        if (!string.IsNullOrWhiteSpace(profile.GitHub) && TryNormalizeSocialUrl(profile.GitHub, "https://github.com/", out var githubUrl))
            return githubUrl;
        return null;
    }

    private static string? NormalizeXHandle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            var handle = uri.AbsolutePath.Trim('/');
            if (string.IsNullOrWhiteSpace(handle))
                return null;
            trimmed = handle.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        }

        trimmed = trimmed.TrimStart('@').Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : "@" + trimmed;
    }

    private static bool TryNormalizeSocialUrl(string value, string baseUrl, out string url)
    {
        url = string.Empty;
        var trimmed = value.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
        {
            url = absolute.ToString();
            return true;
        }

        trimmed = trimmed.TrimStart('@', '/').Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return false;

        url = baseUrl + Uri.EscapeDataString(trimmed);
        return true;
    }

    private static string InsertFrontMatterFields(string frontMatter, string fields)
    {
        if (string.IsNullOrWhiteSpace(fields))
            return frontMatter;

        return frontMatter.TrimEnd('\r', '\n') + "\n" + fields.TrimEnd('\r', '\n') + "\n";
    }

    private static string RewriteFrontMatter(string markdown, Func<string, string> rewrite)
    {
        if (!TryGetFrontMatterBounds(markdown, out var contentStart, out var contentEnd))
            return markdown;

        var rewrittenFrontMatter = rewrite(markdown[contentStart..contentEnd]).TrimEnd('\r', '\n') + "\n";
        return markdown[..contentStart] + rewrittenFrontMatter + markdown[contentEnd..];
    }

    private static bool TryGetFrontMatterBounds(string markdown, out int contentStart, out int contentEnd)
    {
        contentStart = 0;
        contentEnd = 0;
        var matches = FrontMatterDelimiterRegex.Matches(markdown);
        if (matches.Count < 2 || matches[0].Index != 0)
            return false;

        contentStart = matches[0].Index + matches[0].Length;
        if (contentStart < markdown.Length && markdown[contentStart] == '\r')
            contentStart++;
        if (contentStart < markdown.Length && markdown[contentStart] == '\n')
            contentStart++;

        contentEnd = matches[1].Index;
        return contentEnd >= contentStart;
    }

    private static string RemoveFrontMatterKeys(string frontMatter, params string[] keys)
    {
        var keySet = keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var builder = new StringBuilder(frontMatter.Length);
        var skipping = false;
        foreach (var line in frontMatter.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (skipping)
            {
                // Imported author keys are scalar/list fields; remove their immediate YAML continuation block.
                if (string.IsNullOrWhiteSpace(line) || char.IsWhiteSpace(line[0]) || line.TrimStart().StartsWith("-", StringComparison.Ordinal))
                    continue;

                skipping = false;
            }

            if (IsFrontMatterKeyLine(line, keySet))
            {
                skipping = true;
                continue;
            }

            builder.Append(line).Append('\n');
        }

        return builder.ToString();
    }

    private static bool IsFrontMatterKeyLine(string line, ISet<string> keys)
    {
        if (string.IsNullOrWhiteSpace(line) || char.IsWhiteSpace(line[0]))
            return false;

        var colon = line.IndexOf(':');
        return colon > 0 && keys.Contains(line[..colon].Trim());
    }

    private static bool CopyFile(string source, string target, bool force, List<string> errors)
    {
        if (!File.Exists(source))
        {
            errors.Add($"Source file does not exist: {source}");
            return false;
        }

        if (!force && File.Exists(target))
        {
            errors.Add($"Target file already exists: {target}. Use --force to overwrite.");
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target) ?? ".");
        File.Copy(source, target, overwrite: force);
        return true;
    }

    private static string FullPathRequired(string path, string name)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException($"{name} is required.", name);

        return Path.GetFullPath(path);
    }

    private static string ResolveInside(string root, string relative)
    {
        var candidate = Path.GetFullPath(Path.Combine(root, relative));
        var rootPrefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootPrefix, PathComparison) &&
            !string.Equals(candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), PathComparison))
        {
            throw new InvalidOperationException($"Path escapes root: {relative}");
        }

        return candidate;
    }

    private static bool TryResolveInside(string root, string relative, string optionName, List<string> errors, out string resolved)
    {
        resolved = string.Empty;
        try
        {
            resolved = ResolveInside(root, relative);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or InvalidOperationException)
        {
            errors.Add($"{optionName} must stay inside the configured root: {relative}");
            return false;
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string ToSlash(string path) => path.Replace('\\', '/');

    private static string FromSlash(string path) => path.Replace('/', Path.DirectorySeparatorChar);
}
