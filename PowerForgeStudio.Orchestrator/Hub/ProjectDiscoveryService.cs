using PowerForge;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Portfolio;
using static PowerForgeStudio.Orchestrator.Catalog.WorktreeDetector;

using HubProjectKind = PowerForgeStudio.Domain.Hub.ProjectKind;

namespace PowerForgeStudio.Orchestrator.Hub;

public sealed class ProjectDiscoveryService
{
    private readonly RepositoryCatalogScanner _catalogScanner;
    private readonly IGitRemoteResolver _gitRemoteResolver;
    private readonly IProcessRunner _processRunner;

    public ProjectDiscoveryService()
        : this(new RepositoryCatalogScanner(), new GitRemoteResolver(), new ProcessRunner())
    {
    }

    internal ProjectDiscoveryService(
        RepositoryCatalogScanner catalogScanner,
        IGitRemoteResolver gitRemoteResolver,
        IProcessRunner processRunner)
    {
        _catalogScanner = catalogScanner;
        _gitRemoteResolver = gitRemoteResolver;
        _processRunner = processRunner;
    }

    public async Task<IReadOnlyList<ProjectEntry>> ScanWorkspaceAsync(
        string rootPath,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        if (!Directory.Exists(rootPath))
        {
            return [];
        }

        var catalogEntries = _catalogScanner.Scan(rootPath);
        var results = new ProjectEntry[catalogEntries.Count];
        var completed = 0;

        await Parallel.ForEachAsync(
            Enumerable.Range(0, catalogEntries.Count),
            new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken },
            async (index, ct) =>
            {
                var entry = catalogEntries[index];
                results[index] = await EnrichEntryAsync(entry, ct).ConfigureAwait(false);
                var count = Interlocked.Increment(ref completed);
                progress?.Report(count);
            }).ConfigureAwait(false);

