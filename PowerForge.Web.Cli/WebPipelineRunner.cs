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
    private static readonly TimeSpan DefaultWatchDebounce = TimeSpan.FromMilliseconds(250);
    private static readonly StringComparison FileSystemPathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private static readonly string PipelineToolFingerprint = BuildPipelineToolFingerprint();
    private static readonly HashSet<string> FingerprintPathKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "config", "siteRoot", "site-root", "project", "solution", "path",
        "repos", "repositories",
        "repoBaseUrl", "repo-base-url", "repositoryBaseUrl", "repository-base-url", "baseUrl", "base-url", "repoHost", "repo-host",
        "lockPath", "lock-path", "lock", "lockMode", "lock-mode",
        "retry", "retries", "retryCount", "retry-count", "retryDelayMs", "retry-delay-ms", "retryDelay", "retry-delay",
        "contextPath", "context-path", "context", "stdoutPath", "stdout-path", "stdout", "stderrPath", "stderr-path", "stderr",
        "projects", "projectFiles", "project-files",
        "module", "modules", "moduleFiles", "module-files",
        "out", "output", "source", "sourcesFile", "sources-file", "destination", "dest",
        "privateGallery", "private-gallery", "privateGalleryFeed", "private-gallery-feed", "gallery",
        "portalDocs", "portal-docs", "portalDocsIndex", "portal-docs-index", "docs",
        "map", "maps", "input", "inputs", "sources", "mapFiles", "map-files",
        "xml", "xmls", "xmlPaths", "help", "helpPath", "assembly", "assemblies", "assemblyPaths", "entries",
        "siteOut", "site-out", "outRoot", "out-root", "projectsOut", "projects-out",
        "changelog", "changelogPath", "changelog-path", "releasesPath", "releases-path",
        "discoverRoot", "discover-root",
        "apiIndex", "api-index", "apiIndexes", "api-indexes", "apiSitemap", "criticalCss", "hashManifest", "reportPath", "report-path",
        "summaryPath", "sarifPath", "baselinePath", "navCanonicalPath", "navProfiles", "reviewCsv", "reviewCsvPath", "csvReport",
        "summary-path", "sarif-path", "baseline-path", "nav-canonical-path", "nav-profiles", "review-csv", "review-csv-path", "csv-report",
        "targets", "paths", "roots",
        "template", "templatePath", "template-path",
        "templateRoot", "templateIndex", "templateType",
        "templateDocsIndex", "templateDocsType",
        "docsScript", "searchScript",
        "items", "itemsPath", "items-path", "manifest", "manifestPath", "manifest-path", "data",
        "coverageReport", "coverage-report", "coverageReportPath", "coverage-report-path",
        "xrefMap", "xref-map", "xrefMapPath", "xref-map-path",
        "psExamplesPath", "ps-examples-path", "powerShellExamplesPath", "powershell-examples-path",
        "psManifestPath", "ps-manifest-path", "powerShellManifestPath", "powershell-manifest-path",
        "psCommandMetadataPath", "ps-command-metadata-path", "powerShellCommandMetadataPath", "powershell-command-metadata-path",
        "catalog", "catalogPath", "catalog-path",
        "publicationCatalog", "publication-catalog", "agentPublicationCatalog", "agent-publication-catalog",
        "sourcesRoot", "sources-root", "contentRoot", "content-root",
        "apiRoot", "api-root", "examplesRoot", "examples-root",
        "sourceDocsPaths", "source-docs-paths", "sourceApiPaths", "source-api-paths",
        "sourceExamplesPaths", "source-examples-paths",
        "sourceDocsPath", "source-docs-path", "sourceApiPath", "source-api-path",
        "sourceExamplesPath", "source-examples-path",
        "artifactWorkRoot", "artifact-work-root",
        "publishPath", "publish-path", "curationCsv", "curation-csv",
        "statsPath", "stats-path",
        "sources", "sourcePaths", "source-paths", "redirectCsvPaths", "redirect-csv-paths",
        "redirects", "redirectsPath", "redirects-path", "shortlinks", "shortlinksPath", "shortlinks-path",
        "shortlinkSources", "shortlink-sources", "shortlinkOverlays", "shortlink-overlays",
        "csvSources", "csv-sources", "apacheOut", "apache-out",
        "csproj", "csprojFiles", "csproj-files", "psd1", "psd1Files", "psd1-files",
        "markdownOut", "markdown-out", "markdownOutput", "markdown-output",
        "headerHtml", "footerHtml", "quickstart", "extra", "discovery",
        "packageFiles", "package-files",
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
        public string? OutputStamp { get; set; }
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
        string[]? skipTasks = null,
        WebPipelineSplitOptions? split = null)
    {
        if (split is not null && fast)
            throw new InvalidOperationException("Split pipeline runs do not support fast or dev mode.");

        using var doc = LoadPipelineDocumentWithExtends(pipelinePath, out var pipelineSourcePaths);

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
        var runStopwatch = Stopwatch.StartNew();

        var result = new WebPipelineResult
        {
            CachePath = cacheEnabled ? cachePath : null
        };
        var steps = BuildStepDefinitions(stepsElement);
        var totalSteps = steps.Count;
        var enabledIndexNowSteps = steps
            .Where(definition => definition.Task.Equals("indexnow", StringComparison.OrdinalIgnoreCase) &&
                                 GetSkipReason(definition.Task, definition.Element, effectiveMode, onlyTaskSet, skipTaskSet) is null)
            .Select(static definition => definition.Element)
            .ToArray();
        if (enabledIndexNowSteps.Length > 0)
        {
            try
            {
                ValidateIndexNowOutputPaths(enabledIndexNowSteps, baseDir, pipelineSourcePaths,
                    profileEnabled || profileWriteOnFail ? profilePath : null,
                    cacheEnabled ? cachePath : null);
            }
            catch (Exception error)
            {
                result.Success = false;
                result.StepCount = 1;
                result.DurationMs = (long)Math.Round(runStopwatch.Elapsed.TotalMilliseconds);
                result.Steps.Add(new WebPipelineStepResult
                {
                    Task = "indexnow",
                    Success = false,
                    Message = FormatFailureMessage(error)
                });
                logger?.Error($"indexnow preflight: {FormatFailureMessage(error)}");
                return result;
            }
        }
        var splitBlock = split is null ? (First: 0, Last: 0) : ResolveSplitBlock(steps);
        var state = new PipelineRunState
        {
            Split = split,
            SplitFirstIndex = splitBlock.First,
            SplitLastIndex = splitBlock.Last,
            SplitPrepareSteps = split is null ? new HashSet<int>() : ResolveSplitPrepareSteps(steps, splitBlock.First, splitBlock.Last),
            Steps = steps,
            TotalSteps = totalSteps,
            BaseDir = baseDir,
            Fast = fast,
            EffectiveMode = effectiveMode,
            OnlyTaskSet = onlyTaskSet,
            SkipTaskSet = skipTaskSet,
            Logger = logger,
            ProfileEnabled = profileEnabled,
            ProfileWriteOnFail = profileWriteOnFail,
            ProfilePath = profilePath,
            CacheEnabled = cacheEnabled,
            CachePath = cachePath,
            CacheState = cacheState,
            PipelineSourcePaths = pipelineSourcePaths,
            EnabledIndexNowSteps = enabledIndexNowSteps,
            Result = result
        };

        if (split?.Phase == WebPipelineSplitPhase.Finish)
        {
            state.SplitPreparedHere = ConsumeSplitPrepareReceipt(state);
            ApplySplitArchives(state);
        }

        var maxParallelism = GetPipelineMaxParallelism(root);
        PipelineStepOutcome? failure;
        if (maxParallelism > 1)
        {
            failure = RunStepsInParallelGroups(state, maxParallelism);
        }
        else
        {
            failure = null;
            foreach (var definition in steps)
            {
                var outcome = RunPipelineStep(state, definition);
                if (outcome.Failed)
                {
                    failure = outcome;
                    break;
                }
            }
        }

        if (failure is not null)
        {
            result.StepCount = result.Steps.Count;
            result.Success = false;
            result.DurationMs = (long)Math.Round(runStopwatch.Elapsed.TotalMilliseconds);
            // Keep the previous successful cache. A later failing step may
            // have modified an earlier step's output before throwing.
            // Intentionally asymmetric:
            // - Success: write profile only when profile is enabled (to avoid noise/overhead).
            // - Failure: write profile when profile is enabled OR profileOnFail is true (default),
            //   so CI failures still produce actionable artifacts.
            if (failure.FailureProfileOutputIsSafe && !string.IsNullOrWhiteSpace(profilePath) && (profileEnabled || profileWriteOnFail))
            {
                WritePipelineProfile(profilePath, result, logger);
                result.ProfilePath = profilePath;
            }
            return result;
        }

        if (split?.Phase == WebPipelineSplitPhase.Prepare)
        {
            EnsureSplitArchiveWritten(state);
            if (split.ShardIndex == 1 && state.SplitPrepareSteps.Count > 0)
                WriteSplitPrepareReceipt(state);
        }

        var cacheOutputs = state.CacheOutputs;
        var cacheUpdated = state.CacheUpdated;

        runStopwatch.Stop();
        result.StepCount = result.Steps.Count;
        result.Success = result.Steps.All(s => s.Success);
        result.DurationMs = (long)Math.Round(runStopwatch.Elapsed.TotalMilliseconds);
        if (result.Success && cacheEnabled && cacheState is not null && cacheUpdated)
        {
            // Record output integrity after every step has finished. Later steps
            // may add files to an earlier build directory (for example sitemap).
            foreach (var output in cacheOutputs)
            {
                if (cacheState.Entries.TryGetValue(output.Key, out var entry))
                    entry.OutputStamp = ComputeOutputStamp(output.Value);
            }
            SavePipelineCache(cachePath, cacheState, logger);
        }
        if (!string.IsNullOrWhiteSpace(profilePath) && profileEnabled)
        {
            MergeSplitPrepareProfile(state);
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

    private static string BuildApiDocsFailureMessage(
        string label,
        ApiDocsType apiType,
        string? xml,
        string? help,
        string? assembly,
        string? outPath,
        string? configPath,
        string? nav,
        string? sourceRoot,
        Exception ex)
    {
        var fields = new List<string>
        {
            $"type={apiType}"
        };

        AppendApiDocsFailureField(fields, "xml", xml);
        AppendApiDocsFailureField(fields, "help", help);
        AppendApiDocsFailureField(fields, "assembly", assembly);
        AppendApiDocsFailureField(fields, "out", outPath);
        AppendApiDocsFailureField(fields, "config", configPath);
        AppendApiDocsFailureField(fields, "nav", nav);
        AppendApiDocsFailureField(fields, "sourceRoot", sourceRoot);

        return $"{label}: apidocs failed ({string.Join(", ", fields)}): {FormatFailureMessage(ex)}";
    }

    private static void AppendApiDocsFailureField(List<string> fields, string key, string? value)
    {
        if (fields is null || string.IsNullOrWhiteSpace(value))
            return;

        fields.Add($"{key}='{value}'");
    }
}
