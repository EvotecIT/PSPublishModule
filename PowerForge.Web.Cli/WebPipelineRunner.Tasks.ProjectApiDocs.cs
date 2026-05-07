using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static readonly string[] DefaultProjectApiPlaceholderMarkers =
    {
        "{{ Fill in the Synopsis }}",
        "{{ Fill in the Description }}",
        "{{ Add example code here }}",
        "{{ Add example description here }}",
        "{{ Fill HTML Description }}"
    };

    private static void ExecuteProjectApiDocs(
        JsonElement step,
        string label,
        string baseDir,
        bool fast,
        string effectiveMode,
        WebConsoleLogger? logger,
        string lastBuildOutPath,
        WebPipelineStepResult stepResult)
    {
        var catalogPath = ResolvePath(baseDir,
            GetString(step, "catalog") ??
            GetString(step, "catalogPath") ??
            GetString(step, "catalog-path") ??
            "./data/projects/catalog.json");
        if (string.IsNullOrWhiteSpace(catalogPath))
            throw new InvalidOperationException("project-apidocs requires catalog path.");
        catalogPath = Path.GetFullPath(catalogPath);
        if (!File.Exists(catalogPath))
            throw new InvalidOperationException($"project-apidocs catalog file not found: {catalogPath}");

        var sourcesRoot = ResolvePath(baseDir,
            GetString(step, "sourcesRoot") ??
            GetString(step, "sources-root") ??
            "./projects-sources");
        sourcesRoot = string.IsNullOrWhiteSpace(sourcesRoot) ? string.Empty : Path.GetFullPath(sourcesRoot);

        var apiRoot = ResolveProjectApiArtifactRoot(step, baseDir, logger);

        var siteRoot = ResolvePath(baseDir,
            GetString(step, "siteRoot") ??
            GetString(step, "site-root") ??
            GetString(step, "siteOut") ??
            GetString(step, "site-out"));
        if (string.IsNullOrWhiteSpace(siteRoot))
            siteRoot = string.IsNullOrWhiteSpace(lastBuildOutPath) ? null : lastBuildOutPath;
        siteRoot = string.IsNullOrWhiteSpace(siteRoot) ? string.Empty : Path.GetFullPath(siteRoot);

        var outRoot = ResolvePath(baseDir,
            GetString(step, "outRoot") ??
            GetString(step, "out-root") ??
            GetString(step, "projectsOut") ??
            GetString(step, "projects-out"));
        if (string.IsNullOrWhiteSpace(outRoot))
        {
            if (string.IsNullOrWhiteSpace(siteRoot))
                throw new InvalidOperationException("project-apidocs requires siteRoot/outRoot or a prior build step.");
            outRoot = Path.Combine(siteRoot, "projects");
        }
        outRoot = Path.GetFullPath(outRoot);

        var summaryPath = ResolvePath(baseDir,
            GetString(step, "summaryPath") ??
            GetString(step, "summary-path") ??
            "./Build/project-apidocs-last-run.json");
        var suiteTitle = GetString(step, "suiteTitle") ??
                         GetString(step, "suite-title") ??
                         GetString(step, "title") ??
                         "Project APIs";
        var suiteHomeUrl = GetString(step, "suiteHomeUrl") ?? GetString(step, "suite-home-url");
        var suiteHomeLabel = GetString(step, "suiteHomeLabel") ?? GetString(step, "suite-home-label");
        var suiteNarrativeManifestPaths = ResolvePathCandidates(
            step,
            arrayKeys: new[] { "suiteNarrativeManifests", "suite-narrative-manifests" },
            scalarKeys: new[] { "suiteNarrativeManifest", "suite-narrative-manifest" },
            defaults: Array.Empty<string>())
            .Select(path => ResolvePath(baseDir, path) ?? path)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var suiteManifestPath = ResolvePath(baseDir,
            GetString(step, "suiteManifestPath") ??
            GetString(step, "suite-manifest-path"));
        if (string.IsNullOrWhiteSpace(suiteManifestPath))
            suiteManifestPath = Path.Combine(outRoot, "api-suite.json");
        var generateSuiteSearch = GetBool(step, "generateSuiteSearch") ?? GetBool(step, "generate-suite-search") ?? true;
        var generateSuiteXrefMap = GetBool(step, "generateSuiteXrefMap") ?? GetBool(step, "generate-suite-xref-map") ?? true;
        var generateSuiteCoverageReport = GetBool(step, "generateSuiteCoverageReport") ?? GetBool(step, "generate-suite-coverage-report") ?? true;
        var generateSuiteRelatedContent = GetBool(step, "generateSuiteRelatedContent") ?? GetBool(step, "generate-suite-related-content") ?? true;
        var suiteSearchPath = ResolvePath(baseDir,
            GetString(step, "suiteSearchPath") ??
            GetString(step, "suite-search-path"));
        if (string.IsNullOrWhiteSpace(suiteSearchPath))
            suiteSearchPath = Path.Combine(outRoot, "api-suite-search.json");
        var suiteXrefMapPath = ResolvePath(baseDir,
            GetString(step, "suiteXrefMapPath") ??
            GetString(step, "suite-xref-map-path"));
        if (string.IsNullOrWhiteSpace(suiteXrefMapPath))
            suiteXrefMapPath = Path.Combine(outRoot, "api-suite-xrefmap.json");
        var suiteCoveragePath = ResolvePath(baseDir,
            GetString(step, "suiteCoveragePath") ??
            GetString(step, "suite-coverage-path"));
        if (string.IsNullOrWhiteSpace(suiteCoveragePath))
            suiteCoveragePath = Path.Combine(outRoot, "api-suite-coverage.json");
        var suiteRelatedContentPath = ResolvePath(baseDir,
            GetString(step, "suiteRelatedContentPath") ??
            GetString(step, "suite-related-content-path"));
        if (string.IsNullOrWhiteSpace(suiteRelatedContentPath))
            suiteRelatedContentPath = Path.Combine(outRoot, "api-suite-related-content.json");
        var suiteNarrativePath = ResolvePath(baseDir,
            GetString(step, "suiteNarrativePath") ??
            GetString(step, "suite-narrative-path"));
        if (string.IsNullOrWhiteSpace(suiteNarrativePath))
            suiteNarrativePath = Path.Combine(outRoot, "api-suite-narrative.json");
        var generateSuiteLandingPage = GetBool(step, "generateSuiteLandingPage") ?? GetBool(step, "generate-suite-landing-page") ?? true;
        var suiteLandingUrl = GetString(step, "suiteLandingUrl") ?? GetString(step, "suite-landing-url");
        var suiteLandingOutputPath = Path.Combine(outRoot, "api-suite");
        var suiteCoverageThresholds = GetProjectApiSuiteCoverageThresholds(step);
        var suiteFailOnCoverage = GetBool(step, "suiteFailOnCoverage") ??
                                  GetBool(step, "suite-fail-on-coverage") ??
                                  (suiteCoverageThresholds.Count > 0);
        var suiteCoveragePreviewCount = GetInt(step, "suiteCoveragePreviewCount") ??
                                        GetInt(step, "suite-coverage-preview-count") ??
                                        5;
        var suiteNarrativeThresholds = GetProjectApiSuiteNarrativeThresholds(step);
        var suiteFailOnNarrative = GetBool(step, "suiteFailOnNarrative") ??
                                   GetBool(step, "suite-fail-on-narrative") ??
                                   suiteNarrativeThresholds.HasRequirements;
        var suiteNarrativePreviewCount = GetInt(step, "suiteNarrativePreviewCount") ??
                                         GetInt(step, "suite-narrative-preview-count") ??
                                         5;
        var failOnMissingSource = GetBool(step, "failOnMissingSource") ?? GetBool(step, "fail-on-missing-source") ?? false;
        var failOnPlaceholderContent = GetBool(step, "failOnPlaceholderContent") ?? GetBool(step, "fail-on-placeholder-content") ?? false;
        var cleanOutput = GetBool(step, "clean") ?? false;
        var preferredApiMode = NormalizeProjectApiMode(
            GetString(step, "preferredMode") ??
            GetString(step, "preferred-mode") ??
            GetString(step, "modePreference") ??
            GetString(step, "mode-preference"));
        var onlyProjects = ParseProjectApiTokenSet(
            GetString(step, "projects") ??
            GetString(step, "project") ??
            GetString(step, "slugs") ??
            GetString(step, "slug"));
        var sourceApiCandidates = ResolvePathCandidates(
            step,
            arrayKeys: new[] { "sourceApiPaths", "source-api-paths" },
            scalarKeys: new[] { "sourceApiPath", "source-api-path", "sourceApiFolder", "source-api-folder" },
            defaults: new[] { "WebsiteArtifacts/apidocs", "data/apidocs" });
        var placeholderMarkers = ResolveProjectApiPlaceholderMarkers(step);
        var defaultCssHref = ResolveProjectApiCssHref(step, baseDir, logger);
        var siteConfigPath = ResolveProjectApiSiteConfigPath(step, baseDir);
        var apiLanguage = NormalizeOptionalString(
            GetString(step, "language") ??
            GetString(step, "lang") ??
            GetString(step, "languageCode") ??
            GetString(step, "language-code")) ?? "en";
        var navSurfaceName = ResolveProjectApiNavSurfaceName(
            apiLanguage,
            GetString(step, "navSurface") ??
            GetString(step, "nav-surface") ??
            GetString(step, "navSurfaceName") ??
            GetString(step, "nav-surface-name"));

        var serializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        var siteSpec = TryLoadProjectApiSiteSpec(siteConfigPath, logger);
        var apiSiteBaseUrl = ResolveProjectApiSiteBaseUrl(siteSpec, apiLanguage);

        var catalog = JsonSerializer.Deserialize<ProjectCatalogDocument>(File.ReadAllText(catalogPath), serializerOptions)
                      ?? new ProjectCatalogDocument();
        var preparedInputs = new List<ProjectApiDocsPreparedInput>();
        var skippedMissing = new List<string>();
        var skippedPlaceholder = new List<string>();

        foreach (var project in catalog.Projects ?? Enumerable.Empty<ProjectCatalogEntry>())
        {
            if (project is null)
                continue;

            var slug = NormalizeSlug(project.Slug);
            if (string.IsNullOrWhiteSpace(slug))
                continue;
            if (onlyProjects.Count > 0 && !onlyProjects.Contains(slug))
                continue;

            var mode = NormalizeProjectMode(project.Mode, "hub-full");
            var contentMode = NormalizeProjectContentMode(project.ContentMode, mode);
            if (contentMode.Equals("external", StringComparison.OrdinalIgnoreCase))
                continue;

            var hasPowerShell = TryGetProjectSurfaceValue(project.Surfaces, "apiPowerShell") ?? false;
            var hasDotNet = TryGetProjectSurfaceValue(project.Surfaces, "apiDotNet") ?? false;
            if (!hasPowerShell && !hasDotNet)
                continue;

            var discoveredInputs = DiscoverProjectApiInputs(
                slug,
                sourcesRoot,
                apiRoot,
                sourceApiCandidates,
                placeholderMarkers);

            var selected = SelectProjectApiInput(discoveredInputs, preferredApiMode, hasPowerShell, hasDotNet);
            if (selected is null)
            {
                var expected = BuildExpectedProjectApiSourcePath(slug, sourcesRoot, apiRoot, sourceApiCandidates);
                skippedMissing.Add(expected);
                DeleteProjectApiOutputIfRequested(outRoot, slug, cleanOutput);
                if (failOnMissingSource)
                    throw new InvalidOperationException($"project-apidocs source api path not found for '{slug}': {expected}");
                continue;
            }

            if (selected.HasPlaceholderContent)
            {
                var placeholderMessage = $"{slug} ({selected.Type}) -> {selected.PlaceholderPath}";
                skippedPlaceholder.Add(placeholderMessage);
                DeleteProjectApiOutputIfRequested(outRoot, slug, cleanOutput);
                if (failOnPlaceholderContent)
                    throw new InvalidOperationException($"project-apidocs placeholder content detected for '{slug}': {selected.PlaceholderPath}");
                continue;
            }

            preparedInputs.Add(BuildProjectApiDocsPreparedInput(
                project,
                slug,
                selected,
                outRoot,
                cleanOutput,
                defaultCssHref,
                siteSpec,
                apiLanguage,
                navSurfaceName,
                apiSiteBaseUrl,
                GetProjectApiDocsOverrides(project, baseDir)));
        }

        var suiteEntries = preparedInputs
            .Select(static prepared => prepared.SuiteEntry)
            .Where(static entry => entry is not null && !string.IsNullOrWhiteSpace(entry.Href))
            .ToArray();
        var effectiveSuiteLandingUrl = suiteEntries.Length > 1 && generateSuiteLandingPage
            ? ResolveProjectApiSuiteLandingUrl(suiteLandingUrl, suiteHomeUrl, siteRoot, outRoot, suiteEntries)
            : null;
        var effectiveSuiteHomeUrl = string.IsNullOrWhiteSpace(suiteHomeUrl)
            ? effectiveSuiteLandingUrl
            : suiteHomeUrl;
        foreach (var prepared in preparedInputs)
        {
            ApplyProjectApiSuite(
                prepared.InputNode,
                prepared.OutputPath,
                suiteTitle,
                effectiveSuiteHomeUrl,
                suiteHomeLabel,
                generateSuiteSearch ? suiteSearchPath : null,
                generateSuiteXrefMap ? suiteXrefMapPath : null,
                generateSuiteCoverageReport ? suiteCoveragePath : null,
                generateSuiteRelatedContent ? suiteRelatedContentPath : null,
                suiteNarrativeManifestPaths.Length > 0 ? suiteNarrativePath : null,
                suiteEntries,
                prepared.SuiteEntry.Id);
        }

        var completed = 0;
        var notes = new List<string>();
        foreach (var prepared in preparedInputs)
        {
            if (prepared.CleanOutput && Directory.Exists(prepared.OutputPath))
                Directory.Delete(prepared.OutputPath, recursive: true);

            using var inputDocument = JsonDocument.Parse(prepared.InputNode.ToJsonString());
            using var merged = CreateMergedApiDocsStepDocument(step, inputDocument.RootElement);
            var nestedResult = new WebPipelineStepResult { Task = "apidocs" };
            ExecuteApiDocs(
                merged.RootElement,
                $"{label}/{prepared.Slug}",
                baseDir,
                fast,
                effectiveMode,
                logger,
                nestedResult);

            completed++;
            if (!string.IsNullOrWhiteSpace(nestedResult.Message))
                notes.Add($"{prepared.Slug}: {nestedResult.Message}");
        }

        ProjectApiSuiteArtifacts? suiteArtifacts = null;
        WebApiDocsSuitePortalResult? suitePortal = null;
        var suiteStarterRecommendations = EvaluateProjectApiSuiteStarterRecommendations(
            preparedInputs,
            suiteNarrativeManifestPaths,
            catalogPath,
            baseDir,
            suiteEntries.Length);
        if (suiteStarterRecommendations.Count > 0)
        {
            foreach (var recommendation in suiteStarterRecommendations)
                logger?.Warn($"{label}: {recommendation}");
        }
        if (suiteEntries.Length > 1 && !string.IsNullOrWhiteSpace(suiteManifestPath))
        {
            suiteArtifacts = WriteProjectApiSuiteArtifacts(
                preparedInputs,
                suiteTitle,
                effectiveSuiteHomeUrl,
                suiteHomeLabel,
                suiteEntries,
                suiteNarrativeManifestPaths,
                suiteManifestPath,
                generateSuiteSearch ? suiteSearchPath : null,
                generateSuiteXrefMap ? suiteXrefMapPath : null,
                generateSuiteCoverageReport ? suiteCoveragePath : null,
                generateSuiteRelatedContent ? suiteRelatedContentPath : null,
                suiteNarrativeManifestPaths.Length > 0 ? suiteNarrativePath : null,
                logger);
            if (generateSuiteLandingPage && !string.IsNullOrWhiteSpace(effectiveSuiteLandingUrl))
            {
                suitePortal = WriteProjectApiSuitePortal(
                    step,
                    baseDir,
                    suiteTitle,
                    suiteHomeUrl,
                    suiteHomeLabel,
                    effectiveSuiteLandingUrl,
                    suiteLandingOutputPath,
                    suiteEntries,
                    defaultCssHref,
                    siteConfigPath,
                    apiSiteBaseUrl,
                    suiteArtifacts?.SearchOutputPath,
                    suiteArtifacts?.XrefMapOutputPath,
                    suiteArtifacts?.CoverageOutputPath,
                    suiteArtifacts?.RelatedContentOutputPath,
                    suiteArtifacts?.NarrativeOutputPath,
                    logger);
            }
            WriteProjectApiSuiteManifest(
                suiteManifestPath,
                suiteTitle,
                effectiveSuiteHomeUrl,
                suiteHomeLabel,
                suiteEntries,
                suiteArtifacts,
                BuildSuiteArtifactRelativePath(suiteManifestPath, suitePortal?.IndexPath),
                effectiveSuiteLandingUrl);
        }

        var suiteCoverageIssues = 0;
        if (suiteCoverageThresholds.Count > 0)
        {
            var suiteCoverageFailures = EvaluateApiDocsCoverageThresholds(
                suiteEntries.Length > 1 && generateSuiteCoverageReport ? suiteCoveragePath : null,
                suiteCoverageThresholds,
                out var suiteCoverageHeadline);
            suiteCoverageIssues = suiteCoverageFailures.Count;
            if (suiteCoverageIssues > 0)
            {
                logger?.Warn($"{label}: project-apidocs suite coverage threshold failures: {suiteCoverageIssues}");

                var previewLimit = Math.Clamp(suiteCoveragePreviewCount, 0, 20);
                if (previewLimit > 0)
                {
                    foreach (var failure in suiteCoverageFailures.Take(previewLimit))
                        logger?.Warn($"{label}: {failure}");

                    var remaining = suiteCoverageFailures.Count - previewLimit;
                    if (remaining > 0)
                        logger?.Warn($"{label}: (+{remaining} more suite coverage issues)");
                }

                if (suiteFailOnCoverage)
                    throw new InvalidOperationException(suiteCoverageHeadline ?? "project-apidocs suite coverage thresholds failed.");
            }
        }

        var suiteNarrativeIssues = 0;
        if (suiteNarrativeThresholds.HasRequirements)
        {
            var suiteNarrativeFailures = EvaluateProjectApiSuiteNarrativeThresholds(
                suiteEntries.Length > 1 ? (suiteNarrativeManifestPaths.Length > 0 ? suiteNarrativePath : null) : null,
                suiteNarrativeThresholds,
                out var suiteNarrativeHeadline);
            suiteNarrativeIssues = suiteNarrativeFailures.Count;
            if (suiteNarrativeIssues > 0)
            {
                logger?.Warn($"{label}: project-apidocs suite narrative threshold failures: {suiteNarrativeIssues}");

                var previewLimit = Math.Clamp(suiteNarrativePreviewCount, 0, 20);
                if (previewLimit > 0)
                {
                    foreach (var failure in suiteNarrativeFailures.Take(previewLimit))
                        logger?.Warn($"{label}: {failure}");

                    var remaining = suiteNarrativeFailures.Count - previewLimit;
                    if (remaining > 0)
                        logger?.Warn($"{label}: (+{remaining} more suite narrative issues)");
                }

                if (suiteFailOnNarrative)
                    throw new InvalidOperationException(suiteNarrativeHeadline ?? "project-apidocs suite narrative thresholds failed.");
            }
        }

        if (!string.IsNullOrWhiteSpace(summaryPath))
        {
            var summaryDirectory = Path.GetDirectoryName(summaryPath);
            if (!string.IsNullOrWhiteSpace(summaryDirectory))
                Directory.CreateDirectory(summaryDirectory);

            var summary = new
            {
                generatedOn = DateTimeOffset.UtcNow.ToString("O"),
                catalogPath,
                sourcesRoot = string.IsNullOrWhiteSpace(sourcesRoot) ? null : sourcesRoot,
                apiRoot = string.IsNullOrWhiteSpace(apiRoot) ? null : apiRoot,
                outRoot,
                preferredApiMode,
                totalProjects = catalog.Projects?.Count ?? 0,
                selectedProjects = preparedInputs.Select(static input => input.Slug).ToArray(),
                generated = completed,
                skippedMissing,
                skippedPlaceholder,
                failOnMissingSource,
                failOnPlaceholderContent,
                suiteManifestPath = suiteEntries.Length > 1 ? suiteManifestPath : null,
                suiteLandingPath = suitePortal?.IndexPath,
                suiteLandingUrl = effectiveSuiteLandingUrl,
                suiteSearchPath = suiteArtifacts?.SearchPath,
                suiteXrefMapPath = suiteArtifacts?.XrefMapPath,
                suiteCoveragePath = suiteArtifacts?.CoveragePath,
                suiteRelatedContentPath = suiteArtifacts?.RelatedContentPath,
                suiteNarrativePath = suiteArtifacts?.NarrativePath,
                suiteCoverageIssueCount = suiteCoverageIssues,
                suiteNarrativeIssueCount = suiteNarrativeIssues,
                suiteStarterRecommendationCount = suiteStarterRecommendations.Count
            };
            File.WriteAllText(summaryPath, JsonSerializer.Serialize(summary, serializerOptions));
        }

        var summaryMessage = notes.Count > 0
            ? string.Join("; ", notes.Take(2))
            : string.Empty;
        var suffix = notes.Count > 2 ? $" (+{notes.Count - 2} more)" : string.Empty;
        var missingNote = skippedMissing.Count > 0 ? $"; missing={skippedMissing.Count}" : string.Empty;
        var placeholderNote = skippedPlaceholder.Count > 0 ? $"; placeholder={skippedPlaceholder.Count}" : string.Empty;
        var suiteNote = suiteEntries.Length > 1 && !string.IsNullOrWhiteSpace(suiteManifestPath)
            ? $"; suite={Path.GetFileName(suiteManifestPath)}"
            : string.Empty;
        var suiteArtifactsNote = BuildSuiteArtifactsSummary(suiteArtifacts);
        var suiteLandingNote = suitePortal is not null ? "; suite-landing=api-suite/" : string.Empty;
        var suiteCoverageNote = suiteCoverageIssues > 0 ? $"; suite-coverage-issues={suiteCoverageIssues}" : string.Empty;
        var suiteNarrativeNote = suiteNarrativeIssues > 0 ? $"; suite-narrative-issues={suiteNarrativeIssues}" : string.Empty;
        var suiteStarterNote = suiteStarterRecommendations.Count > 0 ? $"; suite-guidance-recommendations={suiteStarterRecommendations.Count}" : string.Empty;
        stepResult.Success = true;
        stepResult.Message = string.IsNullOrWhiteSpace(summaryMessage)
            ? $"project-apidocs ok: generated={completed}{missingNote}{placeholderNote}{suiteNote}{suiteArtifactsNote}{suiteLandingNote}{suiteCoverageNote}{suiteNarrativeNote}{suiteStarterNote}"
            : $"project-apidocs ok: generated={completed}{missingNote}{placeholderNote}{suiteNote}{suiteArtifactsNote}{suiteLandingNote}{suiteCoverageNote}{suiteNarrativeNote}{suiteStarterNote}; {summaryMessage}{suffix}";
    }

    private static HashSet<string> ParseProjectApiTokenSet(string? value)
    {
        var items = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value))
            return items;

        foreach (var token in value.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = NormalizeSlug(token);
            if (!string.IsNullOrWhiteSpace(normalized))
                items.Add(normalized);
        }

        return items;
    }

    private static string NormalizeProjectApiMode(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "dotnet" or "csharp" ? "dotnet" : "powershell";
    }

    private static IReadOnlyList<string> ResolveProjectApiPlaceholderMarkers(JsonElement step)
    {
        var values = GetArrayOfStrings(step, "placeholderMarkers") ??
                     GetArrayOfStrings(step, "placeholder-markers");
        if (values is { Length: > 0 })
        {
            return values
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return DefaultProjectApiPlaceholderMarkers;
    }

    private static List<ProjectApiInputCandidate> DiscoverProjectApiInputs(
        string slug,
        string sourcesRoot,
        string apiRoot,
        IReadOnlyList<string> sourceApiCandidates,
        IReadOnlyList<string> placeholderMarkers)
    {
        var discovered = new List<ProjectApiInputCandidate>();
        foreach (var root in EnumerateProjectApiRoots(slug, sourcesRoot, apiRoot, sourceApiCandidates))
        {
            if (TryBuildPowerShellProjectApiInput(root, placeholderMarkers, out var powerShellCandidate))
                discovered.Add(powerShellCandidate!);
            if (TryBuildDotNetProjectApiInput(root, placeholderMarkers, out var dotNetCandidate))
                discovered.Add(dotNetCandidate!);
        }

        return discovered
            .GroupBy(candidate => candidate.Type, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();
    }

    private static IEnumerable<string> EnumerateProjectApiRoots(
        string slug,
        string sourcesRoot,
        string apiRoot,
        IReadOnlyList<string> sourceApiCandidates)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(apiRoot))
        {
            var directRoot = Path.Combine(apiRoot, slug);
            var full = Path.GetFullPath(directRoot);
            if (seen.Add(full) && Directory.Exists(full))
                yield return full;
        }

        if (!string.IsNullOrWhiteSpace(sourcesRoot) && Directory.Exists(sourcesRoot))
        {
            var resolved = ResolveExistingProjectSourcePath(sourcesRoot, slug, sourceApiCandidates);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                var full = Path.GetFullPath(resolved);
                if (seen.Add(full) && Directory.Exists(full))
                    yield return full;
            }
        }
    }

    private static string BuildExpectedProjectApiSourcePath(
        string slug,
        string sourcesRoot,
        string apiRoot,
        IReadOnlyList<string> sourceApiCandidates)
    {
        if (!string.IsNullOrWhiteSpace(apiRoot))
            return Path.GetFullPath(Path.Combine(apiRoot, slug));
        return ResolveExistingProjectSourcePath(sourcesRoot, slug, sourceApiCandidates);
    }

    private static bool TryBuildPowerShellProjectApiInput(
        string root,
        IReadOnlyList<string> placeholderMarkers,
        out ProjectApiInputCandidate? candidate)
    {
        candidate = null;
        var powerShellRoot = ResolveExistingSubdirectory(root, "powershell", "PowerShell");
        if (string.IsNullOrWhiteSpace(powerShellRoot))
            powerShellRoot = root;

        var helpFile = Directory.Exists(powerShellRoot)
            ? Directory.GetFiles(powerShellRoot, "*-help.xml", SearchOption.AllDirectories)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault()
            : null;
        helpFile ??= Directory.Exists(powerShellRoot)
            ? Directory.GetFiles(powerShellRoot, "*help.xml", SearchOption.AllDirectories)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault()
            : null;
        if (string.IsNullOrWhiteSpace(helpFile))
            return false;

        var examplesPath = ResolveExistingSubdirectory(powerShellRoot, "examples", "Examples", "content/examples");
        var manifestPath = Directory.Exists(powerShellRoot)
            ? Directory.GetFiles(powerShellRoot, "*.psd1", SearchOption.AllDirectories)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault()
            : null;
        var commandMetadataPath = Directory.Exists(powerShellRoot)
            ? Directory.GetFiles(powerShellRoot, "command-metadata.json", SearchOption.AllDirectories)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault()
            : null;
        var hasPlaceholder = TryDetectPlaceholderContent(helpFile, placeholderMarkers, out var placeholderPath);
        if (!hasPlaceholder && !string.IsNullOrWhiteSpace(examplesPath))
            hasPlaceholder = TryDetectPlaceholderContent(examplesPath, placeholderMarkers, out placeholderPath);

        candidate = new ProjectApiInputCandidate
        {
            Type = "PowerShell",
            RootPath = powerShellRoot,
            HelpPath = Path.GetFullPath(helpFile),
            PowerShellManifestPath = string.IsNullOrWhiteSpace(manifestPath) ? null : Path.GetFullPath(manifestPath),
            PowerShellCommandMetadataPath = string.IsNullOrWhiteSpace(commandMetadataPath) ? null : Path.GetFullPath(commandMetadataPath),
            PowerShellExamplesPath = string.IsNullOrWhiteSpace(examplesPath) ? null : Path.GetFullPath(examplesPath),
            HasPlaceholderContent = hasPlaceholder,
            PlaceholderPath = placeholderPath
        };
        return true;
    }

    private static bool TryBuildDotNetProjectApiInput(
        string root,
        IReadOnlyList<string> placeholderMarkers,
        out ProjectApiInputCandidate? candidate)
    {
        candidate = null;
        var dotNetRoot = ResolveExistingSubdirectory(root, "dotnet", "DotNet", "csharp", "CSharp");
        if (string.IsNullOrWhiteSpace(dotNetRoot))
            dotNetRoot = root;
        if (!Directory.Exists(dotNetRoot))
            return false;

        var xmlFiles = Directory.GetFiles(dotNetRoot, "*.xml", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (xmlFiles.Length == 0)
            return false;

        foreach (var xmlPath in xmlFiles)
        {
            var baseName = Path.GetFileNameWithoutExtension(xmlPath);
            var xmlDirectory = Path.GetDirectoryName(xmlPath) ?? dotNetRoot;
            var assemblyPath = Directory.GetFiles(xmlDirectory, "*.dll", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path => Path.GetFileNameWithoutExtension(path).Equals(baseName, StringComparison.OrdinalIgnoreCase));
            assemblyPath ??= Directory.GetFiles(dotNetRoot, "*.dll", SearchOption.AllDirectories)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(assemblyPath))
                continue;

            var hasPlaceholder = TryDetectPlaceholderContent(xmlPath, placeholderMarkers, out var placeholderPath);
            candidate = new ProjectApiInputCandidate
            {
                Type = "CSharp",
                RootPath = dotNetRoot,
                XmlPath = Path.GetFullPath(xmlPath),
                AssemblyPath = Path.GetFullPath(assemblyPath),
                HasPlaceholderContent = hasPlaceholder,
                PlaceholderPath = placeholderPath
            };
            return true;
        }

        return false;
    }

    private static string? ResolveExistingSubdirectory(string root, params string[] candidates)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return null;

        foreach (var candidate in candidates)
        {
            var full = Path.Combine(root, candidate.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
            if (Directory.Exists(full))
                return Path.GetFullPath(full);
        }

        return null;
    }

    private static bool TryDetectPlaceholderContent(
        string path,
        IReadOnlyList<string> placeholderMarkers,
        out string placeholderPath)
    {
        placeholderPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (File.Exists(path))
        {
            if (ContainsPlaceholderMarker(File.ReadAllText(path), placeholderMarkers))
            {
                placeholderPath = Path.GetFullPath(path);
                return true;
            }

            return false;
        }

        if (!Directory.Exists(path))
            return false;

        foreach (var filePath in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                     .Where(static file => HasPlaceholderScanExtension(file))
                     .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase))
        {
            var content = File.ReadAllText(filePath);
            if (!ContainsPlaceholderMarker(content, placeholderMarkers))
                continue;

            placeholderPath = Path.GetFullPath(filePath);
            return true;
        }

        return false;
    }

    private static bool HasPlaceholderScanExtension(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsPlaceholderMarker(string content, IReadOnlyList<string> placeholderMarkers)
    {
        if (string.IsNullOrWhiteSpace(content) || placeholderMarkers is null || placeholderMarkers.Count == 0)
            return false;

        foreach (var marker in placeholderMarkers)
        {
            if (string.IsNullOrWhiteSpace(marker))
                continue;
            if (content.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private static ProjectApiInputCandidate? SelectProjectApiInput(
        IReadOnlyList<ProjectApiInputCandidate> discoveredInputs,
        string preferredApiMode,
        bool hasPowerShell,
        bool hasDotNet)
    {
        if (discoveredInputs is null || discoveredInputs.Count == 0)
            return null;

        if (hasPowerShell && hasDotNet)
        {
            var preferredType = preferredApiMode.Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? "CSharp" : "PowerShell";
            var preferred = discoveredInputs.FirstOrDefault(candidate => candidate.Type.Equals(preferredType, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
                return preferred;
        }

        if (hasPowerShell)
        {
            var powerShell = discoveredInputs.FirstOrDefault(candidate => candidate.Type.Equals("PowerShell", StringComparison.OrdinalIgnoreCase));
            if (powerShell is not null)
                return powerShell;
        }

        if (hasDotNet)
        {
            var dotNet = discoveredInputs.FirstOrDefault(candidate => candidate.Type.Equals("CSharp", StringComparison.OrdinalIgnoreCase));
            if (dotNet is not null)
                return dotNet;
        }

        return discoveredInputs[0];
    }

    private static ProjectApiDocsPreparedInput BuildProjectApiDocsPreparedInput(
        ProjectCatalogEntry project,
        string slug,
        ProjectApiInputCandidate selected,
        string outRoot,
        bool cleanOutput,
        string? cssHref,
        SiteSpec? siteSpec,
        string language,
        string navSurfaceName,
        string? siteBaseUrl,
        ProjectApiDocsCatalogOverrides? apiDocsOverrides)
    {
        var name = NormalizeOptionalString(project.Name) ?? slug;
        var hubPath = NormalizeOptionalString(project.HubPath) ?? $"/projects/{slug}/";
        var overviewRoute = EnsureProjectRouteTrailingSlash(hubPath);
        var apiRoute = $"/projects/{slug}/api/";
        var docsRoute = $"/projects/{slug}/docs/";
        var examplesRoute = $"/projects/{slug}/examples/";
        var overviewUrl = ResolveProjectApiCollectionUrl(siteSpec, "projects", overviewRoute, language);
        var docsHomeUrl = ResolveProjectApiCollectionRelativeUrl(siteSpec, "projects", overviewRoute, language);
        var docsUrl = ResolveProjectApiCollectionUrl(siteSpec, "docs", docsRoute, language);
        var apiUrl = ResolveProjectApiCollectionRelativeUrl(siteSpec, "projects", apiRoute, language);
        var apiBaseUrl = apiUrl.TrimEnd('/');
        var examplesUrl = ResolveProjectApiCollectionUrl(siteSpec, "examples", examplesRoute, language);
        var outputPath = Path.Combine(outRoot, slug, "api");
        var templateTokens = BuildProjectApiTemplateTokens(project, slug, name, overviewUrl, docsUrl, apiUrl, examplesUrl);
        var node = new JsonObject
        {
            ["id"] = slug,
            ["title"] = $"{name} API Reference",
            ["out"] = outputPath,
            ["baseUrl"] = apiBaseUrl,
            ["siteBaseUrl"] = siteBaseUrl,
            ["language"] = string.IsNullOrWhiteSpace(language) ? "en" : language,
            ["docsHome"] = docsHomeUrl,
            ["navContextPath"] = "/",
            ["navContextProject"] = slug,
            ["navSurface"] = string.IsNullOrWhiteSpace(navSurfaceName) ? "main" : navSurfaceName,
            ["type"] = selected.Type,
            ["templateTokens"] = templateTokens
        };

        if (!string.IsNullOrWhiteSpace(cssHref))
            node["css"] = cssHref;
        if (!string.IsNullOrWhiteSpace(apiDocsOverrides?.RelatedContentManifest))
            node["relatedContentManifest"] = apiDocsOverrides.RelatedContentManifest;
        if (apiDocsOverrides?.RelatedContentManifests is { Length: > 0 })
            node["relatedContentManifests"] = new JsonArray(apiDocsOverrides.RelatedContentManifests.Select(static path => (JsonNode?)path).ToArray());
        if (!string.IsNullOrWhiteSpace(apiDocsOverrides?.QuickStartTypes))
            node["quickStartTypes"] = apiDocsOverrides.QuickStartTypes;
        if (!string.IsNullOrWhiteSpace(selected.HelpPath))
            node["helpPath"] = selected.HelpPath;
        if (!string.IsNullOrWhiteSpace(selected.PowerShellManifestPath))
            node["psManifestPath"] = selected.PowerShellManifestPath;
        if (!string.IsNullOrWhiteSpace(selected.PowerShellCommandMetadataPath))
            node["psCommandMetadataPath"] = selected.PowerShellCommandMetadataPath;
        if (!string.IsNullOrWhiteSpace(selected.PowerShellExamplesPath))
            node["psExamplesPath"] = selected.PowerShellExamplesPath;
        if (!string.IsNullOrWhiteSpace(selected.XmlPath))
            node["xml"] = selected.XmlPath;
        if (!string.IsNullOrWhiteSpace(selected.AssemblyPath))
            node["assembly"] = selected.AssemblyPath;

        return new ProjectApiDocsPreparedInput
        {
            Slug = slug,
            OutputPath = outputPath,
            InputNode = node,
            SuiteEntry = new WebApiDocsSuiteEntry
            {
                Id = slug,
                Label = name,
                Href = apiUrl,
                Summary = ResolveProjectApiSummary(project, name)
            },
            CleanOutput = cleanOutput,
            HasQuickStartTypes = !string.IsNullOrWhiteSpace(apiDocsOverrides?.QuickStartTypes),
            RelatedContentManifestPaths = apiDocsOverrides?.RelatedContentManifestPaths ?? Array.Empty<string>()
        };
    }

    private static string ResolveProjectApiNavSurfaceName(string? language, string? configuredNavSurface)
    {
        var configured = NormalizeOptionalString(configuredNavSurface);
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var normalizedLanguage = NormalizeOptionalString(language) ?? "en";
        return normalizedLanguage.Equals("en", StringComparison.OrdinalIgnoreCase)
            ? "main"
            : $"main-{normalizedLanguage}";
    }

    private static SiteSpec? TryLoadProjectApiSiteSpec(string? siteConfigPath, WebConsoleLogger? logger)
    {
        if (string.IsNullOrWhiteSpace(siteConfigPath) || !File.Exists(siteConfigPath))
            return null;

        try
        {
            var (spec, _) = WebSiteSpecLoader.LoadWithPath(siteConfigPath, WebCliJson.Options);
            return spec;
        }
        catch (Exception ex)
        {
            logger?.Warn($"project-apidocs: unable to load site config for localized project routes ({ex.GetType().Name}: {ex.Message})");
            return null;
        }
    }

    private static ProjectApiDocsCatalogOverrides? GetProjectApiDocsOverrides(ProjectCatalogEntry project, string baseDir)
    {
        if (project?.ExtensionData is null || project.ExtensionData.Count == 0)
            return null;

        foreach (var pair in project.ExtensionData)
        {
            if (!pair.Key.Equals("apiDocs", StringComparison.OrdinalIgnoreCase) &&
                !pair.Key.Equals("api-docs", StringComparison.OrdinalIgnoreCase))
                continue;

            if (pair.Value.ValueKind != JsonValueKind.Object)
                return null;

            var relatedContentManifest = GetString(pair.Value, "relatedContentManifest") ??
                                         GetString(pair.Value, "related-content-manifest");
            var relatedContentManifests = GetArrayOfStrings(pair.Value, "relatedContentManifests") ??
                                          GetArrayOfStrings(pair.Value, "related-content-manifests");
            var quickStartTypes = GetString(pair.Value, "quickStartTypes") ??
                                  GetString(pair.Value, "quickstartTypes") ??
                                  GetString(pair.Value, "quick-start-types");
            if (string.IsNullOrWhiteSpace(relatedContentManifest) &&
                (relatedContentManifests is null || relatedContentManifests.Length == 0) &&
                string.IsNullOrWhiteSpace(quickStartTypes))
            {
                return null;
            }

            return new ProjectApiDocsCatalogOverrides
            {
                RelatedContentManifest = string.IsNullOrWhiteSpace(relatedContentManifest) ? null : ResolvePath(baseDir, relatedContentManifest),
                RelatedContentManifests = relatedContentManifests?
                    .Where(static path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => ResolvePath(baseDir, path) ?? path)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                QuickStartTypes = NormalizeOptionalString(quickStartTypes)
            };
        }

        return null;
    }

    private static List<ApiDocsCoverageThreshold> GetProjectApiSuiteCoverageThresholds(JsonElement step)
    {
        if (TryGetObject(step, "suiteCoverage", out var suiteCoverage) ||
            TryGetObject(step, "suite-coverage", out suiteCoverage))
        {
            return GetApiDocsCoverageThresholds(suiteCoverage);
        }

        return new List<ApiDocsCoverageThreshold>();
    }

    private static ProjectApiSuiteNarrativeThresholds GetProjectApiSuiteNarrativeThresholds(JsonElement step)
    {
        if (TryGetObject(step, "suiteNarrative", out var suiteNarrative) ||
            TryGetObject(step, "suite-narrative", out suiteNarrative))
        {
            return new ProjectApiSuiteNarrativeThresholds
            {
                MinSectionCount = GetInt(suiteNarrative, "minSectionCount") ?? GetInt(suiteNarrative, "min-section-count"),
                MinItemCount = GetInt(suiteNarrative, "minItemCount") ?? GetInt(suiteNarrative, "min-item-count"),
                RequireSummary = GetBool(suiteNarrative, "requireSummary") ?? GetBool(suiteNarrative, "require-summary") ?? false,
                MinSuiteEntryCoveragePercent = GetDouble(suiteNarrative, "minSuiteEntryCoveragePercent") ?? GetDouble(suiteNarrative, "min-suite-entry-coverage-percent"),
                MaxUncoveredSuiteEntryCount = GetInt(suiteNarrative, "maxUncoveredSuiteEntryCount") ?? GetInt(suiteNarrative, "max-uncovered-suite-entry-count")
            };
        }

        return new ProjectApiSuiteNarrativeThresholds();
    }

    private static List<string> EvaluateProjectApiSuiteStarterRecommendations(
        IReadOnlyList<ProjectApiDocsPreparedInput> preparedInputs,
        IReadOnlyList<string> suiteNarrativeManifestPaths,
        string? catalogPath,
        string baseDir,
        int suiteProjectCount)
    {
        var recommendations = new List<string>();

        if (HasProjectApiSuiteStarterArtifacts(catalogPath, baseDir))
        {
            if (preparedInputs.Count == 0)
            {
                recommendations.Add("[PFWEB.APIDOCS.SUITE] Multi-project API suite starter is still using an empty catalog or no resolved project APIs; copy the starter template entries into catalog.json and replace the sample placeholders before relying on project-apidocs.");
            }

            var sampleSlugCount = preparedInputs.Count(static prepared =>
                string.Equals(prepared.Slug, "sample-project", StringComparison.OrdinalIgnoreCase));
            if (sampleSlugCount > 0)
            {
                recommendations.Add($"[PFWEB.APIDOCS.SUITE] Multi-project API suite still includes starter slug 'sample-project' ({sampleSlugCount} project(s)); replace scaffold sample entries with real project slugs before publishing the suite.");
            }

            var sampleManifestCount = preparedInputs.Count(static prepared =>
                prepared.RelatedContentManifestPaths.Any(static path =>
                    path.Contains("sample-project-api-guides.json", StringComparison.OrdinalIgnoreCase)));
            if (sampleManifestCount > 0)
            {
                recommendations.Add($"[PFWEB.APIDOCS.SUITE] Multi-project API suite still references starter manifest 'sample-project-api-guides.json' for {sampleManifestCount} project(s); duplicate the template manifest per real project before treating related-content coverage as meaningful.");
            }
        }

        if (suiteProjectCount <= 1)
            return recommendations;

        var narrativeConfigured = suiteNarrativeManifestPaths is { Count: > 0 };
        if (!narrativeConfigured)
        {
            recommendations.Add("[PFWEB.APIDOCS.SUITE] Multi-project API suite is missing suiteNarrativeManifest(s); add a suite narrative so the portal can render a Start Here path.");
        }

        var quickStartProjectCount = preparedInputs.Count(static prepared => prepared.HasQuickStartTypes);
        if (quickStartProjectCount == 0)
        {
            recommendations.Add("[PFWEB.APIDOCS.SUITE] Multi-project API suite has no project-level quickStartTypes; add apiDocs.quickStartTypes in the catalog so entry-point APIs can be tracked and gated.");
        }
        else if (quickStartProjectCount < suiteProjectCount)
        {
            recommendations.Add($"[PFWEB.APIDOCS.SUITE] Only {quickStartProjectCount}/{suiteProjectCount} suite projects declare apiDocs.quickStartTypes; expand quick-start coverage so the portal reflects each project's main entry points.");
        }

        var relatedContentProjectCount = preparedInputs.Count(static prepared => prepared.RelatedContentManifestPaths is { Count: > 0 });
        if (relatedContentProjectCount == 0)
        {
            recommendations.Add("[PFWEB.APIDOCS.SUITE] Multi-project API suite has no project-level relatedContentManifest(s); add curated guides/samples so suite discovery links reference authored docs, not only raw API pages.");
        }
        else if (relatedContentProjectCount < suiteProjectCount)
        {
            recommendations.Add($"[PFWEB.APIDOCS.SUITE] Only {relatedContentProjectCount}/{suiteProjectCount} suite projects declare apiDocs.relatedContentManifest(s); add curated guides for the remaining projects to balance suite discovery.");
        }

        return recommendations;
    }

    private static bool HasProjectApiSuiteStarterArtifacts(string? catalogPath, string baseDir)
    {
        var root = baseDir;
        var catalogDirectory = !string.IsNullOrWhiteSpace(catalogPath)
            ? Path.GetDirectoryName(catalogPath)
            : null;
        if (!string.IsNullOrWhiteSpace(catalogDirectory))
            root = Path.GetDirectoryName(catalogDirectory) is { Length: > 0 } dataDir && dataDir.EndsWith("data", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(dataDir) ?? baseDir
                : baseDir;

        var starterCatalogTemplatePath = !string.IsNullOrWhiteSpace(catalogDirectory)
            ? Path.Combine(catalogDirectory, "catalog.project-template.json")
            : Path.Combine(baseDir, "data", "projects", "catalog.project-template.json");
        var starterRelatedContentPath = !string.IsNullOrWhiteSpace(catalogDirectory)
            ? Path.Combine(catalogDirectory, "sample-project-api-guides.json")
            : Path.Combine(baseDir, "data", "projects", "sample-project-api-guides.json");
        var starterGuidePath = Path.Combine(root, "content", "docs", "projects", "api-guide-template.md");

        return File.Exists(starterCatalogTemplatePath) ||
               File.Exists(starterRelatedContentPath) ||
               File.Exists(starterGuidePath);
    }

    private static List<string> EvaluateProjectApiSuiteNarrativeThresholds(
        string? narrativePath,
        ProjectApiSuiteNarrativeThresholds thresholds,
        out string? headline)
    {
        headline = null;
        var failures = new List<string>();
        if (thresholds is null || !thresholds.HasRequirements)
            return failures;

        if (string.IsNullOrWhiteSpace(narrativePath) || !File.Exists(narrativePath))
        {
            var message = "Suite narrative artifact not found; cannot evaluate suite narrative thresholds. Configure suiteNarrativeManifest(s) or emit api-suite-narrative.json.";
            failures.Add(message);
            headline = message;
            return failures;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(narrativePath));
        var root = document.RootElement;

        if (thresholds.MinSectionCount.HasValue &&
            (!TryGetJsonDoubleByPath(root, "sectionCount", out var sectionCount) || sectionCount + 0.0001 < thresholds.MinSectionCount.Value))
        {
            failures.Add($"Suite narrative section count: {(TryGetJsonDoubleByPath(root, "sectionCount", out sectionCount) ? $"{sectionCount:0.##}" : "missing")} is below required {thresholds.MinSectionCount.Value:0.##}.");
        }

        if (thresholds.MinItemCount.HasValue &&
            (!TryGetJsonDoubleByPath(root, "itemCount", out var itemCount) || itemCount + 0.0001 < thresholds.MinItemCount.Value))
        {
            failures.Add($"Suite narrative item count: {(TryGetJsonDoubleByPath(root, "itemCount", out itemCount) ? $"{itemCount:0.##}" : "missing")} is below required {thresholds.MinItemCount.Value:0.##}.");
        }

        if (thresholds.RequireSummary &&
            (!TryGetJsonBoolByPath(root, "content.summaryPresent", out var summaryPresent) || !summaryPresent))
        {
            failures.Add("Suite narrative summary is required but missing.");
        }

        if (thresholds.MinSuiteEntryCoveragePercent.HasValue)
        {
            if (!TryGetJsonDoubleByPath(root, "coverage.suiteEntries.percent", out var coveragePercent))
            {
                failures.Add("Suite narrative entry coverage percent is missing from the suite narrative artifact.");
            }
            else if (coveragePercent + 0.0001 < thresholds.MinSuiteEntryCoveragePercent.Value)
            {
                failures.Add($"Suite narrative entry coverage: {FormatCoverageValue(coveragePercent, asPercent: true)} is below required {FormatCoverageValue(thresholds.MinSuiteEntryCoveragePercent.Value, asPercent: true)}.");
            }
        }

        if (thresholds.MaxUncoveredSuiteEntryCount.HasValue)
        {
            if (!TryGetJsonDoubleByPath(root, "coverage.suiteEntries.uncoveredCount", out var uncoveredCount))
            {
                failures.Add("Suite narrative uncovered suite entry count is missing from the suite narrative artifact.");
            }
            else if (uncoveredCount - thresholds.MaxUncoveredSuiteEntryCount.Value > 0.0001)
            {
                failures.Add($"Suite narrative uncovered suite entries: {uncoveredCount:0.##} exceeds allowed {thresholds.MaxUncoveredSuiteEntryCount.Value:0.##}.");
            }
        }

        headline = failures.FirstOrDefault();
        return failures;
    }

    private static void ApplyProjectApiSuite(
        JsonObject node,
        string apiOutputPath,
        string? suiteTitle,
        string? suiteHomeUrl,
        string? suiteHomeLabel,
        string? suiteSearchPath,
        string? suiteXrefMapPath,
        string? suiteCoveragePath,
        string? suiteRelatedContentPath,
        string? suiteNarrativePath,
        IReadOnlyList<WebApiDocsSuiteEntry> suiteEntries,
        string currentId)
    {
        if (node is null || suiteEntries is null || suiteEntries.Count <= 1)
            return;

        node["suiteTitle"] = suiteTitle;
        if (!string.IsNullOrWhiteSpace(suiteHomeUrl))
            node["suiteHomeUrl"] = suiteHomeUrl;
        if (!string.IsNullOrWhiteSpace(suiteHomeLabel))
            node["suiteHomeLabel"] = suiteHomeLabel;
        node["suiteCurrentId"] = currentId;
        var suiteSearchUrl = BuildRelativeApiArtifactUrl(apiOutputPath, suiteSearchPath);
        if (!string.IsNullOrWhiteSpace(suiteSearchUrl))
            node["suiteSearchUrl"] = suiteSearchUrl;
        var suiteXrefUrl = BuildRelativeApiArtifactUrl(apiOutputPath, suiteXrefMapPath);
        if (!string.IsNullOrWhiteSpace(suiteXrefUrl))
            node["suiteXrefMapUrl"] = suiteXrefUrl;
        var suiteCoverageUrl = BuildRelativeApiArtifactUrl(apiOutputPath, suiteCoveragePath);
        if (!string.IsNullOrWhiteSpace(suiteCoverageUrl))
            node["suiteCoverageUrl"] = suiteCoverageUrl;
        var suiteRelatedContentUrl = BuildRelativeApiArtifactUrl(apiOutputPath, suiteRelatedContentPath);
        if (!string.IsNullOrWhiteSpace(suiteRelatedContentUrl))
            node["suiteRelatedContentUrl"] = suiteRelatedContentUrl;
        var suiteNarrativeUrl = BuildRelativeApiArtifactUrl(apiOutputPath, suiteNarrativePath);
        if (!string.IsNullOrWhiteSpace(suiteNarrativeUrl))
            node["suiteNarrativeUrl"] = suiteNarrativeUrl;
        node["suiteEntries"] = BuildApiSuiteEntriesNode(suiteEntries);
    }

    private static void WriteProjectApiSuiteManifest(
        string suiteManifestPath,
        string? suiteTitle,
        string? suiteHomeUrl,
        string? suiteHomeLabel,
        IReadOnlyList<WebApiDocsSuiteEntry> suiteEntries,
        ProjectApiSuiteArtifacts? artifacts,
        string? landingPath,
        string? landingUrl)
    {
        if (string.IsNullOrWhiteSpace(suiteManifestPath) || suiteEntries is null || suiteEntries.Count <= 1)
            return;

        var directory = Path.GetDirectoryName(suiteManifestPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var manifest = new JsonObject
        {
            ["title"] = suiteTitle,
            ["homeUrl"] = suiteHomeUrl,
            ["homeLabel"] = suiteHomeLabel,
            ["landingPath"] = landingPath,
            ["landingUrl"] = landingUrl,
            ["entries"] = BuildApiSuiteEntriesNode(suiteEntries)
        };
        if (artifacts is not null)
        {
            manifest["artifacts"] = new JsonObject
            {
                ["searchPath"] = artifacts.SearchPath,
                ["xrefMapPath"] = artifacts.XrefMapPath,
                ["coveragePath"] = artifacts.CoveragePath,
                ["relatedContentPath"] = artifacts.RelatedContentPath,
                ["narrativePath"] = artifacts.NarrativePath
            };
        }
        File.WriteAllText(
            suiteManifestPath,
            JsonSerializer.Serialize(
                manifest,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
    }

    private static WebApiDocsSuitePortalResult? WriteProjectApiSuitePortal(
        JsonElement step,
        string baseDir,
        string? suiteTitle,
        string? suiteHomeUrl,
        string? suiteHomeLabel,
        string suiteLandingUrl,
        string suiteLandingOutputPath,
        IReadOnlyList<WebApiDocsSuiteEntry> suiteEntries,
        string? cssHref,
        string? siteConfigPath,
        string? siteBaseUrl,
        string? suiteSearchPath,
        string? suiteXrefMapPath,
        string? suiteCoveragePath,
        string? suiteRelatedContentPath,
        string? suiteNarrativePath,
        WebConsoleLogger? logger)
    {
        if (suiteEntries is null || suiteEntries.Count <= 1 || string.IsNullOrWhiteSpace(suiteLandingUrl) || string.IsNullOrWhiteSpace(suiteLandingOutputPath))
            return null;

        var landingRoot = Path.GetFullPath(suiteLandingOutputPath);
        var suiteSearchUrl = BuildRelativeApiArtifactUrl(landingRoot, suiteSearchPath);
        var suiteXrefUrl = BuildRelativeApiArtifactUrl(landingRoot, suiteXrefMapPath);
        var suiteCoverageUrl = BuildRelativeApiArtifactUrl(landingRoot, suiteCoveragePath);
        var suiteRelatedContentUrl = BuildRelativeApiArtifactUrl(landingRoot, suiteRelatedContentPath);
        var suiteNarrativeUrl = BuildRelativeApiArtifactUrl(landingRoot, suiteNarrativePath);

        try
        {
            var headHtmlPath = ResolvePath(baseDir, GetString(step, "headHtml") ?? GetString(step, "head-html"));
            var headerHtmlPath = ResolvePath(baseDir, GetString(step, "headerHtml") ?? GetString(step, "header-html"));
            var footerHtmlPath = ResolvePath(baseDir, GetString(step, "footerHtml") ?? GetString(step, "footer-html"));
            if (!string.IsNullOrWhiteSpace(siteConfigPath))
                TryResolveApiSuiteFragmentsFromTheme(siteConfigPath, ref headHtmlPath, ref headerHtmlPath, ref footerHtmlPath);

            var options = new WebApiDocsOptions
            {
                OutputPath = landingRoot,
                Title = string.IsNullOrWhiteSpace(suiteTitle) ? "Project APIs" : suiteTitle,
                BaseUrl = suiteLandingUrl,
                CssHref = cssHref,
                CriticalCssPath = ResolvePath(baseDir, GetString(step, "criticalCssPath") ?? GetString(step, "critical-css-path") ?? GetString(step, "criticalCss") ?? GetString(step, "critical-css")),
                HeadHtmlPath = headHtmlPath,
                HeaderHtmlPath = headerHtmlPath,
                FooterHtmlPath = footerHtmlPath,
                TemplateRootPath = ResolvePath(baseDir, GetString(step, "templateRoot") ?? GetString(step, "template-root")),
                DocsScriptPath = ResolvePath(baseDir, GetString(step, "docsScript") ?? GetString(step, "docs-script")),
                NavJsonPath = siteConfigPath,
                SiteConfigPath = siteConfigPath,
                SiteBaseUrl = siteBaseUrl,
                NavContextPath = GetString(step, "navContextPath") ?? GetString(step, "nav-context-path") ?? "/",
                NavContextCollection = GetString(step, "navContextCollection") ?? GetString(step, "nav-context-collection"),
                NavContextLayout = GetString(step, "navContextLayout") ?? GetString(step, "nav-context-layout"),
                NavContextProject = null,
                NavSurfaceName = ResolveProjectApiNavSurfaceName(
                    GetString(step, "language") ?? GetString(step, "lang") ?? GetString(step, "languageCode") ?? GetString(step, "language-code"),
                    GetString(step, "navSurface") ?? GetString(step, "nav-surface") ?? GetString(step, "navSurfaceName") ?? GetString(step, "nav-surface-name")),
                SiteName = GetString(step, "siteName") ?? GetString(step, "site-name"),
                BodyClass = GetString(step, "bodyClass") ?? GetString(step, "body-class"),
                ApiSuiteTitle = suiteTitle,
                ApiSuiteHomeUrl = suiteHomeUrl,
                ApiSuiteHomeLabel = suiteHomeLabel,
                ApiSuiteSearchUrl = suiteSearchUrl,
                ApiSuiteXrefMapUrl = suiteXrefUrl,
                ApiSuiteCoverageUrl = suiteCoverageUrl,
                ApiSuiteRelatedContentUrl = suiteRelatedContentUrl,
                ApiSuiteNarrativeUrl = suiteNarrativeUrl
            };
            options.ApiSuiteEntries.AddRange(suiteEntries);

            var result = WebApiDocsGenerator.GenerateSuitePortal(options);
            foreach (var warning in result.Warnings)
                logger?.Warn($"project-apidocs: {warning}");
            return result;
        }
        catch (Exception ex)
        {
            logger?.Warn($"project-apidocs: suite landing generation failed ({ex.GetType().Name}: {ex.Message})");
            return null;
        }
    }

    private static ProjectApiSuiteArtifacts? WriteProjectApiSuiteArtifacts(
        IReadOnlyList<ProjectApiDocsPreparedInput> preparedInputs,
        string? suiteTitle,
        string? suiteHomeUrl,
        string? suiteHomeLabel,
        IReadOnlyList<WebApiDocsSuiteEntry> suiteEntries,
        IReadOnlyList<string> suiteNarrativeManifestPaths,
        string suiteManifestPath,
        string? suiteSearchPath,
        string? suiteXrefMapPath,
        string? suiteCoveragePath,
        string? suiteRelatedContentPath,
        string? suiteNarrativePath,
        WebConsoleLogger? logger)
    {
        if (preparedInputs is null || preparedInputs.Count == 0 || suiteEntries is null || suiteEntries.Count <= 1)
            return null;

        var artifacts = new ProjectApiSuiteArtifacts();

        if (!string.IsNullOrWhiteSpace(suiteSearchPath))
        {
            artifacts.SearchOutputPath = WriteProjectApiSuiteSearch(preparedInputs, suiteTitle, suiteHomeUrl, suiteHomeLabel, suiteEntries, suiteSearchPath, logger);
            artifacts.SearchPath = artifacts.SearchOutputPath;
        }

        if (!string.IsNullOrWhiteSpace(suiteXrefMapPath))
        {
            artifacts.XrefMapOutputPath = WriteProjectApiSuiteXref(preparedInputs, suiteXrefMapPath, logger);
            artifacts.XrefMapPath = artifacts.XrefMapOutputPath;
        }

        if (!string.IsNullOrWhiteSpace(suiteCoveragePath))
        {
            artifacts.CoverageOutputPath = WriteProjectApiSuiteCoverage(preparedInputs, suiteTitle, suiteHomeUrl, suiteHomeLabel, suiteEntries, suiteCoveragePath, logger);
            artifacts.CoveragePath = artifacts.CoverageOutputPath;
        }

        if (!string.IsNullOrWhiteSpace(suiteRelatedContentPath))
        {
            artifacts.RelatedContentOutputPath = WriteProjectApiSuiteRelatedContent(preparedInputs, suiteTitle, suiteHomeUrl, suiteHomeLabel, suiteEntries, suiteRelatedContentPath, logger);
            artifacts.RelatedContentPath = artifacts.RelatedContentOutputPath;
        }

        if (!string.IsNullOrWhiteSpace(suiteNarrativePath))
        {
            artifacts.NarrativeOutputPath = WriteProjectApiSuiteNarrative(suiteNarrativeManifestPaths, suiteTitle, suiteHomeUrl, suiteHomeLabel, suiteEntries, suiteNarrativePath, logger);
            artifacts.NarrativePath = artifacts.NarrativeOutputPath;
        }

        artifacts.SearchPath = BuildSuiteArtifactRelativePath(suiteManifestPath, artifacts.SearchPath);
        artifacts.XrefMapPath = BuildSuiteArtifactRelativePath(suiteManifestPath, artifacts.XrefMapPath);
        artifacts.CoveragePath = BuildSuiteArtifactRelativePath(suiteManifestPath, artifacts.CoveragePath);
        artifacts.RelatedContentPath = BuildSuiteArtifactRelativePath(suiteManifestPath, artifacts.RelatedContentPath);
        artifacts.NarrativePath = BuildSuiteArtifactRelativePath(suiteManifestPath, artifacts.NarrativePath);

        if (string.IsNullOrWhiteSpace(artifacts.SearchPath) &&
            string.IsNullOrWhiteSpace(artifacts.XrefMapPath) &&
            string.IsNullOrWhiteSpace(artifacts.CoveragePath) &&
            string.IsNullOrWhiteSpace(artifacts.RelatedContentPath) &&
            string.IsNullOrWhiteSpace(artifacts.NarrativePath))
        {
            return null;
        }

        return artifacts;
    }

    private static string? WriteProjectApiSuiteSearch(
        IReadOnlyList<ProjectApiDocsPreparedInput> preparedInputs,
        string? suiteTitle,
        string? suiteHomeUrl,
        string? suiteHomeLabel,
        IReadOnlyList<WebApiDocsSuiteEntry> suiteEntries,
        string suiteSearchPath,
        WebConsoleLogger? logger)
    {
        var items = new List<JsonObject>();
        foreach (var prepared in preparedInputs)
        {
            var searchPath = Path.Combine(prepared.OutputPath, "search.json");
            if (!File.Exists(searchPath))
            {
                logger?.Warn($"project-apidocs: suite search skipped missing file {searchPath}");
                continue;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(searchPath));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var node = JsonNode.Parse(item.GetRawText()) as JsonObject ?? new JsonObject();
                node["suiteEntryId"] = prepared.SuiteEntry.Id;
                node["suiteEntryLabel"] = prepared.SuiteEntry.Label;
                items.Add(node);
            }
        }

        var deduped = items
            .GroupBy(
                static item => item["url"]?.ToString() ?? item["slug"]?.ToString() ?? Guid.NewGuid().ToString("N"),
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static item => item["suiteEntryLabel"]?.ToString(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item["displayName"]?.ToString() ?? item["title"]?.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (deduped.Length == 0)
            return null;

        var payload = new JsonObject
        {
            ["generatedAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["suite"] = BuildProjectApiSuiteMetadataNode(suiteTitle, suiteHomeUrl, suiteHomeLabel, suiteEntries),
            ["itemCount"] = deduped.Length,
            ["items"] = new JsonArray(deduped.Cast<JsonNode?>().ToArray())
        };

        EnsureParentDirectory(suiteSearchPath);
        File.WriteAllText(
            suiteSearchPath,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        return suiteSearchPath;
    }

    private static string? WriteProjectApiSuiteXref(
        IReadOnlyList<ProjectApiDocsPreparedInput> preparedInputs,
        string suiteXrefMapPath,
        WebConsoleLogger? logger)
    {
        var inputs = preparedInputs
            .Select(static prepared => Path.Combine(prepared.OutputPath, "xrefmap.json"))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (inputs.Length == 0)
            return null;

        var options = new WebXrefMergeOptions
        {
            OutputPath = suiteXrefMapPath,
            PreferLast = true,
            Recursive = false
        };
        options.Inputs.AddRange(inputs);
        var merge = WebXrefMapMerger.Merge(options);
        foreach (var warning in merge.Warnings)
            logger?.Warn($"project-apidocs: {warning}");
        return merge.OutputPath;
    }

    private static string? WriteProjectApiSuiteCoverage(
        IReadOnlyList<ProjectApiDocsPreparedInput> preparedInputs,
        string? suiteTitle,
        string? suiteHomeUrl,
        string? suiteHomeLabel,
        IReadOnlyList<WebApiDocsSuiteEntry> suiteEntries,
        string suiteCoveragePath,
        WebConsoleLogger? logger)
    {
        var coverageDocuments = new List<(ProjectApiDocsPreparedInput Prepared, JsonDocument Document)>();
        try
        {
            foreach (var prepared in preparedInputs)
            {
                var coveragePath = Path.Combine(prepared.OutputPath, "coverage.json");
                if (!File.Exists(coveragePath))
                {
                    logger?.Warn($"project-apidocs: suite coverage skipped missing file {coveragePath}");
                    continue;
                }

                coverageDocuments.Add((prepared, JsonDocument.Parse(File.ReadAllText(coveragePath))));
            }

            if (coverageDocuments.Count == 0)
                return null;

            var payload = new JsonObject
            {
                ["generatedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["suite"] = BuildProjectApiSuiteMetadataNode(suiteTitle, suiteHomeUrl, suiteHomeLabel, suiteEntries),
                ["projectCount"] = coverageDocuments.Count,
                ["projects"] = new JsonArray(coverageDocuments.Select(item => BuildProjectCoverageProjectNode(item.Prepared, item.Document.RootElement, suiteCoveragePath)).Cast<JsonNode?>().ToArray()),
                ["types"] = BuildAggregatedCoverageGroup(coverageDocuments.Select(static item => item.Document.RootElement).ToArray(), "types"),
                ["members"] = BuildAggregatedMemberCoverageGroup(coverageDocuments.Select(static item => item.Document.RootElement).ToArray()),
                ["source"] = BuildAggregatedSourceCoverageGroup(coverageDocuments.Select(static item => item.Document.RootElement).ToArray()),
                ["powershell"] = BuildAggregatedPowerShellCoverageGroup(coverageDocuments.Select(static item => item.Document.RootElement).ToArray())
            };

            EnsureParentDirectory(suiteCoveragePath);
            File.WriteAllText(
                suiteCoveragePath,
                JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            return suiteCoveragePath;
        }
        finally
        {
            foreach (var (_, document) in coverageDocuments)
                document.Dispose();
        }
    }

    private static string? WriteProjectApiSuiteRelatedContent(
        IReadOnlyList<ProjectApiDocsPreparedInput> preparedInputs,
        string? suiteTitle,
        string? suiteHomeUrl,
        string? suiteHomeLabel,
        IReadOnlyList<WebApiDocsSuiteEntry> suiteEntries,
        string suiteRelatedContentPath,
        WebConsoleLogger? logger)
    {
        var items = new List<JsonObject>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var prepared in preparedInputs)
        {
            if (prepared.RelatedContentManifestPaths is not { Count: > 0 })
                continue;

            var warnings = new List<string>();
            var entries = WebApiDocsGenerator.LoadRelatedContentManifestEntriesFromPaths(prepared.RelatedContentManifestPaths, warnings);
            foreach (var warning in warnings)
                logger?.Warn($"project-apidocs: {warning}");

            foreach (var entry in entries)
            {
                var title = NormalizeOptionalString(entry.Title);
                var url = NormalizeOptionalString(entry.Url);
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
                    continue;

                var kind = NormalizeOptionalString(entry.Kind) ?? "guide";
                var dedupeKey = string.Join("|", prepared.SuiteEntry.Id, url, title, kind);
                if (!seen.Add(dedupeKey))
                    continue;

                var targets = entry.Targets
                    .Where(static target => !string.IsNullOrWhiteSpace(target))
                    .Select(static target => target.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static target => target, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var node = new JsonObject
                {
                    ["title"] = title,
                    ["url"] = url,
                    ["summary"] = NormalizeOptionalString(entry.Summary),
                    ["kind"] = kind,
                    ["order"] = entry.Order,
                    ["targetCount"] = targets.Length,
                    ["suiteEntryId"] = prepared.SuiteEntry.Id,
                    ["suiteEntryLabel"] = prepared.SuiteEntry.Label
                };
                if (targets.Length > 0)
                    node["targets"] = new JsonArray(targets.Select(static target => (JsonNode?)target).ToArray());

                items.Add(node);
            }
        }

        var ordered = items
            .OrderBy(static item => item["suiteEntryLabel"]?.ToString(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item["order"]?.GetValue<int?>() ?? int.MaxValue)
            .ThenBy(static item => item["title"]?.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (ordered.Length == 0)
            return null;

        var payload = new JsonObject
        {
            ["generatedAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["suite"] = BuildProjectApiSuiteMetadataNode(suiteTitle, suiteHomeUrl, suiteHomeLabel, suiteEntries),
            ["itemCount"] = ordered.Length,
            ["items"] = new JsonArray(ordered.Cast<JsonNode?>().ToArray())
        };

        EnsureParentDirectory(suiteRelatedContentPath);
        File.WriteAllText(
            suiteRelatedContentPath,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        return suiteRelatedContentPath;
    }

    private static string? WriteProjectApiSuiteNarrative(
        IReadOnlyList<string> suiteNarrativeManifestPaths,
        string? suiteTitle,
        string? suiteHomeUrl,
        string? suiteHomeLabel,
        IReadOnlyList<WebApiDocsSuiteEntry> suiteEntries,
        string suiteNarrativePath,
        WebConsoleLogger? logger)
    {
        if (suiteNarrativeManifestPaths is not { Count: > 0 })
            return null;

        var suiteEntryMap = (suiteEntries ?? Array.Empty<WebApiDocsSuiteEntry>())
            .Where(static entry => entry is not null && !string.IsNullOrWhiteSpace(entry.Id))
            .GroupBy(static entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        var sections = new List<ProjectApiSuiteNarrativeSection>();
        string? narrativeSummary = null;
        var sectionOrder = 0;

        foreach (var rawPath in suiteNarrativeManifestPaths)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                continue;

            var fullPath = Path.GetFullPath(rawPath);
            if (!File.Exists(fullPath))
            {
                logger?.Warn($"project-apidocs: suite narrative manifest was not found: {fullPath}");
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(fullPath));
                var root = document.RootElement;
                if (string.IsNullOrWhiteSpace(narrativeSummary))
                {
                    narrativeSummary = NormalizeOptionalString(
                        GetString(root, "summary") ??
                        GetString(root, "description") ??
                        GetString(root, "lead"));
                }

                var sectionElements = EnumerateSuiteNarrativeSectionElements(root);
                foreach (var sectionElement in sectionElements)
                {
                    var section = ParseProjectApiSuiteNarrativeSection(sectionElement, fullPath, sectionOrder, suiteEntryMap, logger);
                    if (section is null || section.Items.Count == 0)
                        continue;

                    AddProjectApiSuiteNarrativeSection(sections, section);
                    sectionOrder++;
                }
            }
            catch (Exception ex)
            {
                logger?.Warn($"project-apidocs: suite narrative manifest '{fullPath}' failed to load ({ex.GetType().Name}: {ex.Message})");
            }
        }

        var orderedSections = sections
            .OrderBy(static section => section.Order)
            .ThenBy(static section => section.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (orderedSections.Length == 0)
            return null;

        var narrativeItems = orderedSections
            .SelectMany(static section => section.Items)
            .ToArray();
        var coveredSuiteEntryIds = narrativeItems
            .SelectMany(static item => item.SuiteEntryIds)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var uncoveredSuiteEntries = (suiteEntries ?? Array.Empty<WebApiDocsSuiteEntry>())
            .Where(entry => entry is not null && !string.IsNullOrWhiteSpace(entry.Id) && !coveredSuiteEntryIds.Contains(entry.Id, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var coveredSuiteEntryCount = (suiteEntries ?? Array.Empty<WebApiDocsSuiteEntry>())
            .Count(entry => entry is not null && !string.IsNullOrWhiteSpace(entry.Id) && coveredSuiteEntryIds.Contains(entry.Id, StringComparer.OrdinalIgnoreCase));
        var totalSuiteEntryCount = (suiteEntries ?? Array.Empty<WebApiDocsSuiteEntry>())
            .Count(entry => entry is not null && !string.IsNullOrWhiteSpace(entry.Id));
        var itemsWithAudience = narrativeItems.Count(static item => !string.IsNullOrWhiteSpace(item.Audience));
        var itemsWithEstimatedTime = narrativeItems.Count(static item => !string.IsNullOrWhiteSpace(item.EstimatedTime));

        var payload = new JsonObject
        {
            ["generatedAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["suite"] = BuildProjectApiSuiteMetadataNode(suiteTitle, suiteHomeUrl, suiteHomeLabel, suiteEntries ?? Array.Empty<WebApiDocsSuiteEntry>()),
            ["summary"] = narrativeSummary,
            ["content"] = new JsonObject
            {
                ["summaryPresent"] = !string.IsNullOrWhiteSpace(narrativeSummary),
                ["itemsWithAudience"] = new JsonObject
                {
                    ["total"] = narrativeItems.Length,
                    ["covered"] = itemsWithAudience,
                    ["percent"] = narrativeItems.Length == 0 ? 0d : Math.Round(itemsWithAudience * 100d / narrativeItems.Length, 2, MidpointRounding.AwayFromZero)
                },
                ["itemsWithEstimatedTime"] = new JsonObject
                {
                    ["total"] = narrativeItems.Length,
                    ["covered"] = itemsWithEstimatedTime,
                    ["percent"] = narrativeItems.Length == 0 ? 0d : Math.Round(itemsWithEstimatedTime * 100d / narrativeItems.Length, 2, MidpointRounding.AwayFromZero)
                }
            },
            ["coverage"] = new JsonObject
            {
                ["suiteEntries"] = new JsonObject
                {
                    ["total"] = totalSuiteEntryCount,
                    ["covered"] = coveredSuiteEntryCount,
                    ["uncoveredCount"] = uncoveredSuiteEntries.Length,
                    ["percent"] = totalSuiteEntryCount == 0 ? 100d : Math.Round(coveredSuiteEntryCount * 100d / totalSuiteEntryCount, 2, MidpointRounding.AwayFromZero),
                    ["coveredIds"] = new JsonArray(coveredSuiteEntryIds.Select(static value => (JsonNode?)value).ToArray()),
                    ["uncoveredIds"] = new JsonArray(uncoveredSuiteEntries.Select(static entry => (JsonNode?)entry.Id).ToArray()),
                    ["uncoveredLabels"] = new JsonArray(uncoveredSuiteEntries.Select(static entry => (JsonNode?)entry.Label).ToArray())
                }
            },
            ["sectionCount"] = orderedSections.Length,
            ["itemCount"] = narrativeItems.Length,
            ["sections"] = new JsonArray(orderedSections.Select(BuildProjectApiSuiteNarrativeSectionNode).Cast<JsonNode?>().ToArray())
        };

        EnsureParentDirectory(suiteNarrativePath);
        File.WriteAllText(
            suiteNarrativePath,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        return suiteNarrativePath;
    }

    private static IReadOnlyList<JsonElement> EnumerateSuiteNarrativeSectionElements(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root.EnumerateArray().ToArray();

        if (root.ValueKind != JsonValueKind.Object)
            return Array.Empty<JsonElement>();

        foreach (var propertyName in new[] { "sections", "items", "entries", "links" })
        {
            if (root.TryGetProperty(propertyName, out var property))
            {
                if (property.ValueKind == JsonValueKind.Array)
                {
                    if (string.Equals(propertyName, "sections", StringComparison.OrdinalIgnoreCase))
                        return property.EnumerateArray().ToArray();

                    return new[] { root };
                }
            }
        }

        return Array.Empty<JsonElement>();
    }

    private static ProjectApiSuiteNarrativeSection? ParseProjectApiSuiteNarrativeSection(
        JsonElement element,
        string sourcePath,
        int order,
        IReadOnlyDictionary<string, WebApiDocsSuiteEntry> suiteEntryMap,
        WebConsoleLogger? logger)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            logger?.Warn($"project-apidocs: suite narrative manifest '{Path.GetFileName(sourcePath)}' contains a non-object section that was ignored.");
            return null;
        }

        var isImplicitSection = !element.TryGetProperty("items", out _) &&
                                !element.TryGetProperty("entries", out _) &&
                                !element.TryGetProperty("links", out _);
        var title = NormalizeOptionalString(GetString(element, "title") ?? GetString(element, "name") ?? GetString(element, "label"));
        var summary = NormalizeOptionalString(GetString(element, "summary") ?? GetString(element, "description"));
        if (string.IsNullOrWhiteSpace(title))
            title = isImplicitSection ? "Start Here" : $"Section {order + 1}";

        var itemElements = isImplicitSection
            ? new[] { element }
            : EnumerateSuiteNarrativeItemElements(element);
        var items = new List<ProjectApiSuiteNarrativeItem>();
        var itemOrder = 0;
        foreach (var itemElement in itemElements)
        {
            var item = ParseProjectApiSuiteNarrativeItem(itemElement, sourcePath, itemOrder, suiteEntryMap, logger);
            if (item is null)
                continue;

            items.Add(item);
            itemOrder++;
        }

        if (items.Count == 0)
            return null;

        var id = NormalizeOptionalString(GetString(element, "id")) ?? NormalizeSlug(title) ?? $"section-{order + 1}";
        return new ProjectApiSuiteNarrativeSection
        {
            Id = id,
            Title = title,
            Summary = summary,
            Order = GetInt(element, "order") ?? order,
            Items = items
                .OrderBy(static item => item.Order)
                .ThenBy(static item => item.Title, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static IReadOnlyList<JsonElement> EnumerateSuiteNarrativeItemElements(JsonElement section)
    {
        foreach (var propertyName in new[] { "items", "entries", "links" })
        {
            if (section.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Array)
                return property.EnumerateArray().ToArray();
        }

        return Array.Empty<JsonElement>();
    }

    private static ProjectApiSuiteNarrativeItem? ParseProjectApiSuiteNarrativeItem(
        JsonElement element,
        string sourcePath,
        int order,
        IReadOnlyDictionary<string, WebApiDocsSuiteEntry> suiteEntryMap,
        WebConsoleLogger? logger)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            logger?.Warn($"project-apidocs: suite narrative manifest '{Path.GetFileName(sourcePath)}' contains a non-object item that was ignored.");
            return null;
        }

        var title = NormalizeOptionalString(GetString(element, "title") ?? GetString(element, "name") ?? GetString(element, "label"));
        var url = NormalizeOptionalString(GetString(element, "url") ?? GetString(element, "href") ?? GetString(element, "link"));
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
        {
            logger?.Warn($"project-apidocs: suite narrative manifest '{Path.GetFileName(sourcePath)}' contains an item missing title/url and it was ignored.");
            return null;
        }

        var suiteEntryIds = ResolveSuiteNarrativeEntryIds(element, suiteEntryMap, logger, sourcePath);
        return new ProjectApiSuiteNarrativeItem
        {
            Title = title,
            Url = url,
            Summary = NormalizeOptionalString(GetString(element, "summary") ?? GetString(element, "description")),
            Kind = NormalizeOptionalString(GetString(element, "kind") ?? GetString(element, "type")) ?? "guide",
            Audience = NormalizeOptionalString(GetString(element, "audience")),
            EstimatedTime = NormalizeOptionalString(GetString(element, "estimatedTime") ?? GetString(element, "estimated-time") ?? GetString(element, "duration")),
            Order = GetInt(element, "order") ?? order,
            SuiteEntryIds = suiteEntryIds.ToArray(),
            SuiteEntryLabels = suiteEntryIds
                .Where(suiteEntryMap.ContainsKey)
                .Select(id => suiteEntryMap[id].Label)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static IReadOnlyList<string> ResolveSuiteNarrativeEntryIds(
        JsonElement element,
        IReadOnlyDictionary<string, WebApiDocsSuiteEntry> suiteEntryMap,
        WebConsoleLogger? logger,
        string sourcePath)
    {
        var ids = new List<string>();

        var scalar = NormalizeOptionalString(
            GetString(element, "suiteEntryId") ??
            GetString(element, "suite-entry-id") ??
            GetString(element, "project"));
        if (!string.IsNullOrWhiteSpace(scalar))
            ids.Add(scalar);

        var arrays = new[]
        {
            GetArrayOfStrings(element, "suiteEntryIds"),
            GetArrayOfStrings(element, "suite-entry-ids"),
            GetArrayOfStrings(element, "suiteEntries"),
            GetArrayOfStrings(element, "suite-entries"),
            GetArrayOfStrings(element, "projects")
        };
        foreach (var array in arrays)
        {
            if (array is null)
                continue;

            ids.AddRange(array.Where(static value => !string.IsNullOrWhiteSpace(value)));
        }

        var resolved = ids
            .Select(static id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var id in resolved)
        {
            if (!suiteEntryMap.ContainsKey(id))
                logger?.Warn($"project-apidocs: suite narrative manifest '{Path.GetFileName(sourcePath)}' references unknown suite entry '{id}'.");
        }

        return resolved;
    }

    private static void AddProjectApiSuiteNarrativeSection(
        List<ProjectApiSuiteNarrativeSection> sections,
        ProjectApiSuiteNarrativeSection section)
    {
        var existing = sections.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, section.Id, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Title, section.Title, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            sections.Add(section);
            return;
        }

        if (string.IsNullOrWhiteSpace(existing.Summary) && !string.IsNullOrWhiteSpace(section.Summary))
            existing.Summary = section.Summary;
        existing.Order = Math.Min(existing.Order, section.Order);
        foreach (var item in section.Items)
        {
            var duplicate = existing.Items.Any(existingItem =>
                string.Equals(existingItem.Title, item.Title, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existingItem.Url, item.Url, StringComparison.OrdinalIgnoreCase));
            if (!duplicate)
                existing.Items.Add(item);
        }
    }

    private static JsonObject BuildProjectApiSuiteNarrativeSectionNode(ProjectApiSuiteNarrativeSection section)
    {
        return new JsonObject
        {
            ["id"] = section.Id,
            ["title"] = section.Title,
            ["summary"] = section.Summary,
            ["order"] = section.Order,
            ["itemCount"] = section.Items.Count,
            ["items"] = new JsonArray(section.Items
                .OrderBy(static item => item.Order)
                .ThenBy(static item => item.Title, StringComparer.OrdinalIgnoreCase)
                .Select(static item => (JsonNode?)new JsonObject
                {
                    ["title"] = item.Title,
                    ["url"] = item.Url,
                    ["summary"] = item.Summary,
                    ["kind"] = item.Kind,
                    ["audience"] = item.Audience,
                    ["estimatedTime"] = item.EstimatedTime,
                    ["order"] = item.Order,
                    ["suiteEntryIds"] = new JsonArray(item.SuiteEntryIds.Select(static value => (JsonNode?)value).ToArray()),
                    ["suiteEntryLabels"] = new JsonArray(item.SuiteEntryLabels.Select(static value => (JsonNode?)value).ToArray())
                })
                .ToArray())
        };
    }

    private static JsonObject BuildProjectApiSuiteMetadataNode(
        string? suiteTitle,
        string? suiteHomeUrl,
        string? suiteHomeLabel,
        IReadOnlyList<WebApiDocsSuiteEntry> suiteEntries)
    {
        return new JsonObject
        {
            ["title"] = suiteTitle,
            ["homeUrl"] = suiteHomeUrl,
            ["homeLabel"] = suiteHomeLabel,
            ["entryCount"] = suiteEntries?.Count ?? 0,
            ["entries"] = BuildApiSuiteEntriesNode(suiteEntries ?? Array.Empty<WebApiDocsSuiteEntry>())
        };
    }

    private static JsonObject BuildProjectCoverageProjectNode(ProjectApiDocsPreparedInput prepared, JsonElement root, string suiteCoveragePath)
    {
        var projectCoveragePath = Path.Combine(prepared.OutputPath, "coverage.json");
        return new JsonObject
        {
            ["id"] = prepared.SuiteEntry.Id,
            ["label"] = prepared.SuiteEntry.Label,
            ["coveragePath"] = BuildSuiteArtifactRelativePath(suiteCoveragePath, projectCoveragePath),
            ["typeCount"] = ReadIntByPath(root, "types.count"),
            ["memberCount"] = ReadIntByPath(root, "members.count"),
            ["commandCount"] = ReadIntByPath(root, "powershell.commandCount")
        };
    }

    private static JsonObject BuildAggregatedCoverageGroup(IReadOnlyList<JsonElement> roots, string groupName)
    {
        if (string.Equals(groupName, "types", StringComparison.OrdinalIgnoreCase))
        {
            var missingQuickStart = MergeStringListByPath(roots, 100, "types.quickStartMissingRelatedContent.types");
            return new JsonObject
            {
                ["count"] = SumIntByPath(roots, "types.count"),
                ["summary"] = BuildMergedCoveragePercent(roots, "types.summary"),
                ["remarks"] = BuildMergedCoveragePercent(roots, "types.remarks"),
                ["codeExamples"] = BuildMergedCoveragePercent(roots, "types.codeExamples"),
                ["relatedContent"] = BuildMergedCoveragePercent(roots, "types.relatedContent"),
                ["quickStartRelatedContent"] = BuildMergedCoveragePercent(roots, "types.quickStartRelatedContent"),
                ["quickStartMissingRelatedContent"] = new JsonObject
                {
                    ["count"] = SumIntByPath(roots, "types.quickStartMissingRelatedContent.count"),
                    ["types"] = new JsonArray(missingQuickStart.Select(static item => (JsonNode?)item).ToArray())
                }
            };
        }

        return new JsonObject();
    }

    private static JsonObject BuildAggregatedMemberCoverageGroup(IReadOnlyList<JsonElement> roots)
    {
        return new JsonObject
        {
            ["count"] = SumIntByPath(roots, "members.count"),
            ["summary"] = BuildMergedCoveragePercent(roots, "members.summary"),
            ["codeExamples"] = BuildMergedCoveragePercent(roots, "members.codeExamples"),
            ["relatedContent"] = BuildMergedCoveragePercent(roots, "members.relatedContent")
        };
    }

    private static JsonObject BuildAggregatedSourceCoverageGroup(IReadOnlyList<JsonElement> roots)
    {
        return new JsonObject
        {
            ["types"] = BuildMergedSourceCoverageNode(roots, "source.types"),
            ["members"] = BuildMergedSourceCoverageNode(roots, "source.members"),
            ["powershell"] = BuildMergedSourceCoverageNode(roots, "source.powershell")
        };
    }

    private static JsonObject BuildAggregatedPowerShellCoverageGroup(IReadOnlyList<JsonElement> roots)
    {
        return new JsonObject
        {
            ["commandCount"] = SumIntByPath(roots, "powershell.commandCount"),
            ["summary"] = BuildMergedCoveragePercent(roots, "powershell.summary"),
            ["remarks"] = BuildMergedCoveragePercent(roots, "powershell.remarks"),
            ["codeExamples"] = BuildMergedCoveragePercent(roots, "powershell.codeExamples"),
            ["authoredHelpCodeExamples"] = BuildMergedCoveragePercent(roots, "powershell.authoredHelpCodeExamples"),
            ["importedScriptCodeExamples"] = BuildMergedCoveragePercent(roots, "powershell.importedScriptCodeExamples"),
            ["generatedFallbackCodeExamples"] = BuildMergedCoveragePercent(roots, "powershell.generatedFallbackCodeExamples"),
            ["generatedFallbackOnlyExamples"] = BuildMergedCoveragePercent(roots, "powershell.generatedFallbackOnlyExamples"),
            ["importedScriptPlaybackMedia"] = BuildMergedCoveragePercent(roots, "powershell.importedScriptPlaybackMedia"),
            ["importedScriptPlaybackMediaWithPoster"] = BuildMergedCoveragePercent(roots, "powershell.importedScriptPlaybackMediaWithPoster"),
            ["importedScriptPlaybackMediaWithoutPoster"] = BuildMergedCoveragePercent(roots, "powershell.importedScriptPlaybackMediaWithoutPoster"),
            ["importedScriptPlaybackMediaUnsupportedSidecars"] = BuildMergedCoveragePercent(roots, "powershell.importedScriptPlaybackMediaUnsupportedSidecars"),
            ["importedScriptPlaybackMediaOversizedAssets"] = BuildMergedCoveragePercent(roots, "powershell.importedScriptPlaybackMediaOversizedAssets"),
            ["importedScriptPlaybackMediaStaleAssets"] = BuildMergedCoveragePercent(roots, "powershell.importedScriptPlaybackMediaStaleAssets"),
            ["parameters"] = BuildMergedCoveragePercent(roots, "powershell.parameters"),
            ["commandsMissingCodeExamples"] = BuildJsonArray(MergeStringListByPath(roots, 100, "powershell.commandsMissingCodeExamples")),
            ["commandsUsingAuthoredHelpCodeExamples"] = BuildJsonArray(MergeStringListByPath(roots, 100, "powershell.commandsUsingAuthoredHelpCodeExamples")),
            ["commandsUsingImportedScriptCodeExamples"] = BuildJsonArray(MergeStringListByPath(roots, 100, "powershell.commandsUsingImportedScriptCodeExamples")),
            ["commandsUsingGeneratedFallbackCodeExamples"] = BuildJsonArray(MergeStringListByPath(roots, 100, "powershell.commandsUsingGeneratedFallbackCodeExamples")),
            ["commandsUsingGeneratedFallbackOnlyExamples"] = BuildJsonArray(MergeStringListByPath(roots, 100, "powershell.commandsUsingGeneratedFallbackOnlyExamples")),
            ["commandsUsingImportedScriptPlaybackMedia"] = BuildJsonArray(MergeStringListByPath(roots, 100, "powershell.commandsUsingImportedScriptPlaybackMedia")),
            ["commandsUsingImportedScriptPlaybackMediaWithoutPoster"] = BuildJsonArray(MergeStringListByPath(roots, 100, "powershell.commandsUsingImportedScriptPlaybackMediaWithoutPoster")),
            ["commandsUsingImportedScriptPlaybackMediaUnsupportedSidecars"] = BuildJsonArray(MergeStringListByPath(roots, 100, "powershell.commandsUsingImportedScriptPlaybackMediaUnsupportedSidecars")),
            ["commandsUsingImportedScriptPlaybackMediaOversizedAssets"] = BuildJsonArray(MergeStringListByPath(roots, 100, "powershell.commandsUsingImportedScriptPlaybackMediaOversizedAssets")),
            ["commandsUsingImportedScriptPlaybackMediaStaleAssets"] = BuildJsonArray(MergeStringListByPath(roots, 100, "powershell.commandsUsingImportedScriptPlaybackMediaStaleAssets"))
        };
    }

    private static JsonObject BuildMergedSourceCoverageNode(IReadOnlyList<JsonElement> roots, string basePath)
    {
        return new JsonObject
        {
            ["count"] = SumIntByPath(roots, $"{basePath}.count"),
            ["path"] = BuildMergedCoveragePercent(roots, $"{basePath}.path"),
            ["urlPresent"] = BuildMergedCoveragePercent(roots, $"{basePath}.urlPresent"),
            ["url"] = BuildMergedCoveragePercent(roots, $"{basePath}.url"),
            ["invalidUrl"] = BuildCountAndSamplesNode(roots, $"{basePath}.invalidUrl"),
            ["unresolvedTemplateToken"] = BuildCountAndSamplesNode(roots, $"{basePath}.unresolvedTemplateToken"),
            ["repoMismatchHints"] = BuildCountAndSamplesNode(roots, $"{basePath}.repoMismatchHints")
        };
    }

    private static JsonObject BuildMergedCoveragePercent(IReadOnlyList<JsonElement> roots, string basePath)
    {
        var covered = SumIntByPath(roots, $"{basePath}.covered");
        var total = SumIntByPath(roots, $"{basePath}.total");
        var percent = total <= 0 ? 100d : Math.Round((covered * 100d) / total, 2, MidpointRounding.AwayFromZero);
        return new JsonObject
        {
            ["covered"] = covered,
            ["total"] = total,
            ["percent"] = percent
        };
    }

    private static JsonObject BuildCountAndSamplesNode(IReadOnlyList<JsonElement> roots, string basePath)
    {
        var samples = MergeStringListByPath(roots, 24, $"{basePath}.samples");
        return new JsonObject
        {
            ["count"] = SumIntByPath(roots, $"{basePath}.count"),
            ["samples"] = BuildJsonArray(samples)
        };
    }

    private static int SumIntByPath(IReadOnlyList<JsonElement> roots, string path)
        => roots.Sum(root => ReadIntByPath(root, path));

    private static int ReadIntByPath(JsonElement root, string path)
    {
        if (!TryGetJsonElementByPath(root, path, out var value))
            return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var intValue))
            return intValue;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
            return intValue;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var doubleValue))
            return (int)Math.Round(doubleValue, MidpointRounding.AwayFromZero);
        return 0;
    }

    private static bool TryGetJsonBoolByPath(JsonElement root, string path, out bool value)
    {
        value = false;
        if (!TryGetJsonElementByPath(root, path, out var element))
            return false;
        if (element.ValueKind == JsonValueKind.True)
        {
            value = true;
            return true;
        }
        if (element.ValueKind == JsonValueKind.False)
        {
            value = false;
            return true;
        }
        if (element.ValueKind == JsonValueKind.String && bool.TryParse(element.GetString(), out value))
            return true;
        return false;
    }

    private static string[] MergeStringListByPath(IReadOnlyList<JsonElement> roots, int limit, string path)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (!TryGetJsonElementByPath(root, path, out var element) || element.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in element.EnumerateArray())
            {
                var value = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
                if (string.IsNullOrWhiteSpace(value))
                    continue;
                values.Add(value.Trim());
            }
        }

        return values
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();
    }

    private static JsonArray BuildJsonArray(IReadOnlyList<string> values)
        => new(values.Select(static value => (JsonNode?)value).ToArray());

    private static bool TryGetJsonElementByPath(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(part, out value))
                return false;
        }

        return true;
    }

    private static void EnsureParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }

    private static string? BuildSuiteArtifactRelativePath(string manifestPath, string? artifactPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || string.IsNullOrWhiteSpace(artifactPath))
            return null;

        var manifestDirectory = Path.GetDirectoryName(manifestPath);
        if (string.IsNullOrWhiteSpace(manifestDirectory))
            return null;

        var relative = Path.GetRelativePath(manifestDirectory, artifactPath)
            .Replace('\\', '/');
        if (!relative.StartsWith(".", StringComparison.Ordinal))
            relative = "./" + relative;
        return relative;
    }

    private static string? BuildRelativeApiArtifactUrl(string apiOutputPath, string? artifactPath)
    {
        if (string.IsNullOrWhiteSpace(apiOutputPath) || string.IsNullOrWhiteSpace(artifactPath))
            return null;

        var relative = Path.GetRelativePath(apiOutputPath, artifactPath)
            .Replace('\\', '/');
        if (!relative.StartsWith(".", StringComparison.Ordinal))
            relative = "./" + relative;
        return relative;
    }

    private static string BuildSuiteArtifactsSummary(ProjectApiSuiteArtifacts? artifacts)
    {
        if (artifacts is null)
            return string.Empty;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(artifacts.SearchPath))
            parts.Add($"suite-search={Path.GetFileName(artifacts.SearchPath)}");
        if (!string.IsNullOrWhiteSpace(artifacts.XrefMapPath))
            parts.Add($"suite-xref={Path.GetFileName(artifacts.XrefMapPath)}");
        if (!string.IsNullOrWhiteSpace(artifacts.CoveragePath))
            parts.Add($"suite-coverage={Path.GetFileName(artifacts.CoveragePath)}");
        if (!string.IsNullOrWhiteSpace(artifacts.RelatedContentPath))
            parts.Add($"suite-guides={Path.GetFileName(artifacts.RelatedContentPath)}");
        if (!string.IsNullOrWhiteSpace(artifacts.NarrativePath))
            parts.Add($"suite-narrative={Path.GetFileName(artifacts.NarrativePath)}");
        return parts.Count == 0 ? string.Empty : "; " + string.Join("; ", parts);
    }

    private static JsonObject BuildProjectApiTemplateTokens(
        ProjectCatalogEntry project,
        string slug,
        string name,
        string overviewUrl,
        string docsUrl,
        string apiUrl,
        string examplesUrl)
    {
        var sourceUrl = NormalizeOptionalString(GetProjectLink(project, "source"));
        if (string.IsNullOrWhiteSpace(sourceUrl) && !string.IsNullOrWhiteSpace(project.GitHubRepo))
            sourceUrl = $"https://github.com/{project.GitHubRepo.Trim()}";

        var websiteUrl = NormalizeOptionalString(project.ExternalUrl) ??
                         NormalizeOptionalString(GetProjectLink(project, "website"));
        var releasesUrl = NormalizeOptionalString(GetProjectLink(project, "releases")) ??
                          NormalizeOptionalString(project.Metrics?.Release?.LatestUrl);
        var changelogUrl = NormalizeOptionalString(GetProjectLink(project, "changelog"));
        if (IsDefaultGitHubChangelogLink(changelogUrl, project.GitHubRepo))
            changelogUrl = null;
        var downloadsSurfaceVisible = TryGetProjectSurfaceValue(project.Surfaces, "downloads") ?? true;
        var downloadsUrl = NormalizeOptionalString(GetProjectLink(project, "downloads")) ??
                           NormalizeOptionalString(GetProjectLink(project, "powerShellGallery")) ??
                           NormalizeOptionalString(project.Metrics?.PowerShellGallery?.GalleryUrl) ??
                           NormalizeOptionalString(project.Metrics?.NuGet?.PackageUrl);
        if (!downloadsSurfaceVisible)
            downloadsUrl = null;

        var docsVisible = (TryGetProjectSurfaceValue(project.Surfaces, "docs") ?? false);
        var examplesVisible = (TryGetProjectSurfaceValue(project.Surfaces, "examples") ?? false);
        var safeDocsUrl = docsVisible ? docsUrl : overviewUrl;
        var safeExamplesUrl = examplesVisible ? examplesUrl : overviewUrl;
        var description = ResolveProjectApiSummary(project, name);
        var stars = FormatProjectMetric(project.Metrics?.GitHub?.Stars);
        var forks = FormatProjectMetric(project.Metrics?.GitHub?.Forks);
        var openIssues = FormatProjectMetric(project.Metrics?.GitHub?.OpenIssues);
        var (downloadsLabel, downloadsValue) = ResolveProjectDownloadsPresentation(project);
        var downloads = downloadsSurfaceVisible ? FormatProjectMetric(downloadsValue) : null;
        var release = NormalizeOptionalString(project.Metrics?.Release?.LatestTag) ?? NormalizeOptionalString(project.Version);
        var language = NormalizeOptionalString(project.Metrics?.GitHub?.Language);
        var lastPush = FormatProjectApiDate(project.Metrics?.GitHub?.LastPushedAt);
        var metadataRefresh = FormatProjectApiDate(project.ManifestGeneratedAt);
        var manifestCommit = ShortenProjectCommit(project.ManifestCommit);

        return new JsonObject
        {
            ["PROJECT_SLUG"] = EncodeToken(slug),
            ["PROJECT_KIND_LABEL"] = ResolveProjectKindLabel(project),
            ["PROJECT_NAME"] = EncodeToken(name),
            ["PROJECT_DESCRIPTION"] = EncodeToken(description),
            ["PROJECT_DESCRIPTION_HIDDEN"] = BuildHiddenAttribute(description),
            ["PROJECT_SOURCE_URL"] = EncodeHrefToken(sourceUrl),
            ["PROJECT_SOURCE_HIDDEN"] = BuildHiddenAttribute(sourceUrl),
            ["PROJECT_WEBSITE_URL"] = EncodeHrefToken(websiteUrl),
            ["PROJECT_WEBSITE_HIDDEN"] = BuildHiddenAttribute(websiteUrl),
            ["PROJECT_RELEASES_URL"] = EncodeHrefToken(releasesUrl),
            ["PROJECT_RELEASES_HIDDEN"] = BuildHiddenAttribute(releasesUrl),
            ["PROJECT_CHANGELOG_URL"] = EncodeHrefToken(changelogUrl),
            ["PROJECT_CHANGELOG_HIDDEN"] = BuildHiddenAttribute(changelogUrl),
            ["PROJECT_DOWNLOADS_URL"] = EncodeHrefToken(downloadsUrl),
            ["PROJECT_DOWNLOADS_ACTION_HIDDEN"] = BuildHiddenAttribute(downloadsUrl),
            ["PROJECT_DOWNLOADS_TAB_HIDDEN"] = BuildHiddenAttribute(downloadsUrl),
            ["PROJECT_STARS"] = EncodeToken(stars),
            ["PROJECT_STARS_HIDDEN"] = BuildHiddenAttribute(stars),
            ["PROJECT_FORKS"] = EncodeToken(forks),
            ["PROJECT_FORKS_HIDDEN"] = BuildHiddenAttribute(forks),
            ["PROJECT_OPEN_ISSUES"] = EncodeToken(openIssues),
            ["PROJECT_OPEN_ISSUES_HIDDEN"] = BuildHiddenAttribute(openIssues),
            ["PROJECT_DOWNLOADS_LABEL"] = EncodeToken(downloadsLabel ?? "Downloads"),
            ["PROJECT_DOWNLOADS"] = EncodeToken(downloads),
            ["PROJECT_DOWNLOADS_METRIC_HIDDEN"] = BuildHiddenAttribute(downloads),
            ["PROJECT_DOWNLOADS_HIDDEN"] = BuildHiddenAttribute(downloadsUrl),
            ["PROJECT_RELEASE"] = EncodeToken(release),
            ["PROJECT_RELEASE_HIDDEN"] = BuildHiddenAttribute(release),
            ["PROJECT_LANGUAGE"] = EncodeToken(language),
            ["PROJECT_LANGUAGE_HIDDEN"] = BuildHiddenAttribute(language),
            ["PROJECT_LAST_PUSH"] = EncodeToken(lastPush),
            ["PROJECT_LAST_PUSH_HIDDEN"] = BuildHiddenAttribute(lastPush),
            ["PROJECT_METADATA_REFRESH"] = EncodeToken(metadataRefresh),
            ["PROJECT_METADATA_REFRESH_HIDDEN"] = BuildHiddenAttribute(metadataRefresh),
            ["PROJECT_MANIFEST_COMMIT"] = EncodeToken(manifestCommit),
            ["PROJECT_MANIFEST_COMMIT_HIDDEN"] = BuildHiddenAttribute(manifestCommit),
            ["PROJECT_OVERVIEW_URL"] = EncodeHrefToken(overviewUrl),
            ["PROJECT_DOCS_URL"] = EncodeHrefToken(safeDocsUrl),
            ["PROJECT_DOCS_HIDDEN"] = docsVisible ? string.Empty : " hidden",
            ["PROJECT_API_URL"] = EncodeHrefToken(apiUrl),
            ["PROJECT_EXAMPLES_URL"] = EncodeHrefToken(safeExamplesUrl),
            ["PROJECT_EXAMPLES_HIDDEN"] = examplesVisible ? string.Empty : " hidden"
        };
    }

    private static string ResolveProjectKindLabel(ProjectCatalogEntry project)
    {
        var status = NormalizeOptionalString(project.Status)?.ToLowerInvariant();
        if (string.Equals(status, "deprecated", StringComparison.OrdinalIgnoreCase))
            return "Deprecated";
        if (string.Equals(status, "archived", StringComparison.OrdinalIgnoreCase))
            return "Archived";
        return "Project";
    }

    private static string? ResolveProjectApiSummary(ProjectCatalogEntry project, string name)
    {
        var description = NormalizeOptionalString(project.Description);
        if (!IsGenericProjectArtifactDescription(description))
            return description;

        var routeName = string.IsNullOrWhiteSpace(name) ? "This project" : name.Trim();
        var language = NormalizeOptionalString(project.Metrics?.GitHub?.Language);
        var hasPowerShell = !string.IsNullOrWhiteSpace(GetProjectLink(project, "powerShellGallery")) ||
                            !string.IsNullOrWhiteSpace(project.Metrics?.PowerShellGallery?.GalleryUrl) ||
                            (TryGetProjectSurfaceValue(project.Surfaces, "apiPowerShell") ?? false) ||
                            string.Equals(language, "PowerShell", StringComparison.OrdinalIgnoreCase);
        var hasDotNet = !string.IsNullOrWhiteSpace(project.Metrics?.NuGet?.PackageUrl) ||
                        (TryGetProjectSurfaceValue(project.Surfaces, "apiDotNet") ?? false) ||
                        string.Equals(language, "C#", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(language, "F#", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(language, "VB", StringComparison.OrdinalIgnoreCase);

        if (hasPowerShell && hasDotNet)
            return $"{routeName} is an open-source PowerShell and .NET project with packages, release history, and technical documentation.";
        if (hasPowerShell)
            return $"{routeName} is an open-source PowerShell project with packages, release history, and working documentation.";
        if (hasDotNet)
            return $"{routeName} is an open-source .NET project with packages, release history, and project documentation.";

        return $"{routeName} is an open-source project with releases, documentation, and implementation-ready resources.";
    }

    private static bool IsGenericProjectArtifactDescription(string? description)
    {
        return string.IsNullOrWhiteSpace(description) ||
               // Older generated manifests used this placeholder; treat it as "missing"
               // so public pages get a useful project-specific summary.
               description.Contains("website artifacts for the Evotec multi-project hub", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetProjectLink(ProjectCatalogEntry project, string key)
    {
        if (project.Links is null || string.IsNullOrWhiteSpace(key))
            return null;

        foreach (var pair in project.Links)
        {
            if (pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                return NormalizeOptionalString(pair.Value);
        }

        return null;
    }

    private static string? ResolveProjectDownloadsValue(ProjectCatalogEntry project)
    {
        if (project.Metrics?.Downloads?.Total > 0)
            return project.Metrics.Downloads.Total.ToString();
        if (project.Metrics?.PowerShellGallery?.TotalDownloads > 0)
            return project.Metrics.PowerShellGallery.TotalDownloads.ToString();
        if (project.Metrics?.NuGet?.TotalDownloads > 0)
            return project.Metrics.NuGet.TotalDownloads.ToString();
        return null;
    }

    private static (string Label, long? Value) ResolveProjectDownloadsPresentation(ProjectCatalogEntry project)
    {
        var total = project.Metrics?.Downloads?.Total > 0 ? project.Metrics.Downloads.Total : 0;
        var powerShellGallery = project.Metrics?.PowerShellGallery?.TotalDownloads > 0 ? project.Metrics.PowerShellGallery.TotalDownloads : 0;
        var nuGet = project.Metrics?.NuGet?.TotalDownloads > 0 ? project.Metrics.NuGet.TotalDownloads : 0;

        if (total > 0 && powerShellGallery > 0 && nuGet == 0 && total == powerShellGallery)
            return ("PowerShell Gallery downloads", powerShellGallery);
        if (total > 0 && nuGet > 0 && powerShellGallery == 0 && total == nuGet)
            return ("NuGet downloads", nuGet);
        if (powerShellGallery > 0 && nuGet == 0)
            return ("PowerShell Gallery downloads", powerShellGallery);
        if (nuGet > 0 && powerShellGallery == 0)
            return ("NuGet downloads", nuGet);
        if (total > 0)
            return ("Total downloads", total);

        return ("Downloads", null);
    }

    private static string? FormatProjectMetric(long? value)
    {
        if (!value.HasValue || value.Value <= 0)
            return null;

        return value.Value.ToString("N0", CultureInfo.InvariantCulture);
    }

    private static string? FormatProjectMetric(int? value)
    {
        return !value.HasValue ? null : FormatProjectMetric((long)value.Value);
    }

    private static string? FormatProjectApiDate(string? value)
    {
        value = NormalizeOptionalString(value);
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return value;
    }

    private static string? ShortenProjectCommit(string? value)
    {
        value = NormalizeOptionalString(value);
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Length <= 8 ? value : value[..8];
    }

    private static string EnsureProjectRouteTrailingSlash(string path)
    {
        var normalized = NormalizeOptionalString(path) ?? "/";
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = "/" + normalized;
        return normalized.EndsWith("/", StringComparison.Ordinal) ? normalized : normalized + "/";
    }

    private static string ResolveProjectApiCollectionUrl(
        SiteSpec? siteSpec,
        string? collectionName,
        string route,
        string? currentLanguage)
    {
        var localizedRoute = ResolveProjectApiCollectionRelativeUrl(siteSpec, collectionName, route, currentLanguage);
        if (siteSpec is null || ProjectApiCollectionSupportsLanguage(siteSpec, collectionName, currentLanguage))
            return localizedRoute;

        var fallbackLanguage = ResolveProjectApiLanguageCode(siteSpec, siteSpec.Localization?.DefaultLanguage);
        var fallbackRoute = ResolveProjectApiCollectionRelativeUrl(siteSpec, collectionName, route, fallbackLanguage);
        return ResolveProjectApiAbsoluteUrl(siteSpec, fallbackLanguage, fallbackRoute);
    }

    private static string ResolveProjectApiCollectionRelativeUrl(
        SiteSpec? siteSpec,
        string? collectionName,
        string route,
        string? targetLanguage)
    {
        var normalizedRoute = EnsureProjectRouteTrailingSlash(route);
        if (siteSpec is null || IsAbsoluteHttpUrl(normalizedRoute))
            return normalizedRoute;

        var localization = siteSpec.Localization;
        if (localization?.Enabled != true || localization.Languages is not { Length: > 0 })
            return normalizedRoute;

        var language = ResolveProjectApiLanguageSpec(siteSpec, targetLanguage);
        if (language is null)
            return normalizedRoute;

        var isDefaultLanguage = language.Default ||
                                string.Equals(language.Code, localization.DefaultLanguage, StringComparison.OrdinalIgnoreCase);
        if (language.RenderAtRoot || (isDefaultLanguage && !localization.PrefixDefaultLanguage))
            return normalizedRoute;

        var prefix = NormalizeOptionalString(language.Prefix) ?? language.Code;
        if (string.IsNullOrWhiteSpace(prefix))
            return normalizedRoute;

        var combined = "/" + prefix.Trim('/') + "/" + normalizedRoute.TrimStart('/');
        return EnsureProjectRouteTrailingSlash(combined);
    }

    private static string ResolveProjectApiAbsoluteUrl(SiteSpec siteSpec, string? targetLanguage, string route)
    {
        if (IsAbsoluteHttpUrl(route))
            return route;

        var baseUrl = NormalizeOptionalString(ResolveProjectApiLanguageSpec(siteSpec, targetLanguage)?.BaseUrl) ??
                      NormalizeOptionalString(siteSpec.BaseUrl);
        if (string.IsNullOrWhiteSpace(baseUrl))
            return route;

        var normalizedBaseUrl = baseUrl.TrimEnd('/') + "/";
        var normalizedRoute = EnsureProjectRouteTrailingSlash(route).TrimStart('/');
        return new Uri(new Uri(normalizedBaseUrl, UriKind.Absolute), normalizedRoute).ToString();
    }

    private static string? ResolveProjectApiSiteBaseUrl(SiteSpec? siteSpec, string? targetLanguage)
    {
        if (siteSpec is null)
            return null;

        return NormalizeOptionalString(ResolveProjectApiLanguageSpec(siteSpec, targetLanguage)?.BaseUrl) ??
               NormalizeOptionalString(siteSpec.BaseUrl);
    }

    private static bool ProjectApiCollectionSupportsLanguage(SiteSpec siteSpec, string? collectionName, string? languageCode)
    {
        var collection = ResolveProjectApiCollectionSpec(siteSpec, collectionName);
        if (collection is null)
            return true;

        var localization = siteSpec.Localization;
        if (localization?.Enabled != true || localization.Languages is not { Length: > 0 })
            return true;

        var configuredLanguages = collection.LocalizedLanguages is { Length: > 0 }
            ? collection.LocalizedLanguages
            : collection.ExpectedTranslationLanguages;
        if (configuredLanguages is not { Length: > 0 })
            return true;

        var normalizedLanguage = ResolveProjectApiLanguageCode(siteSpec, languageCode);
        return configuredLanguages
            .Select(static value => NormalizeOptionalString(value))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Contains(normalizedLanguage, StringComparer.OrdinalIgnoreCase);
    }

    private static CollectionSpec? ResolveProjectApiCollectionSpec(SiteSpec siteSpec, string? collectionName)
    {
        if (siteSpec.Collections is not { Length: > 0 } || string.IsNullOrWhiteSpace(collectionName))
            return null;

        return siteSpec.Collections.FirstOrDefault(collection =>
            string.Equals(collection.Name, collectionName, StringComparison.OrdinalIgnoreCase));
    }

    private static LanguageSpec? ResolveProjectApiLanguageSpec(SiteSpec siteSpec, string? languageCode)
    {
        var localization = siteSpec.Localization;
        if (localization?.Languages is not { Length: > 0 })
            return null;

        var normalizedCode = ResolveProjectApiLanguageCode(siteSpec, languageCode);
        return localization.Languages.FirstOrDefault(language =>
                   string.Equals(language.Code, normalizedCode, StringComparison.OrdinalIgnoreCase))
               ?? localization.Languages.FirstOrDefault(language => language.Default)
               ?? localization.Languages.FirstOrDefault();
    }

    private static string ResolveProjectApiLanguageCode(SiteSpec siteSpec, string? languageCode)
    {
        return NormalizeOptionalString(languageCode) ??
               NormalizeOptionalString(siteSpec.Localization?.DefaultLanguage) ??
               "en";
    }

    private static bool IsAbsoluteHttpUrl(string value)
    {
        return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    private static string EncodeToken(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : WebUtility.HtmlEncode(value.Trim());
    }

    private static string EncodeHrefToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "#";

        return WebUtility.HtmlEncode(value.Trim());
    }

    private static string BuildHiddenAttribute(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? " hidden" : string.Empty;
    }

    private static string? ResolveProjectApiCssHref(JsonElement step, string baseDir, WebConsoleLogger? logger)
    {
        var explicitCss = GetString(step, "css") ?? GetString(step, "cssHref") ?? GetString(step, "css-href");
        if (!string.IsNullOrWhiteSpace(explicitCss))
            return explicitCss.Trim();

        var configPath = ResolvePath(baseDir, GetString(step, "config"));
        if (string.IsNullOrWhiteSpace(configPath))
        {
            var defaultSiteConfig = Path.Combine(baseDir, "site.json");
            if (File.Exists(defaultSiteConfig))
                configPath = defaultSiteConfig;
        }

        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            return null;

        try
        {
            var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(configPath, WebCliJson.Options);
            var siteRoot = Path.GetDirectoryName(specPath) ?? baseDir;
            var themeRoot = ResolveProjectApiThemeRoot(spec, siteRoot);
            if (string.IsNullOrWhiteSpace(themeRoot) || !Directory.Exists(themeRoot))
                return null;

            var loader = new ThemeLoader();
            var manifest = loader.Load(themeRoot, ResolveProjectApiThemesRoot(spec, siteRoot));
            if (manifest?.FeatureContracts is null)
                return null;

            foreach (var pair in manifest.FeatureContracts)
            {
                if (!pair.Key.Equals("apiDocs", StringComparison.OrdinalIgnoreCase) &&
                    !pair.Key.Equals("apidocs", StringComparison.OrdinalIgnoreCase))
                    continue;

                var hrefs = (pair.Value?.CssHrefs ?? Array.Empty<string>())
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Select(static value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (hrefs.Length > 0)
                    return string.Join(",", hrefs);
            }
        }
        catch (Exception ex)
        {
            logger?.Warn($"project-apidocs: unable to infer api CSS from site config ({ex.GetType().Name}: {ex.Message})");
        }

        return null;
    }

    private static string? ResolveProjectApiSiteConfigPath(JsonElement step, string baseDir)
    {
        var configPath = ResolvePath(baseDir, GetString(step, "config"));
        if (!string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath))
            return configPath;

        var defaultSiteConfig = Path.Combine(baseDir, "site.json");
        return File.Exists(defaultSiteConfig) ? defaultSiteConfig : null;
    }

    private static string ResolveProjectApiArtifactRoot(JsonElement step, string baseDir, WebConsoleLogger? logger)
    {
        var explicitApiRoot = ResolvePath(baseDir,
            GetString(step, "apiRoot") ??
            GetString(step, "api-root"));
        if (!string.IsNullOrWhiteSpace(explicitApiRoot))
            return Path.GetFullPath(explicitApiRoot);

        var defaultRoot = Path.GetFullPath(Path.Combine(baseDir, "data", "project-api"));
        if (Directory.Exists(defaultRoot))
            return defaultRoot;

        var artifactRoot = Path.GetFullPath(Path.Combine(baseDir, "data", "project-api-artifacts"));
        if (Directory.Exists(artifactRoot))
        {
            logger?.Warn($"project-apidocs: apiRoot not configured; using detected artifact root '{artifactRoot}' instead of missing default '{defaultRoot}'.");
            return artifactRoot;
        }

        return defaultRoot;
    }

    private static string ResolveProjectApiSuiteLandingUrl(
        string? explicitLandingUrl,
        string? suiteHomeUrl,
        string siteRoot,
        string outRoot,
        IReadOnlyList<WebApiDocsSuiteEntry> suiteEntries)
    {
        if (!string.IsNullOrWhiteSpace(explicitLandingUrl))
        {
            if (Uri.TryCreate(explicitLandingUrl, UriKind.Absolute, out var absoluteLandingUrl))
            {
                var builder = new UriBuilder(absoluteLandingUrl)
                {
                    Path = EnsureProjectSuiteLandingRoute(absoluteLandingUrl.AbsolutePath)
                };
                return builder.Uri.ToString();
            }

            return EnsureProjectSuiteLandingRoute(explicitLandingUrl);
        }

        if (!string.IsNullOrWhiteSpace(suiteHomeUrl))
        {
            if (Uri.TryCreate(suiteHomeUrl, UriKind.Absolute, out var absolute))
            {
                var builder = new UriBuilder(absolute);
                builder.Path = EnsureProjectSuiteLandingRoute(builder.Path);
                return builder.Uri.ToString();
            }

            if (suiteHomeUrl.StartsWith("/", StringComparison.Ordinal))
                return EnsureProjectSuiteLandingRoute(suiteHomeUrl);
        }

        if (!string.IsNullOrWhiteSpace(siteRoot) && !string.IsNullOrWhiteSpace(outRoot))
        {
            var relative = Path.GetRelativePath(siteRoot, outRoot)
                .Replace('\\', '/')
                .Trim('/');
            if (string.IsNullOrWhiteSpace(relative))
                return "/api-suite/";
            return "/" + relative + "/api-suite/";
        }

        var suiteBaseUrl = BuildProjectApiSuiteBaseUrlFromEntries(suiteEntries);
        if (!string.IsNullOrWhiteSpace(suiteBaseUrl))
            return EnsureProjectSuiteLandingRoute(suiteBaseUrl);

        return "/api-suite/";
    }

    private static string EnsureProjectSuiteLandingRoute(string path)
    {
        var normalized = EnsureProjectRouteTrailingSlash(path);
        return normalized.EndsWith("/api-suite/", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : normalized + "api-suite/";
    }

    private static string? BuildProjectApiSuiteBaseUrlFromEntries(IReadOnlyList<WebApiDocsSuiteEntry> suiteEntries)
    {
        if (suiteEntries is null || suiteEntries.Count == 0)
            return null;

        var rootRelativeHrefs = suiteEntries
            .Select(static entry => entry?.Href?.Trim())
            .Where(static href => !string.IsNullOrWhiteSpace(href) && href.StartsWith("/", StringComparison.Ordinal))
            .Select(static href => EnsureProjectRouteTrailingSlash(href!))
            .ToArray();
        if (rootRelativeHrefs.Length == 0)
            return null;

        var segments = rootRelativeHrefs
            .Select(static href => href.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();
        var segmentCount = segments.Min(static parts => parts.Length);
        var common = new List<string>();
        for (var index = 0; index < segmentCount; index++)
        {
            var candidate = segments[0][index];
            if (segments.All(parts => string.Equals(parts[index], candidate, StringComparison.OrdinalIgnoreCase)))
            {
                common.Add(candidate);
                continue;
            }

            break;
        }

        if (common.Count == 0)
            return null;

        return "/" + string.Join('/', common) + "/";
    }

    private static string? ResolveProjectApiThemeRoot(SiteSpec spec, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(spec.DefaultTheme))
            return null;

        var themesRoot = ResolveProjectApiThemesRoot(spec, rootPath);
        return string.IsNullOrWhiteSpace(themesRoot) ? null : Path.Combine(themesRoot, spec.DefaultTheme);
    }

    private static string ResolveProjectApiThemesRoot(SiteSpec spec, string rootPath)
    {
        var themesRoot = string.IsNullOrWhiteSpace(spec.ThemesRoot) ? "themes" : spec.ThemesRoot;
        return Path.IsPathRooted(themesRoot) ? themesRoot : Path.Combine(rootPath, themesRoot);
    }

    private static void DeleteProjectApiOutputIfRequested(string outRoot, string slug, bool cleanOutput)
    {
        if (!cleanOutput || string.IsNullOrWhiteSpace(outRoot) || string.IsNullOrWhiteSpace(slug))
            return;

        var outputPath = Path.Combine(outRoot, slug.Trim(), "api");
        if (Directory.Exists(outputPath))
            Directory.Delete(outputPath, recursive: true);
    }

    private sealed class ProjectApiInputCandidate
    {
        public string Type { get; init; } = string.Empty;
        public string RootPath { get; init; } = string.Empty;
        public string? HelpPath { get; init; }
        public string? PowerShellManifestPath { get; init; }
        public string? PowerShellCommandMetadataPath { get; init; }
        public string? PowerShellExamplesPath { get; init; }
        public string? XmlPath { get; init; }
        public string? AssemblyPath { get; init; }
        public bool HasPlaceholderContent { get; init; }
        public string PlaceholderPath { get; init; } = string.Empty;
    }

    private sealed class ProjectApiDocsPreparedInput
    {
        public string Slug { get; init; } = string.Empty;
        public string OutputPath { get; init; } = string.Empty;
        public JsonObject InputNode { get; init; } = new();
        public WebApiDocsSuiteEntry SuiteEntry { get; init; } = new();
        public bool CleanOutput { get; init; }
        public bool HasQuickStartTypes { get; init; }
        public IReadOnlyList<string> RelatedContentManifestPaths { get; init; } = Array.Empty<string>();
    }

    private sealed class ProjectApiDocsCatalogOverrides
    {
        public string? RelatedContentManifest { get; init; }
        public string[]? RelatedContentManifests { get; init; }
        public string? QuickStartTypes { get; init; }
        public IReadOnlyList<string> RelatedContentManifestPaths
        {
            get
            {
                var paths = new List<string>();
                if (!string.IsNullOrWhiteSpace(RelatedContentManifest))
                    paths.Add(RelatedContentManifest);
                if (RelatedContentManifests is { Length: > 0 })
                    paths.AddRange(RelatedContentManifests.Where(static path => !string.IsNullOrWhiteSpace(path)));
                return paths
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    private sealed class ProjectApiSuiteArtifacts
    {
        public string? SearchOutputPath { get; set; }
        public string? XrefMapOutputPath { get; set; }
        public string? CoverageOutputPath { get; set; }
        public string? RelatedContentOutputPath { get; set; }
        public string? NarrativeOutputPath { get; set; }
        public string? SearchPath { get; set; }
        public string? XrefMapPath { get; set; }
        public string? CoveragePath { get; set; }
        public string? RelatedContentPath { get; set; }
        public string? NarrativePath { get; set; }
    }

    private sealed class ProjectApiSuiteNarrativeThresholds
    {
        public int? MinSectionCount { get; init; }
        public int? MinItemCount { get; init; }
        public bool RequireSummary { get; init; }
        public double? MinSuiteEntryCoveragePercent { get; init; }
        public int? MaxUncoveredSuiteEntryCount { get; init; }

        public bool HasRequirements =>
            MinSectionCount.HasValue ||
            MinItemCount.HasValue ||
            RequireSummary ||
            MinSuiteEntryCoveragePercent.HasValue ||
            MaxUncoveredSuiteEntryCount.HasValue;
    }

    private sealed class ProjectApiSuiteNarrativeSection
    {
        public string Id { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string? Summary { get; set; }
        public int Order { get; set; }
        public List<ProjectApiSuiteNarrativeItem> Items { get; init; } = new();
    }

    private sealed class ProjectApiSuiteNarrativeItem
    {
        public string Title { get; init; } = string.Empty;
        public string Url { get; init; } = string.Empty;
        public string? Summary { get; init; }
        public string Kind { get; init; } = "guide";
        public string? Audience { get; init; }
        public string? EstimatedTime { get; init; }
        public int Order { get; init; }
        public string[] SuiteEntryIds { get; init; } = Array.Empty<string>();
        public string[] SuiteEntryLabels { get; init; } = Array.Empty<string>();
    }
}
