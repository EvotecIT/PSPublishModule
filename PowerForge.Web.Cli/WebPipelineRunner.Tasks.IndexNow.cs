using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecuteIndexNow(
        JsonElement step,
        string baseDir,
        bool fast,
        string lastBuildOutPath,
        string[] lastBuildUpdatedFiles,
        WebPipelineStepResult stepResult)
    {
        var baseUrl = GetString(step, "baseUrl") ?? GetString(step, "base-url");
        var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
        var failOnEmpty = GetBool(step, "failOnEmpty") ?? false;
        var failOnRequestError = !(GetBool(step, "continueOnError") ?? false);
        if (GetBool(step, "failOnRequestError") is bool explicitFailOnRequestError)
            failOnRequestError = explicitFailOnRequestError;

        var urls = new List<string>();
        urls.AddRange(ReadIndexNowStringList(step, "urls", "url"));

        var paths = ReadIndexNowStringList(step, "paths", "path").ToArray();
        if (paths.Length > 0)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException("indexnow: missing 'baseUrl' (required when using paths).");
            urls.AddRange(paths.Select(path => CombineIndexNowUrl(baseUrl, path)));
        }

        var urlFilePath = ResolvePath(baseDir, GetString(step, "urlFile") ?? GetString(step, "url-file"));
        if (!string.IsNullOrWhiteSpace(urlFilePath))
        {
            if (!File.Exists(urlFilePath))
                throw new FileNotFoundException($"indexnow: url file not found: {urlFilePath}");
            var fileEntries = File.ReadAllLines(urlFilePath)
                .SelectMany(static line => line.Split(new[] { ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                .Select(static token => token.Trim())
                .Where(static token => !string.IsNullOrWhiteSpace(token) && !token.StartsWith('#'))
                .ToArray();
            foreach (var entry in fileEntries)
            {
                if (Uri.TryCreate(entry, UriKind.Absolute, out var absolute) &&
                    (absolute.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                     absolute.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
                {
                    urls.Add(absolute.ToString());
                    continue;
                }

                if (string.IsNullOrWhiteSpace(baseUrl))
                    throw new InvalidOperationException("indexnow: urlFile contains relative paths but baseUrl is not set.");

                urls.Add(CombineIndexNowUrl(baseUrl, entry));
            }
        }

        var sitemapPath = ResolvePath(baseDir,
            GetString(step, "sitemap") ??
            GetString(step, "sitemapPath") ??
            GetString(step, "sitemap-path"));
        if (!string.IsNullOrWhiteSpace(sitemapPath))
        {
            if (!File.Exists(sitemapPath))
                throw new FileNotFoundException($"indexnow: sitemap file not found: {sitemapPath}");
            urls.AddRange(ReadSitemapLocUrls(sitemapPath));
        }

        var scopeFromBuildUpdated = GetBool(step, "scopeFromBuildUpdated") ?? GetBool(step, "scope-from-build-updated");
        if ((scopeFromBuildUpdated == true || (scopeFromBuildUpdated != false && fast)) &&
            !string.IsNullOrWhiteSpace(baseUrl) &&
            !string.IsNullOrWhiteSpace(siteRoot) &&
            lastBuildUpdatedFiles.Length > 0 &&
            string.Equals(Path.GetFullPath(siteRoot), lastBuildOutPath, FileSystemPathComparison))
        {
            urls.AddRange(BuildIndexNowUrlsFromUpdatedFiles(baseUrl, siteRoot, lastBuildUpdatedFiles));
        }

        var normalizedUrls = urls
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var maxUrls = GetInt(step, "maxUrls") ?? GetInt(step, "max-urls") ?? 10_000;
        var truncateToMaxUrls = GetBool(step, "truncateToMaxUrls") ?? GetBool(step, "truncate-to-max-urls") ?? true;
        if (maxUrls > 0 && normalizedUrls.Count > maxUrls)
        {
            if (!truncateToMaxUrls)
                throw new InvalidOperationException($"indexnow: URL count {normalizedUrls.Count} exceeds maxUrls {maxUrls}.");
            normalizedUrls = normalizedUrls.Take(maxUrls).ToList();
        }

        if (normalizedUrls.Count == 0)
        {
            var noUrlsMessage = "indexnow: no URLs to submit.";
            if (failOnEmpty)
                throw new InvalidOperationException(noUrlsMessage);
            stepResult.Success = true;
            stepResult.Message = noUrlsMessage;
            return;
        }

        var key = GetString(step, "key");
        var keyPath = ResolvePath(baseDir, GetString(step, "keyPath") ?? GetString(step, "key-path"));
        if (string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(keyPath))
        {
            if (!File.Exists(keyPath))
                throw new FileNotFoundException($"indexnow: key file not found: {keyPath}");
            key = File.ReadLines(keyPath)
                .Select(static line => line.Trim())
                .FirstOrDefault(static line => !string.IsNullOrWhiteSpace(line));
        }

        var keyEnv = GetString(step, "keyEnv") ?? GetString(step, "key-env") ?? "INDEXNOW_KEY";
        var keyLocation = GetString(step, "keyLocation") ?? GetString(step, "key-location");
        var optionalKey = GetBool(step, "optionalKey") ??
                          GetBool(step, "optional-key") ??
                          GetBool(step, "skipIfMissingKey") ??
                          GetBool(step, "skip-if-missing-key") ??
                          false;
        string? keySource = null;
        if (string.IsNullOrWhiteSpace(key))
        {
            key = Environment.GetEnvironmentVariable(keyEnv);
            if (!string.IsNullOrWhiteSpace(key))
                keySource = $"env '{keyEnv}'";
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            key = TryReadIndexNowVerificationKey(baseDir, siteRoot, keyLocation, out keySource);
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            if (optionalKey)
            {
                stepResult.Success = true;
                stepResult.Message = $"indexnow: skipped (missing key env '{keyEnv}' and verification file)";
                return;
            }
            throw new InvalidOperationException($"indexnow: missing key (set env '{keyEnv}', provide key/keyPath, or publish indexnow.txt).");
        }

        var endpointValues = new List<string>();
        endpointValues.AddRange(ReadIndexNowStringList(step, "endpoint", "apiEndpoint", "engine"));
        endpointValues.AddRange(GetArrayOfStrings(step, "endpoints") ?? Array.Empty<string>());
        endpointValues.AddRange(GetArrayOfStrings(step, "engines") ?? Array.Empty<string>());

        var submissionResult = IndexNowSubmitter.Submit(new IndexNowSubmissionOptions
        {
            Urls = normalizedUrls,
            Endpoints = endpointValues,
            Key = key,
            KeyLocation = keyLocation,
            Host = GetString(step, "host"),
            DryRun = GetBool(step, "dryRun") ?? GetBool(step, "dry-run") ?? false,
            FailOnRequestError = failOnRequestError,
            BatchSize = GetInt(step, "batchSize") ?? GetInt(step, "batch-size") ?? 500,
            RetryCount = GetInt(step, "retry") ?? GetInt(step, "retries") ?? GetInt(step, "retryCount") ?? GetInt(step, "retry-count") ?? 2,
            RetryDelayMs = GetInt(step, "retryDelayMs") ?? GetInt(step, "retry-delay-ms") ?? GetInt(step, "retryDelay") ?? GetInt(step, "retry-delay") ?? 500,
            TimeoutSeconds = GetInt(step, "timeoutSeconds") ?? GetInt(step, "timeout-seconds") ?? 20
        }, logger: null);

        var reportPath = GetString(step, "reportPath") ?? GetString(step, "report-path");
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            var resolvedReportPath = ResolvePathWithinRoot(baseDir, reportPath, reportPath);
            var reportDirectory = Path.GetDirectoryName(resolvedReportPath);
            if (!string.IsNullOrWhiteSpace(reportDirectory))
                Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(resolvedReportPath, JsonSerializer.Serialize(submissionResult, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));
        }

        var summaryPath = GetString(step, "summaryPath") ?? GetString(step, "summary-path");
        if (!string.IsNullOrWhiteSpace(summaryPath))
        {
            var resolvedSummaryPath = ResolvePathWithinRoot(baseDir, summaryPath, summaryPath);
            var summaryDirectory = Path.GetDirectoryName(resolvedSummaryPath);
            if (!string.IsNullOrWhiteSpace(summaryDirectory))
                Directory.CreateDirectory(summaryDirectory);
            File.WriteAllText(resolvedSummaryPath, BuildIndexNowMarkdownSummary(submissionResult));
        }

        stepResult.Success = submissionResult.Success;
        stepResult.Message = BuildIndexNowSummary(submissionResult);
        if (!string.IsNullOrWhiteSpace(keySource))
            stepResult.Message += $", key={keySource}";
        if (!submissionResult.Success)
            throw new InvalidOperationException(stepResult.Message);
    }

    private static string? TryReadIndexNowVerificationKey(string baseDir, string? siteRoot, string? keyLocation, out string? source)
    {
        source = null;
        foreach (var candidate in EnumerateIndexNowKeyCandidates(baseDir, siteRoot, keyLocation))
        {
            if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate))
                continue;

            var key = File.ReadLines(candidate)
                .Select(static line => line.Trim())
                .FirstOrDefault(static line => !string.IsNullOrWhiteSpace(line));
            if (string.IsNullOrWhiteSpace(key))
                continue;

            source = FormatIndexNowKeySource(baseDir, candidate);
            return key;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateIndexNowKeyCandidates(string baseDir, string? siteRoot, string? keyLocation)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keyLocationRelativePaths = EnumerateIndexNowKeyRelativePaths(keyLocation).ToArray();

        foreach (var relative in keyLocationRelativePaths)
        {
            if (!string.IsNullOrWhiteSpace(siteRoot))
            {
                foreach (var candidate in AddCandidateIterator(Path.Combine(siteRoot, relative)))
                    yield return candidate;
            }

            foreach (var candidate in AddCandidateIterator(Path.Combine(baseDir, relative)))
                yield return candidate;

            foreach (var candidate in AddCandidateIterator(Path.Combine(baseDir, "static", relative)))
                yield return candidate;
        }

        if (keyLocationRelativePaths.Length > 0 &&
            !keyLocationRelativePaths.Any(IsRootIndexNowVerificationPath))
        {
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(siteRoot))
        {
            foreach (var candidate in AddCandidateIterator(Path.Combine(siteRoot, "indexnow.txt")))
                yield return candidate;
        }

        foreach (var candidate in AddCandidateIterator(Path.Combine(baseDir, "indexnow.txt")))
            yield return candidate;
        foreach (var candidate in AddCandidateIterator(Path.Combine(baseDir, "static", "indexnow.txt")))
            yield return candidate;

        IEnumerable<string> AddCandidateIterator(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                yield break;

            var full = Path.GetFullPath(path);
            if (seen.Add(full))
                yield return full;
        }
    }

    private static bool IsRootIndexNowVerificationPath(string relativePath)
        => relativePath
            .Replace('\\', '/')
            .Trim('/')
            .Equals("indexnow.txt", StringComparison.OrdinalIgnoreCase);

    private static string FormatIndexNowKeySource(string baseDir, string path)
    {
        var full = Path.GetFullPath(path);
        try
        {
            var relative = Path.GetRelativePath(Path.GetFullPath(baseDir), full);
            if (!string.IsNullOrWhiteSpace(relative) &&
                !relative.StartsWith("..", StringComparison.Ordinal) &&
                !Path.IsPathRooted(relative))
            {
                return relative;
            }
        }
        catch
        {
            // Fall back to the full path when the platform cannot compute a relative display path.
        }

        return full;
    }

    private static IEnumerable<string> EnumerateIndexNowKeyRelativePaths(string? keyLocation)
    {
        if (string.IsNullOrWhiteSpace(keyLocation))
            yield break;

        var trimmed = keyLocation.Trim();
        string? path = null;
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            path = uri.AbsolutePath;
        else
            path = trimmed;

        if (string.IsNullOrWhiteSpace(path))
            yield break;

        path = Uri.UnescapeDataString(path.TrimStart('/').Replace('\\', '/'));
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (string.IsNullOrWhiteSpace(path) ||
            segments.Any(static segment => segment.Equals("..", StringComparison.Ordinal)) ||
            Path.IsPathRooted(path))
        {
            yield break;
        }

        yield return path;
    }

    private static IEnumerable<string> ReadIndexNowStringList(JsonElement step, params string[] names)
    {
        foreach (var name in names)
        {
            var value = GetString(step, name);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            foreach (var token in value.Split(new[] { ',', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = token.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    yield return trimmed;
            }
        }
    }

    private static IEnumerable<string> ReadSitemapLocUrls(string sitemapPath)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(sitemapPath, LoadOptions.None);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"indexnow: failed to parse sitemap '{sitemapPath}': {ex.Message}", ex);
        }

        return document
            .Descendants()
            .Where(static node => node.Name.LocalName.Equals("loc", StringComparison.OrdinalIgnoreCase))
            .Select(node => (node.Value ?? string.Empty).Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> BuildIndexNowUrlsFromUpdatedFiles(string baseUrl, string siteRoot, string[] updatedFiles)
    {
        var siteRootFull = Path.GetFullPath(siteRoot);
        var outputs = new List<string>();

        foreach (var value in updatedFiles)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            if (!value.EndsWith(".html", StringComparison.OrdinalIgnoreCase) &&
                !value.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
                continue;

            var filePath = Path.IsPathRooted(value)
                ? Path.GetFullPath(value)
                : Path.GetFullPath(Path.Combine(siteRootFull, value));
            if (!filePath.StartsWith(siteRootFull, FileSystemPathComparison))
                continue;

            var relative = Path.GetRelativePath(siteRootFull, filePath)
                .Replace('\\', '/')
                .TrimStart('/');
            if (string.IsNullOrWhiteSpace(relative))
                continue;

            string route;
            if (relative.Equals("index.html", StringComparison.OrdinalIgnoreCase) ||
                relative.Equals("index.htm", StringComparison.OrdinalIgnoreCase))
            {
                route = "/";
            }
            else if (relative.EndsWith("/index.html", StringComparison.OrdinalIgnoreCase))
            {
                route = "/" + relative[..^"/index.html".Length] + "/";
            }
            else if (relative.EndsWith("/index.htm", StringComparison.OrdinalIgnoreCase))
            {
                route = "/" + relative[..^"/index.htm".Length] + "/";
            }
            else
            {
                route = "/" + relative;
            }

            outputs.Add(CombineIndexNowUrl(baseUrl, route));
        }

        return outputs
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string CombineIndexNowUrl(string baseUrl, string pathOrUrl)
    {
        if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             absolute.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            return absolute.ToString();

        var normalizedBase = (baseUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedBase))
            return pathOrUrl ?? string.Empty;

        if (!normalizedBase.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !normalizedBase.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalizedBase = "https://" + normalizedBase;
        }

        if (!Uri.TryCreate(normalizedBase, UriKind.Absolute, out var baseUri))
            throw new InvalidOperationException($"indexnow: invalid baseUrl '{baseUrl}'.");

        var candidate = (pathOrUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(candidate))
            return baseUri.ToString();

        if (!candidate.StartsWith("/", StringComparison.Ordinal))
            candidate = "/" + candidate;

        var combined = new Uri(baseUri, candidate);
        return combined.ToString();
    }

    private static string BuildIndexNowSummary(IndexNowSubmissionResult result)
    {
        var summary = $"indexnow: {result.UrlCount} urls, {result.RequestCount} requests, {result.FailedRequestCount} failed";
        if (result.DryRun)
            summary += " (dry-run)";
        if (result.Warnings.Length > 0)
            summary += $", {result.Warnings.Length} warnings";
        if (result.Errors.Length > 0)
            summary += $", {result.Errors.Length} errors";
        if (result.Errors.Length > 0)
            summary += $", first error: {result.Errors[0]}";
        return summary;
    }

    private static string BuildIndexNowMarkdownSummary(IndexNowSubmissionResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# IndexNow Summary");
        builder.AppendLine();
        builder.AppendLine($"- Success: {(result.Success ? "yes" : "no")}");
        builder.AppendLine($"- Dry run: {(result.DryRun ? "yes" : "no")}");
        builder.AppendLine($"- URLs: {result.UrlCount}");
        builder.AppendLine($"- Hosts: {result.HostCount}");
        builder.AppendLine($"- Requests: {result.RequestCount}");
        builder.AppendLine($"- Failed requests: {result.FailedRequestCount}");
        builder.AppendLine($"- Warnings: {result.Warnings.Length}");
        builder.AppendLine($"- Errors: {result.Errors.Length}");

        if (result.Errors.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Errors");
            builder.AppendLine();
            foreach (var error in result.Errors.Take(20))
                builder.AppendLine($"- {error}");
        }

        if (result.Warnings.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Warnings");
            builder.AppendLine();
            foreach (var warning in result.Warnings.Take(20))
                builder.AppendLine($"- {warning}");
        }

        return builder.ToString();
    }
}
