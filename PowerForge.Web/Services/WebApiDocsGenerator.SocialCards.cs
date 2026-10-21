using System.Security.Cryptography;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    private static string ResolveApiSocialImagePath(
        WebApiDocsOptions options,
        ApiSocialProfile social,
        string title,
        string description,
        string route,
        ApiTypeModel? type,
        ApiCardContext? context)
    {
        var imagePath = social.Image;
        if (options.AutoGenerateSocialCards)
        {
            var generated = TryGenerateApiSocialCard(options, title, description, route, social.SiteName, type, context);
            if (!string.IsNullOrWhiteSpace(generated))
                imagePath = generated;
        }

        return imagePath;
    }

    private static string TryGenerateApiSocialCard(
        WebApiDocsOptions options,
        string title,
        string description,
        string route,
        string siteName,
        ApiTypeModel? type,
        ApiCardContext? context)
    {
        if (string.IsNullOrWhiteSpace(options.OutputPath))
            return string.Empty;

        var siteRoot = ResolveApiSiteRoot(options.OutputPath, options.BaseUrl);
        if (string.IsNullOrWhiteSpace(siteRoot))
            return string.Empty;

        if (context is not null)
            return GenerateThemedApiCard(options, title, description, route, siteName, type, context, siteRoot);

        var cardPath = NormalizeApiSocialCardPath(options.SocialCardPath);
        var routeKey = NormalizeApiRouteKey(route);
        var routeLabel = BuildApiRouteDisplayLabel(routeKey);
        var slug = Slugify(routeKey.Replace('/', '-'));
        if (string.IsNullOrWhiteSpace(slug))
            slug = "api";

        var hashInput = string.Join("|", new[]
        {
            routeKey,
            title ?? string.Empty,
            description ?? string.Empty,
            siteName ?? string.Empty,
            options.Title ?? string.Empty
        });
        var hash = ComputeApiSocialHash(hashInput);
        var fileName = $"{slug}-{hash}.png";
        var relativePath = $"{cardPath.TrimStart('/')}/{fileName}".TrimStart('/');
        var fullPath = Path.GetFullPath(Path.Combine(siteRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsApiPathWithinRoot(siteRoot, fullPath))
            return string.Empty;

        var bytes = WebSocialCardGenerator.RenderPng(new WebSocialCardGenerator.SocialCardRenderOptions
        {
            Title = title,
            Description = description,
            Eyebrow = siteName,
            Badge = "API",
            FooterLabel = routeLabel,
            Width = options.SocialCardWidth,
            Height = options.SocialCardHeight,
            StyleKey = "api",
            VariantKey = "reference"
        });
        if (bytes is null || bytes.Length == 0)
        {
            if (options.SocialCardsRequired)
                throw new InvalidOperationException($"API social card not rendered for '{route}'. Install ImageMagick on Linux and check renderer diagnostics.");
            System.Diagnostics.Trace.TraceWarning($"API social card not rendered for '{route}'.");
            return string.Empty;
        }

        WriteApiBytesIfChanged(fullPath, bytes);
        return "/" + relativePath.Replace('\\', '/');
    }

    private sealed record ApiCardContext(SiteSpec Site, string Root);

    private static ApiCardContext? LoadApiSocialCardContext(WebApiDocsOptions options)
    {
        if (!options.AutoGenerateSocialCards)
            return null;
        var site = LoadApiSiteSpec(options);
        if (site?.Social?.SocialCardsRequired == true)
            options.SocialCardsRequired = true;
        var hasNewContentOptions = !string.IsNullOrWhiteSpace(options.SocialCardTitle) ||
            !string.IsNullOrWhiteSpace(site?.Social?.GeneratedCardTitle) || site?.Social?.GeneratedCardTitlesByCollection is { Count: > 0 };
        if (string.IsNullOrWhiteSpace(options.SocialCardTheme) && !hasNewContentOptions)
            return null;
        site ??= new SiteSpec { Name = options.SiteName ?? options.Title, Social = new SocialSpec() };
        site.Social ??= new SocialSpec();
        if (!string.IsNullOrWhiteSpace(options.SocialCardTheme) &&
            site.Social.GeneratedCardThemes?.ContainsKey(options.SocialCardTheme) != true)
            throw new InvalidOperationException($"API social card theme '{options.SocialCardTheme}' was not found in Social.GeneratedCardThemes. Set SiteConfigPath to the site's config.");
        var config = FirstNonEmpty(options.SiteConfigPath, options.NavJsonPath);
        var root = string.IsNullOrWhiteSpace(config) ? Directory.GetCurrentDirectory() : Path.GetDirectoryName(Path.GetFullPath(config))!;
        if (!string.IsNullOrWhiteSpace(config))
        {
            var (_, resolvedPath) = WebSiteSpecLoader.LoadWithPath(config, WebJson.Options);
            root = WebSitePlanner.Plan(site, resolvedPath, WebJson.Options).RootPath;
        }
        // Reuse content planning; absent an explicit API theme, retain the legacy API layout.
        site.Social.GeneratedCardTheme = options.SocialCardTheme;
        site.Social.GeneratedCardThemesByCollection = null;
        if (string.IsNullOrWhiteSpace(options.SocialCardTheme))
        {
            site.Social.GeneratedCardStyle = "api";
            site.Social.GeneratedCardVariant = "reference";
        }
        site.Social.GeneratedCardsPath = NormalizeApiSocialCardPath(options.SocialCardPath);
        site.Social.GeneratedCardWidth = options.SocialCardWidth;
        site.Social.GeneratedCardHeight = options.SocialCardHeight;
        return new ApiCardContext(site, root);
    }

    private static string GenerateThemedApiCard(WebApiDocsOptions options, string title, string description, string route,
        string siteName, ApiTypeModel? type, ApiCardContext context, string siteRoot)
    {
        var titleSource = options.SocialCardTitle;
        if (!string.IsNullOrWhiteSpace(titleSource) && titleSource.ToLowerInvariant() is not ("name" or "seo"))
            throw new InvalidOperationException($"Unknown API social card title source '{titleSource}'. Use name or seo.");
        var collection = options.Type == ApiDocsType.PowerShell ? "powershell" : "api";
        var item = new ContentItem
        {
            Title = type is null ? title : StripGenericArity(type.Name),
            Description = description,
            OutputPath = route,
            Collection = collection,
            Meta = new Dictionary<string, object?>
            {
                ["social_card_badge"] = options.Type == ApiDocsType.PowerShell ? "POWERSHELL" : "API",
                ["social_card_code"] = type is null ? null : BuildApiCardCode(options, type)
            }
        };
        if (titleSource?.Equals("name", StringComparison.OrdinalIgnoreCase) == true)
            item.Meta["social_title"] = item.Title;
        else if (titleSource?.Equals("seo", StringComparison.OrdinalIgnoreCase) == true)
            item.Meta["social_title"] = title;
        var plan = WebSiteBuilder.CreateSocialCardGenerationPlan(context.Site, item, title, description, siteName, context.Root)!;
        var fullPath = Path.GetFullPath(Path.Combine(siteRoot, plan.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsApiPathWithinRoot(siteRoot, fullPath))
            throw new InvalidOperationException("API social card output must be within the site output root.");
        var bytes = WebSocialCardGenerator.RenderPng(plan.RenderOptions);
        if (bytes is not { Length: > 0 })
        {
            var message = $"API social card not rendered for '{route}'. Install ImageMagick on Linux and check renderer diagnostics.";
            if (options.SocialCardsRequired)
                throw new InvalidOperationException(message);
            System.Diagnostics.Trace.TraceWarning(message);
            return string.Empty;
        }
        WriteApiBytesIfChanged(fullPath, bytes);
        return "/" + plan.RelativePath.Replace('\\', '/');
    }

    private static string BuildApiCardCode(WebApiDocsOptions options, ApiTypeModel type)
    {
        if (options.Type == ApiDocsType.PowerShell)
            return type.Methods.FirstOrDefault(method => !string.IsNullOrWhiteSpace(method.Signature))?.Signature ?? type.Name;
        var modifiers = type.IsStatic ? "static " : type.IsAbstract ? "abstract " : type.IsSealed && type.Kind.Equals("Class", StringComparison.OrdinalIgnoreCase) ? "sealed " : string.Empty;
        var parameters = type.TypeParameters.Count == 0 ? string.Empty : "<" + string.Join(", ", type.TypeParameters.Select(parameter => parameter.Name)) + ">";
        return $"public {modifiers}{type.Kind.ToLowerInvariant()} {StripGenericArity(type.Name)}{parameters}";
    }

    private static string ResolveApiSiteRoot(string outputPath, string? baseUrl)
    {
        var root = Path.GetFullPath(outputPath);
        var normalizedBase = baseUrl ?? string.Empty;
        if (Uri.TryCreate(normalizedBase, UriKind.Absolute, out var absolute))
            normalizedBase = absolute.AbsolutePath;

        var segmentCount = normalizedBase
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Length;
        if (segmentCount <= 0)
            return root;

        for (var i = 0; i < segmentCount; i++)
        {
            var parent = Directory.GetParent(root);
            if (parent is null)
                return Path.GetFullPath(outputPath);
            root = parent.FullName;
        }

        return root;
    }

    private static string NormalizeApiSocialCardPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "/assets/social/generated/api";

        var normalized = value.Trim().Replace('\\', '/');
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = "/" + normalized.TrimStart('/');
        return normalized.TrimEnd('/');
    }

    private static string NormalizeApiRouteKey(string route)
    {
        var value = route ?? string.Empty;
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
            value = absolute.AbsolutePath;

        value = value.Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(value))
            return "api";
        if (!value.StartsWith("/", StringComparison.Ordinal))
            value = "/" + value;
        return value.Trim('/');
    }

    private static string BuildApiRouteDisplayLabel(string routeKey)
    {
        if (string.IsNullOrWhiteSpace(routeKey))
            return "/api";

        var segments = routeKey
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return "/api";

        if (segments.Length == 1)
            return "/" + segments[0];

        if (string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase))
        {
            if (segments.Length == 2)
                return $"/api/{segments[1]}";
            if (segments.Length >= 3 &&
                string.Equals(segments[1], "powershell", StringComparison.OrdinalIgnoreCase))
                return $"/api/powershell/{segments[^1]}";
            return $"/api/{segments[^1]}";
        }

        return segments.Length > 2
            ? $"/{segments[0]}/.../{segments[^1]}"
            : "/" + string.Join("/", segments);
    }

    private static string ComputeApiSocialHash(string input)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(input ?? string.Empty);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant()[..10];
    }

    private static bool WriteApiBytesIfChanged(string path, byte[] content)
    {
        try
        {
            if (File.Exists(path))
            {
                var existing = File.ReadAllBytes(path);
                if (existing.AsSpan().SequenceEqual(content))
                    return false;
            }
        }
        catch
        {
            // Fall through and rewrite.
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllBytes(path, content);
        return true;
    }

    private static bool IsApiPathWithinRoot(string rootPath, string candidatePath)
    {
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var full = Path.GetFullPath(candidatePath);
        if (string.Equals(root, full, StringComparison.OrdinalIgnoreCase))
            return true;
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
