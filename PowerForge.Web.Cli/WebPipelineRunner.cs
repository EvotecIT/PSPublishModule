using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private const long MaxStateFileSizeBytes = 10 * 1024 * 1024;
    private const int MaxStampFileCount = 1000;
    private static readonly TimeSpan DefaultWatchDebounce = TimeSpan.FromMilliseconds(250);
    private static readonly StringComparison FileSystemPathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private static readonly string PipelineToolFingerprint = BuildPipelineToolFingerprint();
    private static readonly HashSet<string> FingerprintPathKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "config", "siteRoot", "site-root", "project", "solution", "path",
        "repos", "repositories",
        "repoBaseUrl", "repo-base-url", "repositoryBaseUrl", "repository-base-url", "repoHost", "repo-host",
        "lockPath", "lock-path", "lock", "lockMode", "lock-mode",
        "retry", "retries", "retryCount", "retry-count", "retryDelayMs", "retry-delay-ms", "retryDelay", "retry-delay",
        "contextPath", "context-path", "context", "stdoutPath", "stdout-path", "stdout", "stderrPath", "stderr-path", "stderr",
        "projects", "projectFiles", "project-files",
        "module", "modules", "moduleFiles", "module-files",
        "out", "output", "source", "destination", "dest",
        "map", "maps", "input", "inputs", "sources", "mapFiles", "map-files",
        "xml", "help", "helpPath", "assembly",
        "changelog", "changelogPath",
        "discoverRoot", "discover-root",
        "apiIndex", "apiSitemap", "criticalCss", "hashManifest", "reportPath", "report-path",
        "summaryPath", "sarifPath", "baselinePath", "navCanonicalPath", "navProfiles",
        "summary-path", "sarif-path", "baseline-path", "nav-canonical-path", "nav-profiles",
        "templateRoot", "templateIndex", "templateType",
        "templateDocsIndex", "templateDocsType",
        "docsScript", "searchScript",
        "coverageReport", "coverage-report", "coverageReportPath", "coverage-report-path",
        "xrefMap", "xref-map", "xrefMapPath", "xref-map-path",
        "psExamplesPath", "ps-examples-path", "powerShellExamplesPath", "powershell-examples-path",
        "csproj", "csprojFiles", "csproj-files", "psd1", "psd1Files", "psd1-files",
        "markdownOut", "markdown-out", "markdownOutput", "markdown-output",
        "headerHtml", "footerHtml", "quickstart", "extra",
        "htmlOutput", "htmlTemplate",
        "entriesJson", "entriesFile", "entries-json", "entries-file",
        "jsonOutput", "jsonOut", "json-output", "json-out",
        "cachePath", "profilePath"
    };

    private sealed class WebPipelineCacheState
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, WebPipelineCacheEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class WebPipelineCacheEntry
    {
        public string Fingerprint { get; set; } = string.Empty;
        public string? Message { get; set; }
    }

    private sealed class PipelineStepDefinition
    {
        public int Index { get; set; }
        public string Task { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public string[] DependsOn { get; set; } = Array.Empty<string>();
        public int[] DependencyIndexes { get; set; } = Array.Empty<int>();
        public JsonElement Element { get; set; }
    }

    internal static WebPipelineResult RunPipeline(
        string pipelinePath,
        WebConsoleLogger? logger,
        bool forceProfile = false,
        bool fast = false,
        string? mode = null,
        string[]? onlyTasks = null,
        string[]? skipTasks = null)
    {
        using var doc = LoadPipelineDocumentWithExtends(pipelinePath);

        var root = doc.RootElement;
        if (!root.TryGetProperty("steps", out var stepsElement) || stepsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Pipeline config must include a steps array.");

        var normalizedPipelinePath = Path.GetFullPath(pipelinePath.Trim().Trim('"'));
        var baseDir = Path.GetDirectoryName(normalizedPipelinePath) ?? ".";
        var normalizedMode = string.IsNullOrWhiteSpace(mode) ? null : mode.Trim();
        var effectiveMode = string.IsNullOrWhiteSpace(normalizedMode) ? "default" : normalizedMode;
        var onlyTaskSet = onlyTasks is { Length: > 0 }
            ? new HashSet<string>(onlyTasks.Where(static t => !string.IsNullOrWhiteSpace(t)).Select(static t => t.Trim()), StringComparer.OrdinalIgnoreCase)
            : null;
        var skipTaskSet = skipTasks is { Length: > 0 }
            ? new HashSet<string>(skipTasks.Where(static t => !string.IsNullOrWhiteSpace(t)).Select(static t => t.Trim()), StringComparer.OrdinalIgnoreCase)
            : null;
        if (!string.IsNullOrWhiteSpace(normalizedMode))
            logger?.Info($"Pipeline mode: {normalizedMode}");
        var profileEnabled = (GetBool(root, "profile") ?? false) || forceProfile;
        var profileWriteOnFail = GetBool(root, "profileOnFail") ?? GetBool(root, "profile-on-fail") ?? true;
        var profilePath = ResolvePathWithinRoot(baseDir, GetString(root, "profilePath") ?? GetString(root, "profile-path"), Path.Combine(".powerforge", "pipeline-profile.json"));
        var cacheEnabled = GetBool(root, "cache") ?? false;
        var cachePath = ResolvePathWithinRoot(baseDir, GetString(root, "cachePath") ?? GetString(root, "cache-path"), Path.Combine(".powerforge", "pipeline-cache.json"));
        var cacheState = cacheEnabled ? LoadPipelineCache(cachePath, logger) : null;
        var cacheUpdated = false;
        var runStopwatch = Stopwatch.StartNew();

        var result = new WebPipelineResult
        {
            CachePath = cacheEnabled ? cachePath : null
        };
        var steps = BuildStepDefinitions(stepsElement);
        var totalSteps = steps.Count;
        var stepResultsByIndex = new Dictionary<int, WebPipelineStepResult>();

        // Allows fast mode to scope optimize/audit to just the pages touched by the last build step.
        var lastBuildOutPath = string.Empty;
        var lastBuildUpdatedFiles = Array.Empty<string>();

        foreach (var definition in steps)
        {
            var step = definition.Element;
            var task = definition.Task;
            var stepIndex = definition.Index;
            var label = $"[{stepIndex}/{totalSteps}] {task}";
            var stepResult = new WebPipelineStepResult { Task = task };

            var skipReason = GetSkipReason(task, step, effectiveMode, onlyTaskSet, skipTaskSet);
            if (skipReason is not null)
            {
                stepResult.Success = true;
                stepResult.Cached = false;
                stepResult.DurationMs = 0;
                stepResult.Message = $"skipped ({skipReason})";
                result.Steps.Add(stepResult);
                stepResultsByIndex[stepIndex] = stepResult;
                if (profileEnabled)
                    logger?.Info($"Finished {label} (skipped) in 0 ms");
                continue;
            }

            logger?.Info($"Starting {label}...");
            var stopwatch = Stopwatch.StartNew();
            var cacheKey = $"{stepIndex}:{task}";
            var stepFingerprint = string.Empty;
            var expectedOutputs = GetExpectedStepOutputs(task, step, baseDir);
            if (definition.DependencyIndexes.Length > 0)
            {
                foreach (var dependencyIndex in definition.DependencyIndexes)
                {
                    if (!stepResultsByIndex.TryGetValue(dependencyIndex, out var dependencyResult) || !dependencyResult.Success)
                    {
                        throw new InvalidOperationException($"Step '{definition.Id}' dependency #{dependencyIndex} failed or was not executed.");
                    }
                }
            }

            var dependencyMiss = definition.DependencyIndexes.Any(index =>
                !stepResultsByIndex.TryGetValue(index, out var dependencyResult) || !dependencyResult.Cached);
            var cacheStateLocal = cacheState;
            var cacheable = cacheEnabled && cacheStateLocal is not null && IsCacheableTask(task);
            if (cacheable)
            {
                var fingerprintSalt = fast ? $"fast|{PipelineToolFingerprint}" : PipelineToolFingerprint;
                stepFingerprint = ComputeStepFingerprint(baseDir, step, fingerprintSalt);
                if (cacheStateLocal!.Entries.TryGetValue(cacheKey, out var cacheEntry) &&
                    string.Equals(cacheEntry.Fingerprint, stepFingerprint, StringComparison.Ordinal) &&
                    !dependencyMiss &&
                    AreExpectedOutputsPresent(expectedOutputs))
                {
                    stepResult.Success = true;
                    stepResult.Cached = true;
                    stepResult.Message = AppendDuration(cacheEntry.Message ?? "cache hit", stopwatch);
                    stepResult.DurationMs = (long)Math.Round(stopwatch.Elapsed.TotalMilliseconds);
                    result.Steps.Add(stepResult);
                    stepResultsByIndex[stepIndex] = stepResult;
                    if (profileEnabled)
                        logger?.Info($"Finished {label} (cache hit) in {FormatDuration(stopwatch.Elapsed)}");
                    continue;
                }
            }

            try
            {
                ExecuteTask(task, step, label, baseDir, fast, effectiveMode, logger, ref lastBuildOutPath, ref lastBuildUpdatedFiles, stepResult);
            }
            catch (Exception ex)
            {
                stepResult.Success = false;
                stepResult.Message = AppendDuration(ex.Message, stopwatch);
                stepResult.DurationMs = (long)Math.Round(stopwatch.Elapsed.TotalMilliseconds);
                result.Steps.Add(stepResult);
                stepResultsByIndex[stepIndex] = stepResult;
                result.StepCount = result.Steps.Count;
                result.Success = false;
                result.DurationMs = (long)Math.Round(runStopwatch.Elapsed.TotalMilliseconds);
                if (cacheEnabled && cacheState is not null && cacheUpdated)
                    SavePipelineCache(cachePath, cacheState, logger);
                // Intentionally asymmetric:
                // - Success: write profile only when profile is enabled (to avoid noise/overhead).
                // - Failure: write profile when profile is enabled OR profileOnFail is true (default),
                //   so CI failures still produce actionable artifacts.
                if (!string.IsNullOrWhiteSpace(profilePath) && (profileEnabled || profileWriteOnFail))
                {
                    WritePipelineProfile(profilePath, result, logger);
                    result.ProfilePath = profilePath;
                }
                return result;
            }

            stepResult.Message = AppendDuration(stepResult.Message, stopwatch);
            stepResult.DurationMs = (long)Math.Round(stopwatch.Elapsed.TotalMilliseconds);
            if (cacheable && !string.IsNullOrWhiteSpace(stepFingerprint))
            {
                cacheStateLocal!.Entries[cacheKey] = new WebPipelineCacheEntry
                {
                    Fingerprint = stepFingerprint,
                    Message = stepResult.Message
                };
                cacheUpdated = true;
            }
            result.Steps.Add(stepResult);
            stepResultsByIndex[stepIndex] = stepResult;
            if (profileEnabled)
                logger?.Info($"Finished {label} in {FormatDuration(stopwatch.Elapsed)}");
        }

        runStopwatch.Stop();
        result.StepCount = result.Steps.Count;
        result.Success = result.Steps.All(s => s.Success);
        result.DurationMs = (long)Math.Round(runStopwatch.Elapsed.TotalMilliseconds);
        if (cacheEnabled && cacheState is not null && cacheUpdated)
            SavePipelineCache(cachePath, cacheState, logger);
        if (!string.IsNullOrWhiteSpace(profilePath) && profileEnabled)
        {
            WritePipelineProfile(profilePath, result, logger);
            result.ProfilePath = profilePath;
        }
        return result;
    }

    private static bool UseCiStrictDefaults(string effectiveMode, bool fast)
    {
        var isDev = string.Equals(effectiveMode, "dev", StringComparison.OrdinalIgnoreCase) || fast;
        if (isDev)
            return false;

        // Treat explicit pipeline mode=ci as strict even outside hosted CI environments.
        if (string.Equals(effectiveMode, "ci", StringComparison.OrdinalIgnoreCase))
            return true;

        return ConsoleEnvironment.IsCI;
    }

    private static string BuildPipelineToolFingerprint()
    {
        var parts = new List<string>(capacity: 2);
        AppendAssemblyFingerprint(parts, "cli", typeof(WebPipelineRunner).Assembly);
        AppendAssemblyFingerprint(parts, "engine", typeof(WebSiteBuilder).Assembly);
        return string.Join("|", parts);
    }

    private static void AppendAssemblyFingerprint(List<string> parts, string label, Assembly assembly)
    {
        var name = assembly.GetName();
        var assemblyName = name.Name ?? "unknown";
        var version = name.Version?.ToString() ?? "0.0.0.0";
        var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;
        var mvid = assembly.ManifestModule.ModuleVersionId.ToString("N");
        parts.Add($"{label}:{assemblyName}:{version}:{infoVersion}:{mvid}");
    }
}
