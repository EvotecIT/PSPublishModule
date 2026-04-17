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
        if (task.Equals("engine-lock", StringComparison.OrdinalIgnoreCase))
            return false;
        if (task.Equals("enginelock", StringComparison.OrdinalIgnoreCase))
            return false;
        if (task.Equals("github-artifacts-prune", StringComparison.OrdinalIgnoreCase))
            return false;
        if (task.Equals("github-artifacts", StringComparison.OrdinalIgnoreCase))
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
        if (task.Equals("wordpress-media-sync", StringComparison.OrdinalIgnoreCase))
            return false;
        if (task.Equals("wordpress-sync-media", StringComparison.OrdinalIgnoreCase))
            return false;
        if (task.Equals("sync-wordpress-media", StringComparison.OrdinalIgnoreCase))
            return false;
        if (task.Equals("wordpress-import-snapshot", StringComparison.OrdinalIgnoreCase))
            return false;
        if (task.Equals("wordpress-snapshot-import", StringComparison.OrdinalIgnoreCase))
            return false;
        if (task.Equals("import-wordpress-snapshot", StringComparison.OrdinalIgnoreCase))
            return false;
        if (task.Equals("wordpress-export-snapshot", StringComparison.OrdinalIgnoreCase))
            return false;
        if (task.Equals("wordpress-snapshot-export", StringComparison.OrdinalIgnoreCase))
            return false;
        if (task.Equals("export-wordpress-snapshot", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static bool IsCacheableStep(string task, JsonElement step)
    {
        if (!IsCacheableTask(task))
            return false;

        if (task.Equals("agent-ready", StringComparison.OrdinalIgnoreCase) ||
            task.Equals("agentready", StringComparison.OrdinalIgnoreCase))
        {
            var operation = (GetString(step, "operation") ?? "prepare").Trim();
            return operation.Equals("prepare", StringComparison.OrdinalIgnoreCase);
        }

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

    private static string[] GetExpectedStepOutputs(string task, JsonElement step, string baseDir, string lastBuildOutPath)
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
            case "project-apidocs":
            case "project-apidoc":
            case "project-api-docs":
            {
                var outputs = new List<string>();
                var siteRoot = ResolvePath(baseDir,
                    GetString(step, "siteRoot") ??
                    GetString(step, "site-root") ??
                    GetString(step, "siteOut") ??
                    GetString(step, "site-out"));
                var outRoot = ResolvePath(baseDir,
                    GetString(step, "outRoot") ??
                    GetString(step, "out-root") ??
                    GetString(step, "projectsOut") ??
                    GetString(step, "projects-out"));
                if (!string.IsNullOrWhiteSpace(outRoot))
                {
                    outputs.AddRange(ResolveOutputCandidates(baseDir, outRoot));
                }
                else if (!string.IsNullOrWhiteSpace(siteRoot))
                {
                    outputs.AddRange(ResolveOutputCandidates(baseDir, Path.Combine(siteRoot, "projects")));
                }

                outputs.AddRange(ResolveOutputCandidates(baseDir,
                    GetString(step, "summaryPath") ??
                    GetString(step, "summary-path") ??
                    "./Build/project-apidocs-last-run.json"));

                return outputs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            case "dotnet-publish":
                return ResolveOutputCandidates(baseDir, GetString(step, "out") ?? GetString(step, "output"));
            case "overlay":
                return ResolveOutputCandidates(baseDir, GetString(step, "destination") ?? GetString(step, "dest"));
            case "route-fallbacks":
            case "routefallbacks":
            case "templated-routes":
                return GetExpectedRouteFallbackOutputs(baseDir, step);
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
            case "release-hub":
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
            case "agent-ready":
            case "agentready":
                return GetExpectedAgentReadyPrepareOutputs(step, baseDir, lastBuildOutPath);
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
            case "engine-lock":
            case "enginelock":
            {
                var outputs = new List<string>();
                var operation = (GetString(step, "operation") ??
                                 GetString(step, "action") ??
                                 GetString(step, "lockMode") ??
                                 GetString(step, "lock-mode") ??
                                 "verify").Trim();

                if (operation.Equals("update", StringComparison.OrdinalIgnoreCase))
                {
                    var lockPath = GetString(step, "path") ??
                                   GetString(step, "lockPath") ??
                                   GetString(step, "lock-path") ??
                                   GetString(step, "lock") ??
                                   Path.Combine(".powerforge", "engine-lock.json");
                    if (!string.IsNullOrWhiteSpace(lockPath))
                        outputs.AddRange(ResolveOutputCandidates(baseDir, lockPath));
                }

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
            case "github-artifacts-prune":
            case "github-artifacts":
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
            case "ecosystem-stats":
            case "ecosystemstats":
            {
                var outputs = new List<string>();
                outputs.AddRange(ResolveOutputCandidates(baseDir,
                    GetString(step, "out") ??
                    GetString(step, "output") ??
                    GetString(step, "outputPath") ??
                    GetString(step, "output-path") ??
                    "./data/ecosystem/stats.json"));
                outputs.AddRange(ResolveOutputCandidates(baseDir,
                    GetString(step, "publishPath") ??
                    GetString(step, "publish-path")));
                outputs.AddRange(ResolveOutputCandidates(baseDir,
                    GetString(step, "summaryPath") ??
                    GetString(step, "summary-path")));

                return outputs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            case "search-index-export":
            case "search-index":
            case "search-export":
            {
                var outputs = new List<string>();
                outputs.AddRange(ResolveOutputCandidates(baseDir,
                    GetString(step, "out") ??
                    GetString(step, "output") ??
                    GetString(step, "outputPath") ??
                    GetString(step, "output-path") ??
                    "./_site/search-index.json"));
                outputs.AddRange(ResolveOutputCandidates(baseDir,
                    GetString(step, "summaryPath") ??
                    GetString(step, "summary-path") ??
                    "./Build/generate-search-index-last-run.json"));

                return outputs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            case "project-docs-sync":
            case "sync-project-docs":
            case "project-docs":
            {
                var outputs = new List<string>();
                outputs.AddRange(ResolveOutputCandidates(baseDir,
                    GetString(step, "contentRoot") ??
                    GetString(step, "content-root") ??
                    "./content/docs"));
                var syncApi = GetBool(step, "syncApi") ?? GetBool(step, "sync-api") ?? false;
                if (syncApi)
                {
                    outputs.AddRange(ResolveOutputCandidates(baseDir,
                        GetString(step, "apiRoot") ??
                        GetString(step, "api-root") ??
                        "./data/apidocs"));
                }
                var syncExamples = GetBool(step, "syncExamples") ?? GetBool(step, "sync-examples") ?? true;
                if (syncExamples)
                {
                    outputs.AddRange(ResolveOutputCandidates(baseDir,
                        GetString(step, "examplesRoot") ??
                        GetString(step, "examples-root") ??
                        "./content/examples"));
                }
                outputs.AddRange(ResolveOutputCandidates(baseDir,
                    GetString(step, "summaryPath") ??
                    GetString(step, "summary-path") ??
                    "./Build/sync-project-docs-last-run.json"));

                return outputs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            case "project-catalog":
            case "projectcatalog":
            {
                var outputs = new List<string>();
                outputs.AddRange(ResolveOutputCandidates(baseDir,
                    GetString(step, "catalog") ??
                    GetString(step, "catalogPath") ??
                    GetString(step, "catalog-path") ??
                    "./data/projects/catalog.json"));
                outputs.AddRange(ResolveOutputCandidates(baseDir,
                    GetString(step, "publishPath") ??
                    GetString(step, "publish-path")));
                outputs.AddRange(ResolveOutputCandidates(baseDir,
                    GetString(step, "summaryPath") ??
                    GetString(step, "summary-path")));

                var generatePages = GetBool(step, "generatePages") ?? GetBool(step, "generate-pages") ?? true;
                var generateSections = GetBool(step, "generateSections") ?? GetBool(step, "generate-sections") ?? true;
                if (generatePages || generateSections)
                {
                    outputs.AddRange(ResolveOutputCandidates(baseDir,
                        GetString(step, "contentRoot") ??
                        GetString(step, "content-root") ??
                        "./content/projects"));
                }

                return outputs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            case "apache-redirects":
            case "apache-redirect":
            {
                var outputs = new List<string>();
                outputs.AddRange(ResolveOutputCandidates(baseDir,
                    GetString(step, "out") ??
                    GetString(step, "output") ??
                    GetString(step, "outputPath") ??
                    GetString(step, "output-path") ??
                    GetString(step, "destination") ??
                    GetString(step, "dest") ??
                    "./deploy/apache/wordpress-redirects.conf"));
                outputs.AddRange(ResolveOutputCandidates(baseDir,
                    GetString(step, "summaryPath") ??
                    GetString(step, "summary-path")));

                return outputs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            case "wordpress-normalize":
            case "wordpress-normalize-content":
            case "normalize-wordpress-content":
            {
                var outputs = new List<string>();
                outputs.AddRange(ResolveOutputCandidates(baseDir,
                    GetString(step, "summaryPath") ??
                    GetString(step, "summary-path") ??
                    "./Build/normalize-wordpress-last-run.json"));

                var targets = GetArrayOfStrings(step, "targets") ??
                              GetArrayOfStrings(step, "paths");
                if (targets is { Length: > 0 })
                {
                    foreach (var target in targets.Where(static value => !string.IsNullOrWhiteSpace(value)))
                        outputs.AddRange(ResolveOutputCandidates(baseDir, target));
                }
                else
                {
                    outputs.AddRange(ResolveOutputCandidates(baseDir, "./content/blog"));
                    outputs.AddRange(ResolveOutputCandidates(baseDir, "./content/pages"));
                }

                return outputs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            case "wordpress-media-sync":
            case "wordpress-sync-media":
            case "sync-wordpress-media":
            {
                var outputs = new List<string>();
                outputs.AddRange(ResolveOutputCandidates(baseDir,
                    GetString(step, "summaryPath") ??
                    GetString(step, "summary-path") ??
                    "./Build/sync-wordpress-media-last-run.json"));

                var targets = GetArrayOfStrings(step, "targets") ??
                              GetArrayOfStrings(step, "paths");
                if (targets is { Length: > 0 })
                {
                    foreach (var target in targets.Where(static value => !string.IsNullOrWhiteSpace(value)))
                        outputs.AddRange(ResolveOutputCandidates(baseDir, target));
                }
                else
                {
                    outputs.AddRange(ResolveOutputCandidates(baseDir, "./content/blog"));
                    outputs.AddRange(ResolveOutputCandidates(baseDir, "./content/pages"));
                }

                outputs.AddRange(ResolveOutputCandidates(baseDir, "./static/wp-content/uploads"));

                return outputs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            case "wordpress-import-snapshot":
            case "wordpress-snapshot-import":
            case "import-wordpress-snapshot":
            {
                var outputs = new List<string>();
                outputs.AddRange(ResolveOutputCandidates(baseDir,
                    GetString(step, "summaryPath") ??
                    GetString(step, "summary-path") ??
                    "./Build/import-wordpress-last-run.json"));
                outputs.AddRange(ResolveOutputCandidates(baseDir,
                    GetString(step, "redirectCsvPath") ??
                    GetString(step, "redirect-csv-path") ??
                    "./data/redirects/legacy-wordpress-generated.csv"));
                outputs.AddRange(ResolveOutputCandidates(baseDir, "./content/blog"));
                outputs.AddRange(ResolveOutputCandidates(baseDir, "./content/pages"));

                return outputs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            case "wordpress-export-snapshot":
            case "wordpress-snapshot-export":
            case "export-wordpress-snapshot":
            {
                var outputs = new List<string>();
                outputs.AddRange(ResolveOutputCandidates(baseDir,
                    GetString(step, "out") ??
                    GetString(step, "output") ??
                    GetString(step, "outputPath") ??
                    GetString(step, "output-path") ??
                    GetString(step, "destination") ??
                    GetString(step, "dest") ??
                    GetString(step, "path")));
                outputs.AddRange(ResolveOutputCandidates(baseDir,
                    GetString(step, "manifestPath") ??
                    GetString(step, "manifest-path")));
                outputs.AddRange(ResolveOutputCandidates(baseDir,
                    GetString(step, "summaryPath") ??
                    GetString(step, "summary-path")));

                return outputs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            default:
                return Array.Empty<string>();
        }
    }

    private static string[] GetExpectedAgentReadyPrepareOutputs(JsonElement step, string baseDir, string lastBuildOutPath)
    {
        var operation = (GetString(step, "operation") ?? "prepare").Trim();
        if (!operation.Equals("prepare", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<string>();

        var siteRoot = ResolvePath(baseDir,
            GetString(step, "siteRoot") ??
            GetString(step, "site-root") ??
            GetString(step, "out") ??
            GetString(step, "output"));
        if (string.IsNullOrWhiteSpace(siteRoot) && !string.IsNullOrWhiteSpace(lastBuildOutPath))
            siteRoot = lastBuildOutPath;
        if (string.IsNullOrWhiteSpace(siteRoot))
            return Array.Empty<string>();

        var spec = ResolveAgentReadinessSpecForCache(step, baseDir);
        if (!spec.Enabled)
            return Array.Empty<string>();

        var outputs = new List<string>();
        if (spec.Robots)
            outputs.Add(ResolveAgentReadySiteOutputPath(siteRoot, "robots.txt"));

        if (spec.ApiCatalog?.Enabled == true && AgentReadyApiCatalogWillWrite(siteRoot, spec.ApiCatalog))
            outputs.Add(ResolveAgentReadySiteOutputPath(siteRoot, string.IsNullOrWhiteSpace(spec.ApiCatalog.OutputPath) ? ".well-known/api-catalog" : spec.ApiCatalog.OutputPath!));

        if (spec.AgentSkills?.Enabled == true)
            outputs.Add(ResolveAgentReadySiteOutputPath(siteRoot, string.IsNullOrWhiteSpace(spec.AgentSkills.IndexPath) ? ".well-known/agent-skills/index.json" : spec.AgentSkills.IndexPath!));

        if (spec.AgentsJson?.Enabled == true)
        {
            var agentsPaths = new[]
            {
                string.IsNullOrWhiteSpace(spec.AgentsJson.OutputPath) ? "agents.json" : spec.AgentsJson.OutputPath!,
                string.IsNullOrWhiteSpace(spec.AgentsJson.WellKnownOutputPath) ? ".well-known/agents.json" : spec.AgentsJson.WellKnownOutputPath!
            };

            foreach (var path in agentsPaths.Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
                outputs.Add(ResolveAgentReadySiteOutputPath(siteRoot, path));
        }

        if (spec.A2AAgentCard?.Enabled == true)
            outputs.Add(ResolveAgentReadySiteOutputPath(siteRoot, string.IsNullOrWhiteSpace(spec.A2AAgentCard.OutputPath) ? ".well-known/agent-card.json" : spec.A2AAgentCard.OutputPath!));

        if (spec.McpServerCard?.Enabled == true && !string.IsNullOrWhiteSpace(spec.McpServerCard.Endpoint))
            outputs.Add(ResolveAgentReadySiteOutputPath(siteRoot, string.IsNullOrWhiteSpace(spec.McpServerCard.OutputPath) ? ".well-known/mcp/server-card.json" : spec.McpServerCard.OutputPath!));

        if (ShouldExpectAgentReadyHeaders(siteRoot, spec))
            outputs.Add(ResolveAgentReadySiteOutputPath(siteRoot, string.IsNullOrWhiteSpace(spec.HeadersPath) ? "_headers" : spec.HeadersPath!));

        return outputs
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static AgentReadinessSpec ResolveAgentReadinessSpecForCache(JsonElement step, string baseDir)
    {
        var configPath = ResolvePath(baseDir, GetString(step, "config"));
        if (!string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath))
        {
            var loaded = WebSiteSpecLoader.LoadWithPath(configPath, WebCliJson.Options);
            return NormalizeAgentReadinessSpecForCache(loaded.Spec.AgentReadiness);
        }

        return NormalizeAgentReadinessSpecForCache(null);
    }

    private static AgentReadinessSpec NormalizeAgentReadinessSpecForCache(AgentReadinessSpec? spec)
    {
        if (spec is null)
        {
            return new AgentReadinessSpec
            {
                Enabled = true,
                SecurityHeaders = new AgentSecurityHeadersSpec(),
                ContentSignals = new AgentContentSignalsSpec(),
                ApiCatalog = new AgentApiCatalogSpec(),
                AgentSkills = new AgentSkillsDiscoverySpec(),
                AgentsJson = new AgentDiscoveryDocumentSpec()
            };
        }

        var resolved = new AgentReadinessSpec
        {
            Enabled = spec.Enabled,
            HeadersPath = spec.HeadersPath,
            LinkHeaders = spec.LinkHeaders,
            SecurityHeaders = spec.SecurityHeaders,
            Robots = spec.Robots,
            ContentSignals = spec.ContentSignals,
            BotRules = spec.BotRules,
            ApiCatalog = spec.ApiCatalog,
            AgentSkills = spec.AgentSkills,
            AgentsJson = spec.AgentsJson,
            A2AAgentCard = spec.A2AAgentCard,
            McpServerCard = spec.McpServerCard,
            OpenApi = spec.OpenApi,
            WebMcp = spec.WebMcp,
            MarkdownNegotiation = spec.MarkdownNegotiation
        };

        if (resolved.Enabled)
        {
            resolved.SecurityHeaders ??= new AgentSecurityHeadersSpec();
            resolved.ContentSignals ??= new AgentContentSignalsSpec();
            resolved.ApiCatalog ??= new AgentApiCatalogSpec();
            resolved.AgentSkills ??= new AgentSkillsDiscoverySpec();
            resolved.AgentsJson ??= new AgentDiscoveryDocumentSpec();
        }

        return resolved;
    }

    private static bool AgentReadyApiCatalogWillWrite(string siteRoot, AgentApiCatalogSpec spec)
    {
        if (spec.Entries?.Any(static entry => !string.IsNullOrWhiteSpace(entry.Anchor)) == true)
            return true;

        return File.Exists(Path.Combine(siteRoot, "api", "index.json"));
    }

    private static bool ShouldExpectAgentReadyHeaders(string siteRoot, AgentReadinessSpec spec)
        => spec.SecurityHeaders?.Enabled == true ||
           (spec.LinkHeaders && File.Exists(Path.Combine(siteRoot, "llms.txt"))) ||
           (spec.LinkHeaders && AgentReadyOpenApiRouteExists(siteRoot, spec.OpenApi)) ||
           spec.ApiCatalog?.Enabled == true ||
           spec.AgentSkills?.Enabled == true ||
           spec.AgentsJson?.Enabled == true ||
           spec.A2AAgentCard?.Enabled == true ||
           spec.McpServerCard?.Enabled == true;

    private static bool AgentReadyOpenApiRouteExists(string siteRoot, AgentOpenApiSpec? spec)
    {
        if (spec?.Enabled != true)
            return false;

        if (!string.IsNullOrWhiteSpace(spec.Path) &&
            !Uri.TryCreate(spec.Path, UriKind.Absolute, out _))
        {
            return File.Exists(ResolveAgentReadySiteOutputPath(siteRoot, spec.Path!));
        }

        foreach (var candidate in new[] { "/openapi.json", "/api/openapi.json", "/swagger.json", "/api/swagger.json", "/.well-known/openapi.json" })
        {
            if (File.Exists(ResolveAgentReadySiteOutputPath(siteRoot, candidate)))
                return true;
        }

        return false;
    }

    private static string ResolveAgentReadySiteOutputPath(string siteRoot, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));

        var root = Path.GetFullPath(siteRoot.Trim().Trim('"'));
        var normalized = path.Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        var resolved = Path.GetFullPath(Path.Combine(root, normalized));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!resolved.Equals(root, FileSystemPathComparison) &&
            !resolved.StartsWith(rootWithSeparator, FileSystemPathComparison))
        {
            throw new ArgumentException($"Path '{path}' resolves outside the site root.", nameof(path));
        }

        return resolved;
    }

    private static string[] GetExpectedRouteFallbackOutputs(string baseDir, JsonElement step)
    {
        var outputs = new List<string>();
        var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
        if (!string.IsNullOrWhiteSpace(siteRoot))
            outputs.AddRange(ResolveOutputCandidates(baseDir, siteRoot));
        outputs.AddRange(ResolveOutputCandidates(baseDir, GetString(step, "reportPath") ?? GetString(step, "report-path")));

        if (string.IsNullOrWhiteSpace(siteRoot))
            return outputs
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var normalizedSiteRoot = NormalizeRootPath(siteRoot);
        var rootOutput = GetString(step, "rootOutput") ??
                         GetString(step, "root-output") ??
                         GetString(step, "indexOutput") ??
                         GetString(step, "index-output");
        if (!string.IsNullOrWhiteSpace(rootOutput))
        {
            var rootRelativePath = NormalizeRouteFallbackRelativePath(rootOutput);
            outputs.Add(GetRouteFallbackOutputFullPath(siteRoot, normalizedSiteRoot, rootRelativePath));
        }

        var destinationTemplate = GetString(step, "destinationTemplate") ??
                                  GetString(step, "destination-template") ??
                                  GetString(step, "pathTemplate") ??
                                  GetString(step, "path-template");
        var itemsPath = ResolvePath(baseDir,
            GetString(step, "items") ??
            GetString(step, "itemsPath") ??
            GetString(step, "items-path") ??
            GetString(step, "manifest") ??
            GetString(step, "data"));
        var itemsProperty = GetString(step, "itemsProperty") ??
                            GetString(step, "items-property") ??
                            GetString(step, "itemsKey") ??
                            GetString(step, "items-key");

        if (!string.IsNullOrWhiteSpace(destinationTemplate) &&
            !string.IsNullOrWhiteSpace(itemsPath) &&
            File.Exists(itemsPath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(itemsPath));
                var items = ResolveRouteFallbackItems(document.RootElement, itemsProperty);
                foreach (var item in items)
                {
                    var values = BuildRouteFallbackValueMap(item);
                    var relativePath = NormalizeRouteFallbackRelativePath(
                        ExpandRouteFallbackTemplate(destinationTemplate, values, "destinationTemplate"));
                    outputs.Add(GetRouteFallbackOutputFullPath(siteRoot, normalizedSiteRoot, relativePath));
                }
            }
            catch
            {
                // Fall back to the coarse outputs so the task itself can report the real validation error.
            }
        }

        return outputs
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetRouteFallbackOutputFullPath(string siteRoot, string normalizedSiteRoot, string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(siteRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(normalizedSiteRoot, FileSystemPathComparison))
            throw new InvalidOperationException($"route-fallbacks output path must stay under siteRoot: {relativePath}");
        return fullPath;
    }
}
