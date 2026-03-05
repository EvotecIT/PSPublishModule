using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecuteProjectDocsSync(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var strict = GetBool(step, "strict") ?? true;
        var generateToc = GetBool(step, "generateToc") ?? GetBool(step, "generate-toc") ?? true;
        var failOnMissingSource = GetBool(step, "failOnMissingSource") ?? GetBool(step, "fail-on-missing-source") ?? false;
        var cleanTarget = GetBool(step, "cleanTarget") ?? GetBool(step, "clean-target") ?? false;
        var onlyLocalLinks = GetBool(step, "onlyLocalLinks") ?? GetBool(step, "only-local-links") ?? false;
        var syncApi = GetBool(step, "syncApi") ?? GetBool(step, "sync-api") ?? false;
        var failOnMissingApiSource = GetBool(step, "failOnMissingApiSource") ?? GetBool(step, "fail-on-missing-api-source") ?? false;
        var cleanApiTarget = GetBool(step, "cleanApiTarget") ?? GetBool(step, "clean-api-target") ?? cleanTarget;
        var syncExamples = GetBool(step, "syncExamples") ?? GetBool(step, "sync-examples") ?? true;
        var failOnMissingExamplesSource = GetBool(step, "failOnMissingExamplesSource") ?? GetBool(step, "fail-on-missing-examples-source") ?? false;
        var cleanExamplesTarget = GetBool(step, "cleanExamplesTarget") ?? GetBool(step, "clean-examples-target") ?? cleanTarget;
        var hydrateFromArtifacts = GetBool(step, "hydrateFromArtifacts") ?? GetBool(step, "hydrate-from-artifacts") ?? true;
        var artifactTimeoutSeconds = GetInt(step, "artifactTimeoutSeconds") ?? GetInt(step, "artifact-timeout-seconds") ?? 60;
        if (artifactTimeoutSeconds < 5)
            artifactTimeoutSeconds = 5;

        var catalogPath = ResolvePath(baseDir,
            GetString(step, "catalog") ??
            GetString(step, "catalogPath") ??
            GetString(step, "catalog-path") ??
            "./data/projects/catalog.json");
        if (string.IsNullOrWhiteSpace(catalogPath))
            throw new InvalidOperationException("project-docs-sync requires catalog path.");
        catalogPath = Path.GetFullPath(catalogPath);

        var sourcesRoot = ResolvePath(baseDir,
            GetString(step, "sourcesRoot") ??
            GetString(step, "sources-root") ??
            "./projects-sources");
        if (string.IsNullOrWhiteSpace(sourcesRoot))
            throw new InvalidOperationException("project-docs-sync requires sourcesRoot.");
        sourcesRoot = Path.GetFullPath(sourcesRoot);

        var contentRoot = ResolvePath(baseDir,
            GetString(step, "contentRoot") ??
            GetString(step, "content-root") ??
            "./content/docs");
        if (string.IsNullOrWhiteSpace(contentRoot))
            throw new InvalidOperationException("project-docs-sync requires contentRoot.");
        contentRoot = Path.GetFullPath(contentRoot);

        var docsSourceCandidates = ResolvePathCandidates(
            step,
            arrayKeys: new[] { "sourceDocsPaths", "source-docs-paths" },
            scalarKeys: new[] { "sourceDocsPath", "source-docs-path", "sourceDocsFolder", "source-docs-folder" },
            defaults: new[] { "Docs" });

        var apiSourceCandidates = ResolvePathCandidates(
            step,
            arrayKeys: new[] { "sourceApiPaths", "source-api-paths" },
            scalarKeys: new[] { "sourceApiPath", "source-api-path", "sourceApiFolder", "source-api-folder" },
            defaults: new[] { "Website/data/apidocs", "data/apidocs" });

        var examplesSourceCandidates = ResolvePathCandidates(
            step,
            arrayKeys: new[] { "sourceExamplesPaths", "source-examples-paths" },
            scalarKeys: new[] { "sourceExamplesPath", "source-examples-path", "sourceExamplesFolder", "source-examples-folder" },
            defaults: new[] { "Website/content/examples", "Examples", "content/examples" });

        var apiRoot = ResolvePath(baseDir,
            GetString(step, "apiRoot") ??
            GetString(step, "api-root") ??
            "./data/apidocs");
        if (syncApi && string.IsNullOrWhiteSpace(apiRoot))
            throw new InvalidOperationException("project-docs-sync requires apiRoot when syncApi is enabled.");
        apiRoot = string.IsNullOrWhiteSpace(apiRoot) ? string.Empty : Path.GetFullPath(apiRoot);

        var examplesRoot = ResolvePath(baseDir,
            GetString(step, "examplesRoot") ??
            GetString(step, "examples-root") ??
            "./content/examples");
        if (syncExamples && string.IsNullOrWhiteSpace(examplesRoot))
            throw new InvalidOperationException("project-docs-sync requires examplesRoot when syncExamples is enabled.");
        examplesRoot = string.IsNullOrWhiteSpace(examplesRoot) ? string.Empty : Path.GetFullPath(examplesRoot);

        var artifactWorkRoot = ResolvePath(baseDir,
            GetString(step, "artifactWorkRoot") ??
            GetString(step, "artifact-work-root") ??
            "./_temp/project-artifacts");
        artifactWorkRoot = string.IsNullOrWhiteSpace(artifactWorkRoot)
            ? Path.Combine(Path.GetTempPath(), "powerforge-web-artifacts")
            : Path.GetFullPath(artifactWorkRoot);

        var artifactToken = GetString(step, "artifactToken") ?? GetString(step, "artifact-token") ?? GetString(step, "token");
        var artifactTokenEnv = GetString(step, "artifactTokenEnv") ?? GetString(step, "artifact-token-env") ?? "GITHUB_TOKEN";
        if (string.IsNullOrWhiteSpace(artifactToken) && !string.IsNullOrWhiteSpace(artifactTokenEnv))
            artifactToken = Environment.GetEnvironmentVariable(artifactTokenEnv);
        var projectsContentRoot = ResolvePath(baseDir,
            GetString(step, "projectsContentRoot") ??
            GetString(step, "projects-content-root"));
        if (string.IsNullOrWhiteSpace(projectsContentRoot))
        {
            var contentParent = Directory.GetParent(contentRoot)?.FullName;
            projectsContentRoot = string.IsNullOrWhiteSpace(contentParent)
                ? string.Empty
                : Path.Combine(contentParent, "projects");
        }
        projectsContentRoot = string.IsNullOrWhiteSpace(projectsContentRoot) ? string.Empty : Path.GetFullPath(projectsContentRoot);

        var summaryPath = ResolvePath(baseDir,
            GetString(step, "summaryPath") ??
            GetString(step, "summary-path") ??
            "./Build/sync-project-docs-last-run.json");

        if (!File.Exists(catalogPath))
        {
            if (strict)
                throw new InvalidOperationException($"project-docs-sync catalog file not found: {catalogPath}");

            if (!string.IsNullOrWhiteSpace(summaryPath))
            {
                WriteProjectDocsSummary(
                    summaryPath,
                    catalogPath,
                    sourcesRoot,
                    contentRoot,
                    apiRoot,
                    examplesRoot,
                    docsSourceCandidates,
                    apiSourceCandidates,
                    examplesSourceCandidates,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    Array.Empty<string>(),
                    0,
                    0,
                    0,
                    0,
                    Array.Empty<string>(),
                    0,
                    0,
                    0,
                    0,
                    Array.Empty<string>(),
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    artifactWorkRoot,
                    status: "skipped");
            }

            stepResult.Success = true;
            stepResult.Message = $"project-docs-sync skipped: catalog not found '{catalogPath}'.";
            return;
        }

        var projects = ReadProjectDocsCatalog(catalogPath, onlyLocalLinks);
        var docsProjects = projects.Where(static p => p.HasDocsSurface).ToList();
        var apiProjects = projects.Where(static p => p.HasApiSurface).ToList();
        var examplesProjects = projects.Where(static p => p.HasExamplesSurface).ToList();
        var artifactStats = new ProjectArtifactHydrationStats();
        var artifactCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sourcesRootExists = Directory.Exists(sourcesRoot);

        if (!sourcesRootExists && !hydrateFromArtifacts)
        {
            if (strict)
                throw new InvalidOperationException($"project-docs-sync sources root not found: {sourcesRoot}");

            if (!string.IsNullOrWhiteSpace(summaryPath))
            {
                WriteProjectDocsSummary(
                    summaryPath,
                    catalogPath,
                    sourcesRoot,
                    contentRoot,
                    apiRoot,
                    examplesRoot,
                    docsSourceCandidates,
                    apiSourceCandidates,
                    examplesSourceCandidates,
                    projects.Count,
                    docsProjects.Count,
                    0,
                    docsProjects.Count,
                    0,
                    0,
                    Array.Empty<string>(),
                    apiProjects.Count,
                    0,
                    apiProjects.Count,
                    0,
                    Array.Empty<string>(),
                    examplesProjects.Count,
                    0,
                    examplesProjects.Count,
                    0,
                    Array.Empty<string>(),
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    artifactWorkRoot,
                    status: "skipped");
            }

            stepResult.Success = true;
            stepResult.Message = $"project-docs-sync skipped: sources root not found '{sourcesRoot}'.";
            return;
        }

        if (!sourcesRootExists)
            Directory.CreateDirectory(sourcesRoot);

        if (hydrateFromArtifacts)
        {
            HydrateProjectArtifactSources(
                projects,
                sourcesRoot,
                docsSourceCandidates,
                apiSourceCandidates,
                examplesSourceCandidates,
                syncApi,
                syncExamples,
                artifactWorkRoot,
                artifactTimeoutSeconds,
                artifactToken,
                artifactCache,
                artifactStats);
        }

        var synced = 0;
        var skipped = 0;
        var copiedFiles = 0;
        var tocFiles = 0;
        var missingSources = new List<string>();
        var syncedApi = 0;
        var skippedApi = 0;
        var copiedApiFiles = 0;
        var missingApiSources = new List<string>();
        var syncedExamples = 0;
        var skippedExamples = 0;
        var copiedExampleFiles = 0;
        var missingExampleSources = new List<string>();

        Directory.CreateDirectory(contentRoot);
        if (syncApi)
            Directory.CreateDirectory(apiRoot);
        if (syncExamples)
            Directory.CreateDirectory(examplesRoot);

        foreach (var project in docsProjects)
        {
            if (string.IsNullOrWhiteSpace(project.Slug))
                continue;

            var slug = project.Slug.Trim().ToLowerInvariant();
            var sourceDocsRoot = ResolveExistingProjectSourcePath(sourcesRoot, slug, docsSourceCandidates);
            if (!Directory.Exists(sourceDocsRoot))
            {
                skipped++;
                missingSources.Add(GetExpectedProjectSourcePath(sourcesRoot, slug, docsSourceCandidates));
                if (failOnMissingSource)
                    throw new InvalidOperationException($"project-docs-sync source docs path not found for '{slug}': {GetExpectedProjectSourcePath(sourcesRoot, slug, docsSourceCandidates)}");
                continue;
            }

            var targetDocsRoot = Path.Combine(contentRoot, slug);
            if (cleanTarget && Directory.Exists(targetDocsRoot))
                Directory.Delete(targetDocsRoot, recursive: true);
            Directory.CreateDirectory(targetDocsRoot);

            var markdownFiles = Directory
                .EnumerateFiles(sourceDocsRoot, "*.md", SearchOption.AllDirectories)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var sourceFile in markdownFiles)
            {
                var relativePath = Path.GetRelativePath(sourceDocsRoot, sourceFile);
                var targetFile = Path.Combine(targetDocsRoot, relativePath);
                var targetDirectory = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrWhiteSpace(targetDirectory))
                    Directory.CreateDirectory(targetDirectory);

                File.Copy(sourceFile, targetFile, overwrite: true);
                copiedFiles++;
            }

            if (generateToc)
            {
                var tocPath = Path.Combine(targetDocsRoot, "toc.yml");
                var rootDocs = Directory
                    .EnumerateFiles(targetDocsRoot, "*.md", SearchOption.TopDirectoryOnly)
                    .OrderBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var lines = new List<string>
                {
                    $"# Auto-generated table of contents for {slug}"
                };
                foreach (var docFile in rootDocs)
                {
                    var fileName = Path.GetFileName(docFile);
                    var baseName = Path.GetFileNameWithoutExtension(fileName).Replace('-', ' ');
                    var title = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(baseName);
                    lines.Add($"- title: \"{EscapeYamlString(title)}\"");
                    lines.Add($"  href: {fileName}");
                }

                File.WriteAllText(tocPath, string.Join(Environment.NewLine, lines), Encoding.UTF8);
                tocFiles++;
            }

            synced++;
        }

        if (syncApi)
        {
            foreach (var project in apiProjects)
            {
                if (string.IsNullOrWhiteSpace(project.Slug))
                    continue;

                var slug = project.Slug.Trim().ToLowerInvariant();
                var sourceApiRoot = ResolveExistingProjectSourcePath(sourcesRoot, slug, apiSourceCandidates);
                if (!Directory.Exists(sourceApiRoot))
                {
                    skippedApi++;
                    missingApiSources.Add(GetExpectedProjectSourcePath(sourcesRoot, slug, apiSourceCandidates));
                    if (failOnMissingApiSource)
                        throw new InvalidOperationException($"project-docs-sync source api path not found for '{slug}': {GetExpectedProjectSourcePath(sourcesRoot, slug, apiSourceCandidates)}");
                    continue;
                }

                var targetApiRoot = Path.Combine(apiRoot, slug);
                if (cleanApiTarget && Directory.Exists(targetApiRoot))
                    Directory.Delete(targetApiRoot, recursive: true);
                Directory.CreateDirectory(targetApiRoot);

                var apiFiles = Directory
                    .EnumerateFiles(sourceApiRoot, "*", SearchOption.AllDirectories)
                    .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var sourceFile in apiFiles)
                {
                    var relativePath = Path.GetRelativePath(sourceApiRoot, sourceFile);
                    var targetFile = Path.Combine(targetApiRoot, relativePath);
                    var targetDirectory = Path.GetDirectoryName(targetFile);
                    if (!string.IsNullOrWhiteSpace(targetDirectory))
                        Directory.CreateDirectory(targetDirectory);

                    File.Copy(sourceFile, targetFile, overwrite: true);
                    copiedApiFiles++;
                }

                syncedApi++;
            }
        }

        if (syncExamples)
        {
            foreach (var project in examplesProjects)
            {
                if (string.IsNullOrWhiteSpace(project.Slug))
                    continue;

                var slug = project.Slug.Trim().ToLowerInvariant();
                var sourceExamplesRoot = ResolveExistingProjectSourcePath(sourcesRoot, slug, examplesSourceCandidates);
                if (!Directory.Exists(sourceExamplesRoot))
                {
                    skippedExamples++;
                    missingExampleSources.Add(GetExpectedProjectSourcePath(sourcesRoot, slug, examplesSourceCandidates));
                    if (failOnMissingExamplesSource)
                        throw new InvalidOperationException($"project-docs-sync source examples path not found for '{slug}': {GetExpectedProjectSourcePath(sourcesRoot, slug, examplesSourceCandidates)}");
                    continue;
                }

                var targetExamplesRoot = Path.Combine(examplesRoot, slug);
                if (cleanExamplesTarget && Directory.Exists(targetExamplesRoot))
                    Directory.Delete(targetExamplesRoot, recursive: true);
                Directory.CreateDirectory(targetExamplesRoot);

                var exampleFiles = Directory
                    .EnumerateFiles(sourceExamplesRoot, "*", SearchOption.AllDirectories)
                    .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var sourceFile in exampleFiles)
                {
                    var relativePath = Path.GetRelativePath(sourceExamplesRoot, sourceFile);
                    var targetFile = Path.Combine(targetExamplesRoot, relativePath);
                    var targetDirectory = Path.GetDirectoryName(targetFile);
                    if (!string.IsNullOrWhiteSpace(targetDirectory))
                        Directory.CreateDirectory(targetDirectory);

                    File.Copy(sourceFile, targetFile, overwrite: true);
                    copiedExampleFiles++;
                }

                syncedExamples++;
            }
        }

        UpdateProjectSurfaceAvailabilityMetadata(
            projects,
            projectsContentRoot,
            contentRoot,
            apiRoot,
            examplesRoot);

        if (!string.IsNullOrWhiteSpace(summaryPath))
        {
            WriteProjectDocsSummary(
                summaryPath,
                catalogPath,
                sourcesRoot,
                contentRoot,
                apiRoot,
                examplesRoot,
                docsSourceCandidates,
                apiSourceCandidates,
                examplesSourceCandidates,
                projects.Count,
                docsProjects.Count,
                synced,
                skipped,
                copiedFiles,
                tocFiles,
                missingSources,
                apiProjects.Count,
                syncedApi,
                skippedApi,
                copiedApiFiles,
                missingApiSources,
                examplesProjects.Count,
                syncedExamples,
                skippedExamples,
                copiedExampleFiles,
                missingExampleSources,
                artifactStats.DocsHydrated,
                artifactStats.ApiHydrated,
                artifactStats.ExamplesHydrated,
                artifactStats.Downloads,
                artifactStats.CacheHits,
                artifactStats.Failures,
                artifactWorkRoot,
                status: "updated");
        }

        stepResult.Success = true;
        stepResult.Message = $"project-docs-sync ok: docs={synced}/{docsProjects.Count}; docsSkipped={skipped}; docsFiles={copiedFiles}; toc={tocFiles}; api={syncedApi}/{apiProjects.Count}; apiSkipped={skippedApi}; apiFiles={copiedApiFiles}; examples={syncedExamples}/{examplesProjects.Count}; examplesSkipped={skippedExamples}; examplesFiles={copiedExampleFiles}; artifacts(hydrated/downloads/hits/failures)={(artifactStats.DocsHydrated + artifactStats.ApiHydrated + artifactStats.ExamplesHydrated)}/{artifactStats.Downloads}/{artifactStats.CacheHits}/{artifactStats.Failures}";
    }

    private static void UpdateProjectSurfaceAvailabilityMetadata(
        IReadOnlyList<ProjectDocsCatalogItem> projects,
        string projectsContentRoot,
        string docsRoot,
        string apiRoot,
        string examplesRoot)
    {
        if (projects is null || projects.Count == 0)
            return;
        if (string.IsNullOrWhiteSpace(projectsContentRoot) || !Directory.Exists(projectsContentRoot))
            return;

        foreach (var project in projects)
        {
            if (string.IsNullOrWhiteSpace(project.Slug))
                continue;

            var slug = project.Slug.Trim().ToLowerInvariant();
            var docsAvailable = HasProjectSurfaceContent(docsRoot, slug);
            var apiAvailable = HasProjectSurfaceContent(apiRoot, slug);
            var examplesAvailable = HasProjectSurfaceContent(examplesRoot, slug);

            UpdateProjectLocalSurfaceFlags(Path.Combine(projectsContentRoot, slug + ".md"), docsAvailable, apiAvailable, examplesAvailable);
            UpdateProjectLocalSurfaceFlags(Path.Combine(projectsContentRoot, slug + ".docs.md"), docsAvailable, apiAvailable, examplesAvailable);
            UpdateProjectLocalSurfaceFlags(Path.Combine(projectsContentRoot, slug + ".api.md"), docsAvailable, apiAvailable, examplesAvailable);
            UpdateProjectLocalSurfaceFlags(Path.Combine(projectsContentRoot, slug + ".examples.md"), docsAvailable, apiAvailable, examplesAvailable);

            var nestedSectionsRoot = Path.Combine(projectsContentRoot, slug);
            UpdateProjectLocalSurfaceFlags(Path.Combine(nestedSectionsRoot, "docs.md"), docsAvailable, apiAvailable, examplesAvailable);
            UpdateProjectLocalSurfaceFlags(Path.Combine(nestedSectionsRoot, "api.md"), docsAvailable, apiAvailable, examplesAvailable);
            UpdateProjectLocalSurfaceFlags(Path.Combine(nestedSectionsRoot, "examples.md"), docsAvailable, apiAvailable, examplesAvailable);
        }
    }

    private static bool HasProjectSurfaceContent(string root, string slug)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(slug))
            return false;
        if (!Directory.Exists(root))
            return false;

        var projectRoot = Path.Combine(root, slug);
        if (!Directory.Exists(projectRoot))
            return false;

        return Directory.EnumerateFiles(projectRoot, "*.md", SearchOption.AllDirectories).Any();
    }

    private static void UpdateProjectLocalSurfaceFlags(
        string markdownPath,
        bool docsAvailable,
        bool apiAvailable,
        bool examplesAvailable)
    {
        if (string.IsNullOrWhiteSpace(markdownPath) || !File.Exists(markdownPath))
            return;

        var content = File.ReadAllText(markdownPath);
        if (!TryGetFrontMatterLines(content, out var allLines))
            return;

        var closingIndex = FindFrontMatterClosingMarkerIndex(allLines);
        if (closingIndex <= 0)
            return;

        var changed = false;
        changed |= UpsertFrontMatterBoolean(allLines, closingIndex, "meta.project_local_docs_available", docsAvailable);
        closingIndex = FindFrontMatterClosingMarkerIndex(allLines);
        changed |= UpsertFrontMatterBoolean(allLines, closingIndex, "meta.project_local_api_available", apiAvailable);
        closingIndex = FindFrontMatterClosingMarkerIndex(allLines);
        changed |= UpsertFrontMatterBoolean(allLines, closingIndex, "meta.project_local_examples_available", examplesAvailable);

        if (!changed)
            return;

        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        File.WriteAllText(markdownPath, string.Join(newline, allLines), Encoding.UTF8);
    }

    private static bool UpsertFrontMatterBoolean(List<string> allLines, int closingMarkerIndex, string key, bool value)
    {
        if (allLines is null || allLines.Count == 0 || closingMarkerIndex <= 0)
            return false;

        var expected = $"{key}: {(value ? "true" : "false")}";
        for (var i = 1; i < closingMarkerIndex; i++)
        {
            var currentLine = allLines[i];
            if (!currentLine.TrimStart().StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(currentLine.Trim(), expected, StringComparison.OrdinalIgnoreCase))
                return false;

            allLines[i] = expected;
            return true;
        }

        allLines.Insert(closingMarkerIndex, expected);
        return true;
    }

    private static int FindFrontMatterClosingMarkerIndex(IReadOnlyList<string> lines)
    {
        if (lines is null || lines.Count < 3)
            return -1;

        if (!string.Equals(lines[0].Trim(), "---", StringComparison.Ordinal))
            return -1;

        for (var i = 1; i < lines.Count; i++)
        {
            if (lines[i].Trim() == "---")
                return i;
        }

        return -1;
    }

    private static bool TryGetFrontMatterLines(string content, out List<string> allLines)
    {
        allLines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        if (allLines.Count < 3)
            return false;
        if (!string.Equals(allLines[0].Trim(), "---", StringComparison.Ordinal))
            return false;

        return FindFrontMatterClosingMarkerIndex(allLines) > 0;
    }

    private static void WriteProjectDocsSummary(
        string summaryPath,
        string catalogPath,
        string sourcesRoot,
        string contentRoot,
        string apiRoot,
        string examplesRoot,
        IReadOnlyList<string> docsSourceCandidates,
        IReadOnlyList<string> apiSourceCandidates,
        IReadOnlyList<string> examplesSourceCandidates,
        int totalProjects,
        int docsProjects,
        int synced,
        int skipped,
        int copiedFiles,
        int tocFiles,
        IReadOnlyList<string> missingSources,
        int apiProjects,
        int syncedApi,
        int skippedApi,
        int copiedApiFiles,
        IReadOnlyList<string> missingApiSources,
        int examplesProjects,
        int syncedExamples,
        int skippedExamples,
        int copiedExampleFiles,
        IReadOnlyList<string> missingExampleSources,
        int docsHydrated,
        int apiHydrated,
        int examplesHydrated,
        int artifactDownloads,
        int artifactCacheHits,
        int artifactFailures,
        string artifactWorkRoot,
        string status)
    {
        var summaryDirectory = Path.GetDirectoryName(summaryPath);
        if (!string.IsNullOrWhiteSpace(summaryDirectory))
            Directory.CreateDirectory(summaryDirectory);

        var summary = new
        {
            generatedOn = DateTimeOffset.UtcNow.ToString("O"),
            status,
            catalogPath = Path.GetFullPath(catalogPath),
            sourcesRoot = Path.GetFullPath(sourcesRoot),
            contentRoot = Path.GetFullPath(contentRoot),
            apiRoot = string.IsNullOrWhiteSpace(apiRoot) ? null : Path.GetFullPath(apiRoot),
            examplesRoot = string.IsNullOrWhiteSpace(examplesRoot) ? null : Path.GetFullPath(examplesRoot),
            artifactWorkRoot = string.IsNullOrWhiteSpace(artifactWorkRoot) ? null : Path.GetFullPath(artifactWorkRoot),
            sourceDocsPaths = docsSourceCandidates.ToArray(),
            sourceApiPaths = apiSourceCandidates.ToArray(),
            sourceExamplesPaths = examplesSourceCandidates.ToArray(),
            totalProjects,
            docsProjects,
            synced,
            skipped,
            copiedFiles,
            tocFiles,
            missingSources = missingSources.ToArray(),
            apiProjects,
            syncedApi,
            skippedApi,
            copiedApiFiles,
            missingApiSources = missingApiSources.ToArray(),
            examplesProjects,
            syncedExamples,
            skippedExamples,
            copiedExampleFiles,
            missingExampleSources = missingExampleSources.ToArray(),
            docsHydrated,
            apiHydrated,
            examplesHydrated,
            artifactDownloads,
            artifactCacheHits,
            artifactFailures
        };
        File.WriteAllText(summaryPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static List<ProjectDocsCatalogItem> ReadProjectDocsCatalog(string catalogPath, bool onlyLocalLinks)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(catalogPath));
        if (!document.RootElement.TryGetProperty("projects", out var projectsElement) || projectsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"project-docs-sync catalog payload is missing 'projects' array: {catalogPath}");

        var projects = new List<ProjectDocsCatalogItem>();
        foreach (var projectElement in projectsElement.EnumerateArray())
        {
            var slug = GetString(projectElement, "slug");
            var mode = GetString(projectElement, "mode");
            var contentMode = GetString(projectElement, "contentMode");
            var hasDocsSurface = false;
            var hasApiDotNetSurface = false;
            var hasApiPowerShellSurface = false;
            var hasExamplesSurface = false;
            string? docsLink = null;
            string? apiDotNetLink = null;
            string? apiPowerShellLink = null;
            string? examplesLink = null;
            string? artifactDocs = null;
            string? artifactApi = null;
            string? artifactExamples = null;

            if (projectElement.TryGetProperty("surfaces", out var surfacesElement) &&
                surfacesElement.ValueKind == JsonValueKind.Object)
            {
                hasDocsSurface = GetBool(surfacesElement, "docs") ?? false;
                hasApiDotNetSurface = GetBool(surfacesElement, "apiDotNet") ?? false;
                hasApiPowerShellSurface = GetBool(surfacesElement, "apiPowerShell") ?? false;
                hasExamplesSurface = GetBool(surfacesElement, "examples") ?? false;
            }

            if (projectElement.TryGetProperty("links", out var linksElement) &&
                linksElement.ValueKind == JsonValueKind.Object)
            {
                docsLink = GetString(linksElement, "docs");
                apiDotNetLink = GetString(linksElement, "apiDotNet");
                apiPowerShellLink = GetString(linksElement, "apiPowerShell");
                examplesLink = GetString(linksElement, "examples");
            }

            if (projectElement.TryGetProperty("artifacts", out var artifactsElement) &&
                artifactsElement.ValueKind == JsonValueKind.Object)
            {
                artifactDocs = GetString(artifactsElement, "docs");
                artifactApi = GetString(artifactsElement, "api");
                artifactExamples = GetString(artifactsElement, "examples");
            }

            var normalizedContentMode = NormalizeCatalogContentMode(contentMode, mode);
            if (normalizedContentMode.Equals("external", StringComparison.OrdinalIgnoreCase))
            {
                hasDocsSurface = false;
                hasApiDotNetSurface = false;
                hasApiPowerShellSurface = false;
                hasExamplesSurface = false;
            }

            if (onlyLocalLinks)
            {
                if (hasDocsSurface && !IsLocalSurfaceLink(docsLink) && !IsZipArtifactSource(artifactDocs))
                    hasDocsSurface = false;

                if (hasApiDotNetSurface && !IsLocalSurfaceLink(apiDotNetLink) && !IsZipArtifactSource(artifactApi))
                    hasApiDotNetSurface = false;

                if (hasApiPowerShellSurface && !IsLocalSurfaceLink(apiPowerShellLink) && !IsZipArtifactSource(artifactApi))
                    hasApiPowerShellSurface = false;

                if (hasExamplesSurface && !IsLocalSurfaceLink(examplesLink) && !IsZipArtifactSource(artifactExamples))
                    hasExamplesSurface = false;
            }

            projects.Add(new ProjectDocsCatalogItem
            {
                Slug = slug ?? string.Empty,
                ContentMode = normalizedContentMode,
                HasDocsSurface = hasDocsSurface,
                HasApiDotNetSurface = hasApiDotNetSurface,
                HasApiPowerShellSurface = hasApiPowerShellSurface,
                HasExamplesSurface = hasExamplesSurface,
                DocsLink = docsLink,
                ApiDotNetLink = apiDotNetLink,
                ApiPowerShellLink = apiPowerShellLink,
                ExamplesLink = examplesLink,
                ArtifactDocs = artifactDocs,
                ArtifactApi = artifactApi,
                ArtifactExamples = artifactExamples
            });
        }

        return projects;
    }

    private static bool IsLocalSurfaceLink(string? link)
    {
        if (string.IsNullOrWhiteSpace(link))
            return true;

        var trimmed = link.Trim();
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
            return false;

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out _))
            return false;

        return true;
    }

    private static string NormalizeCatalogContentMode(string? contentMode, string? mode)
    {
        if (!string.IsNullOrWhiteSpace(contentMode))
            return contentMode.Trim().ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(mode) &&
            mode.Trim().Equals("dedicated-external", StringComparison.OrdinalIgnoreCase))
        {
            return "external";
        }

        return "hybrid";
    }

    private static void HydrateProjectArtifactSources(
        IReadOnlyList<ProjectDocsCatalogItem> projects,
        string sourcesRoot,
        IReadOnlyList<string> docsSourceCandidates,
        IReadOnlyList<string> apiSourceCandidates,
        IReadOnlyList<string> examplesSourceCandidates,
        bool syncApi,
        bool syncExamples,
        string artifactWorkRoot,
        int timeoutSeconds,
        string? artifactToken,
        Dictionary<string, string> artifactCache,
        ProjectArtifactHydrationStats stats)
    {
        Directory.CreateDirectory(sourcesRoot);
        Directory.CreateDirectory(artifactWorkRoot);

        foreach (var project in projects)
        {
            if (string.IsNullOrWhiteSpace(project.Slug))
                continue;

            var slug = project.Slug.Trim().ToLowerInvariant();
            if (project.HasDocsSurface)
            {
                HydrateProjectArtifactSurface(
                    project,
                    ProjectDocsSurfaceType.Docs,
                    slug,
                    sourcesRoot,
                    docsSourceCandidates,
                    artifactWorkRoot,
                    timeoutSeconds,
                    artifactToken,
                    artifactCache,
                    stats);
            }

            if (syncApi && project.HasApiSurface)
            {
                HydrateProjectArtifactSurface(
                    project,
                    ProjectDocsSurfaceType.Api,
                    slug,
                    sourcesRoot,
                    apiSourceCandidates,
                    artifactWorkRoot,
                    timeoutSeconds,
                    artifactToken,
                    artifactCache,
                    stats);
            }

            if (syncExamples && project.HasExamplesSurface)
            {
                HydrateProjectArtifactSurface(
                    project,
                    ProjectDocsSurfaceType.Examples,
                    slug,
                    sourcesRoot,
                    examplesSourceCandidates,
                    artifactWorkRoot,
                    timeoutSeconds,
                    artifactToken,
                    artifactCache,
                    stats);
            }
        }
    }

    private static void HydrateProjectArtifactSurface(
        ProjectDocsCatalogItem project,
        ProjectDocsSurfaceType surface,
        string slug,
        string sourcesRoot,
        IReadOnlyList<string> sourceCandidates,
        string artifactWorkRoot,
        int timeoutSeconds,
        string? artifactToken,
        Dictionary<string, string> artifactCache,
        ProjectArtifactHydrationStats stats)
    {
        var artifactSource = TryGetProjectArtifactSource(project, surface);
        if (!IsZipArtifactSource(artifactSource))
            return;

        if (!TryExtractArtifactZip(
                artifactSource!,
                artifactWorkRoot,
                timeoutSeconds,
                artifactToken,
                artifactCache,
                stats,
                out var extractedRoot))
        {
            return;
        }

        var extractedSurfaceRoot = ResolveExistingPathUnderExtractedArtifact(extractedRoot!, slug, sourceCandidates);
        if (string.IsNullOrWhiteSpace(extractedSurfaceRoot))
        {
            stats.Failures++;
            return;
        }

        var candidateTarget = sourceCandidates.Count > 0 ? sourceCandidates[0] : "Docs";
        var targetRoot = BuildProjectSourcePath(sourcesRoot, slug, candidateTarget);
        if (Directory.Exists(targetRoot))
            Directory.Delete(targetRoot, recursive: true);
        CopyDirectory(extractedSurfaceRoot, targetRoot);

        if (surface == ProjectDocsSurfaceType.Docs)
            stats.DocsHydrated++;
        else if (surface == ProjectDocsSurfaceType.Api)
            stats.ApiHydrated++;
        else
            stats.ExamplesHydrated++;
    }

    private static string? TryGetProjectArtifactSource(ProjectDocsCatalogItem project, ProjectDocsSurfaceType surface)
    {
        string? directArtifact = surface switch
        {
            ProjectDocsSurfaceType.Docs => project.ArtifactDocs,
            ProjectDocsSurfaceType.Api => project.ArtifactApi,
            ProjectDocsSurfaceType.Examples => project.ArtifactExamples,
            _ => null
        };

        if (IsZipArtifactSource(directArtifact))
            return directArtifact;

        return surface switch
        {
            ProjectDocsSurfaceType.Docs => project.DocsLink,
            ProjectDocsSurfaceType.Api => !string.IsNullOrWhiteSpace(project.ApiPowerShellLink) ? project.ApiPowerShellLink : project.ApiDotNetLink,
            ProjectDocsSurfaceType.Examples => project.ExamplesLink,
            _ => null
        };
    }

    private static bool IsZipArtifactSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        var value = source.Trim();
        if (value.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            return true;

        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return value.Contains(".zip", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("/zip/", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("archive", StringComparison.OrdinalIgnoreCase);
        }

        return value.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryExtractArtifactZip(
        string source,
        string artifactWorkRoot,
        int timeoutSeconds,
        string? artifactToken,
        Dictionary<string, string> artifactCache,
        ProjectArtifactHydrationStats stats,
        out string? extractedRoot)
    {
        extractedRoot = null;
        if (string.IsNullOrWhiteSpace(source))
            return false;

        var normalizedSource = source.Trim();
        if (normalizedSource.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(normalizedSource, UriKind.Absolute, out var fileUri))
                normalizedSource = fileUri.LocalPath;
        }

        if (!normalizedSource.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !normalizedSource.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalizedSource = Path.GetFullPath(normalizedSource);
            if (!File.Exists(normalizedSource))
                return false;
        }

        if (artifactCache.TryGetValue(normalizedSource, out var cachedExtractRoot) && Directory.Exists(cachedExtractRoot))
        {
            extractedRoot = cachedExtractRoot;
            stats.CacheHits++;
            return true;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedSource))).ToLowerInvariant();
        var downloadsRoot = Path.Combine(artifactWorkRoot, "downloads");
        var extractedBase = Path.Combine(artifactWorkRoot, "extracted");
        Directory.CreateDirectory(downloadsRoot);
        Directory.CreateDirectory(extractedBase);

        var zipPath = Path.Combine(downloadsRoot, hash + ".zip");
        var extractPath = Path.Combine(extractedBase, hash);
        var markerPath = Path.Combine(extractPath, ".extracted.ok");

        if (Directory.Exists(extractPath) && File.Exists(markerPath))
        {
            artifactCache[normalizedSource] = extractPath;
            extractedRoot = extractPath;
            stats.CacheHits++;
            return true;
        }

        try
        {
            if (normalizedSource.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                normalizedSource.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("PowerForge.Web.Cli/project-docs-sync");
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
                if (!string.IsNullOrWhiteSpace(artifactToken))
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", artifactToken);

                using var response = client.GetAsync(normalizedSource).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    stats.Failures++;
                    return false;
                }

                using var responseStream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                using var output = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                responseStream.CopyTo(output);
                stats.Downloads++;
            }
            else
            {
                File.Copy(normalizedSource, zipPath, overwrite: true);
            }

            if (Directory.Exists(extractPath))
                Directory.Delete(extractPath, recursive: true);
            Directory.CreateDirectory(extractPath);
            ZipFile.ExtractToDirectory(zipPath, extractPath);
            File.WriteAllText(markerPath, DateTimeOffset.UtcNow.ToString("O"));

            artifactCache[normalizedSource] = extractPath;
            extractedRoot = extractPath;
            return true;
        }
        catch
        {
            stats.Failures++;
            return false;
        }
    }

    private static string? ResolveExistingPathUnderExtractedArtifact(string extractedRoot, string slug, IReadOnlyList<string> candidates)
    {
        if (string.IsNullOrWhiteSpace(extractedRoot) || !Directory.Exists(extractedRoot))
            return null;

        var roots = new List<string> { extractedRoot };
        var topDirectories = Directory.GetDirectories(extractedRoot);
        if (topDirectories.Length == 1)
            roots.Add(topDirectories[0]);

        foreach (var candidate in candidates)
        {
            var normalized = candidate.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            foreach (var root in roots)
            {
                var direct = Path.GetFullPath(Path.Combine(root, normalized));
                if (Directory.Exists(direct))
                    return direct;

                var withSlug = Path.GetFullPath(Path.Combine(root, slug, normalized));
                if (Directory.Exists(withSlug))
                    return withSlug;
            }
        }

        return null;
    }

    private static void CopyDirectory(string sourceRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        foreach (var filePath in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, filePath);
            var destination = Path.Combine(destinationRoot, relative);
            var destinationDirectory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);
            File.Copy(filePath, destination, overwrite: true);
        }
    }

    private static string ResolveExistingProjectSourcePath(string sourcesRoot, string slug, IReadOnlyList<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            var resolved = BuildProjectSourcePath(sourcesRoot, slug, candidate);
            if (Directory.Exists(resolved))
                return resolved;
        }

        return GetExpectedProjectSourcePath(sourcesRoot, slug, candidates);
    }

    private static string GetExpectedProjectSourcePath(string sourcesRoot, string slug, IReadOnlyList<string> candidates)
    {
        var candidate = candidates.Count > 0 ? candidates[0] : "Docs";
        return BuildProjectSourcePath(sourcesRoot, slug, candidate);
    }

    private static string BuildProjectSourcePath(string sourcesRoot, string slug, string relativeOrAbsolutePath)
    {
        if (Path.IsPathRooted(relativeOrAbsolutePath))
            return Path.GetFullPath(relativeOrAbsolutePath);

        var normalized = relativeOrAbsolutePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(sourcesRoot, slug, normalized));
    }

    private static IReadOnlyList<string> ResolvePathCandidates(
        JsonElement step,
        IReadOnlyList<string> arrayKeys,
        IReadOnlyList<string> scalarKeys,
        IReadOnlyList<string> defaults)
    {
        var values = new List<string>();

        foreach (var arrayKey in arrayKeys)
        {
            var arrayValues = GetArrayOfStrings(step, arrayKey);
            if (arrayValues is null)
                continue;

            foreach (var value in arrayValues)
                AddPathCandidate(values, value);
        }

        foreach (var scalarKey in scalarKeys)
        {
            var value = GetString(step, scalarKey);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var tokens = value.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var token in tokens)
                AddPathCandidate(values, token);
        }

        if (values.Count == 0)
        {
            foreach (var fallback in defaults)
                AddPathCandidate(values, fallback);
        }

        return values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddPathCandidate(ICollection<string> values, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var normalized = value.Trim();
        if (normalized.Length == 0)
            return;

        values.Add(normalized);
    }

    private static string EscapeYamlString(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private sealed class ProjectDocsCatalogItem
    {
        public string Slug { get; init; } = string.Empty;
        public string ContentMode { get; init; } = "hybrid";
        public bool HasDocsSurface { get; init; }
        public bool HasApiDotNetSurface { get; init; }
        public bool HasApiPowerShellSurface { get; init; }
        public bool HasExamplesSurface { get; init; }
        public bool HasApiSurface => HasApiDotNetSurface || HasApiPowerShellSurface;
        public string? DocsLink { get; init; }
        public string? ApiDotNetLink { get; init; }
        public string? ApiPowerShellLink { get; init; }
        public string? ExamplesLink { get; init; }
        public string? ArtifactDocs { get; init; }
        public string? ArtifactApi { get; init; }
        public string? ArtifactExamples { get; init; }
    }

    private enum ProjectDocsSurfaceType
    {
        Docs,
        Api,
        Examples
    }

    private sealed class ProjectArtifactHydrationStats
    {
        public int DocsHydrated { get; set; }
        public int ApiHydrated { get; set; }
        public int ExamplesHydrated { get; set; }
        public int Downloads { get; set; }
        public int CacheHits { get; set; }
        public int Failures { get; set; }
    }
}
