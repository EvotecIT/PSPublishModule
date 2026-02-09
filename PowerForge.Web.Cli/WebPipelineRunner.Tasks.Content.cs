using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using PowerForge;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecuteApiDocs(
        JsonElement step,
        string label,
        string baseDir,
        bool fast,
        string effectiveMode,
        WebConsoleLogger? logger,
        WebPipelineStepResult stepResult)
    {
        var typeText = GetString(step, "type");
        var xml = ResolvePath(baseDir, GetString(step, "xml"));
        var help = ResolvePath(baseDir, GetString(step, "help") ?? GetString(step, "helpPath") ?? GetString(step, "help-path"));
        var outPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output"));
        var assembly = ResolvePath(baseDir, GetString(step, "assembly"));
        var title = GetString(step, "title");
        var baseUrl = GetString(step, "baseUrl") ?? GetString(step, "base-url") ?? "/api";
        var format = GetString(step, "format");
        var css = GetString(step, "css") ?? GetString(step, "cssHref") ?? GetString(step, "css-href");
        var header = ResolvePath(baseDir, GetString(step, "headerHtml") ?? GetString(step, "header-html"));
        var footer = ResolvePath(baseDir, GetString(step, "footerHtml") ?? GetString(step, "footer-html"));
        var template = GetString(step, "template");
        var templateRoot = ResolvePath(baseDir, GetString(step, "templateRoot") ?? GetString(step, "template-root"));
        var indexTemplate = ResolvePath(baseDir, GetString(step, "templateIndex") ?? GetString(step, "template-index"));
        var typeTemplate = ResolvePath(baseDir, GetString(step, "templateType") ?? GetString(step, "template-type"));
        var docsIndexTemplate = ResolvePath(baseDir, GetString(step, "templateDocsIndex") ?? GetString(step, "template-docs-index"));
        var docsTypeTemplate = ResolvePath(baseDir, GetString(step, "templateDocsType") ?? GetString(step, "template-docs-type"));
        var docsScript = ResolvePath(baseDir, GetString(step, "docsScript") ?? GetString(step, "docs-script"));
        var searchScript = ResolvePath(baseDir, GetString(step, "searchScript") ?? GetString(step, "search-script"));
        var docsHome = GetString(step, "docsHome") ?? GetString(step, "docsHomeUrl") ??
                       GetString(step, "docs-home") ?? GetString(step, "docs-home-url");
        var sidebar = GetString(step, "sidebar") ?? GetString(step, "sidebarPosition") ?? GetString(step, "sidebar-position");
        var bodyClass = GetString(step, "bodyClass") ?? GetString(step, "body-class");
        var sourceRoot = ResolvePath(baseDir, GetString(step, "sourceRoot") ?? GetString(step, "source-root"));
        var sourceUrl = GetString(step, "sourceUrl") ?? GetString(step, "source-url") ??
                        GetString(step, "sourcePattern") ?? GetString(step, "source-pattern");
        var includeUndocumented = GetBool(step, "includeUndocumented") ?? GetBool(step, "include-undocumented") ?? true;
        var nav = ResolvePath(baseDir, GetString(step, "nav") ?? GetString(step, "navJson") ?? GetString(step, "nav-json"));
        var navContextPath = GetString(step, "navContextPath") ?? GetString(step, "nav-context-path") ??
                             GetString(step, "navContextRoute") ?? GetString(step, "nav-context-route");
        var navContextCollection = GetString(step, "navContextCollection") ?? GetString(step, "nav-context-collection");
        var navContextLayout = GetString(step, "navContextLayout") ?? GetString(step, "nav-context-layout");
        var navContextProject = GetString(step, "navContextProject") ?? GetString(step, "nav-context-project");
        var includeNamespaces = GetString(step, "includeNamespace") ?? GetString(step, "include-namespace");
        var excludeNamespaces = GetString(step, "excludeNamespace") ?? GetString(step, "exclude-namespace");
        var includeTypes = GetString(step, "includeType") ?? GetString(step, "include-type");
        var excludeTypes = GetString(step, "excludeType") ?? GetString(step, "exclude-type");
        var siteName = GetString(step, "siteName") ?? GetString(step, "site-name");
        var brandUrl = GetString(step, "brandUrl") ?? GetString(step, "brand-url");
        var brandIcon = GetString(step, "brandIcon") ?? GetString(step, "brand-icon");
        var suppressWarnings = GetArrayOfStrings(step, "suppressWarnings");
        var isDev = string.Equals(effectiveMode, "dev", StringComparison.OrdinalIgnoreCase) || fast;
        var ciStrictDefaults = ConsoleEnvironment.IsCI && !isDev;
        var failOnWarnings = GetBool(step, "failOnWarnings") ?? ciStrictDefaults;
        var warningPreviewCount = GetInt(step, "warningPreviewCount") ?? GetInt(step, "warning-preview") ?? (isDev ? 2 : 5);

        var apiType = ApiDocsType.CSharp;
        if (!string.IsNullOrWhiteSpace(typeText) &&
            Enum.TryParse<ApiDocsType>(typeText, true, out var parsedType))
            apiType = parsedType;

        if (string.IsNullOrWhiteSpace(outPath))
            throw new InvalidOperationException("apidocs requires out.");
        if (apiType == ApiDocsType.CSharp && string.IsNullOrWhiteSpace(xml))
            throw new InvalidOperationException("apidocs requires xml for CSharp.");
        if (apiType == ApiDocsType.PowerShell && string.IsNullOrWhiteSpace(help))
            throw new InvalidOperationException("apidocs requires help for PowerShell.");

        // Best-practice default: when pipeline runs at a website repo root, assume ./site.json
        // unless the step overrides it explicitly. This prevents "API reference has no navigation"
        // and theme drift when agents forget to wire config/nav.
        var configPath = ResolvePath(baseDir, GetString(step, "config"));
        if (string.IsNullOrWhiteSpace(configPath))
        {
            var defaultSiteConfig = Path.Combine(baseDir, "site.json");
            if (File.Exists(defaultSiteConfig))
                configPath = defaultSiteConfig;
        }

        // If the user configured header/footer paths but they don't exist, treat them as unset so
        // we can fall back to theme fragments (or embedded fragments) deterministically.
        if (!string.IsNullOrWhiteSpace(header) && !File.Exists(Path.GetFullPath(header)))
        {
            logger?.Warn($"{label}: apidocs headerHtml not found: {header}");
            header = null;
        }
        if (!string.IsNullOrWhiteSpace(footer) && !File.Exists(Path.GetFullPath(footer)))
        {
            logger?.Warn($"{label}: apidocs footerHtml not found: {footer}");
            footer = null;
        }

        if (!string.IsNullOrWhiteSpace(configPath))
        {
            if (string.IsNullOrWhiteSpace(nav))
            {
                // Prefer site-nav.json when the repo provides it (static/data). This supports navigation profiles
                // and avoids "API reference has no navigation" drift across sites.
                try
                {
                    var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(configPath, WebCliJson.Options);
                    var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);
                    var dataRoot = string.IsNullOrWhiteSpace(spec.DataRoot) ? "data" : spec.DataRoot;
                    var relativeRoot = Path.IsPathRooted(dataRoot) ? "data" : dataRoot.TrimStart('/', '\\');
                    var staticNavPath = Path.Combine(plan.RootPath, "static", relativeRoot, "site-nav.json");
                    nav = File.Exists(staticNavPath) ? staticNavPath : configPath;
                }
                catch
                {
                    nav = configPath;
                }
            }

            TryResolveApiFragmentsFromTheme(configPath, ref header, ref footer);
        }

        var options = new WebApiDocsOptions
        {
            Type = apiType,
            XmlPath = xml ?? string.Empty,
            HelpPath = help,
            AssemblyPath = assembly,
            OutputPath = outPath,
            Title = string.IsNullOrWhiteSpace(title) ? "API Reference" : title,
            BaseUrl = baseUrl,
            Format = format,
            CssHref = css,
            HeaderHtmlPath = header,
            FooterHtmlPath = footer,
            Template = template,
            TemplateRootPath = templateRoot,
            IndexTemplatePath = indexTemplate,
            TypeTemplatePath = typeTemplate,
            DocsIndexTemplatePath = docsIndexTemplate,
            DocsTypeTemplatePath = docsTypeTemplate,
            DocsScriptPath = docsScript,
            SearchScriptPath = searchScript,
            DocsHomeUrl = docsHome,
            SidebarPosition = sidebar,
            BodyClass = bodyClass,
            SourceRootPath = sourceRoot,
            SourceUrlPattern = sourceUrl,
            IncludeUndocumentedTypes = includeUndocumented,
            NavJsonPath = nav,
            NavContextPath = navContextPath ?? baseUrl,
            NavContextCollection = navContextCollection,
            NavContextLayout = navContextLayout,
            NavContextProject = navContextProject,
            SiteName = siteName,
            BrandUrl = brandUrl,
            BrandIcon = brandIcon
        };

        var includeList = CliPatternHelper.SplitPatterns(includeNamespaces);
        var excludeList = CliPatternHelper.SplitPatterns(excludeNamespaces);
        var includeTypeList = CliPatternHelper.SplitPatterns(includeTypes);
        var excludeTypeList = CliPatternHelper.SplitPatterns(excludeTypes);
        if (includeList.Length > 0)
            options.IncludeNamespacePrefixes.AddRange(includeList);
        if (excludeList.Length > 0)
            options.ExcludeNamespacePrefixes.AddRange(excludeList);
        if (includeTypeList.Length > 0)
            options.IncludeTypeNames.AddRange(includeTypeList);
        if (excludeTypeList.Length > 0)
            options.ExcludeTypeNames.AddRange(excludeTypeList);

        var res = WebApiDocsGenerator.Generate(options);
        var note = res.UsedReflectionFallback ? " (reflection)" : string.Empty;
        var filteredWarnings = suppressWarnings is { Length: > 0 }
            ? WebVerifyPolicy.FilterWarnings(res.Warnings, suppressWarnings)
            : res.Warnings;

        if (filteredWarnings.Length > 0)
        {
            var firstWarning = filteredWarnings[0];
            if (!string.IsNullOrWhiteSpace(firstWarning))
            {
                var trimmed = firstWarning.Length > 120
                    ? $"{firstWarning.Substring(0, 117)}..."
                    : firstWarning;
                note += $" (warn: {trimmed})";
            }
            else
            {
                note += $" ({filteredWarnings.Length} warnings)";
            }

            logger?.Warn($"{label}: apidocs warnings: {filteredWarnings.Length}");

            var previewLimit = Math.Clamp(warningPreviewCount, 0, 20);
            if (previewLimit > 0)
            {
                foreach (var warning in filteredWarnings.Where(static w => !string.IsNullOrWhiteSpace(w)).Take(previewLimit))
                {
                    logger?.Warn($"{label}: {warning}");
                }

                var remaining = filteredWarnings.Length - previewLimit;
                if (remaining > 0)
                    logger?.Warn($"{label}: (+{remaining} more warnings)");
            }

            if (failOnWarnings)
            {
                var headline = filteredWarnings.FirstOrDefault(static w => !string.IsNullOrWhiteSpace(w)) ?? "API docs warnings encountered.";
                throw new InvalidOperationException(headline);
            }
        }

        stepResult.Success = true;
        stepResult.Message = $"API docs {res.TypeCount} types{note}";
    }

    private static void ExecuteChangelog(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var outPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output"));
        var sourceText = GetString(step, "source");
        var changelog = ResolvePath(baseDir, GetString(step, "changelog") ?? GetString(step, "changelogPath") ?? GetString(step, "changelog-path"));
        var repo = GetString(step, "repo");
        var repoUrl = GetString(step, "repoUrl") ?? GetString(step, "repo-url");
        var token = GetString(step, "token");
        var maxValue = GetInt(step, "max") ?? 0;
        var title = GetString(step, "title");
        if (string.IsNullOrWhiteSpace(outPath))
            throw new InvalidOperationException("changelog requires out.");

        var source = WebChangelogSource.Auto;
        if (!string.IsNullOrWhiteSpace(sourceText) &&
            Enum.TryParse<WebChangelogSource>(sourceText, true, out var parsedSource))
            source = parsedSource;

        var options = new WebChangelogOptions
        {
            Source = source,
            ChangelogPath = changelog,
            OutputPath = outPath,
            Repo = repo,
            RepoUrl = repoUrl,
            Token = token,
            Title = title,
            MaxReleases = maxValue <= 0 ? null : maxValue
        };

        var res = WebChangelogGenerator.Generate(options);
        var note = res.Source != WebChangelogSource.Auto ? $" ({res.Source.ToString().ToLowerInvariant()})" : string.Empty;
        if (res.Warnings.Length > 0)
        {
            var firstWarning = res.Warnings[0];
            if (!string.IsNullOrWhiteSpace(firstWarning))
            {
                var trimmed = firstWarning.Length > 120
                    ? $"{firstWarning.Substring(0, 117)}..."
                    : firstWarning;
                note += $" (warn: {trimmed})";
            }
        }

        stepResult.Success = true;
        stepResult.Message = $"Changelog {res.ReleaseCount} releases{note}";
    }

    private static void ExecuteLlms(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
        if (string.IsNullOrWhiteSpace(siteRoot))
            throw new InvalidOperationException("llms requires siteRoot.");

        var apiLevelText = GetString(step, "apiLevel") ?? GetString(step, "api-level");
        var res = WebLlmsGenerator.Generate(new WebLlmsOptions
        {
            SiteRoot = siteRoot,
            ProjectFile = ResolvePath(baseDir, GetString(step, "project")),
            ApiIndexPath = ResolvePath(baseDir, GetString(step, "apiIndex") ?? GetString(step, "api-index")),
            ApiBase = GetString(step, "apiBase") ?? "/api",
            Name = GetString(step, "name"),
            PackageId = GetString(step, "package") ?? GetString(step, "packageId"),
            Version = GetString(step, "version"),
            QuickstartPath = ResolvePath(baseDir, GetString(step, "quickstart")),
            Overview = GetString(step, "overview"),
            License = GetString(step, "license"),
            Targets = GetString(step, "targets"),
            ExtraContentPath = ResolvePath(baseDir, GetString(step, "extra")),
            ApiDetailLevel = ParseApiDetailLevel(apiLevelText),
            ApiMaxTypes = GetInt(step, "apiMaxTypes") ?? 200,
            ApiMaxMembers = GetInt(step, "apiMaxMembers") ?? 2000
        });
        stepResult.Success = true;
        stepResult.Message = $"LLMS generated ({res.Version})";
    }

    private static void ExecuteSitemap(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
        var baseUrl = GetString(step, "baseUrl") ?? GetString(step, "base-url");
        if (string.IsNullOrWhiteSpace(siteRoot) || string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("sitemap requires siteRoot and baseUrl.");

        var entries = GetSitemapEntries(step, "entries");
        var includeHtml = GetBool(step, "includeHtmlFiles");
        var includeText = GetBool(step, "includeTextFiles");
        var htmlEnabled = GetBool(step, "html") ?? false;
        var htmlOutput = ResolvePath(baseDir, GetString(step, "htmlOutput") ?? GetString(step, "htmlOut") ?? GetString(step, "html-out"));
        var htmlTemplate = ResolvePath(baseDir, GetString(step, "htmlTemplate") ?? GetString(step, "html-template"));
        var htmlCss = GetString(step, "htmlCss") ?? GetString(step, "html-css");
        var htmlTitle = GetString(step, "htmlTitle") ?? GetString(step, "html-title");
        if (!htmlEnabled)
        {
            htmlEnabled = !string.IsNullOrWhiteSpace(htmlOutput) ||
                          !string.IsNullOrWhiteSpace(htmlTemplate) ||
                          !string.IsNullOrWhiteSpace(htmlCss) ||
                          !string.IsNullOrWhiteSpace(htmlTitle);
        }

        var res = WebSitemapGenerator.Generate(new WebSitemapOptions
        {
            SiteRoot = siteRoot,
            BaseUrl = baseUrl,
            OutputPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output")),
            ApiSitemapPath = ResolvePath(baseDir, GetString(step, "apiSitemap") ?? GetString(step, "api-sitemap")),
            ExtraPaths = GetArrayOfStrings(step, "extraPaths") ?? GetArrayOfStrings(step, "extra-paths"),
            Entries = entries.Length == 0 ? null : entries,
            IncludeHtmlFiles = includeHtml ?? true,
            IncludeTextFiles = includeText ?? true,
            GenerateHtml = htmlEnabled,
            HtmlOutputPath = htmlOutput,
            HtmlTemplatePath = htmlTemplate,
            HtmlCssHref = htmlCss,
            HtmlTitle = htmlTitle
        });

        stepResult.Success = true;
        stepResult.Message = $"Sitemap {res.UrlCount} urls";
    }
}
