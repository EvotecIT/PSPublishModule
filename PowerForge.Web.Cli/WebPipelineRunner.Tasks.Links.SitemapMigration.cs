using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecuteLinksCompareSitemaps(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var legacySitemapValues = GetStringOrArrayOfStrings(
            step,
            "legacySitemaps",
            "legacy-sitemaps",
            "legacySitemapPaths",
            "legacy-sitemap-paths",
            "legacySitemap",
            "legacy-sitemap",
            "legacySitemapPath",
            "legacy-sitemap-path");
        if (legacySitemapValues.Length == 0)
            throw new InvalidOperationException("links-compare-sitemaps requires legacySitemaps or legacySitemapPath.");

        var newSitemapValue = GetString(step, "newSitemap") ??
                              GetString(step, "new-sitemap") ??
                              GetString(step, "newSitemapPath") ??
                              GetString(step, "new-sitemap-path");
        if (string.IsNullOrWhiteSpace(newSitemapValue))
            throw new InvalidOperationException("links-compare-sitemaps requires newSitemap or newSitemapPath.");

        var timeoutSeconds = GetInt(step, "timeoutSeconds") ??
                             GetInt(step, "timeout-seconds") ??
                             GetInt(step, "timeoutSec") ??
                             GetInt(step, "timeout-sec") ??
                             30;
        var maxSitemapDepth = GetInt(step, "maxSitemapDepth") ??
                              GetInt(step, "max-sitemap-depth") ??
                              8;

        using var sitemapHttpClient = CreateSitemapHttpClient(timeoutSeconds);
        var legacyUrls = new List<string>();
        foreach (var legacySitemapValue in legacySitemapValues)
        {
            var location = ResolveSitemapInput(baseDir, legacySitemapValue);
            legacyUrls.AddRange(ReadSitemapUrls(location, sitemapHttpClient, maxSitemapDepth));
        }

        legacyUrls.AddRange(GetStringOrArrayOfStrings(
            step,
            "additionalLegacyUrls",
            "additional-legacy-urls",
            "legacyUrls",
            "legacy-urls"));

        var newSitemapLocation = ResolveSitemapInput(baseDir, newSitemapValue);
        var newUrls = ReadSitemapUrls(newSitemapLocation, sitemapHttpClient, maxSitemapDepth);
        var defaultSiteRoot = IsRemoteSitemapLocation(newSitemapLocation)
            ? null
            : Path.GetDirectoryName(newSitemapLocation);
        var newSiteRoot = ResolvePath(
            baseDir,
            GetString(step, "newSiteRoot") ??
            GetString(step, "new-site-root") ??
            GetString(step, "siteRoot") ??
            GetString(step, "site-root")) ?? defaultSiteRoot;

        var result = WebSitemapMigrationAnalyzer.Analyze(new WebSitemapMigrationOptions
        {
            LegacyUrls = legacyUrls.ToArray(),
            NewUrls = newUrls,
            NewSiteRoot = newSiteRoot,
            IncludeSyntheticAmpRedirects = GetBool(step, "includeSyntheticAmpRedirects") ??
                                           GetBool(step, "include-synthetic-amp-redirects") ??
                                           true,
            IncludeAmpListingRoots = GetBool(step, "includeAmpListingRoots") ??
                                     GetBool(step, "include-amp-listing-roots") ??
                                     false
        });

        var outputPath = ResolvePath(
                             baseDir,
                             GetString(step, "out") ??
                             GetString(step, "output") ??
                             GetString(step, "outputPath") ??
                             GetString(step, "output-path") ??
                             GetString(step, "summaryPath") ??
                             GetString(step, "summary-path")) ??
                         Path.GetFullPath(Path.Combine(baseDir, "Build", "link-reports", "sitemap-migration.json"));
        WriteSitemapMigrationSummary(outputPath, result);

        var redirectCsvPath = ResolvePath(
            baseDir,
            GetString(step, "redirectCsv") ??
            GetString(step, "redirect-csv") ??
            GetString(step, "redirectCsvPath") ??
            GetString(step, "redirect-csv-path") ??
            GetString(step, "outputRedirectCsvPath") ??
            GetString(step, "output-redirect-csv-path"));
        WriteSitemapMigrationRedirectCsv(redirectCsvPath, result.Redirects);

        var reviewCsvPath = ResolvePath(
            baseDir,
            GetString(step, "reviewCsv") ??
            GetString(step, "review-csv") ??
            GetString(step, "reviewCsvPath") ??
            GetString(step, "review-csv-path") ??
            GetString(step, "outputReviewCsvPath") ??
            GetString(step, "output-review-csv-path"));
        WriteSitemapMigrationReviewCsv(reviewCsvPath, result.Reviews);

        stepResult.Success = true;
        stepResult.Message = $"links-compare-sitemaps ok: legacy={result.LegacyUrlCount}; new={result.NewUrlCount}; missing={result.MissingLegacyCount}; redirects={result.RedirectCount}; review={result.ReviewCount}";
    }

    private static string ResolveSitemapInput(string baseDir, string value)
        => IsRemoteSitemapLocation(value) ? value.Trim() : ResolvePath(baseDir, value) ?? value.Trim();

    private static string[] ReadSitemapUrls(string location, HttpClient httpClient, int maxDepth)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return ReadSitemapUrls(location, httpClient, Math.Max(0, maxDepth), depth: 0, visited)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> ReadSitemapUrls(string location, HttpClient httpClient, int maxDepth, int depth, ISet<string> visited)
    {
        if (string.IsNullOrWhiteSpace(location) || !visited.Add(location))
            return Array.Empty<string>();
        if (depth > maxDepth)
            throw new InvalidOperationException($"Sitemap nesting exceeded maxSitemapDepth ({maxDepth}) at {location}.");

        var document = LoadSitemapXml(location, httpClient);
        var rootName = document.Root?.Name.LocalName;
        if (string.Equals(rootName, "urlset", StringComparison.OrdinalIgnoreCase))
        {
            return document
                .Descendants()
                .Where(static element => string.Equals(element.Name.LocalName, "loc", StringComparison.OrdinalIgnoreCase))
                .Select(static element => element.Value.Trim())
                .Where(static value => !string.IsNullOrWhiteSpace(value));
        }

        if (!string.Equals(rootName, "sitemapindex", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported sitemap XML root '{rootName ?? "(none)"}' in {location}.");

        var locationBase = IsRemoteSitemapLocation(location)
            ? location
            : Path.GetDirectoryName(location) ?? Directory.GetCurrentDirectory();
        var urls = new List<string>();
        foreach (var nested in document.Descendants().Where(static element => string.Equals(element.Name.LocalName, "loc", StringComparison.OrdinalIgnoreCase)))
        {
            var nestedLocation = ResolveNestedSitemapLocation(locationBase, nested.Value.Trim());
            urls.AddRange(ReadSitemapUrls(nestedLocation, httpClient, maxDepth, depth + 1, visited));
        }

        return urls;
    }

    private static XDocument LoadSitemapXml(string location, HttpClient httpClient)
    {
        using var reader = XmlReader.Create(OpenSitemapReader(location, httpClient), NewSafeSitemapXmlReaderSettings());
        return XDocument.Load(reader);
    }

    private static TextReader OpenSitemapReader(string location, HttpClient httpClient)
    {
        if (!IsRemoteSitemapLocation(location))
        {
            if (!File.Exists(location))
                throw new FileNotFoundException("Sitemap file not found.", location);
            return File.OpenText(location);
        }

        var content = httpClient.GetStringAsync(location).GetAwaiter().GetResult();
        return new StringReader(content);
    }

    private static HttpClient CreateSitemapHttpClient(int timeoutSeconds)
    {
        var handler = new HttpClientHandler
        {
            MaxAutomaticRedirections = 5
        };
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)),
            MaxResponseContentBufferSize = 50L * 1024L * 1024L
        };
    }

    private static XmlReaderSettings NewSafeSitemapXmlReaderSettings()
        => new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };

    private static string ResolveNestedSitemapLocation(string baseLocation, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
            return absolute.IsFile ? absolute.LocalPath : value;
        if (IsRemoteSitemapLocation(baseLocation) && Uri.TryCreate(new Uri(baseLocation), value, out var nestedRemote))
            return nestedRemote.AbsoluteUri;
        return Path.GetFullPath(Path.Combine(baseLocation, value.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static bool IsRemoteSitemapLocation(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));

    private static void WriteSitemapMigrationSummary(string outputPath, WebSitemapMigrationResult result)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(result, LinksSummaryJsonContext.WebSitemapMigrationResult));
    }

    private static void WriteSitemapMigrationRedirectCsv(string? outputPath, IEnumerable<WebSitemapMigrationRedirectRow> rows)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            return;

        WriteCsv(outputPath, "legacy_url,target_url,status,match_kind,notes", rows.Select(static row => new[]
        {
            row.LegacyUrl,
            row.TargetUrl,
            row.Status.ToString(System.Globalization.CultureInfo.InvariantCulture),
            row.MatchKind,
            row.Notes
        }));
    }

    private static void WriteSitemapMigrationReviewCsv(string? outputPath, IEnumerable<WebSitemapMigrationReviewRow> rows)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            return;

        WriteCsv(outputPath, "legacy_url,target_url,match_kind,notes", rows.Select(static row => new[]
        {
            row.LegacyUrl,
            row.TargetUrl,
            row.MatchKind,
            row.Notes
        }));
    }

    private static void WriteCsv(string outputPath, string header, IEnumerable<string[]> rows)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var builder = new StringBuilder();
        builder.AppendLine(header);
        foreach (var row in rows)
            builder.AppendLine(string.Join(",", row.Select(EscapeCsv)));
        File.WriteAllText(outputPath, builder.ToString());
    }

    private static string EscapeCsv(string? value)
    {
        var safe = value ?? string.Empty;
        return safe.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0
            ? safe
            : "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
