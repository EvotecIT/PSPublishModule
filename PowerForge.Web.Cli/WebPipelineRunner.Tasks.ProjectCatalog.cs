using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static readonly string[] GeneratedProjectMarkers =
    {
        "meta.generated_by: powerforge.project-catalog",
        "meta.generated_by: update-project-pages",
        "meta.generated_by: generate-project-section-pages"
    };

    private static readonly string[] AllowedProjectModes = { "hub-full", "dedicated-external" };
    private static readonly string[] AllowedProjectContentModes = { "hybrid", "external" };
    private static readonly string[] AllowedProjectStatuses = { "active", "archived", "deprecated", "experimental" };
    private static readonly string[] AllowedProjectSurfaceKeys = { "docs", "apiDotNet", "apiPowerShell", "examples", "changelog", "releases" };
    private static readonly string[] AllowedProjectLinkKeys = { "docs", "apiDotNet", "apiPowerShell", "examples", "changelog", "releases", "source", "website", "nuget", "powerShellGallery", "blog" };

    private static void ExecuteProjectCatalog(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var catalogPath = ResolvePath(baseDir, GetString(step, "catalog") ?? GetString(step, "catalogPath") ?? GetString(step, "catalog-path") ?? "./data/projects/catalog.json");
        if (string.IsNullOrWhiteSpace(catalogPath))
            throw new InvalidOperationException("project-catalog requires catalog path.");
        if (!File.Exists(catalogPath))
            throw new InvalidOperationException($"project-catalog catalog file not found: {catalogPath}");

        var sourcesRoot = ResolvePath(baseDir, GetString(step, "sourcesRoot") ?? GetString(step, "sources-root") ?? "./projects-sources");
        var contentRoot = ResolvePath(baseDir, GetString(step, "contentRoot") ?? GetString(step, "content-root") ?? "./content/projects");
        var publishPath = ResolvePath(baseDir, GetString(step, "publishPath") ?? GetString(step, "publish-path") ?? "./static/data/projects/catalog.json");
        var summaryPath = ResolvePath(baseDir, GetString(step, "summaryPath") ?? GetString(step, "summary-path") ?? "./Build/project-catalog-last-run.json");
        var curationCsvPath = ResolvePath(baseDir, GetString(step, "curationCsv") ?? GetString(step, "curation-csv") ?? "./data/projects/curation.csv");
        var statsPath = ResolvePath(baseDir, GetString(step, "statsPath") ?? GetString(step, "stats-path") ?? "./data/ecosystem/stats.json");

        var importManifests = GetBool(step, "importManifests") ?? GetBool(step, "import-manifests") ?? true;
        var applyCuration = GetBool(step, "applyCuration") ?? GetBool(step, "apply-curation") ?? true;
        var validate = GetBool(step, "validate") ?? true;
        var failOnWarnings = GetBool(step, "failOnWarnings") ?? GetBool(step, "fail-on-warnings") ?? false;
        var allowCreateProjects = GetBool(step, "allowCreateProjects") ?? GetBool(step, "allow-create-projects") ?? false;
        var generatePages = GetBool(step, "generatePages") ?? GetBool(step, "generate-pages") ?? true;
        var generateSections = GetBool(step, "generateSections") ?? GetBool(step, "generate-sections") ?? true;
        var forceOverwriteExisting = GetBool(step, "forceOverwriteExisting") ?? GetBool(step, "force-overwrite-existing") ?? false;
        var includeUnlistedInIndex = GetBool(step, "includeUnlistedInIndex") ?? GetBool(step, "include-unlisted-in-index") ?? false;
        var mergeTelemetry = GetBool(step, "mergeTelemetry") ?? GetBool(step, "merge-telemetry") ?? true;
        var mergeReleaseTelemetry = GetBool(step, "mergeReleaseTelemetry") ?? GetBool(step, "merge-release-telemetry") ?? false;
        var hubSectionLinkTarget = NormalizeHubSectionLinkTarget(
            GetString(step, "hubSectionLinkTarget") ??
            GetString(step, "hub-section-link-target") ??
            GetString(step, "sectionLinkTarget") ??
            GetString(step, "section-link-target"));
        var bootstrapFromStats = GetBool(step, "bootstrapFromStats") ?? GetBool(step, "bootstrap-from-stats") ?? false;
        var bootstrapTop = GetInt(step, "bootstrapTop") ?? GetInt(step, "bootstrap-top") ?? 0;
        var bootstrapMinimumStars = GetInt(step, "bootstrapMinimumStars") ?? GetInt(step, "bootstrap-minimum-stars") ?? 0;
        var bootstrapIncludeArchived = GetBool(step, "bootstrapIncludeArchived") ?? GetBool(step, "bootstrap-include-archived") ?? false;
        var bootstrapExcludeRepos = ParseTokenSet(
            GetString(step, "bootstrapExcludeRepos") ??
            GetString(step, "bootstrap-exclude-repos") ??
            GetString(step, "excludeRepos") ??
            GetString(step, "exclude-repos"));
        var githubToken = GetString(step, "githubToken") ?? GetString(step, "github-token") ?? GetString(step, "token");
        var githubTokenEnv = GetString(step, "githubTokenEnv") ?? GetString(step, "github-token-env") ?? GetString(step, "tokenEnv") ?? GetString(step, "token-env");
        if (string.IsNullOrWhiteSpace(githubToken) && !string.IsNullOrWhiteSpace(githubTokenEnv))
            githubToken = Environment.GetEnvironmentVariable(githubTokenEnv);
        var githubApiBaseUrl = GetString(step, "githubApiBaseUrl") ?? GetString(step, "github-api-base-url") ?? "https://api.github.com";
        var releaseTimeoutSeconds = GetInt(step, "releaseTimeoutSeconds") ?? GetInt(step, "release-timeout-seconds") ?? 20;

        var serializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        var catalog = JsonSerializer.Deserialize<ProjectCatalogDocument>(File.ReadAllText(catalogPath), serializerOptions)
                      ?? new ProjectCatalogDocument();
        catalog.Projects ??= new List<ProjectCatalogEntry>();

        var projectBySlug = new Dictionary<string, ProjectCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in catalog.Projects)
        {
            var slug = NormalizeSlug(project.Slug);
            if (string.IsNullOrWhiteSpace(slug))
                continue;
            if (!projectBySlug.ContainsKey(slug))
                projectBySlug[slug] = project;
        }

        var discoveredManifests = 0;
        var importedManifests = 0;
        var createdProjects = 0;
        var skippedManifests = 0;

        if (importManifests && !string.IsNullOrWhiteSpace(sourcesRoot) && Directory.Exists(sourcesRoot))
        {
            foreach (var projectDir in Directory.GetDirectories(sourcesRoot))
            {
                var manifestPath = FindProjectManifestPath(projectDir);
                if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
                    continue;

                discoveredManifests++;
                ProjectManifestData? manifest;
                try
                {
                    manifest = JsonSerializer.Deserialize<ProjectManifestData>(File.ReadAllText(manifestPath), serializerOptions);
                }
                catch
                {
                    skippedManifests++;
                    continue;
                }

                if (manifest is null)
                {
                    skippedManifests++;
                    continue;
                }

                var slug = NormalizeSlug(manifest.Slug);
                if (string.IsNullOrWhiteSpace(slug))
                    slug = NormalizeSlug(Path.GetFileName(projectDir));
                if (string.IsNullOrWhiteSpace(slug))
                {
                    skippedManifests++;
                    continue;
                }

                if (!projectBySlug.TryGetValue(slug, out var project))
                {
                    if (!allowCreateProjects)
                    {
                        skippedManifests++;
                        continue;
                    }

                    project = new ProjectCatalogEntry
                    {
                        Slug = slug,
                        Name = string.IsNullOrWhiteSpace(manifest.Name) ? slug : manifest.Name!.Trim(),
                        Mode = NormalizeProjectMode(manifest.Mode, fallback: "hub-full"),
                        ContentMode = NormalizeProjectContentMode(manifest.ContentMode, manifest.Mode ?? "hub-full"),
                        HubPath = $"/projects/{slug}/",
                        Description = string.IsNullOrWhiteSpace(manifest.Description) ? $"{slug} project page." : manifest.Description!.Trim(),
                        Links = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
                        Surfaces = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
                        Artifacts = new ProjectCatalogArtifacts()
                    };
                    catalog.Projects.Add(project);
                    projectBySlug[slug] = project;
                    createdProjects++;
                }

                MergeManifestIntoProject(project, manifest, manifestPath);
                importedManifests++;
            }
        }

        var bootstrapCreatedProjects = 0;
        if (bootstrapFromStats && !string.IsNullOrWhiteSpace(statsPath) && File.Exists(statsPath))
        {
            bootstrapCreatedProjects = BootstrapProjectsFromStats(
                catalog.Projects,
                projectBySlug,
                statsPath,
                serializerOptions,
                allowCreateProjects,
                bootstrapTop,
                bootstrapMinimumStars,
                bootstrapIncludeArchived,
                bootstrapExcludeRepos);
        }

        var curationUpdates = 0;
        if (applyCuration && !string.IsNullOrWhiteSpace(curationCsvPath) && File.Exists(curationCsvPath))
            curationUpdates = ApplyProjectCuration(projectBySlug, curationCsvPath);

        var telemetryMerged = 0;
        if (mergeTelemetry && !string.IsNullOrWhiteSpace(statsPath) && File.Exists(statsPath))
            telemetryMerged = MergeProjectTelemetry(catalog.Projects, statsPath, serializerOptions);

        var releaseTelemetryMerged = 0;
        if (mergeReleaseTelemetry)
            releaseTelemetryMerged = MergeProjectReleaseTelemetry(catalog.Projects, githubToken, githubApiBaseUrl, releaseTimeoutSeconds);

        NormalizeProjectCatalogContracts(catalog.Projects, hubSectionLinkTarget);

        catalog.GeneratedOn = DateTimeOffset.UtcNow.ToString("O");
        catalog.Projects = catalog.Projects
            .OrderBy(static value => value.Name ?? value.Slug ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<ProjectCatalogFinding>? findings = null;
        if (validate)
        {
            findings = ValidateProjectCatalog(catalog.Projects);
            var errors = findings.Count(f => f.Level.Equals("error", StringComparison.OrdinalIgnoreCase));
            var warnings = findings.Count(f => f.Level.Equals("warning", StringComparison.OrdinalIgnoreCase));
            if (errors > 0)
                throw new InvalidOperationException($"project-catalog validation failed: {errors} error(s), {warnings} warning(s).");
            if (failOnWarnings && warnings > 0)
                throw new InvalidOperationException($"project-catalog validation failed: warnings detected ({warnings}) and failOnWarnings is enabled.");
        }

        var writeCatalogDirectory = Path.GetDirectoryName(catalogPath);
        if (!string.IsNullOrWhiteSpace(writeCatalogDirectory))
            Directory.CreateDirectory(writeCatalogDirectory);
        File.WriteAllText(catalogPath, JsonSerializer.Serialize(catalog, serializerOptions));

        var pagesWritten = 0;
        var pagesSkipped = 0;
        if (generatePages)
            GenerateProjectPages(catalog.Projects, contentRoot ?? string.Empty, forceOverwriteExisting, includeUnlistedInIndex, out pagesWritten, out pagesSkipped);

        var sectionsWritten = 0;
        var sectionsSkipped = 0;
        var sectionsDeleted = 0;
        if (generateSections)
            GenerateProjectSectionPages(catalog.Projects, contentRoot ?? string.Empty, forceOverwriteExisting, out sectionsWritten, out sectionsSkipped, out sectionsDeleted);

        if (!string.IsNullOrWhiteSpace(publishPath))
        {
            var publishDirectory = Path.GetDirectoryName(publishPath);
            if (!string.IsNullOrWhiteSpace(publishDirectory))
                Directory.CreateDirectory(publishDirectory);
            File.Copy(catalogPath, publishPath, overwrite: true);
        }

        if (!string.IsNullOrWhiteSpace(summaryPath))
        {
            var summaryDirectory = Path.GetDirectoryName(summaryPath);
            if (!string.IsNullOrWhiteSpace(summaryDirectory))
                Directory.CreateDirectory(summaryDirectory);
            var summary = new
            {
                generatedOn = DateTimeOffset.UtcNow.ToString("O"),
                catalogPath = Path.GetFullPath(catalogPath),
                publishPath = string.IsNullOrWhiteSpace(publishPath) ? null : Path.GetFullPath(publishPath),
                projectCount = catalog.Projects.Count,
                discoveredManifests,
                importedManifests,
                createdProjects,
                skippedManifests,
                bootstrapFromStats,
                bootstrapTop,
                bootstrapMinimumStars,
                bootstrapIncludeArchived,
                bootstrapCreatedProjects,
                curationUpdates,
                mergeTelemetry,
                statsPath = string.IsNullOrWhiteSpace(statsPath) ? null : Path.GetFullPath(statsPath),
                telemetryMerged,
                mergeReleaseTelemetry,
                releaseTelemetryMerged,
                hubSectionLinkTarget,
                pagesWritten,
                pagesSkipped,
                sectionsWritten,
                sectionsSkipped,
                sectionsDeleted,
                errors = findings?.Count(f => f.Level.Equals("error", StringComparison.OrdinalIgnoreCase)) ?? 0,
                warnings = findings?.Count(f => f.Level.Equals("warning", StringComparison.OrdinalIgnoreCase)) ?? 0,
                findings
            };
            File.WriteAllText(summaryPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        }

        stepResult.Success = true;
        stepResult.Message = $"project-catalog ok: projects={catalog.Projects.Count}; manifests={importedManifests}/{discoveredManifests}; bootstrap={bootstrapCreatedProjects}; telemetry={telemetryMerged}; releases={releaseTelemetryMerged}; pages={pagesWritten}; sections={sectionsWritten}; staleSectionsDeleted={sectionsDeleted}";
    }

    private static string? FindProjectManifestPath(string projectSourcePath)
    {
        if (string.IsNullOrWhiteSpace(projectSourcePath) || !Directory.Exists(projectSourcePath))
            return null;

        var preferred = new[]
        {
            Path.Combine(projectSourcePath, "WebsiteArtifacts", "project-manifest.json"),
            Path.Combine(projectSourcePath, "Website", "project-manifest.json"),
            Path.Combine(projectSourcePath, ".powerforge", "project-manifest.json"),
            Path.Combine(projectSourcePath, "project-manifest.json")
        };

        foreach (var candidate in preferred)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return Directory.EnumerateFiles(projectSourcePath, "project-manifest.json", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    private static void MergeManifestIntoProject(ProjectCatalogEntry project, ProjectManifestData manifest, string manifestPath)
    {
        if (!string.IsNullOrWhiteSpace(manifest.Name))
            project.Name = manifest.Name!.Trim();
        if (!string.IsNullOrWhiteSpace(manifest.Description))
            project.Description = manifest.Description!.Trim();
        if (!string.IsNullOrWhiteSpace(manifest.Mode))
            project.Mode = NormalizeProjectMode(manifest.Mode, project.Mode ?? "hub-full");
        if (!string.IsNullOrWhiteSpace(manifest.ContentMode))
            project.ContentMode = NormalizeProjectContentMode(manifest.ContentMode, project.Mode ?? "hub-full");
        if (manifest.Aliases is { Count: > 0 })
        {
            project.Aliases = manifest.Aliases
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        if (!string.IsNullOrWhiteSpace(manifest.Version))
            project.Version = manifest.Version!.Trim();
        if (!string.IsNullOrWhiteSpace(manifest.GeneratedAt))
            project.ManifestGeneratedAt = manifest.GeneratedAt!.Trim();
        if (!string.IsNullOrWhiteSpace(manifest.Commit))
            project.ManifestCommit = manifest.Commit!.Trim();

        if (manifest.Links is { Count: > 0 })
        {
            project.Links ??= new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in manifest.Links)
                project.Links[pair.Key] = pair.Value;

            if (project.Links.TryGetValue("website", out var website) && !string.IsNullOrWhiteSpace(website))
                project.ExternalUrl = website!.Trim();
            if (project.Links.TryGetValue("source", out var source) &&
                !string.IsNullOrWhiteSpace(source) &&
                TryExtractGitHubRepo(source!, out var repo))
            {
                project.GitHubRepo = repo;
            }
        }

        if (manifest.Surfaces is { Count: > 0 })
        {
            project.Surfaces ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in manifest.Surfaces)
                project.Surfaces[pair.Key] = pair.Value;
        }

        if (manifest.Artifacts is not null)
        {
            project.Artifacts ??= new ProjectCatalogArtifacts();
            if (!string.IsNullOrWhiteSpace(manifest.Artifacts.Docs))
                project.Artifacts.Docs = manifest.Artifacts.Docs.Trim();
            if (!string.IsNullOrWhiteSpace(manifest.Artifacts.Api))
                project.Artifacts.Api = manifest.Artifacts.Api.Trim();
            if (!string.IsNullOrWhiteSpace(manifest.Artifacts.Examples))
                project.Artifacts.Examples = manifest.Artifacts.Examples.Trim();
        }

        if (manifest.Listed.HasValue)
            project.Listed = manifest.Listed.Value;
        if (!string.IsNullOrWhiteSpace(manifest.Status))
            project.Status = manifest.Status!.Trim();

        project.ManifestPath = manifestPath.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(project.HubPath) && !string.IsNullOrWhiteSpace(project.Slug))
            project.HubPath = $"/projects/{NormalizeSlug(project.Slug)}/";
    }

    private static bool TryExtractGitHubRepo(string sourceUrl, out string repo)
    {
        repo = string.Empty;
        if (string.IsNullOrWhiteSpace(sourceUrl))
            return false;

        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
            return false;
        if (!uri.Host.Contains("github.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
            return false;

        repo = $"{segments[0]}/{segments[1]}";
        return true;
    }

    private static int ApplyProjectCuration(
        IReadOnlyDictionary<string, ProjectCatalogEntry> projectBySlug,
        string curationCsvPath)
    {
        var lines = File.ReadAllLines(curationCsvPath);
        if (lines.Length <= 1)
            return 0;

        var header = SplitCsvLine(lines[0]);
        var slugIndex = Array.FindIndex(header, h => h.Equals("slug", StringComparison.OrdinalIgnoreCase));
        var statusIndex = Array.FindIndex(header, h => h.Equals("status", StringComparison.OrdinalIgnoreCase));
        var listedIndex = Array.FindIndex(header, h => h.Equals("listed", StringComparison.OrdinalIgnoreCase));
        if (slugIndex < 0 || (statusIndex < 0 && listedIndex < 0))
            return 0;

        var updates = 0;
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;

            var parts = SplitCsvLine(lines[i]);
            if (parts.Length <= slugIndex)
                continue;

            var slug = NormalizeSlug(parts[slugIndex]);
            if (string.IsNullOrWhiteSpace(slug) || !projectBySlug.TryGetValue(slug, out var project))
                continue;

            if (statusIndex >= 0 && statusIndex < parts.Length && !string.IsNullOrWhiteSpace(parts[statusIndex]))
            {
                project.Status = parts[statusIndex].Trim();
                updates++;
            }
            if (listedIndex >= 0 && listedIndex < parts.Length && TryParseBoolean(parts[listedIndex], out var listed))
            {
                project.Listed = listed;
                updates++;
            }
        }

        return updates;
    }

    private static string[] SplitCsvLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return Array.Empty<string>();

        var values = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"' && (i + 1 >= line.Length || line[i + 1] != '"'))
            {
                inQuotes = !inQuotes;
                continue;
            }
            if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
            {
                sb.Append('"');
                i++;
                continue;
            }
            if (c == ',' && !inQuotes)
            {
                values.Add(sb.ToString());
                sb.Clear();
                continue;
            }
            sb.Append(c);
        }
        values.Add(sb.ToString());
        return values.ToArray();
    }

    private static bool TryParseBoolean(string? value, out bool parsed)
    {
        parsed = false;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var token = value.Trim();
        if (bool.TryParse(token, out parsed))
            return true;
        if (token.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("y", StringComparison.OrdinalIgnoreCase))
        {
            parsed = true;
            return true;
        }
        if (token.Equals("0", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("no", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("n", StringComparison.OrdinalIgnoreCase))
        {
            parsed = false;
            return true;
        }
        return false;
    }

    private static HashSet<string> ParseTokenSet(string? value)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value))
            return tokens;

        var parts = value.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (!string.IsNullOrWhiteSpace(part))
                tokens.Add(part.Trim());
        }

        return tokens;
    }

    private static int BootstrapProjectsFromStats(
        IList<ProjectCatalogEntry> projects,
        IDictionary<string, ProjectCatalogEntry> projectBySlug,
        string statsPath,
        JsonSerializerOptions serializerOptions,
        bool allowCreateProjects,
        int top,
        int minimumStars,
        bool includeArchived,
        IReadOnlySet<string> excludeRepos)
    {
        if (!allowCreateProjects || string.IsNullOrWhiteSpace(statsPath) || !File.Exists(statsPath))
            return 0;

        WebEcosystemStatsDocument? stats;
        try
        {
            stats = JsonSerializer.Deserialize<WebEcosystemStatsDocument>(File.ReadAllText(statsPath), serializerOptions);
        }
        catch
        {
            return 0;
        }

        if (stats?.GitHub?.Repositories is not { Count: > 0 })
            return 0;

        var existingRepos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in projects)
        {
            if (!string.IsNullOrWhiteSpace(project.GitHubRepo))
                existingRepos.Add(project.GitHubRepo.Trim());
        }

        IEnumerable<WebEcosystemGitHubRepository> candidates = stats.GitHub.Repositories
            .Where(static repository => !string.IsNullOrWhiteSpace(repository.FullName))
            .Where(repository => includeArchived || !repository.Archived)
            .Where(repository => repository.Stars >= Math.Max(0, minimumStars))
            .OrderByDescending(static repository => repository.Stars)
            .ThenBy(static repository => repository.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        if (top > 0)
            candidates = candidates.Take(top);

        var created = 0;
        foreach (var repository in candidates)
        {
            var fullName = repository.FullName.Trim();
            var repoName = string.IsNullOrWhiteSpace(repository.Name)
                ? ExtractRepositoryName(fullName)
                : repository.Name!.Trim();
            var shortName = ExtractRepositoryName(fullName);

            if (!string.IsNullOrWhiteSpace(repoName) && excludeRepos.Contains(repoName))
                continue;
            if (!string.IsNullOrWhiteSpace(shortName) && excludeRepos.Contains(shortName))
                continue;
            if (excludeRepos.Contains(fullName))
                continue;

            if (existingRepos.Contains(fullName))
                continue;

            var slug = NormalizeSlug(repoName);
            if (string.IsNullOrWhiteSpace(slug))
                continue;
            if (projectBySlug.ContainsKey(slug))
                continue;

            var status = repository.Archived ? "archived" : "active";
            var sourceUrl = string.IsNullOrWhiteSpace(repository.Url)
                ? $"https://github.com/{fullName}"
                : repository.Url.Trim();

            var project = new ProjectCatalogEntry
            {
                Slug = slug,
                Name = string.IsNullOrWhiteSpace(repoName) ? slug : repoName,
                Mode = "hub-full",
                ContentMode = "hybrid",
                Status = status,
                Listed = !repository.Archived,
                HubPath = $"/projects/{slug}/",
                GitHubRepo = fullName,
                Description = string.IsNullOrWhiteSpace(repoName) ? $"{slug} project page." : $"{repoName} project page.",
                Links = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["source"] = sourceUrl
                },
                Surfaces = BuildDefaultProjectSurfaces(repoName, repository.Language)
            };

            projects.Add(project);
            projectBySlug[slug] = project;
            existingRepos.Add(fullName);
            created++;
        }

        return created;
    }

    private static Dictionary<string, bool> BuildDefaultProjectSurfaces(string? repoName, string? language)
    {
        var isPowerShell = !string.IsNullOrWhiteSpace(language) &&
                           language.Equals("PowerShell", StringComparison.OrdinalIgnoreCase);
        if (!isPowerShell && !string.IsNullOrWhiteSpace(repoName))
            isPowerShell = repoName.StartsWith("PS", StringComparison.OrdinalIgnoreCase);

        var isDotNet = !string.IsNullOrWhiteSpace(language) &&
                       (language.Equals("C#", StringComparison.OrdinalIgnoreCase) ||
                        language.Equals("F#", StringComparison.OrdinalIgnoreCase) ||
                        language.Equals("VB.NET", StringComparison.OrdinalIgnoreCase));

        return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["docs"] = true,
            ["apiDotNet"] = isDotNet,
            ["apiPowerShell"] = isPowerShell,
            ["examples"] = false,
            ["changelog"] = true,
            ["releases"] = true
        };
    }

    private static string NormalizeHubSectionLinkTarget(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "collection" ? "collection" : "project";
    }

    private static string GetHubSectionRoute(string slug, string section, string hubSectionLinkTarget)
    {
        var normalizedSection = section?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(normalizedSection))
            return "/";

        return hubSectionLinkTarget.Equals("collection", StringComparison.OrdinalIgnoreCase)
            ? $"/{normalizedSection}/{slug}/"
            : $"/projects/{slug}/{normalizedSection}/";
    }

    private static bool IsKnownHubSectionRoute(string? route, string projectRoute, string collectionRoute)
    {
        if (string.IsNullOrWhiteSpace(route))
            return false;

        return string.Equals(route, projectRoute, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(route, collectionRoute, StringComparison.OrdinalIgnoreCase);
    }

    private static void NormalizeProjectCatalogContracts(IList<ProjectCatalogEntry> projects, string hubSectionLinkTarget)
    {
        if (projects is null || projects.Count == 0)
            return;

        foreach (var project in projects)
        {
            if (project is null)
                continue;

            var slug = NormalizeSlug(project.Slug);
            if (string.IsNullOrWhiteSpace(slug))
                continue;

            var mode = NormalizeProjectMode(project.Mode, "hub-full");
            project.Mode = mode;
            var contentMode = NormalizeProjectContentMode(project.ContentMode, mode);
            project.ContentMode = contentMode;
            project.HubPath = $"/projects/{slug}/";

            var links = EnsureProjectLinks(project);
            var surfaces = EnsureProjectSurfaces(project);
            var artifacts = EnsureProjectArtifacts(project);
            var githubRepo = string.IsNullOrWhiteSpace(project.GitHubRepo)
                ? project.Metrics?.GitHub?.Repository?.Trim()
                : project.GitHubRepo.Trim();
            if (!string.IsNullOrWhiteSpace(githubRepo))
                project.GitHubRepo = githubRepo;

            if (!surfaces.ContainsKey("docs") && contentMode.Equals("hybrid", StringComparison.OrdinalIgnoreCase))
                surfaces["docs"] = true;
            if (!surfaces.ContainsKey("examples") &&
                (!string.IsNullOrWhiteSpace(TryGetDictionaryValue(links, "examples")) ||
                 !string.IsNullOrWhiteSpace(artifacts.Examples)))
            {
                surfaces["examples"] = true;
            }

            if (!surfaces.ContainsKey("apiPowerShell") && project.Metrics?.PowerShellGallery is not null)
                surfaces["apiPowerShell"] = true;
            if (!surfaces.ContainsKey("apiDotNet") && project.Metrics?.NuGet is not null)
                surfaces["apiDotNet"] = true;
            if (!surfaces.ContainsKey("releases") && (!string.IsNullOrWhiteSpace(githubRepo) || project.Metrics?.Release is not null))
                surfaces["releases"] = true;
            if (!surfaces.ContainsKey("changelog") && !string.IsNullOrWhiteSpace(githubRepo))
                surfaces["changelog"] = true;

            if (!string.IsNullOrWhiteSpace(project.ExternalUrl) && string.IsNullOrWhiteSpace(TryGetDictionaryValue(links, "website")))
                links["website"] = project.ExternalUrl.Trim();

            if (string.IsNullOrWhiteSpace(TryGetDictionaryValue(links, "source")))
            {
                var sourceUrl = project.Metrics?.GitHub?.Url;
                if (string.IsNullOrWhiteSpace(sourceUrl) && !string.IsNullOrWhiteSpace(githubRepo))
                    sourceUrl = $"https://github.com/{githubRepo}";
                if (!string.IsNullOrWhiteSpace(sourceUrl))
                    links["source"] = sourceUrl.Trim();
            }

            if (string.IsNullOrWhiteSpace(TryGetDictionaryValue(links, "releases")))
            {
                var releasesUrl = project.Metrics?.Release?.LatestUrl;
                if (string.IsNullOrWhiteSpace(releasesUrl) && !string.IsNullOrWhiteSpace(githubRepo))
                    releasesUrl = $"https://github.com/{githubRepo}/releases";
                if (!string.IsNullOrWhiteSpace(releasesUrl))
                    links["releases"] = releasesUrl.Trim();
            }

            if (string.IsNullOrWhiteSpace(TryGetDictionaryValue(links, "changelog")) && !string.IsNullOrWhiteSpace(githubRepo))
                links["changelog"] = $"https://github.com/{githubRepo}/blob/main/CHANGELOG.md";

            if (string.IsNullOrWhiteSpace(TryGetDictionaryValue(links, "nuget")) &&
                !string.IsNullOrWhiteSpace(project.Metrics?.NuGet?.PackageUrl))
            {
                links["nuget"] = project.Metrics!.NuGet!.PackageUrl!.Trim();
            }

            if (string.IsNullOrWhiteSpace(TryGetDictionaryValue(links, "powerShellGallery")) &&
                !string.IsNullOrWhiteSpace(project.Metrics?.PowerShellGallery?.GalleryUrl))
            {
                links["powerShellGallery"] = project.Metrics!.PowerShellGallery!.GalleryUrl!.Trim();
            }

            var docsEnabled = surfaces.TryGetValue("docs", out var docsSurface) && docsSurface;
            var docsProjectRoute = $"/projects/{slug}/docs/";
            var docsCollectionRoute = $"/docs/{slug}/";
            var docsHubRoute = GetHubSectionRoute(slug, "docs", hubSectionLinkTarget);
            if (mode.Equals("hub-full", StringComparison.OrdinalIgnoreCase) &&
                IsKnownHubSectionRoute(TryGetDictionaryValue(links, "docs"), docsProjectRoute, docsCollectionRoute))
            {
                links["docs"] = docsHubRoute;
            }
            if (docsEnabled && string.IsNullOrWhiteSpace(TryGetDictionaryValue(links, "docs")))
            {
                if (mode.Equals("hub-full", StringComparison.OrdinalIgnoreCase))
                    links["docs"] = docsHubRoute;
                else if (!string.IsNullOrWhiteSpace(project.ExternalUrl))
                    links["docs"] = project.ExternalUrl.Trim();
            }

            var apiPowerShellEnabled = surfaces.TryGetValue("apiPowerShell", out var apiPowerShellSurface) && apiPowerShellSurface;
            var apiProjectRoute = $"/projects/{slug}/api/";
            var apiCollectionRoute = $"/api/{slug}/";
            var apiHubRoute = GetHubSectionRoute(slug, "api", hubSectionLinkTarget);
            if (mode.Equals("hub-full", StringComparison.OrdinalIgnoreCase) &&
                IsKnownHubSectionRoute(TryGetDictionaryValue(links, "apiPowerShell"), apiProjectRoute, apiCollectionRoute))
            {
                links["apiPowerShell"] = apiHubRoute;
            }
            if (apiPowerShellEnabled && string.IsNullOrWhiteSpace(TryGetDictionaryValue(links, "apiPowerShell")))
            {
                if (mode.Equals("hub-full", StringComparison.OrdinalIgnoreCase))
                    links["apiPowerShell"] = apiHubRoute;
                else if (!string.IsNullOrWhiteSpace(project.ExternalUrl))
                    links["apiPowerShell"] = project.ExternalUrl.Trim();
            }

            var apiDotNetEnabled = surfaces.TryGetValue("apiDotNet", out var apiDotNetSurface) && apiDotNetSurface;
            if (mode.Equals("hub-full", StringComparison.OrdinalIgnoreCase) &&
                IsKnownHubSectionRoute(TryGetDictionaryValue(links, "apiDotNet"), apiProjectRoute, apiCollectionRoute))
            {
                links["apiDotNet"] = apiHubRoute;
            }
            if (apiDotNetEnabled && string.IsNullOrWhiteSpace(TryGetDictionaryValue(links, "apiDotNet")))
            {
                if (mode.Equals("hub-full", StringComparison.OrdinalIgnoreCase))
                    links["apiDotNet"] = apiHubRoute;
                else if (!string.IsNullOrWhiteSpace(project.ExternalUrl))
                    links["apiDotNet"] = project.ExternalUrl.Trim();
            }

            var examplesEnabled = surfaces.TryGetValue("examples", out var examplesSurface) && examplesSurface;
            var examplesProjectRoute = $"/projects/{slug}/examples/";
            var examplesCollectionRoute = $"/examples/{slug}/";
            var examplesHubRoute = GetHubSectionRoute(slug, "examples", hubSectionLinkTarget);
            if (mode.Equals("hub-full", StringComparison.OrdinalIgnoreCase) &&
                IsKnownHubSectionRoute(TryGetDictionaryValue(links, "examples"), examplesProjectRoute, examplesCollectionRoute))
            {
                links["examples"] = examplesHubRoute;
            }
            if (examplesEnabled && string.IsNullOrWhiteSpace(TryGetDictionaryValue(links, "examples")))
            {
                if (mode.Equals("hub-full", StringComparison.OrdinalIgnoreCase))
                    links["examples"] = examplesHubRoute;
                else if (!string.IsNullOrWhiteSpace(project.ExternalUrl))
                    links["examples"] = project.ExternalUrl.Trim();
            }

            artifacts.Docs = NormalizeOptionalString(artifacts.Docs);
            artifacts.Api = NormalizeOptionalString(artifacts.Api);
            artifacts.Examples = NormalizeOptionalString(artifacts.Examples);
        }
    }

    private static Dictionary<string, string?> EnsureProjectLinks(ProjectCatalogEntry project)
    {
        var normalized = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (project.Links is { Count: > 0 })
        {
            foreach (var pair in project.Links)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    continue;
                normalized[pair.Key.Trim()] = string.IsNullOrWhiteSpace(pair.Value) ? null : pair.Value.Trim();
            }
        }

        project.Links = normalized;
        return normalized;
    }

    private static Dictionary<string, bool> EnsureProjectSurfaces(ProjectCatalogEntry project)
    {
        var normalized = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (project.Surfaces is { Count: > 0 })
        {
            foreach (var pair in project.Surfaces)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    continue;
                normalized[pair.Key.Trim()] = pair.Value;
            }
        }

        project.Surfaces = normalized;
        return normalized;
    }

    private static ProjectCatalogArtifacts EnsureProjectArtifacts(ProjectCatalogEntry project)
    {
        project.Artifacts ??= new ProjectCatalogArtifacts();
        project.Artifacts.Docs = NormalizeOptionalString(project.Artifacts.Docs);
        project.Artifacts.Api = NormalizeOptionalString(project.Artifacts.Api);
        project.Artifacts.Examples = NormalizeOptionalString(project.Artifacts.Examples);
        return project.Artifacts;
    }

    private static List<ProjectCatalogFinding> ValidateProjectCatalog(IReadOnlyList<ProjectCatalogEntry> projects)
    {
        var findings = new List<ProjectCatalogFinding>();
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hubPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in projects)
        {
            var slug = NormalizeSlug(project.Slug);
            if (string.IsNullOrWhiteSpace(slug))
            {
                findings.Add(ProjectCatalogFinding.Error("missing-slug", null, "Project is missing required field: slug."));
                continue;
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9][a-z0-9-]*$"))
                findings.Add(ProjectCatalogFinding.Error("invalid-slug", slug, $"Slug '{slug}' must match ^[a-z0-9][a-z0-9-]*$."));
            if (!slugs.Add(slug))
                findings.Add(ProjectCatalogFinding.Error("duplicate-slug", slug, $"Duplicate slug '{slug}' detected."));
            if (string.IsNullOrWhiteSpace(project.Name))
                findings.Add(ProjectCatalogFinding.Warning("missing-name", slug, "Project should define a display name."));

            var mode = NormalizeProjectMode(project.Mode, fallback: "hub-full");
            if (!AllowedProjectModes.Contains(mode, StringComparer.OrdinalIgnoreCase))
                findings.Add(ProjectCatalogFinding.Error("invalid-mode", slug, $"Mode '{mode}' is not supported. Allowed: {string.Join(", ", AllowedProjectModes)}."));
            var contentMode = NormalizeProjectContentMode(project.ContentMode, mode);
            if (!AllowedProjectContentModes.Contains(contentMode, StringComparer.OrdinalIgnoreCase))
            {
                findings.Add(ProjectCatalogFinding.Error(
                    "invalid-content-mode",
                    slug,
                    $"contentMode '{contentMode}' is not supported. Allowed: {string.Join(", ", AllowedProjectContentModes)}."));
            }

            var status = NormalizeProjectStatus(project.Status, fallback: "active");
            if (!AllowedProjectStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
                findings.Add(ProjectCatalogFinding.Error("invalid-status", slug, $"Status '{status}' is not supported. Allowed: {string.Join(", ", AllowedProjectStatuses)}."));

            var expectedHubPath = $"/projects/{slug}/";
            var hubPath = string.IsNullOrWhiteSpace(project.HubPath) ? expectedHubPath : project.HubPath!.Trim();
            if (!hubPath.Equals(expectedHubPath, StringComparison.OrdinalIgnoreCase))
                findings.Add(ProjectCatalogFinding.Warning("non-canonical-hub-path", slug, $"hubPath '{hubPath}' differs from canonical '{expectedHubPath}'."));
            if (!hubPath.StartsWith("/", StringComparison.Ordinal) || !hubPath.EndsWith("/", StringComparison.Ordinal))
                findings.Add(ProjectCatalogFinding.Error("invalid-hub-path", slug, $"hubPath '{hubPath}' must start and end with '/'."));
            if (!hubPaths.Add(hubPath))
                findings.Add(ProjectCatalogFinding.Error("duplicate-hub-path", slug, $"Duplicate hubPath '{hubPath}' detected."));

            if (string.IsNullOrWhiteSpace(project.GitHubRepo))
                findings.Add(ProjectCatalogFinding.Warning("missing-github-repo", slug, "Project should define githubRepo."));
            else if (!System.Text.RegularExpressions.Regex.IsMatch(project.GitHubRepo!, "^[^/\\s]+/[^/\\s]+$"))
                findings.Add(ProjectCatalogFinding.Error("invalid-github-repo", slug, $"githubRepo '{project.GitHubRepo}' must match owner/repo."));

            if (contentMode.Equals("external", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(project.ExternalUrl))
                {
                    findings.Add(ProjectCatalogFinding.Error("missing-external-url", slug, "external contentMode requires externalUrl."));
                }
                else if (!project.ExternalUrl!.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(ProjectCatalogFinding.Warning("external-url-not-https", slug, $"externalUrl '{project.ExternalUrl}' should use https://."));
                }
            }

            ValidateProjectLinkAndSurfaceContracts(findings, project, slug, mode, contentMode);
        }

        return findings;
    }

    private static void ValidateProjectLinkAndSurfaceContracts(
        List<ProjectCatalogFinding> findings,
        ProjectCatalogEntry project,
        string slug,
        string mode,
        string contentMode)
    {
        foreach (var unknownSurface in EnumerateUnknownDictionaryKeys(project.Surfaces, AllowedProjectSurfaceKeys))
        {
            findings.Add(ProjectCatalogFinding.Warning(
                "unknown-surface-key",
                slug,
                $"Unknown surface key '{unknownSurface}'. Allowed keys: {string.Join(", ", AllowedProjectSurfaceKeys)}."));
        }

        foreach (var unknownLink in EnumerateUnknownDictionaryKeys(project.Links, AllowedProjectLinkKeys))
        {
            findings.Add(ProjectCatalogFinding.Warning(
                "unknown-link-key",
                slug,
                $"Unknown link key '{unknownLink}'. Allowed keys: {string.Join(", ", AllowedProjectLinkKeys)}."));
        }

        foreach (var key in AllowedProjectLinkKeys)
        {
            var value = TryGetProjectDictionaryValue(project.Links, key);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (!IsValidProjectLinkTarget(value))
            {
                findings.Add(ProjectCatalogFinding.Warning(
                    "invalid-link-target",
                    slug,
                    $"Link '{key}' has unsupported target '{value}'. Use an absolute URL or a root-relative route."));
            }
            else if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(ProjectCatalogFinding.Warning(
                    "link-not-https",
                    slug,
                    $"Link '{key}' uses http://. Prefer https:// when possible."));
            }
        }

        var docsSurface = TryGetProjectSurfaceValue(project.Surfaces, "docs") ?? false;
        var apiDotNetSurface = TryGetProjectSurfaceValue(project.Surfaces, "apiDotNet") ?? false;
        var apiPowerShellSurface = TryGetProjectSurfaceValue(project.Surfaces, "apiPowerShell") ?? false;
        var examplesSurface = TryGetProjectSurfaceValue(project.Surfaces, "examples") ?? false;
        var releasesSurface = TryGetProjectSurfaceValue(project.Surfaces, "releases") ?? false;
        var changelogSurface = TryGetProjectSurfaceValue(project.Surfaces, "changelog") ?? false;

        var docsLink = TryGetProjectDictionaryValue(project.Links, "docs");
        var apiDotNetLink = TryGetProjectDictionaryValue(project.Links, "apiDotNet");
        var apiPowerShellLink = TryGetProjectDictionaryValue(project.Links, "apiPowerShell");
        var examplesLink = TryGetProjectDictionaryValue(project.Links, "examples");
        var changelogLink = TryGetProjectDictionaryValue(project.Links, "changelog");
        var releasesLink = TryGetProjectDictionaryValue(project.Links, "releases");
        var sourceLink = TryGetProjectDictionaryValue(project.Links, "source");
        var websiteLink = TryGetProjectDictionaryValue(project.Links, "website");

        var isDedicatedExternal = contentMode.Equals("external", StringComparison.OrdinalIgnoreCase) ||
                                  mode.Equals("dedicated-external", StringComparison.OrdinalIgnoreCase);
        var hasAnySurface = docsSurface || apiDotNetSurface || apiPowerShellSurface || examplesSurface || releasesSurface || changelogSurface;

        if (isDedicatedExternal && docsSurface &&
            string.IsNullOrWhiteSpace(docsLink) &&
            string.IsNullOrWhiteSpace(project.ExternalUrl) &&
            string.IsNullOrWhiteSpace(websiteLink))
        {
            findings.Add(ProjectCatalogFinding.Warning(
                "docs-surface-missing-link",
                slug,
                "docs surface is enabled for dedicated-external mode but neither links.docs nor externalUrl/links.website is set."));
        }

        if (isDedicatedExternal && apiDotNetSurface && string.IsNullOrWhiteSpace(apiDotNetLink))
        {
            findings.Add(ProjectCatalogFinding.Warning(
                "api-dotnet-surface-missing-link",
                slug,
                "apiDotNet surface is enabled for dedicated-external mode but links.apiDotNet is not set."));
        }

        if (isDedicatedExternal && apiPowerShellSurface && string.IsNullOrWhiteSpace(apiPowerShellLink))
        {
            findings.Add(ProjectCatalogFinding.Warning(
                "api-powershell-surface-missing-link",
                slug,
                "apiPowerShell surface is enabled for dedicated-external mode but links.apiPowerShell is not set."));
        }

        if (isDedicatedExternal &&
            examplesSurface &&
            string.IsNullOrWhiteSpace(examplesLink) &&
            string.IsNullOrWhiteSpace(project.ExternalUrl) &&
            string.IsNullOrWhiteSpace(websiteLink))
        {
            findings.Add(ProjectCatalogFinding.Warning(
                "examples-surface-missing-link",
                slug,
                "examples surface is enabled for dedicated-external mode but neither links.examples nor externalUrl/links.website is set."));
        }

        if (releasesSurface &&
            string.IsNullOrWhiteSpace(releasesLink) &&
            string.IsNullOrWhiteSpace(project.GitHubRepo))
        {
            findings.Add(ProjectCatalogFinding.Warning(
                "releases-surface-missing-link",
                slug,
                "releases surface is enabled but neither links.releases nor githubRepo is available."));
        }

        if (changelogSurface &&
            string.IsNullOrWhiteSpace(changelogLink) &&
            string.IsNullOrWhiteSpace(releasesLink) &&
            string.IsNullOrWhiteSpace(project.GitHubRepo))
        {
            findings.Add(ProjectCatalogFinding.Warning(
                "changelog-surface-missing-link",
                slug,
                "changelog surface is enabled but links.changelog (or fallback release/source location) is missing."));
        }

        if (hasAnySurface &&
            string.IsNullOrWhiteSpace(sourceLink) &&
            string.IsNullOrWhiteSpace(project.GitHubRepo))
        {
            findings.Add(ProjectCatalogFinding.Warning(
                "surface-missing-source",
                slug,
                "At least one surface is enabled, but neither links.source nor githubRepo is set."));
        }
    }

    private static IEnumerable<string> EnumerateUnknownDictionaryKeys<TValue>(
        Dictionary<string, TValue>? dictionary,
        IReadOnlyCollection<string> allowedKeys)
    {
        if (dictionary is null || dictionary.Count == 0)
            yield break;

        foreach (var key in dictionary.Keys)
        {
            if (allowedKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                continue;
            yield return key;
        }
    }

    private static string? TryGetProjectDictionaryValue(Dictionary<string, string?>? dictionary, string key)
    {
        if (dictionary is null || dictionary.Count == 0 || string.IsNullOrWhiteSpace(key))
            return null;

        foreach (var pair in dictionary)
        {
            if (!pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(pair.Value))
                return null;
            return pair.Value.Trim();
        }

        return null;
    }

    private static bool? TryGetProjectSurfaceValue(Dictionary<string, bool>? dictionary, string key)
    {
        if (dictionary is null || dictionary.Count == 0 || string.IsNullOrWhiteSpace(key))
            return null;

        foreach (var pair in dictionary)
        {
            if (pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                return pair.Value;
        }

        return null;
    }

    private static bool IsValidProjectLinkTarget(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (trimmed.StartsWith("/", StringComparison.Ordinal))
            return true;

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return false;

        return uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ||
               uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase);
    }

    private static void GenerateProjectPages(
        IReadOnlyList<ProjectCatalogEntry> projects,
        string contentRoot,
        bool forceOverwriteExisting,
        bool includeUnlistedInIndex,
        out int written,
        out int skipped)
    {
        written = 0;
        skipped = 0;

        if (string.IsNullOrWhiteSpace(contentRoot))
            return;
        Directory.CreateDirectory(contentRoot);

        foreach (var project in projects)
        {
            var slug = NormalizeSlug(project.Slug);
            if (string.IsNullOrWhiteSpace(slug))
                continue;

            var outputPath = Path.Combine(contentRoot, slug + ".md");
            if (!CanOverwriteGenerated(outputPath, forceOverwriteExisting))
            {
                skipped++;
                continue;
            }

            var lines = new List<string>
            {
                "---",
                $"title: {YamlQuote(string.IsNullOrWhiteSpace(project.Name) ? slug : project.Name)}",
                $"description: {YamlQuote(string.IsNullOrWhiteSpace(project.Description) ? $"{slug} project page." : project.Description)}",
                $"slug: {YamlQuote(slug)}",
                "layout: project"
            };

            if (project.Aliases is { Length: > 0 })
            {
                var aliases = project.Aliases
                    .Where(static alias => !string.IsNullOrWhiteSpace(alias))
                    .Select(static alias => alias!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (aliases.Length > 0)
                {
                    lines.Add("aliases:");
                    foreach (var alias in aliases)
                        lines.Add($"  - {YamlQuote(alias)}");
                }
            }

            var mode = NormalizeProjectMode(project.Mode, "hub-full");
            var contentMode = NormalizeProjectContentMode(project.ContentMode, mode);
            var status = NormalizeProjectStatus(project.Status, "active");
            var listed = project.Listed ?? !status.Equals("archived", StringComparison.OrdinalIgnoreCase);
            lines.Add($"meta.project_mode: {YamlQuote(mode)}");
            lines.Add($"meta.project_content_mode: {YamlQuote(contentMode)}");
            lines.Add($"meta.project_status: {YamlQuote(status)}");
            lines.Add($"meta.project_listed: {listed.ToString().ToLowerInvariant()}");
            lines.Add($"meta.project_hub_path: {YamlQuote($"/projects/{slug}/")}");
            lines.Add($"meta.project_base_slug: {YamlQuote(slug)}");
            lines.Add("meta.project_section: \"overview\"");
            if (!string.IsNullOrWhiteSpace(project.ExternalUrl))
                lines.Add($"meta.project_external_url: {YamlQuote(project.ExternalUrl)}");
            if (!string.IsNullOrWhiteSpace(project.GitHubRepo))
                lines.Add($"meta.project_github_repo: {YamlQuote(project.GitHubRepo)}");
            if (!string.IsNullOrWhiteSpace(project.Version))
                lines.Add($"meta.project_version: {YamlQuote(project.Version)}");
            if (!string.IsNullOrWhiteSpace(project.ManifestGeneratedAt))
                lines.Add($"meta.project_manifest_generated_at: {YamlQuote(project.ManifestGeneratedAt)}");
            if (!string.IsNullOrWhiteSpace(project.ManifestCommit))
                lines.Add($"meta.project_manifest_commit: {YamlQuote(project.ManifestCommit)}");
            AppendProjectFrontMatterExtensions(lines, project);
            lines.Add("meta.generated_by: powerforge.project-catalog");
            lines.Add("---");
            lines.Add(string.Empty);
            lines.Add(string.IsNullOrWhiteSpace(project.Description) ? $"{project.Name ?? slug} project page." : project.Description!);
            lines.Add(string.Empty);
            if (!string.IsNullOrWhiteSpace(project.Version))
            {
                lines.Add($"- Latest known version: {project.Version}");
                lines.Add(string.Empty);
            }

            if (status.Equals("archived", StringComparison.OrdinalIgnoreCase))
            {
                lines.Add("This project is archived and kept for historical reference.");
                lines.Add(string.Empty);
            }
            else if (status.Equals("deprecated", StringComparison.OrdinalIgnoreCase))
            {
                lines.Add("This project is deprecated. Prefer newer replacements where available.");
                lines.Add(string.Empty);
            }
            else if (status.Equals("experimental", StringComparison.OrdinalIgnoreCase))
            {
                lines.Add("This project is experimental and may change frequently.");
                lines.Add(string.Empty);
            }

            if (contentMode.Equals("external", StringComparison.OrdinalIgnoreCase))
            {
                var external = project.ExternalUrl;
                if (string.IsNullOrWhiteSpace(external) && project.Links is not null && project.Links.TryGetValue("website", out var website))
                    external = website;

                if (!string.IsNullOrWhiteSpace(external))
                {
                    lines.Add($"This project has a dedicated website: [{external}]({external})");
                    lines.Add(string.Empty);
                }
                else
                {
                    lines.Add("This project is marked as dedicated-external but no `externalUrl` is configured.");
                    lines.Add(string.Empty);
                }
                lines.Add("This hub page exists for discovery, aliases, and navigation continuity.");
                lines.Add(string.Empty);
            }
            else
            {
                var docsLink = TryGetProjectDictionaryValue(project.Links, "docs");
                var apiLink = TryGetProjectDictionaryValue(project.Links, "apiPowerShell");
                var examplesLink = TryGetProjectDictionaryValue(project.Links, "examples");
                if (string.IsNullOrWhiteSpace(apiLink))
                    apiLink = TryGetProjectDictionaryValue(project.Links, "apiDotNet");
                if (string.IsNullOrWhiteSpace(docsLink))
                    docsLink = $"/docs/{slug}/";
                if (string.IsNullOrWhiteSpace(apiLink))
                    apiLink = $"/api/{slug}/";
                if (string.IsNullOrWhiteSpace(examplesLink))
                    examplesLink = $"/examples/{slug}/";

                lines.Add("This project is hosted as part of the main hub website.");
                lines.Add(string.Empty);
                lines.Add($"- Docs: {docsLink}");
                lines.Add($"- API: {apiLink}");
                lines.Add($"- Examples: {examplesLink}");
                if (!string.IsNullOrWhiteSpace(project.ExternalUrl))
                    lines.Add($"- External website: [{project.ExternalUrl}]({project.ExternalUrl})");
                lines.Add(string.Empty);
            }

            if (!string.IsNullOrWhiteSpace(project.GitHubRepo))
            {
                var repoUrl = $"https://github.com/{project.GitHubRepo}";
                lines.Add($"- Source: [{project.GitHubRepo}]({repoUrl})");
            }

            WriteMarkdown(outputPath, lines);
            written++;
        }

        var indexPath = Path.Combine(contentRoot, "_index.md");
        if (CanOverwriteGenerated(indexPath, forceOverwriteExisting))
        {
            var indexLines = new List<string>
            {
                "---",
                "title: \"Project Catalog\"",
                "description: \"All projects in the Evotec ecosystem.\"",
                "slug: \"index\"",
                "layout: project-catalog",
                "meta.generated_by: powerforge.project-catalog",
                "---",
                string.Empty,
                "Canonical project routes are `/projects/<slug>/`.",
                string.Empty,
                "| Project | Mode | Status | Primary Link | Source |",
                "| --- | --- | --- | --- | --- |"
            };

            var indexProjects = projects
                .Where(project =>
                {
                    if (includeUnlistedInIndex)
                        return true;
                    var status = NormalizeProjectStatus(project.Status, "active");
                    var listed = project.Listed ?? !status.Equals("archived", StringComparison.OrdinalIgnoreCase);
                    return listed;
                })
                .OrderBy(project => project.Name ?? project.Slug ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var project in indexProjects)
            {
                var slug = NormalizeSlug(project.Slug);
                if (string.IsNullOrWhiteSpace(slug))
                    continue;
                var name = string.IsNullOrWhiteSpace(project.Name) ? slug : project.Name!;
                var mode = NormalizeProjectMode(project.Mode, "hub-full");
                var contentMode = NormalizeProjectContentMode(project.ContentMode, mode);
                var status = NormalizeProjectStatus(project.Status, "active");
                var hubPath = $"/projects/{slug}/";
                var primaryLink = contentMode.Equals("external", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(project.ExternalUrl)
                    ? $"[{project.ExternalUrl}]({project.ExternalUrl})"
                    : $"[{hubPath}]({hubPath})";
                var source = string.IsNullOrWhiteSpace(project.GitHubRepo)
                    ? "-"
                    : $"[{project.GitHubRepo}](https://github.com/{project.GitHubRepo})";
                indexLines.Add($"| {name} | {mode} | {status} | {primaryLink} | {source} |");
            }

            WriteMarkdown(indexPath, indexLines);
            written++;
        }
        else
        {
            skipped++;
        }
    }

    private static void GenerateProjectSectionPages(
        IReadOnlyList<ProjectCatalogEntry> projects,
        string contentRoot,
        bool forceOverwriteExisting,
        out int written,
        out int skipped,
        out int deleted)
    {
        written = 0;
        skipped = 0;
        deleted = 0;
        if (string.IsNullOrWhiteSpace(contentRoot))
            return;
        Directory.CreateDirectory(contentRoot);
        var expectedSectionPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in projects)
        {
            var slug = NormalizeSlug(project.Slug);
            if (string.IsNullOrWhiteSpace(slug))
                continue;

            var mode = NormalizeProjectMode(project.Mode, "hub-full");
            var contentMode = NormalizeProjectContentMode(project.ContentMode, mode);
            var status = NormalizeProjectStatus(project.Status, "active");
            var listed = project.Listed ?? !status.Equals("archived", StringComparison.OrdinalIgnoreCase);
            var name = string.IsNullOrWhiteSpace(project.Name) ? slug : project.Name!;
            var description = string.IsNullOrWhiteSpace(project.Description) ? $"{name} project page." : project.Description!;
            var sections = new List<string> { "docs", "api" };
            var includeExamplesSection =
                (TryGetProjectSurfaceValue(project.Surfaces, "examples") ?? false) ||
                !string.IsNullOrWhiteSpace(TryGetProjectDictionaryValue(project.Links, "examples")) ||
                !string.IsNullOrWhiteSpace(project.Artifacts?.Examples);
            if (includeExamplesSection)
                sections.Add("examples");

            foreach (var section in sections)
            {
                var outputPath = Path.Combine(contentRoot, $"{slug}.{section}.md");
                expectedSectionPaths.Add(Path.GetFullPath(outputPath));
                if (!CanOverwriteGenerated(outputPath, forceOverwriteExisting))
                {
                    skipped++;
                    continue;
                }

                var sectionTitle = section switch
                {
                    "docs" => $"{name} Docs",
                    "api" => $"{name} API",
                    "examples" => $"{name} Examples",
                    _ => $"{name} {section}"
                };
                var sectionDescription = section switch
                {
                    "docs" => $"Documentation routes and references for {name}.",
                    "api" => $"API routes and references for {name}.",
                    "examples" => $"Examples and usage guides for {name}.",
                    _ => $"{name} project section."
                };

                var lines = new List<string>
                {
                    "---",
                    $"title: {YamlQuote(sectionTitle)}",
                    $"description: {YamlQuote(sectionDescription)}",
                    $"slug: {YamlQuote($"{slug}/{section}")}",
                    "layout: project",
                    $"meta.project_mode: {YamlQuote(mode)}",
                    $"meta.project_content_mode: {YamlQuote(contentMode)}",
                    $"meta.project_status: {YamlQuote(status)}",
                    $"meta.project_listed: {listed.ToString().ToLowerInvariant()}",
                    $"meta.project_hub_path: {YamlQuote($"/projects/{slug}/")}",
                    $"meta.project_base_slug: {YamlQuote(slug)}",
                    $"meta.project_section: {YamlQuote(section)}",
                    "meta.generated_by: powerforge.project-catalog"
                };
                if (!string.IsNullOrWhiteSpace(project.GitHubRepo))
                    lines.Add($"meta.project_github_repo: {YamlQuote(project.GitHubRepo)}");
                if (!string.IsNullOrWhiteSpace(project.ExternalUrl))
                    lines.Add($"meta.project_external_url: {YamlQuote(project.ExternalUrl)}");
                AppendProjectFrontMatterExtensions(lines, project);
                lines.Add("---");
                lines.Add(string.Empty);
                lines.Add(description);
                lines.Add(string.Empty);

                if (section.Equals("docs", StringComparison.OrdinalIgnoreCase))
                {
                    var docsLink = string.Empty;
                    var apiLink = string.Empty;
                    var examplesLink = string.Empty;
                    if (project.Links is not null)
                    {
                        project.Links.TryGetValue("docs", out docsLink);
                        project.Links.TryGetValue("apiPowerShell", out apiLink);
                        if (string.IsNullOrWhiteSpace(apiLink))
                            project.Links.TryGetValue("apiDotNet", out apiLink);
                        project.Links.TryGetValue("examples", out examplesLink);
                    }
                    if (string.IsNullOrWhiteSpace(docsLink))
                        docsLink = $"/projects/{slug}/docs/";
                    if (string.IsNullOrWhiteSpace(apiLink))
                        apiLink = $"/projects/{slug}/api/";
                    if (string.IsNullOrWhiteSpace(examplesLink))
                        examplesLink = $"/projects/{slug}/examples/";

                    if (!string.IsNullOrWhiteSpace(docsLink))
                        lines.Add($"Documentation is available at [{docsLink}]({docsLink}).");
                    else if (!string.IsNullOrWhiteSpace(project.ExternalUrl))
                        lines.Add("This project is hosted externally. Documentation is published on the project website.");
                    else
                        lines.Add("Documentation for this project is being prepared.");
                    lines.Add(string.Empty);
                    lines.Add($"- Overview: [/projects/{slug}/](/projects/{slug}/)");
                    lines.Add($"- API: [{apiLink}]({apiLink})");
                    lines.Add($"- Examples: [{examplesLink}]({examplesLink})");
                }
                else if (section.Equals("api", StringComparison.OrdinalIgnoreCase))
                {
                    var docsLink = string.Empty;
                    var examplesLink = string.Empty;
                    if (project.Links is not null)
                    {
                        project.Links.TryGetValue("docs", out docsLink);
                        project.Links.TryGetValue("examples", out examplesLink);
                    }
                    if (string.IsNullOrWhiteSpace(docsLink))
                        docsLink = $"/projects/{slug}/docs/";
                    if (string.IsNullOrWhiteSpace(examplesLink))
                        examplesLink = $"/projects/{slug}/examples/";

                    var hasApi = false;
                    if (project.Links is not null && project.Links.TryGetValue("apiPowerShell", out var psApi) && !string.IsNullOrWhiteSpace(psApi))
                    {
                        lines.Add($"- PowerShell API: [{psApi}]({psApi})");
                        hasApi = true;
                    }
                    if (project.Links is not null && project.Links.TryGetValue("apiDotNet", out var dotnetApi) && !string.IsNullOrWhiteSpace(dotnetApi))
                    {
                        lines.Add($"- .NET API: [{dotnetApi}]({dotnetApi})");
                        hasApi = true;
                    }
                    if (!hasApi)
                    {
                        if (!string.IsNullOrWhiteSpace(project.ExternalUrl))
                            lines.Add("API references are provided on the dedicated external website.");
                        else
                            lines.Add("API references for this project are being prepared.");
                    }
                    lines.Add(string.Empty);
                    lines.Add($"- Overview: [/projects/{slug}/](/projects/{slug}/)");
                    lines.Add($"- Docs: [{docsLink}]({docsLink})");
                    lines.Add($"- Examples: [{examplesLink}]({examplesLink})");
                }
                else
                {
                    var docsLink = string.Empty;
                    var apiLink = string.Empty;
                    var examplesLink = string.Empty;
                    if (project.Links is not null)
                    {
                        project.Links.TryGetValue("docs", out docsLink);
                        project.Links.TryGetValue("apiPowerShell", out apiLink);
                        if (string.IsNullOrWhiteSpace(apiLink))
                            project.Links.TryGetValue("apiDotNet", out apiLink);
                        project.Links.TryGetValue("examples", out examplesLink);
                    }
                    if (string.IsNullOrWhiteSpace(docsLink))
                        docsLink = $"/projects/{slug}/docs/";
                    if (string.IsNullOrWhiteSpace(apiLink))
                        apiLink = $"/projects/{slug}/api/";
                    if (string.IsNullOrWhiteSpace(examplesLink))
                        examplesLink = $"/projects/{slug}/examples/";

                    if (!string.IsNullOrWhiteSpace(examplesLink))
                        lines.Add($"Examples are available at [{examplesLink}]({examplesLink}).");
                    else if (!string.IsNullOrWhiteSpace(project.ExternalUrl))
                        lines.Add("Examples are provided on the dedicated external website.");
                    else
                        lines.Add("Examples for this project are being prepared.");
                    lines.Add(string.Empty);
                    lines.Add($"- Overview: [/projects/{slug}/](/projects/{slug}/)");
                    lines.Add($"- Docs: [{docsLink}]({docsLink})");
                    lines.Add($"- API: [{apiLink}]({apiLink})");
                }

                if (!string.IsNullOrWhiteSpace(project.GitHubRepo))
                {
                    lines.Add(string.Empty);
                    lines.Add($"- Source: [{project.GitHubRepo}](https://github.com/{project.GitHubRepo})");
                }

                WriteMarkdown(outputPath, lines);
                written++;
            }
        }

        deleted = CleanupStaleProjectSectionPages(contentRoot, expectedSectionPaths, forceOverwriteExisting);
    }

    private static int CleanupStaleProjectSectionPages(string contentRoot, HashSet<string> expectedSectionPaths, bool forceOverwriteExisting)
    {
        if (string.IsNullOrWhiteSpace(contentRoot) || !Directory.Exists(contentRoot))
            return 0;

        var deleted = 0;
        foreach (var filePath in Directory.EnumerateFiles(contentRoot, "*.md", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(filePath);
            if (!(fileName.EndsWith(".docs.md", StringComparison.OrdinalIgnoreCase) ||
                  fileName.EndsWith(".api.md", StringComparison.OrdinalIgnoreCase) ||
                  fileName.EndsWith(".examples.md", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var normalizedPath = Path.GetFullPath(filePath);
            if (expectedSectionPaths.Contains(normalizedPath))
                continue;
            if (!CanOverwriteGenerated(filePath, forceOverwriteExisting))
                continue;

            try
            {
                File.Delete(filePath);
                deleted++;
            }
            catch
            {
                // Keep generation resilient when stale cleanup cannot remove a file.
            }
        }

        return deleted;
    }

    private static int MergeProjectTelemetry(
        IReadOnlyList<ProjectCatalogEntry> projects,
        string statsPath,
        JsonSerializerOptions serializerOptions)
    {
        if (projects.Count == 0 || string.IsNullOrWhiteSpace(statsPath) || !File.Exists(statsPath))
            return 0;

        WebEcosystemStatsDocument? stats;
        try
        {
            stats = JsonSerializer.Deserialize<WebEcosystemStatsDocument>(File.ReadAllText(statsPath), serializerOptions);
        }
        catch
        {
            return 0;
        }

        if (stats is null)
            return 0;

        var githubByFullName = new Dictionary<string, WebEcosystemGitHubRepository>(StringComparer.OrdinalIgnoreCase);
        var githubByRepoName = new Dictionary<string, WebEcosystemGitHubRepository>(StringComparer.OrdinalIgnoreCase);
        if (stats.GitHub?.Repositories is { Count: > 0 })
        {
            foreach (var repository in stats.GitHub.Repositories)
            {
                if (!string.IsNullOrWhiteSpace(repository.FullName) && !githubByFullName.ContainsKey(repository.FullName))
                    githubByFullName[repository.FullName] = repository;

                var shortName = ExtractRepositoryName(repository.FullName);
                if (!string.IsNullOrWhiteSpace(shortName) && !githubByRepoName.ContainsKey(shortName))
                    githubByRepoName[shortName] = repository;
                if (!string.IsNullOrWhiteSpace(repository.Name) && !githubByRepoName.ContainsKey(repository.Name))
                    githubByRepoName[repository.Name] = repository;
            }
        }

        var nugetById = new Dictionary<string, WebEcosystemNuGetPackage>(StringComparer.OrdinalIgnoreCase);
        var nugetByCompactId = new Dictionary<string, WebEcosystemNuGetPackage>(StringComparer.OrdinalIgnoreCase);
        if (stats.NuGet?.Items is { Count: > 0 })
        {
            foreach (var package in stats.NuGet.Items)
            {
                if (!string.IsNullOrWhiteSpace(package.Id) && !nugetById.ContainsKey(package.Id))
                    nugetById[package.Id] = package;

                var compact = CompactToken(package.Id);
                if (!string.IsNullOrWhiteSpace(compact) && !nugetByCompactId.ContainsKey(compact))
                    nugetByCompactId[compact] = package;
            }
        }

        var psgalleryById = new Dictionary<string, WebEcosystemPowerShellGalleryModule>(StringComparer.OrdinalIgnoreCase);
        var psgalleryByCompactId = new Dictionary<string, WebEcosystemPowerShellGalleryModule>(StringComparer.OrdinalIgnoreCase);
        if (stats.PowerShellGallery?.Modules is { Count: > 0 })
        {
            foreach (var module in stats.PowerShellGallery.Modules)
            {
                if (!string.IsNullOrWhiteSpace(module.Id) && !psgalleryById.ContainsKey(module.Id))
                    psgalleryById[module.Id] = module;

                var compact = CompactToken(module.Id);
                if (!string.IsNullOrWhiteSpace(compact) && !psgalleryByCompactId.ContainsKey(compact))
                    psgalleryByCompactId[compact] = module;
            }
        }

        var merged = 0;
        foreach (var project in projects)
        {
            var candidates = BuildProjectIdentifierCandidates(project);

            WebEcosystemGitHubRepository? github = null;
            if (!string.IsNullOrWhiteSpace(project.GitHubRepo) && githubByFullName.TryGetValue(project.GitHubRepo, out var byFullName))
            {
                github = byFullName;
            }
            else
            {
                foreach (var candidate in candidates)
                {
                    if (githubByRepoName.TryGetValue(candidate, out var repository))
                    {
                        github = repository;
                        break;
                    }
                }
            }

            WebEcosystemNuGetPackage? nuget = null;
            foreach (var candidate in candidates)
            {
                if (nugetById.TryGetValue(candidate, out var package))
                {
                    nuget = package;
                    break;
                }

                var compact = CompactToken(candidate);
                if (!string.IsNullOrWhiteSpace(compact) && nugetByCompactId.TryGetValue(compact, out package))
                {
                    nuget = package;
                    break;
                }
            }

            WebEcosystemPowerShellGalleryModule? module = null;
            foreach (var candidate in candidates)
            {
                if (psgalleryById.TryGetValue(candidate, out var found))
                {
                    module = found;
                    break;
                }

                var compact = CompactToken(candidate);
                if (!string.IsNullOrWhiteSpace(compact) && psgalleryByCompactId.TryGetValue(compact, out found))
                {
                    module = found;
                    break;
                }
            }

            var hasAnyMetrics = github is not null || nuget is not null || module is not null;
            if (!hasAnyMetrics)
                continue;

            project.Metrics = new ProjectCatalogMetrics
            {
                GitHub = github is null
                    ? null
                    : new ProjectCatalogGitHubMetrics
                    {
                        Repository = github.FullName,
                        Url = github.Url,
                        Language = github.Language,
                        Stars = github.Stars,
                        Forks = github.Forks,
                        Watchers = github.Watchers,
                        OpenIssues = github.OpenIssues,
                        Archived = github.Archived,
                        LastPushedAt = github.PushedAt?.ToString("O")
                    },
                NuGet = nuget is null
                    ? null
                    : new ProjectCatalogNuGetMetrics
                    {
                        Id = nuget.Id,
                        Version = nuget.Version,
                        TotalDownloads = nuget.TotalDownloads,
                        PackageUrl = nuget.PackageUrl ?? string.Empty,
                        ProjectUrl = nuget.ProjectUrl
                    },
                PowerShellGallery = module is null
                    ? null
                    : new ProjectCatalogPowerShellGalleryMetrics
                    {
                        Id = module.Id,
                        Version = module.Version,
                        TotalDownloads = module.DownloadCount,
                        GalleryUrl = module.GalleryUrl,
                        ProjectUrl = module.ProjectUrl
                    }
            };

            var totalDownloads = 0L;
            if (project.Metrics.NuGet is not null)
                totalDownloads += Math.Max(0, project.Metrics.NuGet.TotalDownloads);
            if (project.Metrics.PowerShellGallery is not null)
                totalDownloads += Math.Max(0, project.Metrics.PowerShellGallery.TotalDownloads);
            project.Metrics.Downloads = new ProjectCatalogDownloadsMetrics
            {
                Total = totalDownloads,
                NuGet = project.Metrics.NuGet?.TotalDownloads ?? 0,
                PowerShellGallery = project.Metrics.PowerShellGallery?.TotalDownloads ?? 0
            };

            merged++;
        }

        return merged;
    }

    private static int MergeProjectReleaseTelemetry(
        IReadOnlyList<ProjectCatalogEntry> projects,
        string? githubToken,
        string? githubApiBaseUrl,
        int timeoutSeconds)
    {
        if (projects.Count == 0)
            return 0;

        var apiBaseUrl = string.IsNullOrWhiteSpace(githubApiBaseUrl)
            ? "https://api.github.com"
            : githubApiBaseUrl.Trim().TrimEnd('/');

        var releaseByRepo = new Dictionary<string, ProjectCatalogReleaseMetrics?>(StringComparer.OrdinalIgnoreCase);
        var merged = 0;

        using var client = CreateGitHubApiClient(githubToken, timeoutSeconds);
        foreach (var project in projects)
        {
            if (string.IsNullOrWhiteSpace(project.GitHubRepo))
                continue;

            var repository = project.GitHubRepo.Trim();
            if (!releaseByRepo.TryGetValue(repository, out var release))
            {
                release = TryGetLatestGitHubRelease(client, apiBaseUrl, repository);
                releaseByRepo[repository] = release;
            }

            if (release is null)
                continue;

            project.Metrics ??= new ProjectCatalogMetrics();
            project.Metrics.Release = release;
            merged++;
        }

        return merged;
    }

    private static HttpClient CreateGitHubApiClient(string? githubToken, int timeoutSeconds)
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds <= 0 ? 20 : timeoutSeconds, 5, 120))
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PowerForge.Web.Cli", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        if (!string.IsNullOrWhiteSpace(githubToken))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", githubToken.Trim());

        return client;
    }

    private static ProjectCatalogReleaseMetrics? TryGetLatestGitHubRelease(HttpClient client, string apiBaseUrl, string repository)
    {
        if (string.IsNullOrWhiteSpace(repository))
            return null;

        var ownerRepo = repository.Trim().Trim('/');
        var parts = ownerRepo.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return null;

        var releaseUrl = $"{apiBaseUrl}/repos/{parts[0]}/{parts[1]}/releases/latest";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, releaseUrl);
            using var response = client.Send(request);

            // Common no-release and auth/rate-limit statuses should not fail the step.
            if ((int)response.StatusCode == 404 || (int)response.StatusCode == 401 || (int)response.StatusCode == 403)
                return null;
            if (!response.IsSuccessStatusCode)
                return null;

            using var stream = response.Content.ReadAsStream();
            using var json = JsonDocument.Parse(stream);
            if (json.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            var tag = ReadJsonString(json.RootElement, "tag_name", "tag");
            var name = ReadJsonString(json.RootElement, "name");
            var url = ReadJsonString(json.RootElement, "html_url", "url");
            var publishedAt = ReadJsonDate(json.RootElement, "published_at", "created_at");
            var isPrerelease = ReadJsonBool(json.RootElement, "prerelease");
            var isDraft = ReadJsonBool(json.RootElement, "draft");

            if (string.IsNullOrWhiteSpace(tag) && string.IsNullOrWhiteSpace(name))
                return null;

            return new ProjectCatalogReleaseMetrics
            {
                LatestTag = string.IsNullOrWhiteSpace(tag) ? name : tag,
                LatestName = name,
                LatestUrl = url,
                LatestPublishedAt = publishedAt,
                IsPrerelease = isPrerelease ?? false,
                IsDraft = isDraft ?? false
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadJsonString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
                continue;

            var value = property.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private static bool? ReadJsonBool(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var property))
                continue;

            if (property.ValueKind == JsonValueKind.True)
                return true;
            if (property.ValueKind == JsonValueKind.False)
                return false;
        }

        return null;
    }

    private static string? ReadJsonDate(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
                continue;

            var value = property.GetString();
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (DateTimeOffset.TryParse(value, out var parsed))
                return parsed.ToString("O");

            return value.Trim();
        }

        return null;
    }

    private static string ExtractRepositoryName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return string.Empty;

        var token = fullName.Trim();
        var slashIndex = token.LastIndexOf('/');
        if (slashIndex >= 0 && slashIndex + 1 < token.Length)
            return token[(slashIndex + 1)..];
        return token;
    }

    private static string CompactToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private static HashSet<string> BuildProjectIdentifierCandidates(ProjectCatalogEntry project)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            var token = value.Trim();
            if (string.IsNullOrWhiteSpace(token))
                return;

            candidates.Add(token);
            var dotVariant = token.Replace("-", ".", StringComparison.Ordinal);
            if (!string.Equals(dotVariant, token, StringComparison.Ordinal))
                candidates.Add(dotVariant);
            var dashVariant = token.Replace(".", "-", StringComparison.Ordinal);
            if (!string.Equals(dashVariant, token, StringComparison.Ordinal))
                candidates.Add(dashVariant);
        }

        Add(project.Slug);
        Add(project.Name);
        Add(project.GitHubRepo);
        Add(ExtractRepositoryName(project.GitHubRepo));
        return candidates;
    }

    private static void AppendProjectFrontMatterExtensions(List<string> lines, ProjectCatalogEntry project)
    {
        if (!string.IsNullOrWhiteSpace(project.ContentMode))
            WriteMetaString(lines, "meta.project_content_mode", project.ContentMode);

        if (project.Links is { Count: > 0 })
        {
            WriteMetaString(lines, "meta.project_link_docs", TryGetDictionaryValue(project.Links, "docs"));
            WriteMetaString(lines, "meta.project_link_blog", TryGetDictionaryValue(project.Links, "blog"));
            WriteMetaString(lines, "meta.project_link_api_dotnet", TryGetDictionaryValue(project.Links, "apiDotNet"));
            WriteMetaString(lines, "meta.project_link_api_powershell", TryGetDictionaryValue(project.Links, "apiPowerShell"));
            WriteMetaString(lines, "meta.project_link_examples", TryGetDictionaryValue(project.Links, "examples"));
            WriteMetaString(lines, "meta.project_link_changelog", TryGetDictionaryValue(project.Links, "changelog"));
            WriteMetaString(lines, "meta.project_link_releases", TryGetDictionaryValue(project.Links, "releases"));
            WriteMetaString(lines, "meta.project_link_source", TryGetDictionaryValue(project.Links, "source"));
            WriteMetaString(lines, "meta.project_link_website", TryGetDictionaryValue(project.Links, "website"));
            WriteMetaString(lines, "meta.project_link_nuget", TryGetDictionaryValue(project.Links, "nuget"));
            WriteMetaString(lines, "meta.project_link_psgallery", TryGetDictionaryValue(project.Links, "powerShellGallery"));
        }

        if (project.Surfaces is { Count: > 0 })
        {
            WriteMetaBoolean(lines, "meta.project_surface_docs", TryGetDictionaryBool(project.Surfaces, "docs"));
            WriteMetaBoolean(lines, "meta.project_surface_api_dotnet", TryGetDictionaryBool(project.Surfaces, "apiDotNet"));
            WriteMetaBoolean(lines, "meta.project_surface_api_powershell", TryGetDictionaryBool(project.Surfaces, "apiPowerShell"));
            WriteMetaBoolean(lines, "meta.project_surface_examples", TryGetDictionaryBool(project.Surfaces, "examples"));
            WriteMetaBoolean(lines, "meta.project_surface_changelog", TryGetDictionaryBool(project.Surfaces, "changelog"));
            WriteMetaBoolean(lines, "meta.project_surface_releases", TryGetDictionaryBool(project.Surfaces, "releases"));
        }

        if (project.Artifacts is not null)
        {
            WriteMetaString(lines, "meta.project_artifact_docs", project.Artifacts.Docs);
            WriteMetaString(lines, "meta.project_artifact_api", project.Artifacts.Api);
            WriteMetaString(lines, "meta.project_artifact_examples", project.Artifacts.Examples);
        }

        if (project.Metrics is null)
            return;

        if (project.Metrics.GitHub is not null)
        {
            WriteMetaString(lines, "meta.project_github_repository", project.Metrics.GitHub.Repository);
            WriteMetaString(lines, "meta.project_github_url", project.Metrics.GitHub.Url);
            WriteMetaString(lines, "meta.project_github_language", project.Metrics.GitHub.Language);
            WriteMetaInteger(lines, "meta.project_github_stars", project.Metrics.GitHub.Stars);
            WriteMetaInteger(lines, "meta.project_github_forks", project.Metrics.GitHub.Forks);
            WriteMetaInteger(lines, "meta.project_github_watchers", project.Metrics.GitHub.Watchers);
            WriteMetaInteger(lines, "meta.project_github_open_issues", project.Metrics.GitHub.OpenIssues);
            WriteMetaBoolean(lines, "meta.project_github_archived", project.Metrics.GitHub.Archived);
            WriteMetaString(lines, "meta.project_github_last_pushed_at", project.Metrics.GitHub.LastPushedAt);
            WriteMetaString(lines, "meta.project_github_last_pushed_at_display", FormatDateForDisplay(project.Metrics.GitHub.LastPushedAt));
        }

        if (project.Metrics.NuGet is not null)
        {
            WriteMetaString(lines, "meta.project_nuget_id", project.Metrics.NuGet.Id);
            WriteMetaString(lines, "meta.project_nuget_version", project.Metrics.NuGet.Version);
            WriteMetaInteger(lines, "meta.project_nuget_downloads", project.Metrics.NuGet.TotalDownloads);
            WriteMetaString(lines, "meta.project_nuget_package_url", project.Metrics.NuGet.PackageUrl);
            WriteMetaString(lines, "meta.project_nuget_project_url", project.Metrics.NuGet.ProjectUrl);
        }

        if (project.Metrics.PowerShellGallery is not null)
        {
            WriteMetaString(lines, "meta.project_psgallery_id", project.Metrics.PowerShellGallery.Id);
            WriteMetaString(lines, "meta.project_psgallery_version", project.Metrics.PowerShellGallery.Version);
            WriteMetaInteger(lines, "meta.project_psgallery_downloads", project.Metrics.PowerShellGallery.TotalDownloads);
            WriteMetaString(lines, "meta.project_psgallery_url", project.Metrics.PowerShellGallery.GalleryUrl);
            WriteMetaString(lines, "meta.project_psgallery_project_url", project.Metrics.PowerShellGallery.ProjectUrl);
        }

        if (project.Metrics.Downloads is not null)
        {
            WriteMetaInteger(lines, "meta.project_downloads_total", project.Metrics.Downloads.Total);
            WriteMetaInteger(lines, "meta.project_downloads_nuget", project.Metrics.Downloads.NuGet);
            WriteMetaInteger(lines, "meta.project_downloads_psgallery", project.Metrics.Downloads.PowerShellGallery);
        }

        if (project.Metrics.Release is not null)
        {
            WriteMetaString(lines, "meta.project_release_latest_tag", project.Metrics.Release.LatestTag);
            WriteMetaString(lines, "meta.project_release_latest_name", project.Metrics.Release.LatestName);
            WriteMetaString(lines, "meta.project_release_latest_url", project.Metrics.Release.LatestUrl);
            WriteMetaString(lines, "meta.project_release_latest_published_at", project.Metrics.Release.LatestPublishedAt);
            WriteMetaString(lines, "meta.project_release_latest_published_at_display", FormatDateForDisplay(project.Metrics.Release.LatestPublishedAt));
            WriteMetaBoolean(lines, "meta.project_release_is_prerelease", project.Metrics.Release.IsPrerelease);
            WriteMetaBoolean(lines, "meta.project_release_is_draft", project.Metrics.Release.IsDraft);
        }
    }

    private static string? FormatDateForDisplay(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (DateTimeOffset.TryParse(value, out var parsed))
            return parsed.ToString("yyyy-MM-dd");
        return value.Trim();
    }

    private static string? TryGetDictionaryValue(Dictionary<string, string?> dictionary, string key)
    {
        if (dictionary.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value.Trim();
        return null;
    }

    private static bool? TryGetDictionaryBool(Dictionary<string, bool> dictionary, string key)
    {
        if (dictionary.TryGetValue(key, out var value))
            return value;
        return null;
    }

    private static void WriteMetaString(List<string> lines, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        lines.Add($"{key}: {YamlQuote(value)}");
    }

    private static void WriteMetaInteger(List<string> lines, string key, long value)
    {
        if (value <= 0)
            return;
        lines.Add($"{key}: {value}");
    }

    private static void WriteMetaBoolean(List<string> lines, string key, bool? value)
    {
        if (!value.HasValue)
            return;
        lines.Add($"{key}: {value.Value.ToString().ToLowerInvariant()}");
    }

    private static bool CanOverwriteGenerated(string filePath, bool forceOverwriteExisting)
    {
        if (!File.Exists(filePath))
            return true;
        if (forceOverwriteExisting)
            return true;

        try
        {
            var lines = File.ReadLines(filePath).Take(80);
            var head = string.Join('\n', lines);
            return GeneratedProjectMarkers.Any(marker =>
                head.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        catch
        {
            return false;
        }
    }

    private static void WriteMarkdown(string path, IEnumerable<string> lines)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n");
    }

    private static string NormalizeSlug(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();
    }

    private static string NormalizeProjectMode(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        return value.Trim().ToLowerInvariant();
    }

    private static string NormalizeProjectContentMode(string? value, string? modeFallback)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return value.Trim().ToLowerInvariant();

        var mode = NormalizeProjectMode(modeFallback, "hub-full");
        return mode.Equals("dedicated-external", StringComparison.OrdinalIgnoreCase)
            ? "external"
            : "hybrid";
    }

    private static string? NormalizeOptionalString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Trim();
    }

    private static string NormalizeProjectStatus(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        return value.Trim().ToLowerInvariant();
    }

    private static string YamlQuote(string? value)
    {
        var text = value ?? string.Empty;
        return "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private sealed class ProjectCatalogDocument
    {
        [JsonPropertyName("generatedOn")]
        public string? GeneratedOn { get; set; }

        [JsonPropertyName("source")]
        public string? Source { get; set; }

        [JsonPropertyName("top")]
        public int? Top { get; set; }

        [JsonPropertyName("projects")]
        public List<ProjectCatalogEntry> Projects { get; set; } = new();

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    private sealed class ProjectCatalogEntry
    {
        [JsonPropertyName("slug")]
        public string? Slug { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("mode")]
        public string? Mode { get; set; }

        [JsonPropertyName("contentMode")]
        public string? ContentMode { get; set; }

        [JsonPropertyName("hubPath")]
        public string? HubPath { get; set; }

        [JsonPropertyName("githubRepo")]
        public string? GitHubRepo { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("listed")]
        public bool? Listed { get; set; }

        [JsonPropertyName("externalUrl")]
        public string? ExternalUrl { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("manifestGeneratedAt")]
        public string? ManifestGeneratedAt { get; set; }

        [JsonPropertyName("manifestCommit")]
        public string? ManifestCommit { get; set; }

        [JsonPropertyName("manifestPath")]
        public string? ManifestPath { get; set; }

        [JsonPropertyName("aliases")]
        public string[]? Aliases { get; set; }

        [JsonPropertyName("links")]
        public Dictionary<string, string?>? Links { get; set; }

        [JsonPropertyName("surfaces")]
        public Dictionary<string, bool>? Surfaces { get; set; }

        [JsonPropertyName("artifacts")]
        public ProjectCatalogArtifacts? Artifacts { get; set; }

        [JsonPropertyName("metrics")]
        public ProjectCatalogMetrics? Metrics { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    private sealed class ProjectCatalogMetrics
    {
        [JsonPropertyName("github")]
        public ProjectCatalogGitHubMetrics? GitHub { get; set; }

        [JsonPropertyName("nuget")]
        public ProjectCatalogNuGetMetrics? NuGet { get; set; }

        [JsonPropertyName("powerShellGallery")]
        public ProjectCatalogPowerShellGalleryMetrics? PowerShellGallery { get; set; }

        [JsonPropertyName("release")]
        public ProjectCatalogReleaseMetrics? Release { get; set; }

        [JsonPropertyName("downloads")]
        public ProjectCatalogDownloadsMetrics? Downloads { get; set; }
    }

    private sealed class ProjectCatalogArtifacts
    {
        [JsonPropertyName("docs")]
        public string? Docs { get; set; }

        [JsonPropertyName("api")]
        public string? Api { get; set; }

        [JsonPropertyName("examples")]
        public string? Examples { get; set; }
    }

    private sealed class ProjectCatalogGitHubMetrics
    {
        [JsonPropertyName("repository")]
        public string? Repository { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("language")]
        public string? Language { get; set; }

        [JsonPropertyName("stars")]
        public int Stars { get; set; }

        [JsonPropertyName("forks")]
        public int Forks { get; set; }

        [JsonPropertyName("watchers")]
        public int Watchers { get; set; }

        [JsonPropertyName("openIssues")]
        public int OpenIssues { get; set; }

        [JsonPropertyName("archived")]
        public bool Archived { get; set; }

        [JsonPropertyName("lastPushedAt")]
        public string? LastPushedAt { get; set; }
    }

    private sealed class ProjectCatalogNuGetMetrics
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("totalDownloads")]
        public long TotalDownloads { get; set; }

        [JsonPropertyName("packageUrl")]
        public string? PackageUrl { get; set; }

        [JsonPropertyName("projectUrl")]
        public string? ProjectUrl { get; set; }
    }

    private sealed class ProjectCatalogPowerShellGalleryMetrics
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("totalDownloads")]
        public long TotalDownloads { get; set; }

        [JsonPropertyName("galleryUrl")]
        public string? GalleryUrl { get; set; }

        [JsonPropertyName("projectUrl")]
        public string? ProjectUrl { get; set; }
    }

    private sealed class ProjectCatalogDownloadsMetrics
    {
        [JsonPropertyName("total")]
        public long Total { get; set; }

        [JsonPropertyName("nuget")]
        public long NuGet { get; set; }

        [JsonPropertyName("powerShellGallery")]
        public long PowerShellGallery { get; set; }
    }

    private sealed class ProjectCatalogReleaseMetrics
    {
        [JsonPropertyName("latestTag")]
        public string? LatestTag { get; set; }

        [JsonPropertyName("latestName")]
        public string? LatestName { get; set; }

        [JsonPropertyName("latestUrl")]
        public string? LatestUrl { get; set; }

        [JsonPropertyName("latestPublishedAt")]
        public string? LatestPublishedAt { get; set; }

        [JsonPropertyName("isPrerelease")]
        public bool IsPrerelease { get; set; }

        [JsonPropertyName("isDraft")]
        public bool IsDraft { get; set; }
    }

    private sealed class ProjectManifestData
    {
        [JsonPropertyName("slug")]
        public string? Slug { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("mode")]
        public string? Mode { get; set; }

        [JsonPropertyName("contentMode")]
        public string? ContentMode { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("listed")]
        public bool? Listed { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("generatedAt")]
        public string? GeneratedAt { get; set; }

        [JsonPropertyName("commit")]
        public string? Commit { get; set; }

        [JsonPropertyName("aliases")]
        public List<string?>? Aliases { get; set; }

        [JsonPropertyName("links")]
        public Dictionary<string, string?>? Links { get; set; }

        [JsonPropertyName("surfaces")]
        public Dictionary<string, bool>? Surfaces { get; set; }

        [JsonPropertyName("artifacts")]
        public ProjectCatalogArtifacts? Artifacts { get; set; }
    }

    private sealed class ProjectCatalogFinding
    {
        [JsonPropertyName("level")]
        public string Level { get; set; } = "warning";

        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("slug")]
        public string? Slug { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        public static ProjectCatalogFinding Error(string code, string? slug, string message)
            => new() { Level = "error", Code = code, Slug = slug, Message = message };

        public static ProjectCatalogFinding Warning(string code, string? slug, string message)
            => new() { Level = "warning", Code = code, Slug = slug, Message = message };
    }
}
