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
        var syncDocs = GetBool(step, "syncDocs") ?? GetBool(step, "sync-docs") ?? true;
        var syncApi = GetBool(step, "syncApi") ?? GetBool(step, "sync-api") ?? false;
        var failOnMissingApiSource = GetBool(step, "failOnMissingApiSource") ?? GetBool(step, "fail-on-missing-api-source") ?? false;
        var cleanApiTarget = GetBool(step, "cleanApiTarget") ?? GetBool(step, "clean-api-target") ?? cleanTarget;
        var syncExamples = GetBool(step, "syncExamples") ?? GetBool(step, "sync-examples") ?? true;
        var failOnMissingExamplesSource = GetBool(step, "failOnMissingExamplesSource") ?? GetBool(step, "fail-on-missing-examples-source") ?? false;
        var cleanExamplesTarget = GetBool(step, "cleanExamplesTarget") ?? GetBool(step, "clean-examples-target") ?? cleanTarget;
        var includeDedicatedExternal = GetBool(step, "includeDedicatedExternal") ?? GetBool(step, "include-dedicated-external") ?? false;
        var hydrateFromArtifacts = GetBool(step, "hydrateFromArtifacts") ?? GetBool(step, "hydrate-from-artifacts") ?? true;
        var artifactTimeoutSeconds = GetInt(step, "artifactTimeoutSeconds") ?? GetInt(step, "artifact-timeout-seconds") ?? 60;
        if (artifactTimeoutSeconds < 5)
            artifactTimeoutSeconds = 5;
        var docsSectionFolder = NormalizeRelativePathSegment(GetString(step, "docsSectionFolder") ?? GetString(step, "docs-section-folder"));
        var apiSectionFolder = NormalizeRelativePathSegment(GetString(step, "apiSectionFolder") ?? GetString(step, "api-section-folder"));
        var examplesSectionFolder = NormalizeRelativePathSegment(GetString(step, "examplesSectionFolder") ?? GetString(step, "examples-section-folder"));

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
            defaults: new[] { "Website/content/examples", "content/examples" });
        var selectedProjectSlugs = ResolveProjectDocsProjectFilter(step);

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
        if (selectedProjectSlugs.Count > 0)
        {
            projects = projects
                .Where(project => selectedProjectSlugs.Contains(NormalizeSlug(project.Slug)))
                .ToList();
        }
        var docsProjects = syncDocs
            ? projects.Where(p => p.HasDocsSurface && (includeDedicatedExternal || !p.ContentMode.Equals("external", StringComparison.OrdinalIgnoreCase))).ToList()
            : new List<ProjectDocsCatalogItem>();
        var apiProjects = syncApi
            ? projects.Where(p => p.HasApiSurface && (includeDedicatedExternal || !p.ContentMode.Equals("external", StringComparison.OrdinalIgnoreCase))).ToList()
            : new List<ProjectDocsCatalogItem>();
        var examplesProjects = syncExamples
            ? projects.Where(p => p.HasExamplesSurface && (includeDedicatedExternal || !p.ContentMode.Equals("external", StringComparison.OrdinalIgnoreCase))).ToList()
            : new List<ProjectDocsCatalogItem>();
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
        var examplesArtifactSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

            var targetDocsProjectRoot = Path.Combine(contentRoot, slug);
            var targetDocsRoot = string.IsNullOrWhiteSpace(docsSectionFolder)
                ? targetDocsProjectRoot
                : Path.Combine(targetDocsProjectRoot, docsSectionFolder);
            if (cleanTarget && Directory.Exists(targetDocsProjectRoot))
                Directory.Delete(targetDocsProjectRoot, recursive: true);
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

            StampProjectDocsMetadata(targetDocsRoot, slug, GetProjectDisplayName(project.GitHubRepoUrl, slug));

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

                var targetApiProjectRoot = Path.Combine(apiRoot, slug);
                var targetApiRoot = string.IsNullOrWhiteSpace(apiSectionFolder)
                    ? targetApiProjectRoot
                    : Path.Combine(targetApiProjectRoot, apiSectionFolder);
                if (cleanApiTarget && Directory.Exists(targetApiProjectRoot))
                    Directory.Delete(targetApiProjectRoot, recursive: true);
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

                var targetExamplesProjectRoot = Path.Combine(examplesRoot, slug);
                var targetExamplesRoot = string.IsNullOrWhiteSpace(examplesSectionFolder)
                    ? targetExamplesProjectRoot
                    : Path.Combine(targetExamplesProjectRoot, examplesSectionFolder);
                if (cleanExamplesTarget && Directory.Exists(targetExamplesProjectRoot))
                    Directory.Delete(targetExamplesProjectRoot, recursive: true);
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

                var matchedExamplesCandidate = TryGetMatchedSourceCandidate(sourcesRoot, slug, sourceExamplesRoot, examplesSourceCandidates);
                if (!string.IsNullOrWhiteSpace(matchedExamplesCandidate))
                    examplesArtifactSources[slug] = matchedExamplesCandidate;

                MaterializeProjectExampleDocs(targetExamplesRoot, project);
                syncedExamples++;
            }
        }

        UpdateProjectSurfaceAvailabilityMetadata(
            projects,
            projectsContentRoot,
            contentRoot,
            apiRoot,
            examplesRoot,
            examplesArtifactSources);

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
        string examplesRoot,
        IReadOnlyDictionary<string, string> examplesArtifactSources)
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
            var docsAvailable = HasProjectMarkdownSurfaceContent(docsRoot, slug);
            var apiAvailable = HasProjectArtifactSurfaceContent(apiRoot, slug);
            var examplesAvailable = HasProjectMarkdownSurfaceContent(examplesRoot, slug);

            examplesArtifactSources.TryGetValue(slug, out var examplesArtifactSource);

            UpdateProjectLocalSurfaceFlags(Path.Combine(projectsContentRoot, slug + ".md"), docsAvailable, apiAvailable, examplesAvailable, examplesArtifactSource);
            UpdateProjectLocalSurfaceFlags(Path.Combine(projectsContentRoot, slug + ".docs.md"), docsAvailable, apiAvailable, examplesAvailable, examplesArtifactSource);
            UpdateProjectLocalSurfaceFlags(Path.Combine(projectsContentRoot, slug + ".api.md"), docsAvailable, apiAvailable, examplesAvailable, examplesArtifactSource);
            UpdateProjectLocalSurfaceFlags(Path.Combine(projectsContentRoot, slug + ".examples.md"), docsAvailable, apiAvailable, examplesAvailable, examplesArtifactSource);

            var nestedSectionsRoot = Path.Combine(projectsContentRoot, slug);
            UpdateProjectLocalSurfaceFlags(Path.Combine(nestedSectionsRoot, "docs.md"), docsAvailable, apiAvailable, examplesAvailable, examplesArtifactSource);
            UpdateProjectLocalSurfaceFlags(Path.Combine(nestedSectionsRoot, "api.md"), docsAvailable, apiAvailable, examplesAvailable, examplesArtifactSource);
            UpdateProjectLocalSurfaceFlags(Path.Combine(nestedSectionsRoot, "examples.md"), docsAvailable, apiAvailable, examplesAvailable, examplesArtifactSource);
        }
    }

    private static bool HasProjectMarkdownSurfaceContent(string root, string slug)
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

    private static bool HasProjectArtifactSurfaceContent(string root, string slug)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(slug))
            return false;
        if (!Directory.Exists(root))
            return false;

        var projectRoot = Path.Combine(root, slug);
        if (!Directory.Exists(projectRoot))
            return false;

        return Directory.EnumerateFiles(projectRoot, "*", SearchOption.AllDirectories)
            .Any(static path => !string.IsNullOrWhiteSpace(Path.GetFileName(path)));
    }

    private static void StampProjectDocsMetadata(string targetDocsRoot, string slug, string projectName)
    {
        if (string.IsNullOrWhiteSpace(targetDocsRoot) || !Directory.Exists(targetDocsRoot) || string.IsNullOrWhiteSpace(slug))
            return;

        var normalizedSlug = slug.Trim().ToLowerInvariant();
        var hubPath = $"/projects/{normalizedSlug}/";
        var docsPath = hubPath + "docs/";

        foreach (var markdownPath in Directory.EnumerateFiles(targetDocsRoot, "*", SearchOption.AllDirectories).Where(static path => IsMarkdownExtension(Path.GetExtension(path))))
        {
            var content = File.ReadAllText(markdownPath);
            var changed = false;
            var hasFrontMatter = TryGetFrontMatterLines(content, out var allLines);
            if (!hasFrontMatter)
            {
                var normalizedContent = content.TrimStart('\uFEFF');
                allLines = new List<string>
                {
                    "---",
                    $"title: {YamlQuote(GetMarkdownHeadingTitle(normalizedContent) ?? HumanizeExampleTitle(Path.GetFileNameWithoutExtension(markdownPath)))}",
                    "layout: docs",
                    "---",
                    string.Empty
                };
                allLines.AddRange(normalizedContent.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'));
                changed = true;
            }

            var closingIndex = FindFrontMatterClosingMarkerIndex(allLines);
            if (closingIndex <= 0)
                continue;

            if (!TryGetFrontMatterValue(allLines, closingIndex, "layout", out var layoutValue) || string.IsNullOrWhiteSpace(layoutValue))
            {
                changed |= UpsertFrontMatterString(allLines, closingIndex, "layout", "docs");
                closingIndex = FindFrontMatterClosingMarkerIndex(allLines);
            }

            changed |= UpsertFrontMatterString(allLines, closingIndex, "meta.generated_by", "powerforge.project-docs-sync");
            closingIndex = FindFrontMatterClosingMarkerIndex(allLines);
            changed |= UpsertFrontMatterString(allLines, closingIndex, "meta.project_base_slug", normalizedSlug);
            closingIndex = FindFrontMatterClosingMarkerIndex(allLines);
            changed |= UpsertFrontMatterString(allLines, closingIndex, "meta.project_name", projectName);
            closingIndex = FindFrontMatterClosingMarkerIndex(allLines);
            changed |= UpsertFrontMatterString(allLines, closingIndex, "meta.project_section", "docs");
            closingIndex = FindFrontMatterClosingMarkerIndex(allLines);
            changed |= UpsertFrontMatterString(allLines, closingIndex, "meta.project_hub_path", hubPath);
            closingIndex = FindFrontMatterClosingMarkerIndex(allLines);
            changed |= UpsertFrontMatterString(allLines, closingIndex, "meta.project_link_docs", docsPath);

            if (!changed)
                continue;

            var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            File.WriteAllText(markdownPath, string.Join(newline, allLines), Encoding.UTF8);
        }
    }

    private static void MaterializeProjectExampleDocs(string targetExamplesRoot, ProjectDocsCatalogItem project)
    {
        if (string.IsNullOrWhiteSpace(targetExamplesRoot) || !Directory.Exists(targetExamplesRoot))
            return;

        var slug = string.IsNullOrWhiteSpace(project.Slug) ? "project" : project.Slug.Trim().ToLowerInvariant();
        var projectName = GetProjectDisplayName(project.GitHubRepoUrl, slug);
        var sourceRepo = string.IsNullOrWhiteSpace(project.GitHubRepoUrl)
            ? null
            : project.GitHubRepoUrl.Trim();

        var scriptFiles = Directory
            .EnumerateFiles(targetExamplesRoot, "*.ps1", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var scriptPath in scriptFiles)
        {
            var companionMarkdown = Path.ChangeExtension(scriptPath, ".md");
            if (File.Exists(companionMarkdown) && !IsGeneratedExampleMarkdown(companionMarkdown))
                continue;

            var title = HumanizeExampleTitle(Path.GetFileNameWithoutExtension(scriptPath));
            var relativeFolder = Path.GetDirectoryName(Path.GetRelativePath(targetExamplesRoot, scriptPath)) ?? string.Empty;
            var lines = new List<string>
            {
                "---",
                $"title: {YamlQuote(title)}",
                "layout: docs",
                $"meta.generated_by: {YamlQuote("powerforge.project-docs-sync")}",
                "---",
                string.Empty,
                $"Source-owned example maintained with {projectName}.",
                string.Empty
            };

            if (!string.IsNullOrWhiteSpace(sourceRepo))
            {
                lines.Add($"- Source repository: [{sourceRepo}]({sourceRepo})");
                if (!string.IsNullOrWhiteSpace(relativeFolder))
                    lines.Add($"- Example group: `{relativeFolder.Replace('\\', '/')}`");
                lines.Add(string.Empty);
            }

            lines.Add("```powershell");
            lines.AddRange(File.ReadAllLines(scriptPath));
            lines.Add("```");

            File.WriteAllText(companionMarkdown, string.Join(Environment.NewLine, lines), Encoding.UTF8);
        }

        var markdownFiles = Directory
            .EnumerateFiles(targetExamplesRoot, "*", SearchOption.AllDirectories)
            .Where(static path => IsMarkdownExtension(Path.GetExtension(path)))
            .Where(static path => !string.Equals(Path.GetFileName(path), "_index.md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var markdownPath in markdownFiles)
        {
            var existingContent = File.ReadAllText(markdownPath);
            if (HasYamlFrontMatter(existingContent))
                continue;

            var title = HumanizeExampleTitle(Path.GetFileNameWithoutExtension(markdownPath));
            var relativeFolder = Path.GetDirectoryName(Path.GetRelativePath(targetExamplesRoot, markdownPath)) ?? string.Empty;
            var lines = new List<string>
            {
                "---",
                $"title: {YamlQuote(title)}",
                "layout: docs",
                $"meta.generated_by: {YamlQuote("powerforge.project-docs-sync")}",
                "---",
                string.Empty
            };

            if (!string.IsNullOrWhiteSpace(sourceRepo))
            {
                lines.Add($"- Source repository: [{sourceRepo}]({sourceRepo})");
                if (!string.IsNullOrWhiteSpace(relativeFolder))
                    lines.Add($"- Example group: `{relativeFolder.Replace('\\', '/')}`");
                lines.Add(string.Empty);
            }

            lines.Add(existingContent.TrimStart('\uFEFF'));
            File.WriteAllText(markdownPath, string.Join(Environment.NewLine, lines), Encoding.UTF8);
        }

        var directories = Directory
            .EnumerateDirectories(targetExamplesRoot, "*", SearchOption.AllDirectories)
            .OrderBy(static path => path.Length)
            .ThenBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Prepend(targetExamplesRoot)
            .ToList();

        foreach (var directory in directories)
        {
            var indexPath = Path.Combine(directory, "_index.md");
            if (File.Exists(indexPath) && !IsGeneratedExampleMarkdown(indexPath))
                continue;

            var relativeDirectory = Path.GetRelativePath(targetExamplesRoot, directory);
            var directoryName = directory.Equals(targetExamplesRoot, StringComparison.OrdinalIgnoreCase)
                ? projectName
                : HumanizeExampleTitle(Path.GetFileName(directory));

            var childDirectories = Directory
                .EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var childMarkdown = Directory
                .EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly)
                .Where(static path => !string.Equals(Path.GetFileName(path), "_index.md", StringComparison.OrdinalIgnoreCase))
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var lines = new List<string>
            {
                "---",
                $"title: {YamlQuote(directory.Equals(targetExamplesRoot, StringComparison.OrdinalIgnoreCase) ? $"{projectName} Examples" : directoryName)}",
                $"description: {YamlQuote(directory.Equals(targetExamplesRoot, StringComparison.OrdinalIgnoreCase) ? $"Project-scoped examples for {projectName}." : $"Example group for {directoryName}.")}",
                "layout: docs",
                $"meta.generated_by: {YamlQuote("powerforge.project-docs-sync")}",
                "---",
                string.Empty,
                directory.Equals(targetExamplesRoot, StringComparison.OrdinalIgnoreCase)
                    ? $"Browse runnable examples and usage patterns maintained with {projectName}."
                    : $"Examples in the `{relativeDirectory.Replace('\\', '/')}` group.",
                string.Empty
            };

            if (!string.IsNullOrWhiteSpace(sourceRepo))
            {
                lines.Add($"- Source repository: [{sourceRepo}]({sourceRepo})");
                lines.Add(string.Empty);
            }

            if (childDirectories.Count > 0)
            {
                lines.Add("## Groups");
                lines.Add(string.Empty);
                foreach (var childDirectory in childDirectories)
                {
                    var childName = HumanizeExampleTitle(Path.GetFileName(childDirectory));
                    lines.Add($"- [{childName}](./{Path.GetFileName(childDirectory)}/)");
                }
                lines.Add(string.Empty);
            }

            if (childMarkdown.Count > 0)
            {
                lines.Add("## Examples");
                lines.Add(string.Empty);
                foreach (var childFile in childMarkdown)
                {
                    var childName = HumanizeExampleTitle(Path.GetFileNameWithoutExtension(childFile));
                    lines.Add($"- [{childName}](./{Path.GetFileNameWithoutExtension(childFile)}/)");
                }
                lines.Add(string.Empty);
            }

            File.WriteAllText(indexPath, string.Join(Environment.NewLine, lines), Encoding.UTF8);
        }

        StampProjectExampleMetadata(targetExamplesRoot, slug, projectName);
    }

    private static void StampProjectExampleMetadata(string targetExamplesRoot, string slug, string projectName)
    {
        if (string.IsNullOrWhiteSpace(targetExamplesRoot) || !Directory.Exists(targetExamplesRoot) || string.IsNullOrWhiteSpace(slug))
            return;

        var normalizedSlug = slug.Trim().ToLowerInvariant();
        var hubPath = $"/projects/{normalizedSlug}/";
        var examplesPath = hubPath + "examples/";

        foreach (var markdownPath in Directory.EnumerateFiles(targetExamplesRoot, "*", SearchOption.AllDirectories).Where(static path => IsMarkdownExtension(Path.GetExtension(path))))
        {
            var content = File.ReadAllText(markdownPath);
            if (!TryGetFrontMatterLines(content, out var allLines))
                continue;

            var closingIndex = FindFrontMatterClosingMarkerIndex(allLines);
            if (closingIndex <= 0)
                continue;

            var changed = false;
            changed |= UpsertFrontMatterString(allLines, closingIndex, "meta.project_base_slug", normalizedSlug);
            closingIndex = FindFrontMatterClosingMarkerIndex(allLines);
            changed |= UpsertFrontMatterString(allLines, closingIndex, "meta.project_name", projectName);
            closingIndex = FindFrontMatterClosingMarkerIndex(allLines);
            changed |= UpsertFrontMatterString(allLines, closingIndex, "meta.project_section", "examples");
            closingIndex = FindFrontMatterClosingMarkerIndex(allLines);
            changed |= UpsertFrontMatterString(allLines, closingIndex, "meta.project_hub_path", hubPath);
            closingIndex = FindFrontMatterClosingMarkerIndex(allLines);
            changed |= UpsertFrontMatterString(allLines, closingIndex, "meta.project_link_examples", examplesPath);

            if (!changed)
                continue;

            var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            File.WriteAllText(markdownPath, string.Join(newline, allLines), Encoding.UTF8);
        }
    }

    private static string HumanizeExampleTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Example";

        var normalized = value.Replace('_', ' ').Replace('-', ' ').Trim();
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized);
    }

    private static string? GetMarkdownHeadingTitle(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        foreach (var line in content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("# ", StringComparison.Ordinal))
                continue;

            var title = trimmed.TrimStart('#').Trim();
            if (!string.IsNullOrWhiteSpace(title))
                return title;
        }

        return null;
    }

    private static string GetProjectDisplayName(string? sourceRepoUrl, string slug)
    {
        if (!string.IsNullOrWhiteSpace(sourceRepoUrl) &&
            Uri.TryCreate(sourceRepoUrl.Trim(), UriKind.Absolute, out var uri))
        {
            var repoSegment = uri.Segments.LastOrDefault();
            if (!string.IsNullOrWhiteSpace(repoSegment))
            {
                var repoName = repoSegment.Trim('/').Trim();
                if (repoName.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                    repoName = repoName[..^4];

                if (!string.IsNullOrWhiteSpace(repoName))
                    return repoName;
            }
        }

        var normalizedSlug = string.IsNullOrWhiteSpace(slug) ? "Project" : slug.Replace('-', ' ').Replace('_', ' ').Trim();
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalizedSlug);
    }

    private static bool IsGeneratedExampleMarkdown(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        var content = File.ReadAllText(path);
        return content.Contains("meta.generated_by: \"powerforge.project-docs-sync\"", StringComparison.Ordinal);
    }

    private static bool HasYamlFrontMatter(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        var normalized = content.TrimStart('\uFEFF');
        return normalized.StartsWith("---", StringComparison.Ordinal);
    }

    private static bool IsMarkdownExtension(string? extension)
    {
        return string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".markdown", StringComparison.OrdinalIgnoreCase);
    }

    private static void UpdateProjectLocalSurfaceFlags(
        string markdownPath,
        bool docsAvailable,
        bool apiAvailable,
        bool examplesAvailable,
        string? examplesArtifactSource = null)
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
        closingIndex = FindFrontMatterClosingMarkerIndex(allLines);

        var hasExamplesLink =
            TryGetFrontMatterValue(allLines, closingIndex, "meta.project_link_examples", out var projectExamplesLink) &&
            !string.IsNullOrWhiteSpace(projectExamplesLink);
        var isExamplesSection =
            TryGetFrontMatterValue(allLines, closingIndex, "meta.project_section", out var projectSection) &&
            string.Equals(projectSection, "examples", StringComparison.OrdinalIgnoreCase);

        if (examplesAvailable && (hasExamplesLink || isExamplesSection))
        {
            changed |= UpsertFrontMatterBoolean(allLines, closingIndex, "meta.project_surface_examples", true);
            closingIndex = FindFrontMatterClosingMarkerIndex(allLines);

            if (!hasExamplesLink &&
                TryGetFrontMatterValue(allLines, closingIndex, "meta.project_hub_path", out var projectHubPath) &&
                !string.IsNullOrWhiteSpace(projectHubPath))
            {
                var normalizedHubPath = projectHubPath.Trim();
                if (!normalizedHubPath.EndsWith("/", StringComparison.Ordinal))
                    normalizedHubPath += "/";

                changed |= UpsertFrontMatterString(allLines, closingIndex, "meta.project_link_examples", normalizedHubPath + "examples/");
                closingIndex = FindFrontMatterClosingMarkerIndex(allLines);
            }

            if (!string.IsNullOrWhiteSpace(examplesArtifactSource))
                changed |= UpsertFrontMatterString(allLines, closingIndex, "meta.project_artifact_examples", examplesArtifactSource);
        }

        if (!changed)
            return;

        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        File.WriteAllText(markdownPath, string.Join(newline, allLines), Encoding.UTF8);
    }

    private static string? TryGetMatchedSourceCandidate(
        string sourcesRoot,
        string slug,
        string resolvedSourcePath,
        IReadOnlyList<string> sourceCandidates)
    {
        if (string.IsNullOrWhiteSpace(sourcesRoot) ||
            string.IsNullOrWhiteSpace(slug) ||
            string.IsNullOrWhiteSpace(resolvedSourcePath) ||
            sourceCandidates is null ||
            sourceCandidates.Count == 0)
        {
            return null;
        }

        var resolvedFullPath = Path.GetFullPath(resolvedSourcePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var candidate in sourceCandidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            var candidatePath = Path.GetFullPath(Path.Combine(sourcesRoot, slug, candidate))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(candidatePath, resolvedFullPath, StringComparison.OrdinalIgnoreCase))
                return candidate.Replace('\\', '/');
        }

        return null;
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

    private static bool UpsertFrontMatterString(List<string> allLines, int closingMarkerIndex, string key, string value)
    {
        if (allLines is null || allLines.Count == 0 || closingMarkerIndex <= 0 || string.IsNullOrWhiteSpace(value))
            return false;

        var expected = $"{key}: \"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
        for (var i = 1; i < closingMarkerIndex; i++)
        {
            var currentLine = allLines[i];
            if (!currentLine.TrimStart().StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(currentLine.Trim(), expected, StringComparison.Ordinal))
                return false;

            // Match keys case-insensitively but rewrite the canonical generated casing.
            allLines[i] = expected;
            return true;
        }

        allLines.Insert(closingMarkerIndex, expected);
        return true;
    }

    private static bool TryGetFrontMatterValue(IReadOnlyList<string> allLines, int closingMarkerIndex, string key, out string? value)
    {
        value = null;
        if (allLines is null || allLines.Count == 0 || closingMarkerIndex <= 0)
            return false;

        for (var i = 1; i < closingMarkerIndex; i++)
        {
            var currentLine = allLines[i];
            if (!currentLine.TrimStart().StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
                continue;

            var separatorIndex = currentLine.IndexOf(':');
            if (separatorIndex < 0 || separatorIndex + 1 >= currentLine.Length)
                return false;

            value = currentLine[(separatorIndex + 1)..].Trim().Trim('"');
            return true;
        }

        return false;
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
            string? sourceLink = null;
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
                sourceLink = GetString(linksElement, "source");
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
                GitHubRepoUrl = sourceLink,
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

    private static HashSet<string> ResolveProjectDocsProjectFilter(JsonElement step)
    {
        var selected = ParseTokenSet(
            GetString(step, "projects") ??
            GetString(step, "project") ??
            GetString(step, "projectSlugs") ??
            GetString(step, "project-slugs"));

        foreach (var key in new[] { "projects", "projectSlugs", "project-slugs" })
        {
            var values = GetArrayOfStrings(step, key);
            if (values is null)
                continue;

            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    selected.Add(value.Trim());
            }
        }

        if (selected.Count == 0)
            return selected;

        return selected
            .Select(NormalizeSlug)
            .Where(static slug => !string.IsNullOrWhiteSpace(slug))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
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

    private static string NormalizeRelativePathSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim()
            .Replace('\\', '/')
            .Trim('/');
        if (normalized.Length == 0)
            return string.Empty;
        if (normalized.Contains("..", StringComparison.Ordinal))
            throw new InvalidOperationException($"project-docs-sync section folder cannot contain '..': {value}");
        if (Path.IsPathRooted(normalized))
            throw new InvalidOperationException($"project-docs-sync section folder must be relative: {value}");

        return normalized.Replace('/', Path.DirectorySeparatorChar);
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
        public string? GitHubRepoUrl { get; init; }
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
