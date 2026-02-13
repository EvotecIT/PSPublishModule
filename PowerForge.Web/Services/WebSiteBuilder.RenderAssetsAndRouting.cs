using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PowerForge.Web;

namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    private static string RenderPreloads(AssetRegistrySpec? assets)
    {
        if (assets?.Preloads is null || assets.Preloads.Length == 0)
            return string.Empty;

        return string.Join(Environment.NewLine, assets.Preloads.Select(p =>
        {
            var type = string.IsNullOrWhiteSpace(p.Type) ? string.Empty : $" type=\"{p.Type}\"";
            var cross = string.IsNullOrWhiteSpace(p.Crossorigin) ? string.Empty : $" crossorigin=\"{p.Crossorigin}\"";
            return $"<link rel=\"preload\" href=\"{p.Href}\" as=\"{p.As}\"{type}{cross} />";
        }));
    }

    private static string RenderCriticalCss(AssetRegistrySpec? assets, string rootPath)
    {
        if (assets?.CriticalCss is null || assets.CriticalCss.Length == 0)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        foreach (var css in assets.CriticalCss)
        {
            if (string.IsNullOrWhiteSpace(css.Path)) continue;
            var fullPath = Path.IsPathRooted(css.Path)
                ? css.Path
                : Path.Combine(rootPath, css.Path);
            if (!File.Exists(fullPath)) continue;
            sb.Append("<style>");
            sb.Append(File.ReadAllText(fullPath));
            sb.AppendLine("</style>");
        }
        return sb.ToString();
    }

    private static string RenderCssLinks(IEnumerable<string> cssLinks, AssetRegistrySpec? assets)
    {
        var links = cssLinks.ToArray();
        if (links.Length == 0) return string.Empty;

        if (assets?.CssStrategy is not null &&
            assets.CssStrategy.Equals("async", StringComparison.OrdinalIgnoreCase))
        {
            return string.Join(Environment.NewLine, links.Select(RenderAsyncCssLink));
        }

        return string.Join(Environment.NewLine, links.Select(c => $"<link rel=\"stylesheet\" href=\"{c}\" />"));
    }

    private static string RenderAsyncCssLink(string href)
    {
        return $@"<link rel=""stylesheet"" href=""{href}"" media=""print"" onload=""this.media='all'"" />
<noscript><link rel=""stylesheet"" href=""{href}"" /></noscript>";
    }

    private static string BuildHeadHtml(SiteSpec spec, ContentItem item, IReadOnlyList<ContentItem> allItems, string rootPath)
    {
        var parts = new List<string>();
        var head = spec.Head;
        if (head is not null)
        {
            var links = RenderHeadLinks(head);
            if (!string.IsNullOrWhiteSpace(links))
                parts.Add(links);

            var meta = RenderHeadMeta(head);
            if (!string.IsNullOrWhiteSpace(meta))
                parts.Add(meta);

            if (!string.IsNullOrWhiteSpace(head.Html))
                parts.Add(head.Html!);
        }

        var pageHead = GetMetaString(item.Meta, "head_html");
        if (!string.IsNullOrWhiteSpace(pageHead))
            parts.Add(pageHead);
        var headFile = GetMetaString(item.Meta, "head_file");
        if (!string.IsNullOrWhiteSpace(headFile))
        {
            var resolved = ResolveMetaFilePath(item, rootPath, headFile);
            if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
                parts.Add(File.ReadAllText(resolved));
        }

        var languageAlternates = BuildLanguageAlternateHeadLinks(spec, item, allItems);
        if (!string.IsNullOrWhiteSpace(languageAlternates))
            parts.Add(languageAlternates);

        return string.Join(Environment.NewLine, parts);
    }

    private static string BuildLanguageAlternateHeadLinks(SiteSpec spec, ContentItem item, IReadOnlyList<ContentItem> allItems)
    {
        if (spec is null || item is null || allItems is null)
            return string.Empty;

        var localization = ResolveLocalizationConfig(spec);
        if (!localization.Enabled || localization.Languages.Length <= 1)
            return string.Empty;

        var currentLanguage = ResolveEffectiveLanguageCode(localization, item.Language);
        var alternates = new List<(string HrefLang, string Url)>();
        foreach (var language in localization.Languages)
        {
            var route = ResolveLocalizedPageUrl(spec, localization, item, allItems, language.Code, currentLanguage);
            if (string.IsNullOrWhiteSpace(route))
                continue;

            var absoluteUrl = ResolveAbsoluteUrl(spec.BaseUrl, route);
            if (string.IsNullOrWhiteSpace(absoluteUrl))
                continue;

            alternates.Add((language.Code, absoluteUrl));
        }

        if (alternates.Count <= 1)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        foreach (var alternate in alternates
                     .OrderBy(static value => value.HrefLang, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static value => value.Url, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine(
                $"<link rel=\"alternate\" hreflang=\"{System.Web.HttpUtility.HtmlEncode(alternate.HrefLang)}\" href=\"{System.Web.HttpUtility.HtmlEncode(alternate.Url)}\" />");
        }

        var defaultLanguage = localization.Languages.FirstOrDefault(static language => language.IsDefault);
        if (defaultLanguage is not null)
        {
            var defaultAlternate = alternates.FirstOrDefault(value =>
                value.HrefLang.Equals(defaultLanguage.Code, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(defaultAlternate.Url))
            {
                sb.AppendLine(
                    $"<link rel=\"alternate\" hreflang=\"x-default\" href=\"{System.Web.HttpUtility.HtmlEncode(defaultAlternate.Url)}\" />");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string RenderHeadLinks(HeadSpec head)
    {
        if (head.Links.Length == 0)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        foreach (var link in head.Links)
        {
            if (string.IsNullOrWhiteSpace(link.Rel) || string.IsNullOrWhiteSpace(link.Href))
                continue;

            var rel = System.Web.HttpUtility.HtmlEncode(link.Rel);
            var href = System.Web.HttpUtility.HtmlEncode(link.Href);
            var type = string.IsNullOrWhiteSpace(link.Type) ? string.Empty : $" type=\"{System.Web.HttpUtility.HtmlEncode(link.Type)}\"";
            var sizes = string.IsNullOrWhiteSpace(link.Sizes) ? string.Empty : $" sizes=\"{System.Web.HttpUtility.HtmlEncode(link.Sizes)}\"";
            var cross = string.IsNullOrWhiteSpace(link.Crossorigin) ? string.Empty : $" crossorigin=\"{System.Web.HttpUtility.HtmlEncode(link.Crossorigin)}\"";
            sb.AppendLine($"<link rel=\"{rel}\" href=\"{href}\"{type}{sizes}{cross} />");
        }
        return sb.ToString().TrimEnd();
    }

    private static string RenderHeadMeta(HeadSpec head)
    {
        if (head.Meta.Length == 0)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        foreach (var meta in head.Meta)
        {
            if (string.IsNullOrWhiteSpace(meta.Content))
                continue;

            var name = string.IsNullOrWhiteSpace(meta.Name) ? string.Empty : $" name=\"{System.Web.HttpUtility.HtmlEncode(meta.Name)}\"";
            var property = string.IsNullOrWhiteSpace(meta.Property) ? string.Empty : $" property=\"{System.Web.HttpUtility.HtmlEncode(meta.Property)}\"";
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(property))
                continue;

            var content = System.Web.HttpUtility.HtmlEncode(meta.Content);
            sb.AppendLine($"<meta{name}{property} content=\"{content}\" />");
        }
        return sb.ToString().TrimEnd();
    }

    private static string BuildExtraScriptsHtml(ContentItem item, string rootPath)
    {
        var parts = new List<string>();
        var inline = GetMetaString(item.Meta, "extra_scripts");
        if (!string.IsNullOrWhiteSpace(inline))
            parts.Add(inline);

        var scriptsFile = GetMetaString(item.Meta, "extra_scripts_file");
        if (!string.IsNullOrWhiteSpace(scriptsFile))
        {
            var resolved = ResolveMetaFilePath(item, rootPath, scriptsFile);
            if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
                parts.Add(File.ReadAllText(resolved));
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static string BuildBodyClass(SiteSpec spec, ContentItem item)
    {
        var classes = new List<string>();
        if (!string.IsNullOrWhiteSpace(spec.Head?.BodyClass))
            classes.Add(spec.Head.BodyClass!);
        var pageClass = GetMetaString(item.Meta, "body_class");
        if (!string.IsNullOrWhiteSpace(pageClass))
            classes.Add(pageClass);
        return string.Join(" ", classes.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string BuildOpenGraphHtml(SiteSpec spec, ContentItem item)
    {
        if (spec.Social is null || !spec.Social.Enabled)
            return string.Empty;

        if (TryGetMetaBool(item.Meta, "social", out var enabled) && !enabled)
            return string.Empty;

        if (TryGetMetaBool(item.Meta, "social", out enabled) == false)
            return string.Empty;

        var title = GetMetaString(item.Meta, "social_title");
        if (string.IsNullOrWhiteSpace(title))
            title = item.Title;
        var description = GetMetaString(item.Meta, "social_description");
        if (string.IsNullOrWhiteSpace(description))
            description = item.Description;
        var url = string.IsNullOrWhiteSpace(item.Canonical)
            ? ResolveAbsoluteUrl(spec.BaseUrl, item.OutputPath)
            : item.Canonical;
        var siteName = string.IsNullOrWhiteSpace(spec.Social.SiteName) ? spec.Name : spec.Social.SiteName;
        var imageOverride = GetMetaString(item.Meta, "social_image");
        var image = ResolveAbsoluteUrl(spec.BaseUrl, string.IsNullOrWhiteSpace(imageOverride) ? spec.Social.Image : imageOverride);
        var twitterCard = string.IsNullOrWhiteSpace(spec.Social.TwitterCard) ? "summary" : spec.Social.TwitterCard;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<!-- Open Graph -->");
        sb.AppendLine($@"<meta property=""og:title"" content=""{System.Web.HttpUtility.HtmlEncode(title)}"" />");
        if (!string.IsNullOrWhiteSpace(description))
            sb.AppendLine($@"<meta property=""og:description"" content=""{System.Web.HttpUtility.HtmlEncode(description)}"" />");
        sb.AppendLine(@"<meta property=""og:type"" content=""website"" />");
        if (!string.IsNullOrWhiteSpace(url))
            sb.AppendLine($@"<meta property=""og:url"" content=""{System.Web.HttpUtility.HtmlEncode(url)}"" />");
        if (!string.IsNullOrWhiteSpace(image))
            sb.AppendLine($@"<meta property=""og:image"" content=""{System.Web.HttpUtility.HtmlEncode(image)}"" />");
        if (!string.IsNullOrWhiteSpace(siteName))
            sb.AppendLine($@"<meta property=""og:site_name"" content=""{System.Web.HttpUtility.HtmlEncode(siteName)}"" />");

        sb.AppendLine();
        sb.AppendLine("<!-- Twitter Card -->");
        sb.AppendLine($@"<meta name=""twitter:card"" content=""{System.Web.HttpUtility.HtmlEncode(twitterCard)}"" />");
        sb.AppendLine($@"<meta name=""twitter:title"" content=""{System.Web.HttpUtility.HtmlEncode(title)}"" />");
        if (!string.IsNullOrWhiteSpace(description))
            sb.AppendLine($@"<meta name=""twitter:description"" content=""{System.Web.HttpUtility.HtmlEncode(description)}"" />");
        if (!string.IsNullOrWhiteSpace(image))
            sb.AppendLine($@"<meta name=""twitter:image"" content=""{System.Web.HttpUtility.HtmlEncode(image)}"" />");

        return sb.ToString().TrimEnd();
    }

    private static string BuildStructuredDataHtml(SiteSpec spec, ContentItem item, BreadcrumbItem[] breadcrumbs)
    {
        if (spec.StructuredData is null || !spec.StructuredData.Enabled)
            return string.Empty;

        if (TryGetMetaBool(item.Meta, "structured_data", out var enabled) && !enabled)
            return string.Empty;

        if (TryGetMetaBool(item.Meta, "structured_data", out enabled) == false)
            return string.Empty;

        if (!spec.StructuredData.Breadcrumbs || breadcrumbs.Length == 0)
            return string.Empty;

        var baseUrl = spec.BaseUrl?.TrimEnd('/') ?? string.Empty;
        var lines = new List<string>
        {
            "<script type=\"application/ld+json\">",
            "  {",
            "      \"@context\": \"https://schema.org\",",
            "      \"@type\": \"BreadcrumbList\",",
            "      \"itemListElement\": ["
        };

        for (var i = 0; i < breadcrumbs.Length; i++)
        {
            var crumb = breadcrumbs[i];
            var url = ResolveAbsoluteUrl(baseUrl, crumb.Url);
            var name = System.Text.Json.JsonSerializer.Serialize(crumb.Title ?? string.Empty);
            var itemUrl = System.Text.Json.JsonSerializer.Serialize(url ?? string.Empty);
            var suffix = i == breadcrumbs.Length - 1 ? string.Empty : ",";
            lines.Add($"          {{ \"@type\": \"ListItem\", \"position\": {i + 1}, \"name\": {name}, \"item\": {itemUrl} }}{suffix}");
        }

        lines.Add("      ]");
        lines.Add("  }");
        lines.Add("  </script>");

        return string.Join(Environment.NewLine, lines);
    }

    private static string? ResolveMetaFilePath(ContentItem item, string rootPath, string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        var allowedRoots = new List<string> { NormalizeRootPathForSink(rootPath) };
        var baseDir = Path.GetDirectoryName(item.SourcePath);
        if (!string.IsNullOrWhiteSpace(baseDir))
            allowedRoots.Add(NormalizeRootPathForSink(baseDir));

        if (Path.IsPathRooted(filePath))
        {
            var rootedPath = Path.GetFullPath(filePath);
            return IsPathWithinAnyRoot(allowedRoots, rootedPath) ? rootedPath : null;
        }

        if (!string.IsNullOrWhiteSpace(baseDir))
        {
            var candidate = Path.GetFullPath(Path.Combine(baseDir, filePath));
            if (IsPathWithinAnyRoot(allowedRoots, candidate) && File.Exists(candidate))
                return candidate;
        }

        var rootCandidate = Path.GetFullPath(Path.Combine(rootPath, filePath));
        return IsPathWithinAnyRoot(allowedRoots, rootCandidate) ? rootCandidate : null;
    }

    private static string ResolveAbsoluteUrl(string? baseUrl, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return path;
        if (string.IsNullOrWhiteSpace(baseUrl)) return path;
        var trimmedBase = baseUrl.TrimEnd('/');
        var trimmedPath = path.StartsWith("/") ? path : "/" + path;
        return trimmedBase + trimmedPath;
    }

    private static string? ResolveThemeRoot(SiteSpec spec, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(spec.DefaultTheme)) return null;
        var basePath = ResolveThemesRoot(spec, rootPath);
        return string.IsNullOrWhiteSpace(basePath) ? null : Path.Combine(basePath, spec.DefaultTheme);
    }

    private static string? ResolveThemesRoot(SiteSpec spec, string rootPath)
    {
        var themesRoot = string.IsNullOrWhiteSpace(spec.ThemesRoot) ? "themes" : spec.ThemesRoot;
        return Path.IsPathRooted(themesRoot) ? themesRoot : Path.Combine(rootPath, themesRoot);
    }

    private static IEnumerable<(string Root, ThemeManifest Manifest)> BuildThemeChain(string themeRoot, ThemeManifest manifest)
    {
        var chain = new List<(string Root, ThemeManifest Manifest)>();
        var current = manifest;
        var currentRoot = themeRoot;
        while (current is not null)
        {
            chain.Add((currentRoot, current));
            if (current.Base is null || string.IsNullOrWhiteSpace(current.BaseRoot))
                break;
            currentRoot = current.BaseRoot;
            current = current.Base;
        }

        chain.Reverse();
        return chain;
    }

    private static IEnumerable<string> ResolveCssLinks(AssetRegistrySpec? assets, string route)
    {
        var bundles = ResolveBundles(assets, route);
        return bundles.SelectMany(b => b.Css).Distinct();
    }

    private static IEnumerable<string> ResolveJsLinks(AssetRegistrySpec? assets, string route)
    {
        var bundles = ResolveBundles(assets, route);
        return bundles.SelectMany(b => b.Js).Distinct();
    }

    private static IEnumerable<AssetBundleSpec> ResolveBundles(AssetRegistrySpec? assets, string route)
    {
        if (assets is null)
            return Array.Empty<AssetBundleSpec>();

        var bundleMap = assets.Bundles.ToDictionary(b => b.Name, StringComparer.OrdinalIgnoreCase);
        var results = new List<AssetBundleSpec>();
        foreach (var mapping in assets.RouteBundles)
        {
            if (!GlobMatch(mapping.Match, route)) continue;
            foreach (var name in mapping.Bundles)
            {
                if (bundleMap.TryGetValue(name, out var bundle))
                    results.Add(bundle);
            }
        }

        return results;
    }

    private static AssetRegistrySpec? BuildAssetRegistry(
        SiteSpec spec,
        string? themeRoot,
        ThemeManifest? manifest)
    {
        AssetRegistrySpec? themeAssets = null;
        if (manifest is not null && !string.IsNullOrWhiteSpace(themeRoot))
        {
            foreach (var entry in BuildThemeChain(themeRoot, manifest))
            {
                if (entry.Manifest.Assets is null) continue;
                if (entry.Manifest.Base is not null &&
                    ReferenceEquals(entry.Manifest.Assets, entry.Manifest.Base.Assets))
                    continue;
                var themeName = string.IsNullOrWhiteSpace(entry.Manifest.Name)
                    ? Path.GetFileName(entry.Root)
                    : entry.Manifest.Name;
                if (string.IsNullOrWhiteSpace(themeName)) continue;
                var normalized = NormalizeThemeAssets(entry.Manifest.Assets, themeName, spec);
                themeAssets = MergeAssetRegistryForTheme(themeAssets, normalized);
            }
        }

        return MergeAssetRegistry(themeAssets, spec.AssetRegistry);
    }

    private static AssetRegistrySpec? MergeAssetRegistry(AssetRegistrySpec? baseAssets, AssetRegistrySpec? overrides)
    {
        if (baseAssets is null && overrides is null) return null;
        if (baseAssets is null) return CloneAssets(overrides);
        if (overrides is null) return CloneAssets(baseAssets);

        var bundleMap = baseAssets.Bundles.ToDictionary(b => b.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var bundle in overrides.Bundles)
            bundleMap[bundle.Name] = CloneBundle(bundle);

        var routeBundles = overrides.RouteBundles.Length > 0 ? overrides.RouteBundles : baseAssets.RouteBundles;
        var preloads = overrides.Preloads.Length > 0 ? overrides.Preloads : baseAssets.Preloads;
        var criticalCss = overrides.CriticalCss.Length > 0 ? overrides.CriticalCss : baseAssets.CriticalCss;
        var cssStrategy = !string.IsNullOrWhiteSpace(overrides.CssStrategy) ? overrides.CssStrategy : baseAssets.CssStrategy;

        return new AssetRegistrySpec
        {
            Bundles = bundleMap.Values.ToArray(),
            RouteBundles = routeBundles.Select(r => new RouteBundleSpec
            {
                Match = r.Match,
                Bundles = r.Bundles.ToArray()
            }).ToArray(),
            Preloads = preloads.Select(p => new PreloadSpec
            {
                Href = p.Href,
                As = p.As,
                Type = p.Type,
                Crossorigin = p.Crossorigin
            }).ToArray(),
            CriticalCss = criticalCss.Select(c => new CriticalCssSpec
            {
                Name = c.Name,
                Path = c.Path
            }).ToArray(),
            CssStrategy = cssStrategy
        };
    }

    private static AssetRegistrySpec? MergeAssetRegistryForTheme(AssetRegistrySpec? baseAssets, AssetRegistrySpec? child)
    {
        if (baseAssets is null && child is null) return null;
        if (baseAssets is null) return CloneAssets(child);
        if (child is null) return CloneAssets(baseAssets);

        var bundleMap = baseAssets.Bundles.ToDictionary(b => b.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var bundle in child.Bundles)
            bundleMap[bundle.Name] = CloneBundle(bundle);

        var criticalMap = baseAssets.CriticalCss.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var css in child.CriticalCss)
            criticalMap[css.Name] = new CriticalCssSpec { Name = css.Name, Path = css.Path };

        return new AssetRegistrySpec
        {
            Bundles = bundleMap.Values.ToArray(),
            RouteBundles = baseAssets.RouteBundles.Concat(child.RouteBundles)
                .Select(r => new RouteBundleSpec
                {
                    Match = r.Match,
                    Bundles = r.Bundles.ToArray()
                }).ToArray(),
            Preloads = baseAssets.Preloads.Concat(child.Preloads)
                .Select(p => new PreloadSpec
                {
                    Href = p.Href,
                    As = p.As,
                    Type = p.Type,
                    Crossorigin = p.Crossorigin
                }).ToArray(),
            CriticalCss = criticalMap.Values.ToArray(),
            CssStrategy = child.CssStrategy ?? baseAssets.CssStrategy
        };
    }

    private static AssetRegistrySpec? NormalizeThemeAssets(AssetRegistrySpec assets, string themeName, SiteSpec spec)
    {
        if (string.IsNullOrWhiteSpace(themeName)) return CloneAssets(assets);
        var themesRoot = ResolveThemesFolder(spec);
        var urlPrefix = "/" + themesRoot.TrimEnd('/', '\\') + "/" + themeName.Trim().Trim('/', '\\') + "/";
        var filePrefix = themesRoot.TrimEnd('/', '\\') + "/" + themeName.Trim().Trim('/', '\\') + "/";

        var normalized = CloneAssets(assets) ?? new AssetRegistrySpec();
        foreach (var bundle in normalized.Bundles)
        {
            bundle.Css = bundle.Css.Select(path => NormalizeAssetUrl(path, urlPrefix)).ToArray();
            bundle.Js = bundle.Js.Select(path => NormalizeAssetUrl(path, urlPrefix)).ToArray();
        }

        foreach (var preload in normalized.Preloads)
            preload.Href = NormalizeAssetUrl(preload.Href, urlPrefix);

        foreach (var css in normalized.CriticalCss)
        {
            if (!string.IsNullOrWhiteSpace(css.Path) && !Path.IsPathRooted(css.Path) && !IsExternalPath(css.Path))
                css.Path = filePrefix + css.Path.TrimStart('/', '\\');
        }

        return normalized;
    }

    private static string NormalizeAssetUrl(string path, string prefix)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        if (IsExternalPath(path) || path.StartsWith("/", StringComparison.Ordinal))
            return path;
        return prefix + path.TrimStart('/', '\\');
    }

    private static bool IsExternalPath(string path)
    {
        return path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("//", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("data:", StringComparison.OrdinalIgnoreCase);
    }

    private static AssetRegistrySpec? CloneAssets(AssetRegistrySpec? spec)
    {
        if (spec is null) return null;
        return new AssetRegistrySpec
        {
            Bundles = spec.Bundles.Select(CloneBundle).ToArray(),
            RouteBundles = spec.RouteBundles.Select(r => new RouteBundleSpec
            {
                Match = r.Match,
                Bundles = r.Bundles.ToArray()
            }).ToArray(),
            Preloads = spec.Preloads.Select(p => new PreloadSpec
            {
                Href = p.Href,
                As = p.As,
                Type = p.Type,
                Crossorigin = p.Crossorigin
            }).ToArray(),
            CriticalCss = spec.CriticalCss.Select(c => new CriticalCssSpec
            {
                Name = c.Name,
                Path = c.Path
            }).ToArray(),
            CssStrategy = spec.CssStrategy
        };
    }

    private static AssetBundleSpec CloneBundle(AssetBundleSpec bundle)
    {
        return new AssetBundleSpec
        {
            Name = bundle.Name,
            Css = bundle.Css.ToArray(),
            Js = bundle.Js.ToArray()
        };
    }

    private static string ResolveThemesFolder(SiteSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.ThemesRoot)) return "themes";
        if (Path.IsPathRooted(spec.ThemesRoot)) return "themes";
        return spec.ThemesRoot.Trim().TrimStart('/', '\\');
    }

    private static bool GlobMatch(string pattern, string value)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        var regex = "^" + Regex.Escape(pattern)
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*") + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
    }

    private static string BuildRoute(string baseOutput, string slug, TrailingSlashMode slashMode)
    {
        var basePath = NormalizePath(baseOutput);
        var slugPath = NormalizePath(slug);
        string combined;

        if (string.IsNullOrWhiteSpace(slugPath) || slugPath == "index")
        {
            combined = basePath;
        }
        else if (string.IsNullOrWhiteSpace(basePath) || basePath == "/")
        {
            combined = slugPath;
        }
        else
        {
            combined = $"{basePath}/{slugPath}";
        }

        combined = "/" + combined.Trim('/');
        return EnsureTrailingSlash(combined, slashMode);
    }

    private static string EnsureTrailingSlash(string path, TrailingSlashMode mode)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";

        if (mode == TrailingSlashMode.Never)
            return path.EndsWith("/") && path.Length > 1 ? path.TrimEnd('/') : path;

        if (mode == TrailingSlashMode.Always && !path.EndsWith("/"))
            return path + "/";

        return path;
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var trimmed = path.Trim();
        if (trimmed == "/") return "/";

        var normalized = trimmed.Replace('\\', '/');
        var segments = normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment != ".")
            .ToList();
        if (segments.Count == 0)
            return string.Empty;

        var stack = new List<string>(segments.Count);
        foreach (var segment in segments)
        {
            if (segment == "..")
            {
                if (stack.Count > 0)
                    stack.RemoveAt(stack.Count - 1);
                continue;
            }
            stack.Add(segment);
        }

        return string.Join("/", stack);
    }

    private static string ResolveOutputDirectory(string outputRoot, string route)
    {
        var trimmed = route.Trim('/');
        if (string.IsNullOrWhiteSpace(trimmed))
            return outputRoot;

        var combined = Path.GetFullPath(Path.Combine(outputRoot, trimmed.Replace('/', Path.DirectorySeparatorChar)));
        var normalizedRoot = NormalizeRootPathForSink(outputRoot);
        if (!IsPathWithinRoot(normalizedRoot, combined))
            return outputRoot;
        return combined;
    }

    private static string NormalizeAlias(string alias)
    {
        if (Uri.TryCreate(alias, UriKind.Absolute, out var uri))
            return uri.AbsolutePath;
        return alias;
    }

    private static string Slugify(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var lower = input.Trim().ToLowerInvariant();
        var sb = new System.Text.StringBuilder();
        foreach (var ch in lower)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (char.IsWhiteSpace(ch) || ch == '-' || ch == '_') sb.Append('-');
        }
        var slug = sb.ToString();
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }

    private static IEnumerable<string> EnumerateCollectionFiles(string rootPath, string[] contentRoots, string input, string[]? includePatterns, string[]? excludePatterns)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Array.Empty<string>();

        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var full in BuildCollectionInputCandidates(rootPath, contentRoots, input))
        {
            if (full.Contains('*', StringComparison.Ordinal))
            {
                foreach (var file in EnumerateCollectionFilesWithWildcard(full, includePatterns ?? Array.Empty<string>(), excludePatterns ?? Array.Empty<string>()))
                {
                    if (seen.Add(file))
                        results.Add(file);
                }
                continue;
            }

            if (!Directory.Exists(full))
                continue;

            foreach (var file in FilterByPatterns(full,
                         Directory.EnumerateFiles(full, "*.md", SearchOption.AllDirectories),
                         includePatterns ?? Array.Empty<string>(),
                         excludePatterns ?? Array.Empty<string>()))
            {
                if (seen.Add(file))
                    results.Add(file);
            }
        }

        return results
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> BuildCollectionInputCandidates(string rootPath, string[] contentRoots, string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Array.Empty<string>();
        if (Path.IsPathRooted(input))
            return new[] { Path.GetFullPath(input) };

        var normalizedInput = input.Trim();
        var normalizedRoot = Path.GetFullPath(rootPath);
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(Path.Combine(normalizedRoot, normalizedInput))
        };

        var inputSegments = normalizedInput.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        var firstSegment = inputSegments.Length == 0 ? string.Empty : inputSegments[0];
        foreach (var contentRoot in contentRoots)
        {
            if (string.IsNullOrWhiteSpace(contentRoot))
                continue;

            var fullContentRoot = Path.GetFullPath(contentRoot);
            candidates.Add(Path.GetFullPath(Path.Combine(fullContentRoot, normalizedInput)));

            if (string.IsNullOrWhiteSpace(firstSegment))
                continue;
            var rootName = Path.GetFileName(fullContentRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!firstSegment.Equals(rootName, StringComparison.OrdinalIgnoreCase))
                continue;

            var remainder = normalizedInput.Substring(firstSegment.Length).TrimStart('/', '\\');
            candidates.Add(string.IsNullOrWhiteSpace(remainder)
                ? fullContentRoot
                : Path.GetFullPath(Path.Combine(fullContentRoot, remainder)));
        }

        return candidates
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateCollectionFilesWithWildcard(string path, string[] includePatterns, string[] excludePatterns)
    {
        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var parts = normalized.Split('*');
        if (parts.Length != 2)
            return Array.Empty<string>();

        var basePath = parts[0].TrimEnd(Path.DirectorySeparatorChar);
        var tail = parts[1].TrimStart(Path.DirectorySeparatorChar);
        if (!Directory.Exists(basePath))
            return Array.Empty<string>();

        var results = new List<string>();
        foreach (var dir in Directory.GetDirectories(basePath).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            var candidate = string.IsNullOrEmpty(tail) ? dir : Path.Combine(dir, tail);
            if (!Directory.Exists(candidate))
                continue;
            var files = Directory.EnumerateFiles(candidate, "*.md", SearchOption.AllDirectories)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
            results.AddRange(FilterByPatterns(candidate, files, includePatterns, excludePatterns));
        }

        return results;
    }

    private static IEnumerable<string> FilterByPatterns(string basePath, IEnumerable<string> files, string[] includePatterns, string[] excludePatterns)
    {
        var includes = NormalizePatterns(includePatterns);
        var excludes = NormalizePatterns(excludePatterns);

        foreach (var file in files)
        {
            if (excludes.Length > 0 && MatchesAny(excludes, basePath, file))
                continue;
            if (includes.Length == 0 || MatchesAny(includes, basePath, file))
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

    private static bool MatchesAny(string[] patterns, string basePath, string file)
    {
        foreach (var pattern in patterns)
        {
            if (Path.IsPathRooted(pattern))
            {
                if (GlobMatch(pattern.Replace('\\', '/'), file.Replace('\\', '/')))
                    return true;
                continue;
            }

            var relative = Path.GetRelativePath(basePath, file).Replace('\\', '/');
            if (GlobMatch(pattern, relative))
                return true;
        }
        return false;
    }

    private static bool IsPathWithinBase(string basePath, string filePath)
    {
        var fullBase = Path.GetFullPath(basePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullFile = Path.GetFullPath(filePath);

        if (fullFile.Equals(fullBase, FileSystemPathComparison))
            return true;

        return fullFile.StartsWith(fullBase + Path.DirectorySeparatorChar, FileSystemPathComparison);
    }

    private static bool IsPathWithinAnyRoot(IEnumerable<string> normalizedRoots, string candidatePath)
    {
        var full = Path.GetFullPath(candidatePath);
        foreach (var root in normalizedRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;
            if (full.StartsWith(root, FileSystemPathComparison))
                return true;
        }

        return false;
    }

    internal static string[] BuildContentRootsForDiscovery(WebSitePlan plan)
        => BuildContentRoots(plan);

    internal static string[] EnumerateCollectionFilesForDiscovery(WebSitePlan plan, CollectionSpec collection)
    {
        var roots = BuildContentRoots(plan);
        return EnumerateCollectionFiles(plan.RootPath, roots, collection.Input, collection.Include, collection.Exclude)
            .ToArray();
    }

    internal static string? ResolveCollectionRootForDiscovery(WebSitePlan plan, CollectionSpec collection, string filePath)
    {
        var roots = BuildContentRoots(plan);
        return ResolveCollectionRootForFile(plan.RootPath, roots, collection.Input, filePath);
    }
}
