using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

/// <summary>Markdown rendering, prism injection, and data-template helpers.</summary>
public static partial class WebSiteBuilder
{
    private static string RenderMarkdown(string content, string sourcePath, BuildCacheSpec? cache, string? cacheRoot)
    {
        if (cache?.Enabled != true || string.IsNullOrWhiteSpace(cacheRoot))
            return MarkdownRenderer.RenderToHtml(content);

        var key = ComputeCacheKey(content, sourcePath, cache);
        var cacheFile = Path.Combine(cacheRoot, key + ".html");
        if (File.Exists(cacheFile))
            return File.ReadAllText(cacheFile);

        var html = MarkdownRenderer.RenderToHtml(content);
        File.WriteAllText(cacheFile, html);
        return html;
    }

    private static void EnsurePrismAssets(Dictionary<string, object?> meta, string htmlContent, SiteSpec spec, string rootPath)
    {
        if (meta is null) return;

        if (TryGetMetaBool(meta, "prism", out var prismEnabled) && !prismEnabled)
            return;

        var prismSpec = spec.Prism;
        var mode = GetMetaStringOrNull(meta, "prism_mode") ?? prismSpec?.Mode ?? "auto";
        if (mode.Equals("off", StringComparison.OrdinalIgnoreCase))
            return;

        var hasCode = ContainsMarkdownCode(htmlContent);
        var always = mode.Equals("always", StringComparison.OrdinalIgnoreCase);
        if (!hasCode && !always && !(prismEnabled))
            return;

        if (MetaContains(meta, "extra_css", "prism") || MetaContains(meta, "extra_scripts", "prism"))
            return;

        var sourceOverride = GetMetaStringOrNull(meta, "prism_source");
        var source = sourceOverride
            ?? prismSpec?.Source
            ?? spec.AssetPolicy?.Mode
            ?? "cdn";

        var localAssets = ResolvePrismLocalAssets(meta, prismSpec);
        var localExists = LocalPrismAssetsExist(localAssets, rootPath);
        var sourceIsLocal = source.Equals("local", StringComparison.OrdinalIgnoreCase);
        var sourceIsHybrid = source.Equals("hybrid", StringComparison.OrdinalIgnoreCase);
        var useLocal = (sourceIsLocal && localExists) || (sourceIsHybrid && localExists);
        if (sourceIsLocal && !localExists)
            Trace.TraceWarning("Prism source is set to local, but local assets are missing. Falling back to CDN.");

        var css = useLocal
            ? string.Join(Environment.NewLine, new[]
            {
                $"<link rel=\"stylesheet\" href=\"{localAssets.light}\" media=\"(prefers-color-scheme: light)\" />",
                $"<link rel=\"stylesheet\" href=\"{localAssets.dark}\" media=\"(prefers-color-scheme: dark)\" />"
            })
            : BuildPrismCdnCss(meta, prismSpec);

        var scripts = useLocal
            ? string.Join(Environment.NewLine, new[]
            {
                $"<script src=\"{localAssets.core}\"></script>",
                $"<script src=\"{localAssets.autoloader}\"></script>",
                BuildPrismInitScript(localAssets.langPath)
            })
            : BuildPrismCdnScripts(meta, prismSpec);

        AppendMetaHtml(meta, "extra_css", css);
        AppendMetaHtml(meta, "extra_scripts", scripts);
    }

    private static (string light, string dark, string core, string autoloader, string langPath) ResolvePrismLocalAssets(
        Dictionary<string, object?> meta,
        PrismSpec? prismSpec)
    {
        var local = prismSpec?.Local;
        var lightOverride = GetMetaStringOrNull(meta, "prism_css_light") ?? local?.ThemeLight ?? prismSpec?.ThemeLight;
        var darkOverride = GetMetaStringOrNull(meta, "prism_css_dark") ?? local?.ThemeDark ?? prismSpec?.ThemeDark;
        var light = ResolvePrismThemeHref(lightOverride, isCdn: false, cdnBase: null, defaultCdnName: "prism", defaultLocalPath: "/assets/prism/prism.css");
        var dark = ResolvePrismThemeHref(darkOverride, isCdn: false, cdnBase: null, defaultCdnName: "prism-okaidia", defaultLocalPath: "/assets/prism/prism-okaidia.css");
        var core = GetMetaStringOrNull(meta, "prism_core") ?? local?.Core ?? "/assets/prism/prism-core.js";
        var autoloader = GetMetaStringOrNull(meta, "prism_autoloader") ?? local?.Autoloader ?? "/assets/prism/prism-autoloader.js";
        var langPath = GetMetaStringOrNull(meta, "prism_lang_path") ?? local?.LanguagesPath ?? "/assets/prism/components/";
        return (light, dark, core, autoloader, langPath);
    }