        // Filter out non-git directories (empty folders, leftover directories without repos)
        return results.Where(IsGitDirectory).ToArray();
    }

    private async Task<ProjectEntry> EnrichEntryAsync(RepositoryCatalogEntry catalogEntry, CancellationToken cancellationToken)
    {
        var rootPath = catalogEntry.RootPath;

        // Resolve origin URL once and parse for both GitHub and ADO
        var originUrl = await ResolveOriginUrlAsync(rootPath, cancellationToken).ConfigureAwait(false);
        var gitHubSlug = ParseGitHubSlug(originUrl);
        var azureDevOpsSlug = ParseAzureDevOpsSlug(originUrl);

        var hasPowerForgeJson = File.Exists(Path.Combine(rootPath, "powerforge.json"));
        var hasProjectBuildJson = FileExistsInBuildFolder(rootPath, "project.build.json");
        var hasSolution = Directory.EnumerateFiles(rootPath, "*.sln", SearchOption.TopDirectoryOnly).Any();
        var hasGitHubWorkflows = Directory.Exists(Path.Combine(rootPath, ".github", "workflows"));
        var hasAzurePipelines = File.Exists(Path.Combine(rootPath, "azure-pipelines.yml"));

        var buildScriptKind = DetermineBuildScriptKind(catalogEntry, hasPowerForgeJson, hasProjectBuildJson);
        var category = DetermineCategory(catalogEntry, hasSolution);
        var kind = DetermineProjectKind(catalogEntry);
        var lastCommitUtc = await GetLastCommitDateAsync(rootPath, cancellationToken).ConfigureAwait(false);

        return new ProjectEntry(
            Name: catalogEntry.Name,
            RootPath: rootPath,
            GitHubSlug: gitHubSlug,
            AzureDevOpsSlug: azureDevOpsSlug,
            Category: category,
            Kind: kind,
            RepositoryKind: catalogEntry.RepositoryKind,
            WorkspaceKind: catalogEntry.WorkspaceKind,
            BuildScriptKind: buildScriptKind,
            PrimaryBuildScriptPath: catalogEntry.PrimaryBuildScriptPath,
            HasPowerForgeJson: hasPowerForgeJson,
            HasProjectBuildJson: hasProjectBuildJson,
            HasSolution: hasSolution,
            HasGitHubWorkflows: hasGitHubWorkflows,
            HasAzurePipelines: hasAzurePipelines,
            LastCommitUtc: lastCommitUtc,
            LastScanUtc: DateTimeOffset.UtcNow);
    }

    private async Task<string?> ResolveOriginUrlAsync(string rootPath, CancellationToken cancellationToken)
    {
        try
        {
            return await _gitRemoteResolver.ResolveOriginUrlAsync(rootPath, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static string? ParseGitHubSlug(string? originUrl)
    {
        if (GitHubInboxService.TryParseGitHubSlug(originUrl, out var owner, out var repo))
        {
            return $"{owner}/{repo}";
        }

        return null;
    }

    private static string? ParseAzureDevOpsSlug(string? originUrl)
    {
        return TryParseAzureDevOpsSlug(originUrl, out var slug) ? slug : null;
    }

    private async Task<DateTimeOffset?> GetLastCommitDateAsync(string rootPath, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _processRunner.RunAsync(
                new ProcessRunRequest("git", rootPath, ["log", "-1", "--format=%aI"], TimeSpan.FromSeconds(5)),
                cancellationToken).ConfigureAwait(false);

            if (result.Succeeded && DateTimeOffset.TryParse(result.StdOut.Trim(), out var date))
            {
                return date;
            }
        }
        catch
        {
            // Not a git repo
        }

        return null;
    }

    private static BuildScriptKind DetermineBuildScriptKind(
        RepositoryCatalogEntry entry,
        bool hasPowerForgeJson,
        bool hasProjectBuildJson)
    {
        var hasBuildModule = entry.ModuleBuildScriptPath is not null;
        var hasBuildProject = entry.ProjectBuildScriptPath is not null;

        if (hasPowerForgeJson && (hasBuildModule || hasBuildProject))
        {
            return BuildScriptKind.Hybrid;
        }

        if (hasPowerForgeJson)
        {
            return BuildScriptKind.PowerForgeJson;
        }

        if (hasProjectBuildJson)
        {
            return BuildScriptKind.ProjectBuildJson;
        }

        if (hasBuildModule && hasBuildProject)
        {
            return BuildScriptKind.Hybrid;
        }

        if (hasBuildModule)
        {
            return BuildScriptKind.BuildModule;
        }

        if (hasBuildProject)
        {
            return BuildScriptKind.BuildProject;
        }

        return BuildScriptKind.None;
    }

    private static ProjectCategory DetermineCategory(RepositoryCatalogEntry entry, bool hasSolution)
    {
        return entry.RepositoryKind switch
        {
            ReleaseRepositoryKind.Module => ProjectCategory.PowerShellModule,
            ReleaseRepositoryKind.Library => hasSolution ? ProjectCategory.DotNetLibrary : ProjectCategory.Tool,
            ReleaseRepositoryKind.Mixed => ProjectCategory.Mixed,
            ReleaseRepositoryKind.Website => ProjectCategory.Website,
            _ => hasSolution ? ProjectCategory.DotNetLibrary : ProjectCategory.Unknown
        };
    }

    private static HubProjectKind DetermineProjectKind(RepositoryCatalogEntry entry)
    {
        if (entry.IsWorktree || entry.WorkspaceKind == ReleaseWorkspaceKind.Worktree)
        {
            return HubProjectKind.Worktree;
        }

        // Real worktree detection via shared utility
        if (WorktreeDetector.IsWorktree(entry.RootPath))
        {
            return HubProjectKind.Worktree;
        }

        if (entry.WorkspaceKind == ReleaseWorkspaceKind.TemporaryClone)
        {
            return HubProjectKind.Archive;
        }

        return HubProjectKind.Primary;
    }

    private static bool IsGitDirectory(ProjectEntry entry)
        => WorktreeDetector.IsGitRepository(entry.RootPath);

    private static bool FileExistsInBuildFolder(string rootPath, string fileName)
    {
        var directCandidate = Path.Combine(rootPath, "Build", fileName);
        if (File.Exists(directCandidate))
        {
            return true;
        }

        foreach (var childDirectory in Directory.EnumerateDirectories(rootPath))
        {
            var nestedCandidate = Path.Combine(childDirectory, "Build", fileName);
            if (File.Exists(nestedCandidate))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool TryParseAzureDevOpsSlug(string? originUrl, out string slug)
    {
        slug = string.Empty;
        if (string.IsNullOrWhiteSpace(originUrl))
        {
            return false;
        }

        var trimmed = originUrl.Trim();

        // https://dev.azure.com/{org}/{project}/_git/{repo}
        if (trimmed.StartsWith("https://dev.azure.com/", StringComparison.OrdinalIgnoreCase))
        {
            var path = trimmed["https://dev.azure.com/".Length..].TrimEnd('/');
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4 && string.Equals(parts[2], "_git", StringComparison.OrdinalIgnoreCase))
            {
                slug = $"{parts[0]}/{parts[1]}/{parts[3]}";
                return true;
            }
        }

        // https://{org}.visualstudio.com/{project}/_git/{repo}
        if (trimmed.Contains(".visualstudio.com/", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            {
                var org = uri.Host.Split('.')[0];
                var pathParts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (pathParts.Length >= 3 && string.Equals(pathParts[1], "_git", StringComparison.OrdinalIgnoreCase))
                {
                    slug = $"{org}/{pathParts[0]}/{pathParts[2]}";
                    return true;
                }
            }
        }

        return false;
    }
}
