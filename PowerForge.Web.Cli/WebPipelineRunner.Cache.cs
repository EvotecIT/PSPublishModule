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
                if (item.ValueKind != JsonValueKind.String)
                    continue;
                var value = item.GetString();
                if (string.IsNullOrWhiteSpace(value) || IsExternalUri(value))
                    continue;
                var resolved = ResolvePath(baseDir, value);
                if (!string.IsNullOrWhiteSpace(resolved))
                    yield return Path.GetFullPath(resolved);
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
                return ResolveOutputCandidates(baseDir, GetString(step, "out") ?? GetString(step, "output"));
            case "dotnet-publish":
                return ResolveOutputCandidates(baseDir, GetString(step, "out") ?? GetString(step, "output"));
            case "overlay":
                return ResolveOutputCandidates(baseDir, GetString(step, "destination") ?? GetString(step, "dest"));
            case "changelog":
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
                return ResolveOutputCandidates(baseDir, outPath);
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
