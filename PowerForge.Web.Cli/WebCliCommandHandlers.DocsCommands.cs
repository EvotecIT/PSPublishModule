using System;
using System.Collections.Generic;
using System.IO;
using PowerForge.Web;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static int HandleApiDocs(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var typeText = TryGetOptionValue(subArgs, "--type");
        var xmlPath = TryGetOptionValue(subArgs, "--xml");
        var helpPath = TryGetOptionValue(subArgs, "--help-path");
        var outPath = TryGetOptionValue(subArgs, "--out") ??
                      TryGetOptionValue(subArgs, "--out-path") ??
                      TryGetOptionValue(subArgs, "--output-path");
        var assemblyPath = TryGetOptionValue(subArgs, "--assembly");
        var title = TryGetOptionValue(subArgs, "--title");
        var baseUrl = TryGetOptionValue(subArgs, "--base-url") ?? "/api";
        var format = TryGetOptionValue(subArgs, "--format");
        var cssHref = TryGetOptionValue(subArgs, "--css");
        var headerHtml = TryGetOptionValue(subArgs, "--header-html");
        var footerHtml = TryGetOptionValue(subArgs, "--footer-html");
        var template = TryGetOptionValue(subArgs, "--template");
        var templateRoot = TryGetOptionValue(subArgs, "--template-root");
        var indexTemplate = TryGetOptionValue(subArgs, "--template-index");
        var typeTemplate = TryGetOptionValue(subArgs, "--template-type");
        var docsIndexTemplate = TryGetOptionValue(subArgs, "--template-docs-index");
        var docsTypeTemplate = TryGetOptionValue(subArgs, "--template-docs-type");
        var docsScript = TryGetOptionValue(subArgs, "--docs-script");
        var searchScript = TryGetOptionValue(subArgs, "--search-script");
        var docsHome = TryGetOptionValue(subArgs, "--docs-home") ?? TryGetOptionValue(subArgs, "--docs-home-url");
        var sidebarPosition = TryGetOptionValue(subArgs, "--sidebar") ?? TryGetOptionValue(subArgs, "--sidebar-position");
        var bodyClass = TryGetOptionValue(subArgs, "--body-class") ?? TryGetOptionValue(subArgs, "--bodyClass");
        var sourceRoot = TryGetOptionValue(subArgs, "--source-root");
        var sourceUrl = TryGetOptionValue(subArgs, "--source-url") ?? TryGetOptionValue(subArgs, "--source-pattern");
        var coverageReport = TryGetOptionValue(subArgs, "--coverage-report");
        var generateCoverageReport = !HasOption(subArgs, "--no-coverage-report");
        if (HasOption(subArgs, "--coverage-report-off"))
            generateCoverageReport = false;
        var powerShellExamplesPath = TryGetOptionValue(subArgs, "--ps-examples") ?? TryGetOptionValue(subArgs, "--powershell-examples");
        var generatePowerShellFallbackExamples = !HasOption(subArgs, "--no-ps-fallback-examples");
        if (HasOption(subArgs, "--ps-fallback-examples-off"))
            generatePowerShellFallbackExamples = false;
        var powerShellFallbackLimit = ParseIntOption(TryGetOptionValue(subArgs, "--ps-fallback-limit"), 2);
        var sourceMapValues = GetOptionValues(subArgs, "--source-map");
        var includeUndocumented = !HasOption(subArgs, "--documented-only") && !HasOption(subArgs, "--no-undocumented");
        if (HasOption(subArgs, "--include-undocumented"))
            includeUndocumented = true;
        var navJson = TryGetOptionValue(subArgs, "--nav") ?? TryGetOptionValue(subArgs, "--nav-json");
        var includeNamespaces = ReadOptionList(subArgs, "--include-namespace", "--namespace-prefix");
        var excludeNamespaces = ReadOptionList(subArgs, "--exclude-namespace");
        var includeTypes = ReadOptionList(subArgs, "--include-type");
        var excludeTypes = ReadOptionList(subArgs, "--exclude-type");
        var quickStartTypes = ReadOptionList(subArgs, "--quickstart-types", "--quick-start-types");
        var suppressWarnings = ReadOptionList(subArgs, "--suppress-warning", "--suppress-warnings").ToArray();

        var apiType = ApiDocsType.CSharp;
        if (!string.IsNullOrWhiteSpace(typeText) &&
            Enum.TryParse<ApiDocsType>(typeText, true, out var parsedType))
            apiType = parsedType;

        if (apiType == ApiDocsType.CSharp && string.IsNullOrWhiteSpace(xmlPath))
            return Fail("Missing required --xml (CSharp API docs).", outputJson, logger, "web.apidocs");
        if (apiType == ApiDocsType.PowerShell && string.IsNullOrWhiteSpace(helpPath))
            return Fail("Missing required --help-path (PowerShell API docs).", outputJson, logger, "web.apidocs");
        if (string.IsNullOrWhiteSpace(outPath))
            return Fail("Missing required --out.", outputJson, logger, "web.apidocs");

        var options = new WebApiDocsOptions
        {
            Type = apiType,
            XmlPath = xmlPath ?? string.Empty,
            HelpPath = helpPath,
            AssemblyPath = assemblyPath,
            OutputPath = outPath,
            Title = string.IsNullOrWhiteSpace(title) ? "API Reference" : title,
            BaseUrl = baseUrl,
            Format = format,
            CssHref = cssHref,
            HeaderHtmlPath = headerHtml,
            FooterHtmlPath = footerHtml,
            Template = template,
            TemplateRootPath = templateRoot,
            IndexTemplatePath = indexTemplate,
            TypeTemplatePath = typeTemplate,
            DocsIndexTemplatePath = docsIndexTemplate,
            DocsTypeTemplatePath = docsTypeTemplate,
            DocsScriptPath = docsScript,
            SearchScriptPath = searchScript,
            DocsHomeUrl = docsHome,
            SidebarPosition = sidebarPosition,
            BodyClass = bodyClass,
            SourceRootPath = sourceRoot,
            SourceUrlPattern = sourceUrl,
            IncludeUndocumentedTypes = includeUndocumented,
            NavJsonPath = navJson,
            GenerateCoverageReport = generateCoverageReport,
            CoverageReportPath = coverageReport,
            GeneratePowerShellFallbackExamples = generatePowerShellFallbackExamples,
            PowerShellExamplesPath = powerShellExamplesPath,
            PowerShellFallbackExampleLimitPerCommand = powerShellFallbackLimit > 0 ? powerShellFallbackLimit : 2
        };
        foreach (var sourceMapValue in sourceMapValues)
        {
            if (!TryParseApiDocsSourceMap(sourceMapValue, out var mapping))
            {
                logger.Warn($"Ignoring invalid --source-map value '{sourceMapValue}'. Expected format: <pathPrefix>=<urlPattern>");
                continue;
            }
            options.SourceUrlMappings.Add(mapping);
        }
        if (includeNamespaces.Count > 0)
            options.IncludeNamespacePrefixes.AddRange(includeNamespaces);
        if (excludeNamespaces.Count > 0)
            options.ExcludeNamespacePrefixes.AddRange(excludeNamespaces);
        if (includeTypes.Count > 0)
            options.IncludeTypeNames.AddRange(includeTypes);
        if (excludeTypes.Count > 0)
            options.ExcludeTypeNames.AddRange(excludeTypes);
        if (quickStartTypes.Count > 0)
            options.QuickStartTypeNames.AddRange(quickStartTypes);

        var result = WebApiDocsGenerator.Generate(options);
        var filteredWarnings = WebVerifyPolicy.FilterWarnings(result.Warnings, suppressWarnings);

        if (!outputJson && filteredWarnings.Length > 0)
        {
            foreach (var warning in filteredWarnings)
                logger.Warn(warning);
        }
        if (!outputJson && result.UsedReflectionFallback)
            logger.Info("API docs used reflection fallback (XML missing or empty).");

        if (outputJson)
        {
            if (filteredWarnings.Length != result.Warnings.Length)
            {
                result = new WebApiDocsResult
                {
                    OutputPath = result.OutputPath,
                    IndexPath = result.IndexPath,
                    SearchPath = result.SearchPath,
                    TypesPath = result.TypesPath,
                    CoveragePath = result.CoveragePath,
                    TypeCount = result.TypeCount,
                    UsedReflectionFallback = result.UsedReflectionFallback,
                    Warnings = filteredWarnings
                };
            }

            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.apidocs",
                Success = true,
                ExitCode = 0,
                Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebApiDocsResult)
            });
            return 0;
        }

        logger.Success($"API docs generated: {result.OutputPath}");
        logger.Info($"Types: {result.TypeCount}");
        logger.Info($"Index: {result.IndexPath}");
        return 0;
    }

    private static int HandleChangelog(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var sourceText = TryGetOptionValue(subArgs, "--source");
        var changelogPath = TryGetOptionValue(subArgs, "--changelog") ?? TryGetOptionValue(subArgs, "--changelog-path");
        var outPath = TryGetOptionValue(subArgs, "--out") ??
                      TryGetOptionValue(subArgs, "--out-path") ??
                      TryGetOptionValue(subArgs, "--output-path");
        var repo = TryGetOptionValue(subArgs, "--repo");
        var repoUrl = TryGetOptionValue(subArgs, "--repo-url");
        var token = TryGetOptionValue(subArgs, "--token");
        var maxText = TryGetOptionValue(subArgs, "--max");
        var title = TryGetOptionValue(subArgs, "--title");

        if (string.IsNullOrWhiteSpace(outPath))
            return Fail("Missing required --out.", outputJson, logger, "web.changelog");

        var source = WebChangelogSource.Auto;
        if (!string.IsNullOrWhiteSpace(sourceText) &&
            Enum.TryParse<WebChangelogSource>(sourceText, true, out var parsedSource))
            source = parsedSource;

        var max = ParseIntOption(maxText, 0);
        var options = new WebChangelogOptions
        {
            Source = source,
            ChangelogPath = changelogPath,
            OutputPath = outPath,
            BaseDirectory = Directory.GetCurrentDirectory(),
            Repo = repo,
            RepoUrl = repoUrl,
            Token = token,
            Title = title,
            MaxReleases = max <= 0 ? null : max
        };

        var result = WebChangelogGenerator.Generate(options);

        if (!outputJson && result.Warnings.Length > 0)
        {
            foreach (var warning in result.Warnings)
                logger.Warn(warning);
        }

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.changelog",
                Success = true,
                ExitCode = 0,
                Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebChangelogResult)
            });
            return 0;
        }

        logger.Success($"Changelog generated: {result.OutputPath}");
        logger.Info($"Releases: {result.ReleaseCount}");
        return 0;
    }

    private static int HandleLlms(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var siteRoot = TryGetOptionValue(subArgs, "--site-root") ??
                       TryGetOptionValue(subArgs, "--root") ??
                       TryGetOptionValue(subArgs, "--path");
        var projectFile = TryGetOptionValue(subArgs, "--project");
        var apiIndex = TryGetOptionValue(subArgs, "--api-index");
        var apiBase = TryGetOptionValue(subArgs, "--api-base");
        var name = TryGetOptionValue(subArgs, "--name");
        var packageId = TryGetOptionValue(subArgs, "--package") ?? TryGetOptionValue(subArgs, "--package-id");
        var version = TryGetOptionValue(subArgs, "--version");
        var quickstart = TryGetOptionValue(subArgs, "--quickstart");
        var overview = TryGetOptionValue(subArgs, "--overview");
        var license = TryGetOptionValue(subArgs, "--license");
        var targets = TryGetOptionValue(subArgs, "--targets");
        var extra = TryGetOptionValue(subArgs, "--extra");
        var apiLevelText = TryGetOptionValue(subArgs, "--api-level");
        var apiMaxTypesText = TryGetOptionValue(subArgs, "--api-max-types");
        var apiMaxMembersText = TryGetOptionValue(subArgs, "--api-max-members");

        if (string.IsNullOrWhiteSpace(siteRoot))
            return Fail("Missing required --site-root.", outputJson, logger, "web.llms");

        var apiLevel = WebApiDetailLevel.None;
        if (!string.IsNullOrWhiteSpace(apiLevelText) &&
            Enum.TryParse<WebApiDetailLevel>(apiLevelText, true, out var parsedLevel))
            apiLevel = parsedLevel;
        var apiMaxTypes = ParseIntOption(apiMaxTypesText, 200);
        var apiMaxMembers = ParseIntOption(apiMaxMembersText, 2000);

        var result = WebLlmsGenerator.Generate(new WebLlmsOptions
        {
            SiteRoot = siteRoot,
            ProjectFile = projectFile,
            ApiIndexPath = apiIndex,
            ApiBase = apiBase ?? "/api",
            Name = name,
            PackageId = packageId,
            Version = version,
            QuickstartPath = quickstart,
            Overview = overview,
            License = license,
            Targets = targets,
            ExtraContentPath = extra,
            ApiDetailLevel = apiLevel,
            ApiMaxTypes = apiMaxTypes,
            ApiMaxMembers = apiMaxMembers
        });

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.llms",
                Success = true,
                ExitCode = 0,
                Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebLlmsResult)
            });
            return 0;
        }

        logger.Success("LLMS files generated.");
        logger.Info($"llms.txt: {result.LlmsTxtPath}");
        logger.Info($"llms.json: {result.LlmsJsonPath}");
        logger.Info($"llms-full.txt: {result.LlmsFullPath}");
        return 0;
    }

    private static bool TryParseApiDocsSourceMap(string? value, out WebApiDocsSourceUrlMapping mapping)
    {
        mapping = new WebApiDocsSourceUrlMapping();
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        var separator = trimmed.IndexOf('=');
        if (separator <= 0 || separator >= trimmed.Length - 1)
            return false;

        var prefix = trimmed.Substring(0, separator).Trim();
        var pattern = trimmed.Substring(separator + 1).Trim();
        if (string.IsNullOrWhiteSpace(prefix) || string.IsNullOrWhiteSpace(pattern))
            return false;

        var strip = false;
        if (prefix.EndsWith(":strip", StringComparison.OrdinalIgnoreCase))
        {
            prefix = prefix.Substring(0, prefix.Length - ":strip".Length).Trim();
            strip = true;
        }
        if (string.IsNullOrWhiteSpace(prefix))
            return false;

        mapping = new WebApiDocsSourceUrlMapping
        {
            PathPrefix = prefix,
            UrlPattern = pattern,
            StripPathPrefix = strip
        };
        return true;
    }

    private static int HandleSitemap(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var siteRoot = TryGetOptionValue(subArgs, "--site-root") ??
                       TryGetOptionValue(subArgs, "--root") ??
                       TryGetOptionValue(subArgs, "--path");
        var baseUrl = TryGetOptionValue(subArgs, "--base-url");
        var outputPath = TryGetOptionValue(subArgs, "--out") ??
                         TryGetOptionValue(subArgs, "--out-path") ??
                         TryGetOptionValue(subArgs, "--output-path");
        var apiSitemap = TryGetOptionValue(subArgs, "--api-sitemap");
        var entriesPath = TryGetOptionValue(subArgs, "--entries");
        var htmlOutput = TryGetOptionValue(subArgs, "--html-out") ??
                         TryGetOptionValue(subArgs, "--html-output") ??
                         TryGetOptionValue(subArgs, "--html-path");
        var htmlTemplate = TryGetOptionValue(subArgs, "--html-template");
        var htmlCss = TryGetOptionValue(subArgs, "--html-css");
        var htmlTitle = TryGetOptionValue(subArgs, "--html-title");
        var generateHtml = HasOption(subArgs, "--html") ||
                           !string.IsNullOrWhiteSpace(htmlOutput) ||
                           !string.IsNullOrWhiteSpace(htmlTemplate) ||
                           !string.IsNullOrWhiteSpace(htmlCss) ||
                           !string.IsNullOrWhiteSpace(htmlTitle);

        if (string.IsNullOrWhiteSpace(siteRoot))
            return Fail("Missing required --site-root.", outputJson, logger, "web.sitemap");
        if (string.IsNullOrWhiteSpace(baseUrl))
            return Fail("Missing required --base-url.", outputJson, logger, "web.sitemap");

        var result = WebSitemapGenerator.Generate(new WebSitemapOptions
        {
            SiteRoot = siteRoot,
            BaseUrl = baseUrl,
            OutputPath = outputPath,
            ApiSitemapPath = apiSitemap,
            Entries = LoadSitemapEntries(entriesPath),
            GenerateHtml = generateHtml,
            HtmlOutputPath = htmlOutput,
            HtmlTemplatePath = htmlTemplate,
            HtmlCssHref = htmlCss,
            HtmlTitle = htmlTitle
        });

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.sitemap",
                Success = true,
                ExitCode = 0,
                Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebSitemapResult)
            });
            return 0;
        }

        logger.Success($"Sitemap generated: {result.OutputPath}");
        logger.Info($"URL count: {result.UrlCount}");
        if (!string.IsNullOrWhiteSpace(result.HtmlOutputPath))
            logger.Info($"HTML sitemap: {result.HtmlOutputPath}");
        return 0;
    }
}
