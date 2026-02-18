using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static bool IsCacheableTask(string task)
    {
        if (string.IsNullOrWhiteSpace(task))
            return false;

        // Tasks with external side-effects should not be cached.
        if (task.Equals("cloudflare", StringComparison.OrdinalIgnoreCase))
            return false;
        if (task.Equals("indexnow", StringComparison.OrdinalIgnoreCase))
            return false;
        if (task.Equals("exec", StringComparison.OrdinalIgnoreCase))
            return false;
        if (task.Equals("hook", StringComparison.OrdinalIgnoreCase))
            return false;
        if (task.Equals("html-transform", StringComparison.OrdinalIgnoreCase))
            return false;
        if (task.Equals("data-transform", StringComparison.OrdinalIgnoreCase))
            return false;
        if (task.Equals("git-sync", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static WebPipelineCacheState LoadPipelineCache(string cachePath, WebConsoleLogger? logger)
    {
        try
        {
            if (!File.Exists(cachePath))
                return new WebPipelineCacheState();

            var fileInfo = new FileInfo(cachePath);
            if (fileInfo.Length > MaxStateFileSizeBytes)
            {
                logger?.Warn($"Pipeline cache file too large ({fileInfo.Length} bytes), ignoring cache.");
                return new WebPipelineCacheState();
            }

            using var stream = File.OpenRead(cachePath);
            var state = JsonSerializer.Deserialize<WebPipelineCacheState>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return state ?? new WebPipelineCacheState();
        }
        catch (Exception ex)
        {
            logger?.Warn($"Pipeline cache load failed: {ex.Message}");
            return new WebPipelineCacheState();
        }
    }

    private static void SavePipelineCache(string cachePath, WebPipelineCacheState state, WebConsoleLogger? logger)
    {
        try
        {
            var directory = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(cachePath, json);
        }
        catch (Exception ex)
        {
            logger?.Warn($"Pipeline cache save failed: {ex.Message}");
        }
    }

    private static void WritePipelineProfile(string profilePath, WebPipelineResult result, WebConsoleLogger? logger)
    {
        try
        {
            var directory = Path.GetDirectoryName(profilePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(result, WebCliJson.Context.WebPipelineResult);
            File.WriteAllText(profilePath, json);
        }
        catch (Exception ex)
        {
            logger?.Warn($"Pipeline profile write failed: {ex.Message}");
        }
    }

    private static string ComputeStepFingerprint(string baseDir, JsonElement step, string? salt = null)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(salt))
            parts.Add($"salt:{salt}");
        parts.Add(step.GetRawText());
        var paths = EnumerateFingerprintPaths(baseDir, step)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            parts.Add(BuildPathStamp(path));
        }

        var payload = string.Join('\n', parts);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static IEnumerable<string> EnumerateFingerprintPaths(string baseDir, JsonElement step)
    {
        if (step.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (var property in step.EnumerateObject())
        {
            if (!FingerprintPathKeys.Contains(property.Name))
                continue;

            if (property.Value.ValueKind == JsonValueKind.String)
            {
                var value = property.Value.GetString();
                if (string.IsNullOrWhiteSpace(value) || IsExternalUri(value))
                    continue;
                var resolved = ResolvePath(baseDir, value);
                if (!string.IsNullOrWhiteSpace(resolved))
                    yield return Path.GetFullPath(resolved);
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in property.Value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (string.IsNullOrWhiteSpace(value) || IsExternalUri(value))
                        continue;
                    var resolved = ResolvePath(baseDir, value);
                    if (!string.IsNullOrWhiteSpace(resolved))
                        yield return Path.GetFullPath(resolved);
                    continue;
                }

                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var nestedProperty in item.EnumerateObject())
                {
                    if (!FingerprintPathKeys.Contains(nestedProperty.Name))
                        continue;
                    if (nestedProperty.Value.ValueKind != JsonValueKind.String)
                        continue;

                    var nestedValue = nestedProperty.Value.GetString();
                    if (string.IsNullOrWhiteSpace(nestedValue) || IsExternalUri(nestedValue))
                        continue;

                    var nestedResolved = ResolvePath(baseDir, nestedValue);
                    if (!string.IsNullOrWhiteSpace(nestedResolved))
                        yield return Path.GetFullPath(nestedResolved);
                }
            }
        }
    }

    private static string BuildPathStamp(string path)
    {
        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            return $"f|{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }

        if (!Directory.Exists(path))
            return $"m|{path}";

        try
        {
            var maxTicks = Directory.GetLastWriteTimeUtc(path).Ticks;
            var fileCount = 0;
            var truncated = false;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                if (fileCount >= MaxStampFileCount)
                {
                    truncated = true;
                    break;
                }

                fileCount++;
                var ticks = File.GetLastWriteTimeUtc(file).Ticks;
                if (ticks > maxTicks)
                    maxTicks = ticks;
            }

            return truncated
                ? $"d|{path}|{fileCount}|{maxTicks}|truncated"
                : $"d|{path}|{fileCount}|{maxTicks}";
        }
        catch
        {
            return $"d|{path}|unreadable";
        }
    }

    private static bool IsExternalUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;
        return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
               uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] GetExpectedStepOutputs(string task, JsonElement step, string baseDir)
    {
        switch (task)
        {
            case "build":
                return ResolveOutputCandidates(baseDir, GetString(step, "out") ?? GetString(step, "output"));
            case "apidocs":
            {
                var outputs = new List<string>();
                outputs.AddRange(ResolveOutputCandidates(baseDir, GetString(step, "out") ?? GetString(step, "output")));

                var inputs = GetArrayOfObjects(step, "inputs") ?? GetArrayOfObjects(step, "entries");
                if (inputs is { Length: > 0 })
                {
                    foreach (var input in inputs)
                        outputs.AddRange(ResolveOutputCandidates(baseDir, GetString(input, "out") ?? GetString(input, "output")));
                }

                return outputs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            case "dotnet-publish":
                return ResolveOutputCandidates(baseDir, GetString(step, "out") ?? GetString(step, "output"));
            case "overlay":
                return ResolveOutputCandidates(baseDir, GetString(step, "destination") ?? GetString(step, "dest"));
            case "html-transform":
            {
                var outputs = new List<string>();
                outputs.AddRange(ResolveOutputCandidates(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root")));
                outputs.AddRange(ResolveOutputCandidates(baseDir, GetString(step, "reportPath") ?? GetString(step, "report-path")));
                return outputs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            case "data-transform":
            {
                var outputs = new List<string>();
                outputs.AddRange(ResolveOutputCandidates(baseDir,
                    GetString(step, "out") ??
                    GetString(step, "output") ??
                    GetString(step, "outputPath") ??
                    GetString(step, "output-path") ??
                    GetString(step, "destination") ??
                    GetString(step, "dest")));
                outputs.AddRange(ResolveOutputCandidates(baseDir, GetString(step, "reportPath") ?? GetString(step, "report-path")));
                return outputs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            case "model-transform":
            {
                var outputs = new List<string>();
                outputs.AddRange(ResolveOutputCandidates(baseDir,
                    GetString(step, "out") ??
                    GetString(step, "output") ??
                    GetString(step, "outputPath") ??
                    GetString(step, "output-path") ??
                    GetString(step, "destination") ??
                    GetString(step, "dest")));
                outputs.AddRange(ResolveOutputCandidates(baseDir, GetString(step, "reportPath") ?? GetString(step, "report-path")));
                return outputs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            case "git-sync":
            {
                var outputs = new List<string>();
                outputs.AddRange(ResolveOutputCandidates(baseDir, GetString(step, "destination") ?? GetString(step, "dest") ?? GetString(step, "path")));

                var repos = GetArrayOfObjects(step, "repos") ?? GetArrayOfObjects(step, "repositories");
                if (repos is { Length: > 0 })
                {
                    foreach (var repo in repos)
                        outputs.AddRange(ResolveOutputCandidates(baseDir, GetString(repo, "destination") ?? GetString(repo, "dest") ?? GetString(repo, "path")));
                }

                var manifestPath = ResolveGitSyncManifestPath(step, baseDir);
                if (!string.IsNullOrWhiteSpace(manifestPath))
                    outputs.Add(manifestPath);
                var lockMode = NormalizeGitLockMode(GetString(step, "lockMode") ?? GetString(step, "lock-mode"));
                var lockPath = ResolveGitSyncLockPath(step, baseDir, lockMode);
                if (!string.IsNullOrWhiteSpace(lockPath))
                    outputs.Add(lockPath);

                return outputs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            case "hosting":
            {
                var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
                if (string.IsNullOrWhiteSpace(siteRoot))
                    return Array.Empty<string>();

                var outputs = new List<string> { Path.GetFullPath(siteRoot) };
                string[] targets;
                try
                {
                    targets = ResolveHostingTargets(step);
                }
                catch
                {
                    targets = HostingTargetsAll;
                }

                foreach (var target in targets)
                {
                    var file = ResolveHostingFileName(target);
                    if (string.IsNullOrWhiteSpace(file))
                        continue;
                    outputs.Add(Path.GetFullPath(Path.Combine(siteRoot, file)));
                }

                return outputs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            case "changelog":
                return ResolveOutputCandidates(baseDir, GetString(step, "out") ?? GetString(step, "output"));
            case "version-hub":
                return ResolveOutputCandidates(baseDir, GetString(step, "out") ?? GetString(step, "output"));
            case "compat-matrix":
            {
                var outputs = new List<string>();
                outputs.AddRange(ResolveOutputCandidates(baseDir, GetString(step, "out") ?? GetString(step, "output")));
                outputs.AddRange(ResolveOutputCandidates(baseDir,
                    GetString(step, "markdownOut") ??
                    GetString(step, "markdown-out") ??
                    GetString(step, "markdownOutput") ??
                    GetString(step, "markdown-output")));
                return outputs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            case "xref-merge":
                return ResolveOutputCandidates(baseDir, GetString(step, "out") ?? GetString(step, "output"));
            case "package-hub":
                return ResolveOutputCandidates(baseDir, GetString(step, "out") ?? GetString(step, "output"));
            case "llms":
            {
                var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
                if (string.IsNullOrWhiteSpace(siteRoot))
                    return Array.Empty<string>();
                return new[]
                {
                    Path.Combine(siteRoot, "llms.txt"),
                    Path.Combine(siteRoot, "llms.json"),
                    Path.Combine(siteRoot, "llms-full.txt")
                };
            }
            case "sitemap":
            {
                var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
                var outPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output"));
                if (string.IsNullOrWhiteSpace(outPath) && !string.IsNullOrWhiteSpace(siteRoot))
                    outPath = Path.Combine(siteRoot, "sitemap.xml");
                var outputs = new List<string>();
                outputs.AddRange(ResolveOutputCandidates(baseDir, outPath));

                var htmlEnabled = GetBool(step, "html") ?? false;
                var htmlOutput = ResolvePath(baseDir,
                    GetString(step, "htmlOutput") ??
                    GetString(step, "htmlOut") ??
                    GetString(step, "html-out"));
                if (htmlEnabled || !string.IsNullOrWhiteSpace(htmlOutput))
                {
                    if (string.IsNullOrWhiteSpace(htmlOutput) && !string.IsNullOrWhiteSpace(siteRoot))
                        htmlOutput = Path.Combine(siteRoot, "sitemap", "index.html");
                    outputs.AddRange(ResolveOutputCandidates(baseDir, htmlOutput));
                }

                var jsonEnabled = GetBool(step, "json") ?? htmlEnabled;
                var jsonOutput = ResolvePath(baseDir,
                    GetString(step, "jsonOutput") ??
                    GetString(step, "jsonOut") ??
                    GetString(step, "json-output") ??
                    GetString(step, "json-out"));
                if (jsonEnabled || !string.IsNullOrWhiteSpace(jsonOutput))
                {
                    if (string.IsNullOrWhiteSpace(jsonOutput) && !string.IsNullOrWhiteSpace(siteRoot))
                        jsonOutput = Path.Combine(siteRoot, "sitemap", "index.json");
                    outputs.AddRange(ResolveOutputCandidates(baseDir, jsonOutput));
                }

                var newsOutput = ResolvePath(baseDir,
                    GetString(step, "newsOutput") ??
                    GetString(step, "newsOut") ??
                    GetString(step, "news-output") ??
                    GetString(step, "news-out"));
                var newsEnabled = !string.IsNullOrWhiteSpace(newsOutput) ||
                                  (GetArrayOfStrings(step, "newsPaths")?.Length ?? 0) > 0 ||
                                  (GetArrayOfStrings(step, "news-paths")?.Length ?? 0) > 0 ||
                                  GetString(step, "newsMetadata") is not null ||
                                  GetString(step, "news-metadata") is not null;
                if (newsEnabled)
                {
                    if (string.IsNullOrWhiteSpace(newsOutput) && !string.IsNullOrWhiteSpace(siteRoot))
                        newsOutput = Path.Combine(siteRoot, "sitemap-news.xml");
                    outputs.AddRange(ResolveOutputCandidates(baseDir, newsOutput));
                }

                var imageOutput = ResolvePath(baseDir,
                    GetString(step, "imageOutput") ??
                    GetString(step, "imageOut") ??
                    GetString(step, "image-output") ??
                    GetString(step, "image-out"));
                var imageEnabled = !string.IsNullOrWhiteSpace(imageOutput) ||
                                   (GetArrayOfStrings(step, "imagePaths")?.Length ?? 0) > 0 ||
                                   (GetArrayOfStrings(step, "image-paths")?.Length ?? 0) > 0;
                if (imageEnabled)
                {
                    if (string.IsNullOrWhiteSpace(imageOutput) && !string.IsNullOrWhiteSpace(siteRoot))
                        imageOutput = Path.Combine(siteRoot, "sitemap-images.xml");
                    outputs.AddRange(ResolveOutputCandidates(baseDir, imageOutput));
                }

                var videoOutput = ResolvePath(baseDir,
                    GetString(step, "videoOutput") ??
                    GetString(step, "videoOut") ??
                    GetString(step, "video-output") ??
                    GetString(step, "video-out"));
                var videoEnabled = !string.IsNullOrWhiteSpace(videoOutput) ||
                                   (GetArrayOfStrings(step, "videoPaths")?.Length ?? 0) > 0 ||
                                   (GetArrayOfStrings(step, "video-paths")?.Length ?? 0) > 0;
                if (videoEnabled)
                {
                    if (string.IsNullOrWhiteSpace(videoOutput) && !string.IsNullOrWhiteSpace(siteRoot))
                        videoOutput = Path.Combine(siteRoot, "sitemap-videos.xml");
                    outputs.AddRange(ResolveOutputCandidates(baseDir, videoOutput));
                }

                var indexOutput = ResolvePath(baseDir,
                    GetString(step, "sitemapIndex") ??
                    GetString(step, "sitemap-index") ??
                    GetString(step, "indexOut") ??
                    GetString(step, "index-out"));
                if (!string.IsNullOrWhiteSpace(indexOutput))
                    outputs.AddRange(ResolveOutputCandidates(baseDir, indexOutput));

                return outputs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            case "optimize":
            {
                var outputs = new List<string>();
                var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
                var reportPath = GetString(step, "reportPath") ?? GetString(step, "report-path");
                var hashManifest = GetString(step, "hashManifest") ?? GetString(step, "hash-manifest");
                var cacheHeaders = GetBool(step, "cacheHeaders") ?? GetBool(step, "headers") ?? false;
                var cacheHeadersOut = GetString(step, "cacheHeadersOut") ?? GetString(step, "headersOut") ?? GetString(step, "headers-out");

                if (!string.IsNullOrWhiteSpace(siteRoot))
                {
                    if (!string.IsNullOrWhiteSpace(reportPath))
                        outputs.AddRange(ResolveOutputCandidates(siteRoot, reportPath));
                    if (!string.IsNullOrWhiteSpace(hashManifest))
                        outputs.AddRange(ResolveOutputCandidates(siteRoot, hashManifest));
                    if (cacheHeaders)
                    {
                        var headersPath = string.IsNullOrWhiteSpace(cacheHeadersOut) ? "_headers" : cacheHeadersOut;
                        outputs.AddRange(ResolveOutputCandidates(siteRoot, headersPath));
                    }
                }

                return outputs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            case "audit":
            {
                var outputs = new List<string>();
                var summaryEnabled = GetBool(step, "summary") ?? false;
                var summaryPath = GetString(step, "summaryPath");
                var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
                if (summaryEnabled || !string.IsNullOrWhiteSpace(summaryPath))
                {
                    if (string.IsNullOrWhiteSpace(summaryPath))
                        summaryPath = "audit-summary.json";
                    if (!string.IsNullOrWhiteSpace(siteRoot) && !Path.IsPathRooted(summaryPath))
                        summaryPath = Path.Combine(siteRoot, summaryPath);
                    outputs.AddRange(ResolveOutputCandidates(baseDir, summaryPath));
                }

                var sarifEnabled = GetBool(step, "sarif") ?? false;
                var sarifPath = GetString(step, "sarifPath") ?? GetString(step, "sarif-path");
                if (sarifEnabled || !string.IsNullOrWhiteSpace(sarifPath))
                {
                    if (string.IsNullOrWhiteSpace(sarifPath))
                        sarifPath = "audit.sarif.json";
                    if (!string.IsNullOrWhiteSpace(siteRoot) && !Path.IsPathRooted(sarifPath))
                        sarifPath = Path.Combine(siteRoot, sarifPath);
                    outputs.AddRange(ResolveOutputCandidates(baseDir, sarifPath));
                }

                return outputs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            case "seo-doctor":
            {
                var outputs = new List<string>();
                var reportPath = GetString(step, "reportPath") ?? GetString(step, "report-path");
                if (!string.IsNullOrWhiteSpace(reportPath))
                    outputs.AddRange(ResolveOutputCandidates(baseDir, reportPath));

                var summaryPath = GetString(step, "summaryPath") ?? GetString(step, "summary-path");
                if (!string.IsNullOrWhiteSpace(summaryPath))
                    outputs.AddRange(ResolveOutputCandidates(baseDir, summaryPath));

                var baselineGenerate = GetBool(step, "baselineGenerate") ?? false;
                var baselineUpdate = GetBool(step, "baselineUpdate") ?? false;
                if (baselineGenerate || baselineUpdate)
                {
                    var baselinePath = GetString(step, "baselinePath") ?? GetString(step, "baseline");
                    if (string.IsNullOrWhiteSpace(baselinePath))
                        baselinePath = ".powerforge/seo-baseline.json";
                    outputs.AddRange(ResolveOutputCandidates(baseDir, baselinePath));
                }

                return outputs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            case "indexnow":
            {
                var outputs = new List<string>();
                var reportPath = GetString(step, "reportPath") ?? GetString(step, "report-path");
                if (!string.IsNullOrWhiteSpace(reportPath))
                    outputs.AddRange(ResolveOutputCandidates(baseDir, reportPath));

                var summaryPath = GetString(step, "summaryPath") ?? GetString(step, "summary-path");
                if (!string.IsNullOrWhiteSpace(summaryPath))
                    outputs.AddRange(ResolveOutputCandidates(baseDir, summaryPath));

                return outputs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            case "doctor":
            {
                var outputs = new List<string>();
                var configPath = ResolvePath(baseDir, GetString(step, "config"));
                var outPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output"));
                var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
                var runBuild = GetBool(step, "build");
                var noBuild = GetBool(step, "noBuild") ?? false;
                var executeBuild = runBuild ?? !noBuild;
                if (string.IsNullOrWhiteSpace(outPath) && !string.IsNullOrWhiteSpace(configPath))
                    outPath = Path.Combine(Path.GetDirectoryName(configPath) ?? ".", "_site");
                var effectiveSiteRoot = string.IsNullOrWhiteSpace(siteRoot) ? outPath : siteRoot;

                if (executeBuild && !string.IsNullOrWhiteSpace(outPath))
                    outputs.AddRange(ResolveOutputCandidates(baseDir, outPath));

                var runAudit = GetBool(step, "audit");
                var noAudit = GetBool(step, "noAudit") ?? false;
                var executeAudit = runAudit ?? !noAudit;
                if (executeAudit && !string.IsNullOrWhiteSpace(effectiveSiteRoot))
                {
                    var summaryEnabled = GetBool(step, "summary") ?? false;
                    var summaryPath = GetString(step, "summaryPath");
                    if (summaryEnabled || !string.IsNullOrWhiteSpace(summaryPath))
                    {
                        if (string.IsNullOrWhiteSpace(summaryPath))
                            summaryPath = "audit-summary.json";
                        if (!Path.IsPathRooted(summaryPath))
                            summaryPath = Path.Combine(effectiveSiteRoot, summaryPath);
                        outputs.AddRange(ResolveOutputCandidates(baseDir, summaryPath));
                    }

                    var sarifEnabled = GetBool(step, "sarif") ?? false;
                    var sarifPath = GetString(step, "sarifPath") ?? GetString(step, "sarif-path");
                    if (sarifEnabled || !string.IsNullOrWhiteSpace(sarifPath))
                    {
                        if (string.IsNullOrWhiteSpace(sarifPath))
                            sarifPath = "audit.sarif.json";
                        if (!Path.IsPathRooted(sarifPath))
                            sarifPath = Path.Combine(effectiveSiteRoot, sarifPath);
                        outputs.AddRange(ResolveOutputCandidates(baseDir, sarifPath));
                    }
                }

                return outputs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            default:
                return Array.Empty<string>();
        }
    }
}
