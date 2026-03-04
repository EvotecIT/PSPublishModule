using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
        var syncApi = GetBool(step, "syncApi") ?? GetBool(step, "sync-api") ?? false;
        var failOnMissingApiSource = GetBool(step, "failOnMissingApiSource") ?? GetBool(step, "fail-on-missing-api-source") ?? false;
        var cleanApiTarget = GetBool(step, "cleanApiTarget") ?? GetBool(step, "clean-api-target") ?? cleanTarget;

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

        var apiRoot = ResolvePath(baseDir,
            GetString(step, "apiRoot") ??
            GetString(step, "api-root") ??
            "./data/apidocs");
        if (syncApi && string.IsNullOrWhiteSpace(apiRoot))
            throw new InvalidOperationException("project-docs-sync requires apiRoot when syncApi is enabled.");
        apiRoot = string.IsNullOrWhiteSpace(apiRoot) ? string.Empty : Path.GetFullPath(apiRoot);

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
                    docsSourceCandidates,
                    apiSourceCandidates,
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
                    status: "skipped");
            }

            stepResult.Success = true;
            stepResult.Message = $"project-docs-sync skipped: catalog not found '{catalogPath}'.";
            return;
        }

        if (!Directory.Exists(sourcesRoot))
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
                    docsSourceCandidates,
                    apiSourceCandidates,
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
                    status: "skipped");
            }

            stepResult.Success = true;
            stepResult.Message = $"project-docs-sync skipped: sources root not found '{sourcesRoot}'.";
            return;
        }

        var projects = ReadProjectDocsCatalog(catalogPath);
        var docsProjects = projects.Where(static p => p.HasDocsSurface).ToList();
        var apiProjects = projects.Where(static p => p.HasApiSurface).ToList();
        var synced = 0;
        var skipped = 0;
        var copiedFiles = 0;
        var tocFiles = 0;
        var missingSources = new List<string>();
        var syncedApi = 0;
        var skippedApi = 0;
        var copiedApiFiles = 0;
        var missingApiSources = new List<string>();

        Directory.CreateDirectory(contentRoot);
        if (syncApi)
            Directory.CreateDirectory(apiRoot);

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

        if (!string.IsNullOrWhiteSpace(summaryPath))
        {
            WriteProjectDocsSummary(
                summaryPath,
                catalogPath,
                sourcesRoot,
                contentRoot,
                apiRoot,
                docsSourceCandidates,
                apiSourceCandidates,
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
                status: "updated");
        }

        stepResult.Success = true;
        if (syncApi)
        {
            stepResult.Message = $"project-docs-sync ok: docs={synced}/{docsProjects.Count}; docsSkipped={skipped}; docsFiles={copiedFiles}; toc={tocFiles}; api={syncedApi}/{apiProjects.Count}; apiSkipped={skippedApi}; apiFiles={copiedApiFiles}";
        }
        else
        {
            stepResult.Message = $"project-docs-sync ok: synced={synced}/{docsProjects.Count}; skipped={skipped}; files={copiedFiles}; toc={tocFiles}";
        }
    }

    private static void WriteProjectDocsSummary(
        string summaryPath,
        string catalogPath,
        string sourcesRoot,
        string contentRoot,
        string apiRoot,
        IReadOnlyList<string> docsSourceCandidates,
        IReadOnlyList<string> apiSourceCandidates,
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
            sourceDocsPaths = docsSourceCandidates.ToArray(),
            sourceApiPaths = apiSourceCandidates.ToArray(),
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
            missingApiSources = missingApiSources.ToArray()
        };
        File.WriteAllText(summaryPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static List<ProjectDocsCatalogItem> ReadProjectDocsCatalog(string catalogPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(catalogPath));
        if (!document.RootElement.TryGetProperty("projects", out var projectsElement) || projectsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"project-docs-sync catalog payload is missing 'projects' array: {catalogPath}");

        var projects = new List<ProjectDocsCatalogItem>();
        foreach (var projectElement in projectsElement.EnumerateArray())
        {
            var slug = GetString(projectElement, "slug");
            var hasDocsSurface = false;
            var hasApiDotNetSurface = false;
            var hasApiPowerShellSurface = false;
            if (projectElement.TryGetProperty("surfaces", out var surfacesElement) &&
                surfacesElement.ValueKind == JsonValueKind.Object)
            {
                hasDocsSurface = GetBool(surfacesElement, "docs") ?? false;
                hasApiDotNetSurface = GetBool(surfacesElement, "apiDotNet") ?? false;
                hasApiPowerShellSurface = GetBool(surfacesElement, "apiPowerShell") ?? false;
            }

            projects.Add(new ProjectDocsCatalogItem
            {
                Slug = slug ?? string.Empty,
                HasDocsSurface = hasDocsSurface,
                HasApiDotNetSurface = hasApiDotNetSurface,
                HasApiPowerShellSurface = hasApiPowerShellSurface
            });
        }

        return projects;
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
        public bool HasDocsSurface { get; init; }
        public bool HasApiDotNetSurface { get; init; }
        public bool HasApiPowerShellSurface { get; init; }
        public bool HasApiSurface => HasApiDotNetSurface || HasApiPowerShellSurface;
    }
}
