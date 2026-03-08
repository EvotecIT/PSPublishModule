using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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

        var apiRoot = ResolvePath(baseDir,
            GetString(step, "apiRoot") ??
            GetString(step, "api-root") ??
            "./data/project-api");
        apiRoot = string.IsNullOrWhiteSpace(apiRoot) ? string.Empty : Path.GetFullPath(apiRoot);

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

        var serializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

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

            preparedInputs.Add(BuildProjectApiDocsPreparedInput(project, slug, selected, outRoot, cleanOutput, defaultCssHref));
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
                failOnPlaceholderContent
            };
            File.WriteAllText(summaryPath, JsonSerializer.Serialize(summary, serializerOptions));
        }

        var summaryMessage = notes.Count > 0
            ? string.Join("; ", notes.Take(2))
            : string.Empty;
        var suffix = notes.Count > 2 ? $" (+{notes.Count - 2} more)" : string.Empty;
        var missingNote = skippedMissing.Count > 0 ? $"; missing={skippedMissing.Count}" : string.Empty;
        var placeholderNote = skippedPlaceholder.Count > 0 ? $"; placeholder={skippedPlaceholder.Count}" : string.Empty;
        stepResult.Success = true;
        stepResult.Message = string.IsNullOrWhiteSpace(summaryMessage)
            ? $"project-apidocs ok: generated={completed}{missingNote}{placeholderNote}"
            : $"project-apidocs ok: generated={completed}{missingNote}{placeholderNote}; {summaryMessage}{suffix}";
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
        var hasPlaceholder = TryDetectPlaceholderContent(helpFile, placeholderMarkers, out var placeholderPath);
        if (!hasPlaceholder && !string.IsNullOrWhiteSpace(examplesPath))
            hasPlaceholder = TryDetectPlaceholderContent(examplesPath, placeholderMarkers, out placeholderPath);

        candidate = new ProjectApiInputCandidate
        {
            Type = "PowerShell",
            RootPath = powerShellRoot,
            HelpPath = Path.GetFullPath(helpFile),
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
        string? cssHref)
    {
        var name = NormalizeOptionalString(project.Name) ?? slug;
        var hubPath = NormalizeOptionalString(project.HubPath) ?? $"/projects/{slug}/";
        var apiBaseUrl = $"/projects/{slug}/api";
        var overviewUrl = EnsureProjectRouteTrailingSlash(hubPath);
        var docsUrl = $"/projects/{slug}/docs/";
        var apiUrl = $"{apiBaseUrl}/";
        var examplesUrl = $"/projects/{slug}/examples/";
        var outputPath = Path.Combine(outRoot, slug, "api");
        var templateTokens = BuildProjectApiTemplateTokens(project, slug, name, overviewUrl, docsUrl, apiUrl, examplesUrl);
        var node = new JsonObject
        {
            ["id"] = slug,
            ["title"] = $"{name} API Reference",
            ["out"] = outputPath,
            ["baseUrl"] = apiBaseUrl,
            ["docsHome"] = hubPath,
            ["navContextPath"] = "/",
            ["navContextProject"] = slug,
            ["navSurface"] = "main",
            ["type"] = selected.Type,
            ["templateTokens"] = templateTokens
        };

        if (!string.IsNullOrWhiteSpace(cssHref))
            node["css"] = cssHref;
        if (!string.IsNullOrWhiteSpace(selected.HelpPath))
            node["helpPath"] = selected.HelpPath;
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
            CleanOutput = cleanOutput
        };
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
        var downloadsUrl = NormalizeOptionalString(GetProjectLink(project, "powerShellGallery")) ??
                           NormalizeOptionalString(project.Metrics?.PowerShellGallery?.GalleryUrl) ??
                           NormalizeOptionalString(project.Metrics?.NuGet?.PackageUrl);

        var docsVisible = (TryGetProjectSurfaceValue(project.Surfaces, "docs") ?? false);
        var examplesVisible = (TryGetProjectSurfaceValue(project.Surfaces, "examples") ?? false);
        var description = NormalizeOptionalString(project.Description);
        var stars = project.Metrics?.GitHub?.Stars > 0 ? project.Metrics.GitHub.Stars.ToString() : null;
        var forks = project.Metrics?.GitHub?.Forks > 0 ? project.Metrics.GitHub.Forks.ToString() : null;
        var openIssues = project.Metrics?.GitHub?.OpenIssues > 0 ? project.Metrics.GitHub.OpenIssues.ToString() : null;
        var downloads = ResolveProjectDownloadsValue(project);
        var release = NormalizeOptionalString(project.Metrics?.Release?.LatestTag) ?? NormalizeOptionalString(project.Version);
        var language = NormalizeOptionalString(project.Metrics?.GitHub?.Language);
        var lastPush = NormalizeOptionalString(project.Metrics?.GitHub?.LastPushedAt);
        var metadataRefresh = NormalizeOptionalString(project.ManifestGeneratedAt);
        var manifestCommit = NormalizeOptionalString(project.ManifestCommit);

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
            ["PROJECT_DOWNLOADS_HIDDEN"] = BuildHiddenAttribute(downloadsUrl),
            ["PROJECT_STARS"] = EncodeToken(stars),
            ["PROJECT_STARS_HIDDEN"] = BuildHiddenAttribute(stars),
            ["PROJECT_FORKS"] = EncodeToken(forks),
            ["PROJECT_FORKS_HIDDEN"] = BuildHiddenAttribute(forks),
            ["PROJECT_OPEN_ISSUES"] = EncodeToken(openIssues),
            ["PROJECT_OPEN_ISSUES_HIDDEN"] = BuildHiddenAttribute(openIssues),
            ["PROJECT_DOWNLOADS"] = EncodeToken(downloads),
            ["PROJECT_DOWNLOADS_HIDDEN"] = BuildHiddenAttribute(downloads),
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
            ["PROJECT_DOCS_URL"] = EncodeHrefToken(docsUrl),
            ["PROJECT_DOCS_HIDDEN"] = docsVisible ? string.Empty : " hidden",
            ["PROJECT_API_URL"] = EncodeHrefToken(apiUrl),
            ["PROJECT_EXAMPLES_URL"] = EncodeHrefToken(examplesUrl),
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

    private static string EnsureProjectRouteTrailingSlash(string path)
    {
        var normalized = NormalizeOptionalString(path) ?? "/";
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = "/" + normalized;
        return normalized.EndsWith("/", StringComparison.Ordinal) ? normalized : normalized + "/";
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
        public bool CleanOutput { get; init; }
    }
}
