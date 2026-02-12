using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static class WebCliHelpers
{
    private const int ErrorSchemaVersion = 1;

    internal static void PrintUsage()
    {
        Console.WriteLine("PowerForge.Web CLI");
        Console.WriteLine("Usage:");
        Console.WriteLine("  powerforge-web plan --config <site.json> [--output json]");
        Console.WriteLine("  powerforge-web build --config <site.json> --out <path> [--clean] [--output json]");
        Console.WriteLine("  powerforge-web publish --config <publish.json> [--output json]");
        Console.WriteLine("  powerforge-web verify --config <site.json> [--fail-on-warnings] [--fail-on-nav-lint] [--fail-on-theme-contract] [--suppress-warning <pattern>] [--output json]");
        Console.WriteLine("  powerforge-web doctor --config <site.json> [--out <path>] [--site-root <dir>] [--no-build] [--no-verify] [--no-audit]");
        Console.WriteLine("                     [--include <glob>] [--exclude <glob>] [--summary] [--summary-path <file>] [--sarif] [--sarif-path <file>]");
        Console.WriteLine("                     [--required-route <path[,path]>] [--nav-required-link <path[,path]>]");
        Console.WriteLine("                     [--fail-on-warnings] [--fail-on-nav-lint] [--fail-on-theme-contract] [--suppress-warning <pattern>] [--output json]");
        Console.WriteLine("  powerforge-web markdown-fix --path <dir> [--include <glob>] [--exclude <glob>] [--apply] [--output json]");
        Console.WriteLine("  powerforge-web markdown-fix --config <site.json> [--path <dir>] [--include <glob>] [--exclude <glob>] [--apply] [--output json]");
        Console.WriteLine("  powerforge-web audit --site-root <dir> [--include <glob>] [--exclude <glob>] [--max-html-files <n>] [--nav-selector <css>]");
        Console.WriteLine("  powerforge-web audit --config <site.json> [--out <path>] [--include <glob>] [--exclude <glob>] [--max-html-files <n>] [--nav-selector <css>]");
        Console.WriteLine("                     [--no-links] [--no-assets] [--no-nav] [--no-titles] [--no-ids] [--no-structure]");
        Console.WriteLine("                     [--no-heading-order] [--no-link-purpose]");
        Console.WriteLine("                     [--rendered] [--rendered-engine <chromium|firefox|webkit>] [--rendered-max <n>] [--rendered-timeout <ms>]");
        Console.WriteLine("                     [--rendered-headful] [--rendered-base-url <url>] [--rendered-host <host>] [--rendered-port <n>] [--rendered-no-serve]");
        Console.WriteLine("                     [--rendered-no-install]");
        Console.WriteLine("                     [--rendered-no-console-errors] [--rendered-no-console-warnings] [--rendered-no-failures]");
        Console.WriteLine("                     [--rendered-include <glob>] [--rendered-exclude <glob>]");
        Console.WriteLine("                     [--ignore-nav <glob>] [--no-default-ignore-nav] [--nav-ignore-prefix <path>]");
        Console.WriteLine("                     [--nav-profiles <file.json>]");
        Console.WriteLine("                     [--nav-canonical <file>] [--nav-canonical-selector <css>] [--nav-canonical-required]");
        Console.WriteLine("                     [--nav-required-link <path[,path]>]");
        Console.WriteLine("                     [--min-nav-coverage <0-100>] [--required-route <path[,path]>]");
        Console.WriteLine("                     [--nav-optional]");
        Console.WriteLine("                     [--baseline <file>] [--fail-on-warnings] [--fail-on-new] [--max-errors <n>] [--max-warnings <n>] [--fail-category <name[,name]>] [--max-total-files <n>]");
        Console.WriteLine("                     [--baseline-generate] [--baseline-update]");
        Console.WriteLine("                     [--no-utf8] [--no-meta-charset] [--no-replacement-char-check]");
        Console.WriteLine("                     [--no-network-hints] [--no-render-blocking] [--max-head-blocking <n>]");
        Console.WriteLine("                     [--no-default-exclude]");
        Console.WriteLine("                     [--summary] [--summary-path <file>] [--summary-max <n>]");
        Console.WriteLine("                     [--sarif] [--sarif-path <file>]");
        Console.WriteLine("                     [--warning-preview <n>] [--error-preview <n>]");
        Console.WriteLine("                     [--suppress-issue <code|substring|wildcard|re:...>]");
        Console.WriteLine("  powerforge-web scaffold --out <path> [--name <SiteName>] [--base-url <url>] [--engine simple|scriban] [--output json]");
        Console.WriteLine("  powerforge-web new --config <site.json> --title <Title> [--collection <name>] [--slug <slug>] [--out <path>]");
        Console.WriteLine("  powerforge-web serve --path <dir> [--port 8080] [--host localhost]");
        Console.WriteLine("  powerforge-web serve --config <site.json> [--out <path>] [--port 8080] [--host localhost]");
        Console.WriteLine("                     (if the requested port is busy, serve will try the next available port)");
        Console.WriteLine("  powerforge-web apidocs --type csharp --xml <file> --out <dir> [--assembly <file>] [--title <text>] [--base-url <url>] [--docs-home <url>] [--sidebar <left|right>] [--body-class <class>]");
        Console.WriteLine("  powerforge-web apidocs --type powershell --help-path <file|dir> --out <dir> [--title <text>] [--base-url <url>] [--docs-home <url>] [--sidebar <left|right>] [--body-class <class>]");
        Console.WriteLine("                     [--template <name>] [--template-root <dir>] [--template-index <file>] [--template-type <file>]");
        Console.WriteLine("                     [--template-docs-index <file>] [--template-docs-type <file>] [--docs-script <file>] [--search-script <file>]");
        Console.WriteLine("                     [--format json|hybrid] [--css <href>] [--header-html <file>] [--footer-html <file>]");
        Console.WriteLine("                     [--coverage-report <file>] [--no-coverage-report]");
        Console.WriteLine("                     [--ps-examples <file|dir>] [--no-ps-fallback-examples] [--ps-fallback-limit <n>]");
        Console.WriteLine("                     [--fail-on-warnings] [--suppress-warning <pattern>]");
        Console.WriteLine("                     [--source-root <dir>] [--source-path-prefix <prefix>] [--source-url <pattern>] [--source-map <prefix[(:strip)]=pattern>] [--documented-only]");
        Console.WriteLine("                     (source-url/source-map tokens: {path} {line} {root} {pathNoRoot} {pathNoPrefix})");
        Console.WriteLine("                     [--nav <file>] [--nav-surface <name>] [--include-namespace <prefix[,prefix]>] [--exclude-namespace <prefix[,prefix]>]");
        Console.WriteLine("                     [--quickstart-types <type[,type]>]");
        Console.WriteLine("  powerforge-web changelog --out <file> [--source auto|file|github] [--changelog <file>] [--repo <owner/name>]");
        Console.WriteLine("                     [--repo-url <url>] [--token <token>] [--max <n>] [--title <text>]");
        Console.WriteLine("  powerforge-web optimize --site-root <dir> [--config <site.json>] [--critical-css <file>] [--css-pattern <regex>]");
        Console.WriteLine("                     [--minify-html] [--minify-css] [--minify-js]");
        Console.WriteLine("                     [--optimize-images] [--image-ext <.png,.jpg,.jpeg,.webp>] [--image-include <glob[,glob]>] [--image-exclude <glob[,glob]>]");
        Console.WriteLine("                     [--image-quality <1-100>] [--image-keep-metadata] [--image-generate-webp] [--image-generate-avif]");
        Console.WriteLine("                     [--image-prefer-nextgen] [--image-widths <320,640,1024>] [--image-enhance-tags]");
        Console.WriteLine("                     [--image-max-bytes <n>] [--image-max-total-bytes <n>] [--image-fail-on-budget]");
        Console.WriteLine("                     [--hash-assets] [--hash-ext <.css,.js>] [--hash-exclude <glob[,glob]>] [--hash-manifest <file>]");
        Console.WriteLine("                     [--headers] [--headers-out <file>] [--headers-html <value>] [--headers-assets <value>] [--report-path <file>]");
        Console.WriteLine("  powerforge-web dotnet-build --project <path> [--configuration <cfg>] [--framework <tfm>] [--runtime <rid>] [--no-restore]");
        Console.WriteLine("  powerforge-web dotnet-publish --project <path> --out <dir> [--configuration <cfg>] [--framework <tfm>] [--runtime <rid>] [--define-constants <list>]");
        Console.WriteLine("                     [--clean]");
        Console.WriteLine("                     [--self-contained] [--no-build] [--no-restore] [--base-href <path>] [--no-blazor-fixes]");
        Console.WriteLine("  powerforge-web overlay --source <dir> --destination <dir> [--include <glob[,glob...]>] [--exclude <glob[,glob...]>]");
        Console.WriteLine("  powerforge-web pipeline --config <pipeline.json> [--profile] [--watch] [--fast] [--dev] [--mode <name>] [--only <task[,task...]>] [--skip <task[,task...]>]");
        Console.WriteLine("  powerforge-web llms --site-root <dir> [--project <path>] [--api-index <path>] [--api-base /api]");
        Console.WriteLine("                     [--name <Name>] [--package <Id>] [--version <X.Y.Z>] [--quickstart <file>]");
        Console.WriteLine("                     [--overview <text>] [--license <text>] [--targets <text>] [--extra <file>]");
        Console.WriteLine("                     [--api-level none|summary|full] [--api-max-types <n>] [--api-max-members <n>]");
        Console.WriteLine("  powerforge-web sitemap --site-root <dir> --base-url <url> [--api-sitemap <path>] [--out <file>] [--entries <file>]");
        Console.WriteLine("                     [--html] [--html-out <file>] [--html-template <file>] [--html-css <href>] [--html-title <text>]");
        Console.WriteLine("  powerforge-web cloudflare purge --zone-id <id> [--token <token> | --token-env <env>]");
        Console.WriteLine("                     [--purge-everything] [--base-url <url>] [--path <p[,p...]>] [--url <u[,u...]>] [--dry-run]");
    }

    internal static int Fail(string message, bool outputJson, WebConsoleLogger logger, string command)
    {
        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = ErrorSchemaVersion,
                Command = command,
                Success = false,
                ExitCode = 2,
                Error = message
            });
            return 2;
        }

        logger.Error(message);
        PrintUsage();
        return 2;
    }

    internal static string? TryGetOptionValue(string[] argv, string optionName)
    {
        for (var i = 0; i < argv.Length; i++)
        {
            if (!argv[i].Equals(optionName, StringComparison.OrdinalIgnoreCase)) continue;
            return ++i < argv.Length ? argv[i] : null;
        }

        return null;
    }

    internal static List<string> ReadOptionList(string[] argv, params string[] optionNames)
    {
        var values = new List<string>();
        foreach (var optionName in optionNames)
            values.AddRange(GetOptionValues(argv, optionName));

        var results = new List<string>();
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            var parts = value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    results.Add(trimmed);
            }
        }

        return results;
    }

    internal static List<string> GetOptionValues(string[] argv, string optionName)
    {
        var values = new List<string>();
        for (var i = 0; i < argv.Length; i++)
        {
            if (!argv[i].Equals(optionName, StringComparison.OrdinalIgnoreCase)) continue;
            if (++i < argv.Length && !string.IsNullOrWhiteSpace(argv[i]))
                values.Add(argv[i]);
        }

        return values;
    }

    internal static bool HasOption(string[] argv, string optionName)
    {
        for (var i = 0; i < argv.Length; i++)
        {
            if (argv[i].Equals(optionName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    internal static int ParseIntOption(string? value, int fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    internal static long ParseLongOption(string? value, long fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        return long.TryParse(value, out var parsed) ? parsed : fallback;
    }

    internal static int[] ParseIntListOption(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<int>();

        var values = new List<int>();
        foreach (var token in value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(token.Trim(), out var parsed) && parsed > 0)
                values.Add(parsed);
        }

        return values
            .Distinct()
            .OrderBy(v => v)
            .ToArray();
    }

    internal static string? ResolveSummaryPath(bool summaryEnabled, string? summaryPath)
    {
        if (!summaryEnabled && string.IsNullOrWhiteSpace(summaryPath))
            return null;

        return string.IsNullOrWhiteSpace(summaryPath) ? "audit-summary.json" : summaryPath;
    }

    internal static string? ResolveSarifPath(bool sarifEnabled, string? sarifPath)
    {
        if (!sarifEnabled && string.IsNullOrWhiteSpace(sarifPath))
            return null;

        return string.IsNullOrWhiteSpace(sarifPath) ? "audit.sarif.json" : sarifPath;
    }

    internal static WebAuditNavProfile[] LoadAuditNavProfiles(string? navProfilesPath)
    {
        if (string.IsNullOrWhiteSpace(navProfilesPath))
            return Array.Empty<WebAuditNavProfile>();

        var fullPath = ResolveExistingFilePath(navProfilesPath);
        using var stream = File.OpenRead(fullPath);
        var profiles = JsonSerializer.Deserialize(stream, WebCliJson.Context.WebAuditNavProfileArray)
                       ?? Array.Empty<WebAuditNavProfile>();
        return profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Match))
            .ToArray();
    }

    internal static WebAuditResult RunDoctorAudit(string siteRoot, string[] argv)
    {
        var include = ReadOptionList(argv, "--include");
        var exclude = ReadOptionList(argv, "--exclude");
        var ignoreNav = ReadOptionList(argv, "--ignore-nav", "--ignore-nav-path");
        var navIgnorePrefixes = ReadOptionList(argv, "--nav-ignore-prefix", "--nav-ignore-prefixes");
        var navRequiredLinks = ReadOptionList(argv, "--nav-required-link", "--nav-required-links");
        var navProfilesPath = TryGetOptionValue(argv, "--nav-profiles");
        var requiredRoutes = ReadOptionList(argv, "--required-route", "--required-routes");
        var minNavCoverageText = TryGetOptionValue(argv, "--min-nav-coverage");
        var navSelector = TryGetOptionValue(argv, "--nav-selector") ?? "nav";
        var navRequired = !HasOption(argv, "--nav-optional");
        var useDefaultIgnoreNav = !HasOption(argv, "--no-default-ignore-nav");
        var useDefaultExclude = !HasOption(argv, "--no-default-exclude");
        var summaryEnabled = HasOption(argv, "--summary");
        var summaryPath = TryGetOptionValue(argv, "--summary-path");
        var summaryMaxText = TryGetOptionValue(argv, "--summary-max");
        var sarifEnabled = HasOption(argv, "--sarif");
        var sarifPath = TryGetOptionValue(argv, "--sarif-path");
        var navCanonical = TryGetOptionValue(argv, "--nav-canonical");
        var navCanonicalSelector = TryGetOptionValue(argv, "--nav-canonical-selector");
        var navCanonicalRequired = HasOption(argv, "--nav-canonical-required");
        var checkUtf8 = !HasOption(argv, "--no-utf8");
        var checkMetaCharset = !HasOption(argv, "--no-meta-charset");
        var checkReplacementChars = !HasOption(argv, "--no-replacement-char-check");
        var checkHeadingOrder = !HasOption(argv, "--no-heading-order");
        var checkLinkPurpose = !HasOption(argv, "--no-link-purpose");
        var checkNetworkHints = !HasOption(argv, "--no-network-hints");
        var checkRenderBlocking = !HasOption(argv, "--no-render-blocking");
        var maxHeadBlockingText = TryGetOptionValue(argv, "--max-head-blocking");
        var maxTotalFilesText = TryGetOptionValue(argv, "--max-total-files") ?? TryGetOptionValue(argv, "--max-files-total");
        var suppressIssues = ReadOptionList(argv, "--suppress-issue", "--suppress-issues");

        if (requiredRoutes.Count == 0)
            requiredRoutes.Add("/404.html");
        if (navRequiredLinks.Count == 0)
            navRequiredLinks.Add("/");

        var ignoreNavPatterns = BuildIgnoreNavPatterns(ignoreNav, useDefaultIgnoreNav);
        var summaryMax = ParseIntOption(summaryMaxText, 10);
        var minNavCoveragePercent = ParseIntOption(minNavCoverageText, 0);
        var maxHeadBlockingResources = ParseIntOption(maxHeadBlockingText, new WebAuditOptions().MaxHeadBlockingResources);
        var maxTotalFiles = ParseIntOption(maxTotalFilesText, 0);
        var resolvedSummaryPath = ResolveSummaryPath(summaryEnabled, summaryPath);
        var resolvedSarifPath = ResolveSarifPath(sarifEnabled, sarifPath);
        var navProfiles = LoadAuditNavProfiles(navProfilesPath);

        return WebSiteAuditor.Audit(new WebAuditOptions
        {
            SiteRoot = siteRoot,
            Include = include.ToArray(),
            Exclude = exclude.ToArray(),
            UseDefaultExcludes = useDefaultExclude,
            MaxTotalFiles = Math.Max(0, maxTotalFiles),
            SuppressIssues = suppressIssues.ToArray(),
            IgnoreNavFor = ignoreNavPatterns,
            NavSelector = navSelector,
            NavRequired = navRequired,
            NavIgnorePrefixes = navIgnorePrefixes.ToArray(),
            NavRequiredLinks = navRequiredLinks.ToArray(),
            NavProfiles = navProfiles,
            MinNavCoveragePercent = minNavCoveragePercent,
            RequiredRoutes = requiredRoutes.ToArray(),
            CheckLinks = !HasOption(argv, "--no-links"),
            CheckAssets = !HasOption(argv, "--no-assets"),
            CheckNavConsistency = !HasOption(argv, "--no-nav"),
            CheckTitles = !(HasOption(argv, "--no-titles") || HasOption(argv, "--no-title")),
            CheckDuplicateIds = !HasOption(argv, "--no-ids"),
            CheckHtmlStructure = !HasOption(argv, "--no-structure"),
            SummaryPath = resolvedSummaryPath,
            SarifPath = resolvedSarifPath,
            SummaryMaxIssues = summaryMax,
            NavCanonicalPath = navCanonical,
            NavCanonicalSelector = navCanonicalSelector,
            NavCanonicalRequired = navCanonicalRequired,
            CheckUtf8 = checkUtf8,
            CheckMetaCharset = checkMetaCharset,
            CheckUnicodeReplacementChars = checkReplacementChars,
            CheckHeadingOrder = checkHeadingOrder,
            CheckLinkPurposeConsistency = checkLinkPurpose,
            CheckNetworkHints = checkNetworkHints,
            CheckRenderBlockingResources = checkRenderBlocking,
            MaxHeadBlockingResources = maxHeadBlockingResources
        });
    }

    internal static string[] BuildDoctorRecommendations(WebVerifyResult? verify, WebAuditResult? audit, string[]? policyFailures = null)
    {
        var recommendations = new List<string>();

        static bool ContainsText(IEnumerable<string> source, string text) =>
            source.Any(line => line.Contains(text, StringComparison.OrdinalIgnoreCase));

        static bool ContainsCategory(WebAuditResult result, string category) =>
            result.Issues.Any(issue => issue.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

        if (verify is not null)
        {
            if (verify.Errors.Length > 0)
                recommendations.Add("Fix `verify` errors first; they indicate broken site configuration or portability contracts.");
            if (ContainsText(verify.Warnings, "Theme contract:"))
                recommendations.Add("Resolve theme contract warnings (schemaVersion, engine, manifest path, and portable asset paths) to keep themes reusable across repos.");
            if (ContainsText(verify.Warnings, "Theme CSS contract:"))
                recommendations.Add("Resolve theme CSS contract warnings (missing selectors / missing CSS entrypoints) to prevent visual regressions across sites.");
            if (ContainsText(verify.Warnings, "schemaVersion") || ContainsText(verify.Warnings, "contractVersion"))
                recommendations.Add("Standardize all themes on `schemaVersion: 2` and keep only one version field in theme manifests.");
            if (ContainsText(verify.Warnings, "theme manifest"))
                recommendations.Add("Standardize themes on `theme.manifest.json` contract v2 (including `scriptsPath`) for portable reusable themes.");
            if (ContainsText(verify.Warnings, "Navigation lint:"))
                recommendations.Add("Fix navigation lint findings (duplicate IDs, unknown menu references, and stale profile/path filters) before publishing.");
            if (ContainsText(verify.Warnings, "does not match any generated route"))
                recommendations.Add("Align navigation links and visibility/path patterns with generated routes to prevent dead menu entries.");
            if (ContainsText(verify.Warnings, "Markdown hygiene"))
                recommendations.Add("Convert raw HTML-heavy docs to native Markdown to reduce styling drift and simplify maintenance.");
            if (ContainsText(verify.Warnings, "portable relative path"))
                recommendations.Add("Replace rooted/OS-specific paths in theme mappings with portable relative paths.");
        }

        if (audit is not null)
        {
            if (audit.BrokenLinkCount > 0)
                recommendations.Add("Fix broken internal links before publish (`audit` link errors).");
            if (audit.MissingAssetCount > 0)
                recommendations.Add("Fix missing CSS/JS/image assets to avoid runtime regressions.");
            if (audit.MissingRequiredRouteCount > 0)
                recommendations.Add("Ensure required routes like `/404.html` are generated and published.");
            if (audit.NavMismatchCount > 0 || ContainsCategory(audit, "nav"))
                recommendations.Add("Unify navigation templates/components so all page families (docs/api/404) share a consistent nav contract.");
            if (ContainsCategory(audit, "network-hint"))
                recommendations.Add("Add `preconnect`/`dns-prefetch` hints for external origins (for example Google Fonts) to reduce critical path latency.");
            if (ContainsCategory(audit, "render-blocking"))
                recommendations.Add("Reduce render-blocking head resources: defer non-critical scripts and consolidate CSS.");
            if (ContainsCategory(audit, "heading-order"))
                recommendations.Add("Fix heading hierarchy so content does not skip levels (for example h2 -> h4) to improve accessibility.");
            if (ContainsCategory(audit, "link-purpose"))
                recommendations.Add("Use destination-specific link labels (avoid repeated generic labels like 'Learn more').");
            if (ContainsCategory(audit, "utf8"))
                recommendations.Add("Enforce UTF-8 output and meta charset declarations to avoid encoding regressions.");
            if (ContainsCategory(audit, "duplicate-id"))
                recommendations.Add("Remove duplicate HTML IDs to improve accessibility and scripting reliability.");
        }

        if (policyFailures is { Length: > 0 })
            recommendations.Add($"Doctor strict verify policy failed: {string.Join(" | ", policyFailures)}");

        if (recommendations.Count == 0)
            recommendations.Add("No major engine findings detected by doctor. Keep running verify+audit in CI.");

        return recommendations
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static string[] BuildIgnoreNavPatterns(List<string> userPatterns, bool useDefaults)
    {
        if (!useDefaults)
            return userPatterns.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();

        var defaults = new WebAuditOptions().IgnoreNavFor;
        if (userPatterns.Count == 0)
            return defaults;

        return defaults.Concat(userPatterns)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static string ResolveExistingFilePath(string path)
    {
        var full = Path.GetFullPath(path.Trim().Trim('"'));
        if (!File.Exists(full)) throw new FileNotFoundException($"Config file not found: {full}");
        return full;
    }

    internal static string ResolvePathRelative(string baseDir, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (Path.IsPathRooted(value))
            return Path.GetFullPath(value);
        return Path.GetFullPath(Path.Combine(baseDir, value));
    }

    internal static string ApplyArchetypeTemplate(string template, string title, string slug, string collection)
    {
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
        return template
            .Replace("{{title}}", title, StringComparison.OrdinalIgnoreCase)
            .Replace("{{slug}}", slug, StringComparison.OrdinalIgnoreCase)
            .Replace("{{date}}", date, StringComparison.OrdinalIgnoreCase)
            .Replace("{{collection}}", collection, StringComparison.OrdinalIgnoreCase);
    }

    internal static string Slugify(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var lower = input.Trim().ToLowerInvariant();
        var sb = new StringBuilder();
        foreach (var ch in lower)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (char.IsWhiteSpace(ch) || ch == '-' || ch == '_') sb.Append('-');
        }

        var slug = sb.ToString();
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }

    internal static WebSitemapEntry[] LoadSitemapEntries(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Array.Empty<WebSitemapEntry>();
        var full = ResolveExistingFilePath(path);
        var json = File.ReadAllText(full);
        return JsonSerializer.Deserialize<WebSitemapEntry[]>(json, WebCliJson.Options) ?? Array.Empty<WebSitemapEntry>();
    }

    internal static bool IsJsonOutput(string[] argv)
    {
        foreach (var a in argv)
        {
            if (a.Equals("--output-json", StringComparison.OrdinalIgnoreCase) || a.Equals("--json", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var output = TryGetOptionValue(argv, "--output");
        return string.Equals(output, "json", StringComparison.OrdinalIgnoreCase);
    }

    internal static void EnsureUtf8ConsoleEncoding()
    {
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        try
        {
            if (Console.OutputEncoding.CodePage != Encoding.UTF8.CodePage)
                Console.OutputEncoding = utf8NoBom;
        }
        catch
        {
            // Best effort only.
        }

        try
        {
            if (Console.InputEncoding.CodePage != Encoding.UTF8.CodePage)
                Console.InputEncoding = utf8NoBom;
        }
        catch
        {
            // Best effort only.
        }
    }
}