    private static bool LocalPrismAssetsExist((string light, string dark, string core, string autoloader, string langPath) assets, string rootPath)
    {
        var paths = new[] { assets.light, assets.dark, assets.core, assets.autoloader };
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var full = ResolveLocalAssetPath(path, rootPath);
            if (string.IsNullOrWhiteSpace(full) || !File.Exists(full))
                return false;
        }
        return true;
    }

    private static string? ResolveLocalAssetPath(string href, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(href)) return null;
        if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;
        var trimmed = href.TrimStart('/');
        var relative = trimmed.Replace('/', Path.DirectorySeparatorChar);
        var primary = Path.Combine(rootPath, relative);
        if (File.Exists(primary))
            return primary;
        var staticPath = Path.Combine(rootPath, "static", relative);
        return File.Exists(staticPath) ? staticPath : primary;
    }

    private static string BuildPrismCdnCss(Dictionary<string, object?> meta, PrismSpec? prismSpec)
    {
        var cdn = GetMetaStringOrNull(meta, "prism_cdn") ?? prismSpec?.CdnBase ?? "https://cdn.jsdelivr.net/npm/prismjs@1.29.0";
        cdn = cdn.TrimEnd('/');
        var lightOverride = GetMetaStringOrNull(meta, "prism_css_light") ?? prismSpec?.ThemeLight;
        var darkOverride = GetMetaStringOrNull(meta, "prism_css_dark") ?? prismSpec?.ThemeDark;
        var light = ResolvePrismThemeHref(lightOverride, isCdn: true, cdnBase: cdn, defaultCdnName: "prism", defaultLocalPath: "/assets/prism/prism.css");
        var dark = ResolvePrismThemeHref(darkOverride, isCdn: true, cdnBase: cdn, defaultCdnName: "prism-okaidia", defaultLocalPath: "/assets/prism/prism-okaidia.css");
        return string.Join(Environment.NewLine, new[]
        {
            $"<link rel=\"stylesheet\" href=\"{light}\" media=\"(prefers-color-scheme: light)\" />",
            $"<link rel=\"stylesheet\" href=\"{dark}\" media=\"(prefers-color-scheme: dark)\" />"
        });
    }

    private static string BuildPrismCdnScripts(Dictionary<string, object?> meta, PrismSpec? prismSpec)
    {
        var cdn = GetMetaStringOrNull(meta, "prism_cdn") ?? prismSpec?.CdnBase ?? "https://cdn.jsdelivr.net/npm/prismjs@1.29.0";
        cdn = cdn.TrimEnd('/');
        return string.Join(Environment.NewLine, new[]
        {
            $"<script src=\"{cdn}/components/prism-core.min.js\"></script>",
            $"<script src=\"{cdn}/plugins/autoloader/prism-autoloader.min.js\"></script>",
            BuildPrismInitScript($"{cdn}/components/")
        });
    }

    private static string BuildPrismInitScript(string languagesPath)
    {
        var safePath = (languagesPath ?? string.Empty).Replace("'", "\\'");
        return
            "<script>(function(){" +
            "var p=window.Prism;" +
            "if(!p){return;}" +
            "if(p.plugins&&p.plugins.autoloader){p.plugins.autoloader.languages_path='" + safePath + "';}" +
            "var run=function(){if(!document.querySelector('code[class*=\\\"language-\\\"] .token')){p.highlightAll();}};" +
            "if(document.readyState==='loading'){document.addEventListener('DOMContentLoaded', run);}" +
            "else{run();}" +
            "})();</script>";
    }

    private static string ResolvePrismThemeHref(
        string? value,
        bool isCdn,
        string? cdnBase,
        string defaultCdnName,
        string defaultLocalPath)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (!isCdn)
                return defaultLocalPath;
            var cdn = (cdnBase ?? string.Empty).TrimEnd('/');
            return $"{cdn}/themes/{defaultCdnName}.min.css";
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("//", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        if (trimmed.StartsWith("/"))
            return trimmed;

        if (trimmed.Contains("/"))
            return "/" + trimmed.TrimStart('/');

        if (!isCdn)
        {
            if (trimmed.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                return "/" + trimmed.TrimStart('/');
            return "/assets/prism/prism-" + trimmed + ".css";
        }

        var name = trimmed.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
            ? trimmed.Substring(0, trimmed.Length - 4)
            : trimmed;
        var cdnRoot = (cdnBase ?? string.Empty).TrimEnd('/');
        return $"{cdnRoot}/themes/{name}.min.css";
    }

    private static bool ContainsMarkdownCode(string htmlContent)
    {
        if (string.IsNullOrWhiteSpace(htmlContent)) return false;
        return htmlContent.IndexOf("class=\"language-", StringComparison.OrdinalIgnoreCase) >= 0 ||
               htmlContent.IndexOf("class=language-", StringComparison.OrdinalIgnoreCase) >= 0 ||
               htmlContent.IndexOf("<pre><code", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string NormalizeCodeBlockClasses(string htmlContent, string defaultLanguageClass)
    {
        if (string.IsNullOrWhiteSpace(htmlContent)) return htmlContent;

        return CodeBlockRegex.Replace(
            htmlContent,
            match =>
            {
                var preAttrs = match.Groups["preAttrs"].Value;
                var codeAttrs = match.Groups["codeAttrs"].Value;

                var updatedPre = EnsureClass(preAttrs, "code-block");
                var updatedCode = EnsureLanguageClass(codeAttrs, defaultLanguageClass);

                return $"<pre{updatedPre}><code{updatedCode}>";
            });
    }

    private static string EnsureLanguageClass(string attrs, string defaultLanguageClass)
    {
        if (string.IsNullOrWhiteSpace(attrs))
            return $" class=\"{defaultLanguageClass}\"";

        var classMatch = ClassAttrRegex.Match(attrs);
        if (!classMatch.Success)
            return attrs + $" class=\"{defaultLanguageClass}\"";

        var classes = classMatch.Groups["value"].Value;
        if (classes.IndexOf("language-", StringComparison.OrdinalIgnoreCase) >= 0)
            return attrs;

        var replacement = classMatch.Value.Replace(classes, (classes + " " + defaultLanguageClass).Trim());
        return attrs.Replace(classMatch.Value, replacement);
    }

    private static string ResolvePrismDefaultLanguage(Dictionary<string, object?>? meta, SiteSpec spec)
    {
        var metaLanguage = meta is null ? null : GetMetaStringOrNull(meta, "prism_default_language");
        var configured = metaLanguage ?? spec.Prism?.DefaultLanguage;
        if (string.IsNullOrWhiteSpace(configured))
            return "language-plain";
        return configured.StartsWith("language-", StringComparison.OrdinalIgnoreCase)
            ? configured
            : $"language-{configured}";
    }

    private static string EnsureClass(string attrs, string className)
    {
        if (string.IsNullOrWhiteSpace(attrs))
            return $" class=\"{className}\"";

        var classMatch = ClassAttrRegex.Match(attrs);
        if (!classMatch.Success)
            return attrs + $" class=\"{className}\"";

        var classes = classMatch.Groups["value"].Value;
        if (classes.IndexOf(className, StringComparison.OrdinalIgnoreCase) >= 0)
            return attrs;

        var replacement = classMatch.Value.Replace(classes, (classes + " " + className).Trim());
        return attrs.Replace(classMatch.Value, replacement);
    }

    private static void AppendMetaHtml(Dictionary<string, object?> meta, string key, string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return;
        if (meta.TryGetValue(key, out var existing) && existing is string existingText && !string.IsNullOrWhiteSpace(existingText))
            meta[key] = existingText + Environment.NewLine + html;
        else
            meta[key] = html;
    }

    private static bool MetaContains(Dictionary<string, object?> meta, string key, string needle)
    {
        if (!meta.TryGetValue(key, out var existing) || existing is not string text) return false;
        return text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string ComputeCacheKey(string content, string sourcePath, BuildCacheSpec? cache)
    {
        var mode = cache?.Mode ?? "contenthash";
        var input = mode.Equals("mtime", StringComparison.OrdinalIgnoreCase)
            ? $"{sourcePath}|{File.GetLastWriteTimeUtc(sourcePath).Ticks}"
            : content;

        using var sha = SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool ShouldRenderMarkdown(Dictionary<string, object?>? meta)
    {
        if (meta is null) return true;

        if (TryGetMetaBool(meta, "raw_html", out var rawHtml) && rawHtml)
            return false;

        if (TryGetMetaString(meta, "render", out var render) && render.Equals("html", StringComparison.OrdinalIgnoreCase))
            return false;

        if (TryGetMetaString(meta, "format", out var format) && format.Equals("html", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static bool TryGetMetaBool(Dictionary<string, object?> meta, string key, out bool value)
    {
        value = false;
        if (!TryGetMetaValue(meta, key, out var obj) || obj is null) return false;
        if (obj is bool b)
        {
            value = b;
            return true;
        }
        if (obj is string s && bool.TryParse(s, out var parsed))
        {
            value = parsed;
            return true;
        }
        return false;
    }

    private static bool TryGetMetaString(Dictionary<string, object?> meta, string key, out string value)
    {
        value = string.Empty;
        if (!TryGetMetaValue(meta, key, out var obj) || obj is null) return false;
        value = obj.ToString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryRenderDataOverride(
        Dictionary<string, object?>? meta,
        ShortcodeRenderContext context,
        out string html,
        out string mode)
    {
        html = string.Empty;
        mode = string.Empty;
        if (meta is null || context is null)
            return false;

        if (!TryGetMetaString(meta, "data_shortcode", out var shortcode) &&
            !TryGetMetaString(meta, "data.shortcode", out shortcode))
            return false;

        var dataPath = GetMetaString(meta, "data_path");
        if (string.IsNullOrWhiteSpace(dataPath))
            dataPath = GetMetaString(meta, "data.path");
        if (string.IsNullOrWhiteSpace(dataPath))
            dataPath = GetMetaString(meta, "data_key");
        if (string.IsNullOrWhiteSpace(dataPath))
            dataPath = GetMetaString(meta, "data.key");
        if (string.IsNullOrWhiteSpace(dataPath))
            dataPath = GetMetaString(meta, "data");
        if (string.IsNullOrWhiteSpace(dataPath))
            dataPath = shortcode;

        if (!TryResolveDataPath(context.Data, dataPath, out _))
            return false;

        var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["data"] = dataPath
        };

        html = context.TryRenderThemeShortcode(shortcode, attrs) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(html) && ShortcodeRegistry.TryGet(shortcode, out var handler))
            html = handler(context, attrs);

        if (string.IsNullOrWhiteSpace(html))
            return false;

        mode = GetMetaString(meta, "data_mode");
        if (string.IsNullOrWhiteSpace(mode))
            mode = GetMetaString(meta, "data.mode");
        return true;
    }

    private static string ApplyContentTemplate(
        string content,
        Dictionary<string, object?>? meta,
        ShortcodeRenderContext context)
    {
        if (string.IsNullOrWhiteSpace(content) || meta is null || context is null)
            return content;

        var engineName = ResolveContentEngineName(meta);
        if (string.IsNullOrWhiteSpace(engineName))
            return content;

        var engine = ResolveContentEngine(engineName, context);
        if (engine is null)
            return content;

        var page = new ContentItem
        {
            Title = context.FrontMatter?.Title ?? string.Empty,
            Description = context.FrontMatter?.Description ?? string.Empty,
            Meta = context.FrontMatter?.Meta ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            EditUrl = context.FrontMatter?.EditUrl ?? context.EditUrl
        };

        var renderContext = new ThemeRenderContext
        {
            Site = context.Site,
            Page = page,
            Data = context.Data,
            Project = context.Project,
            Localization = BuildLocalizationRuntime(context.Site, page, Array.Empty<ContentItem>()),
            Versioning = BuildVersioningRuntime(context.Site, string.Empty)
        };

        var resolver = context.PartialResolver ?? (_ => null);
        return engine.Render(content, renderContext, resolver);
    }

    private static string? ResolveContentEngineName(Dictionary<string, object?>? meta)
    {
        if (meta is null) return null;
        if (TryGetMetaString(meta, "content_engine", out var engine))
            return engine;
        if (TryGetMetaString(meta, "content.engine", out engine))
            return engine;
        if (TryGetMetaString(meta, "content.template_engine", out engine))
            return engine;
        return null;
    }

    private static ITemplateEngine? ResolveContentEngine(string engineName, ShortcodeRenderContext context)
    {
        if (string.IsNullOrWhiteSpace(engineName))
            return null;

        if (engineName.Equals("theme", StringComparison.OrdinalIgnoreCase) ||
            engineName.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            if (context.Engine is not null)
                return context.Engine;

            var themeEngine = context.Site.ThemeEngine ?? context.ThemeManifest?.Engine;
            return ThemeEngineRegistry.Resolve(themeEngine);
        }

        return ThemeEngineRegistry.Resolve(engineName);
    }

    private static bool TryResolveDataPath(IReadOnlyDictionary<string, object?> data, string path, out object? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        object? current = data;
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current is IReadOnlyDictionary<string, object?> map)
            {
                if (!map.TryGetValue(part, out current))
                    return false;
                continue;
            }
            return false;
        }

        value = current;
        return true;
    }

    private enum DataRenderMode
    {
        Override,
        Append,
        Prepend
    }

    private static DataRenderMode NormalizeDataRenderMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return DataRenderMode.Override;
        if (mode.Equals("append", StringComparison.OrdinalIgnoreCase))
            return DataRenderMode.Append;
        if (mode.Equals("prepend", StringComparison.OrdinalIgnoreCase))
            return DataRenderMode.Prepend;
        return DataRenderMode.Override;
    }

    private static bool TryGetMetaValue(Dictionary<string, object?> meta, string key, out object? value)
    {
        value = null;
        if (meta is null || string.IsNullOrWhiteSpace(key)) return false;
        var parts = key.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        object? current = meta;
        foreach (var part in parts)
        {
            if (current is IReadOnlyDictionary<string, object?> map)
            {
                if (!map.TryGetValue(part, out current))
                    return false;
                continue;
            }

            return false;
        }

        value = current;
        return true;
    }

    private static int? GetMetaInt(Dictionary<string, object?>? meta, string key)
    {
        if (meta is null) return null;
        if (!TryGetMetaValue(meta, key, out var value) || value is null)
            return null;
        if (value is int i) return i;
        if (value is long l) return (int)l;
        if (value is string s && int.TryParse(s, out var parsed)) return parsed;
        return null;
    }

    private static string GetMetaString(Dictionary<string, object?>? meta, string key)
    {
        if (meta is null)
            return string.Empty;

        return TryGetMetaString(meta, key, out var value) ? value : string.Empty;
    }

    private static string? GetMetaStringOrNull(Dictionary<string, object?>? meta, string key)
    {
        if (meta is null)
            return null;

        return TryGetMetaString(meta, key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private static string? ResolveProjectSlug(WebSitePlan plan, string filePath)
    {
        foreach (var project in plan.Projects)
        {
            if (string.IsNullOrWhiteSpace(project.ContentPath))
                continue;

            if (filePath.StartsWith(project.ContentPath, StringComparison.OrdinalIgnoreCase))
                return project.Slug;
        }

        return null;
    }

    private static bool MatchesFile(string rootPath, string[] contentRoots, string filePath, string input, string[] includePatterns, string[] excludePatterns)
    {
        var includes = NormalizePatterns(includePatterns);
        var excludes = NormalizePatterns(excludePatterns);

        foreach (var full in BuildCollectionInputCandidates(rootPath, contentRoots, input))
        {
            var basePath = full.Contains('*', StringComparison.Ordinal)
                ? full.Split('*')[0].TrimEnd(Path.DirectorySeparatorChar)
                : full;
            if (!Directory.Exists(basePath))
                continue;
            if (!IsPathWithinBase(basePath, filePath))
                continue;

            if (excludes.Length > 0 && MatchesAny(excludes, basePath, filePath))
                return false;

            if (includes.Length == 0 || MatchesAny(includes, basePath, filePath))
                return true;
        }

        return false;
    }
}

