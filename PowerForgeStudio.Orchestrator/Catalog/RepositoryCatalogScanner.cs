using System.Text.Json;
using PowerForge;
using PowerForgeStudio.Domain.Catalog;

namespace PowerForgeStudio.Orchestrator.Catalog;

public sealed class RepositoryCatalogScanner
{
    public RepositoryCatalogEntry InspectRepository(string rootPath, bool includeImmediateChildBuildFolders = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        return InspectDirectory(rootPath, includeImmediateChildBuildFolders);
    }

    public IReadOnlyList<RepositoryCatalogEntry> Scan(string rootPath)
    {
        var options = new ReleaseCatalogScanOptions {
            RootPath = rootPath
        };

        return Scan(options);
    }

    public IReadOnlyList<RepositoryCatalogEntry> Scan(ReleaseCatalogScanOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RootPath);

        if (!Directory.Exists(options.RootPath))
        {
            return Array.Empty<RepositoryCatalogEntry>();
        }

        var entries = new List<RepositoryCatalogEntry>();
        foreach (var directory in Directory.EnumerateDirectories(options.RootPath).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            entries.Add(InspectDirectory(directory, options.IncludeImmediateChildBuildFolders));
        }

        return entries;
    }

    public RepositoryCatalogSummary BuildSummary(IEnumerable<RepositoryCatalogEntry> entries)
    {
        var materialized = entries.ToList();
        return new RepositoryCatalogSummary(
            TotalRepositories: materialized.Count,
            ManagedRepositories: materialized.Count(entry => entry.IsReleaseManaged),
            ModuleRepositories: materialized.Count(entry => entry.RepositoryKind is ReleaseRepositoryKind.Module or ReleaseRepositoryKind.Mixed),
            LibraryRepositories: materialized.Count(entry => entry.RepositoryKind is ReleaseRepositoryKind.Library or ReleaseRepositoryKind.Mixed),
            WorktreeRepositories: materialized.Count(entry => entry.IsWorktree));
    }

    private static RepositoryCatalogEntry InspectDirectory(string directoryPath, bool includeImmediateChildBuildFolders)
    {
        var moduleBuildScript = FindBuildScript(directoryPath, "Build-Module.ps1", includeImmediateChildBuildFolders);
        var releaseContract = FindReleaseBuildContract(directoryPath, includeImmediateChildBuildFolders);
        var releaseModuleBuildConfig = releaseContract?.RequiresUnifiedExecution == true
            ? releaseContract.ModuleConfigPath
            : moduleBuildScript is null
                ? releaseContract?.ModuleConfigPath
                : null;
        var moduleBuildConfig = releaseModuleBuildConfig ?? (moduleBuildScript is null
            ? FindDirectModuleBuildConfig(directoryPath)
            : null);
        var moduleBuildInput = moduleBuildConfig ?? moduleBuildScript;
        var projectBuildScript = releaseContract?.IncludesPackages == true &&
                                 !releaseContract.ModuleIncludesPackages
            ? releaseContract.ConfigPath
            : releaseModuleBuildConfig is null
                ? FindBuildScript(directoryPath, "Build-Project.ps1", includeImmediateChildBuildFolders)
                : null;
        var hasWebsiteSignals = HasWebsiteSignals(directoryPath);

        return new RepositoryCatalogEntry(
            Name: Path.GetFileName(directoryPath),
            RootPath: directoryPath,
            RepositoryKind: DetermineKind(moduleBuildInput, projectBuildScript, hasWebsiteSignals),
            WorkspaceKind: DetermineWorkspaceKind(directoryPath),
            ModuleBuildScriptPath: moduleBuildInput,
            ProjectBuildScriptPath: projectBuildScript,
            IsWorktree: IsWorktree(directoryPath),
            HasWebsiteSignals: hasWebsiteSignals,
            UnifiedReleaseConfigPath: releaseContract?.RequiresUnifiedExecution == true
                ? releaseContract.ConfigPath
                : null);
    }

    private static ReleaseBuildContract? FindReleaseBuildContract(string directoryPath, bool includeImmediateChildBuildFolders)
    {
        foreach (var releaseConfigPath in EnumerateBuildFiles(directoryPath, "release.json", includeImmediateChildBuildFolders))
        {
            var contract = ResolveReleaseBuildContract(releaseConfigPath);
            if (contract is not null)
            {
                return contract;
            }
        }

        return null;
    }

    private static string? FindDirectModuleBuildConfig(string directoryPath)
    {
        var directConfig = Path.Combine(directoryPath, "powerforge.json");
        return IsModulePipelineConfig(directConfig) ? directConfig : null;
    }

    private static IEnumerable<string> EnumerateBuildFiles(string directoryPath, string fileName, bool includeImmediateChildBuildFolders)
    {
        var directCandidate = Path.Combine(directoryPath, "Build", fileName);
        if (File.Exists(directCandidate))
        {
            yield return directCandidate;
        }

        if (!includeImmediateChildBuildFolders)
        {
            yield break;
        }

        foreach (var childDirectory in Directory.EnumerateDirectories(directoryPath))
        {
            var nestedCandidate = Path.Combine(childDirectory, "Build", fileName);
            if (File.Exists(nestedCandidate))
            {
                yield return nestedCandidate;
            }
        }
    }

    private static ReleaseBuildContract? ResolveReleaseBuildContract(string releaseConfigPath)
    {
        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(releaseConfigPath),
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var includesPackages = document.RootElement.TryGetProperty("Packages", out var packages) &&
                                   packages.ValueKind == JsonValueKind.Object;
            string? moduleConfigPath = null;
            var moduleIncludesPackages = false;
            if (document.RootElement.TryGetProperty("Module", out var module) &&
                module.ValueKind == JsonValueKind.Object)
            {
                moduleIncludesPackages = module.TryGetProperty("IncludesPackages", out var includesPackagesElement) &&
                                         includesPackagesElement.ValueKind == JsonValueKind.True;
                if (module.TryGetProperty("ConfigPath", out var configPathElement))
                {
                    var configuredPath = configPathElement.GetString();
                    if (!string.IsNullOrWhiteSpace(configuredPath))
                    {
                        var releaseDirectory = Path.GetDirectoryName(releaseConfigPath) ?? Directory.GetCurrentDirectory();
                        var repositoryRoot = releaseDirectory;
                        if (module.TryGetProperty("RepositoryRoot", out var rootElement) &&
                            !string.IsNullOrWhiteSpace(rootElement.GetString()))
                        {
                            repositoryRoot = Path.GetFullPath(Path.Combine(releaseDirectory, rootElement.GetString()!));
                        }

                        var fullPath = Path.GetFullPath(Path.IsPathRooted(configuredPath)
                            ? configuredPath
                            : Path.Combine(repositoryRoot, configuredPath));
                        // Keep the declared release contract even if the module JSON later becomes
                        // missing or invalid. Plan/build/publish surfaces must report that failure
                        // instead of silently downgrading the repository to an unmanaged shape.
                        moduleConfigPath = fullPath;
                    }
                }
            }

            var hasTools = document.RootElement.TryGetProperty("Tools", out var tools) &&
                           tools.ValueKind == JsonValueKind.Object;
            var hasGitHub = document.RootElement.TryGetProperty("GitHub", out var gitHub) &&
                            gitHub.ValueKind == JsonValueKind.Object;
            var hasWorkspaceValidation = document.RootElement.TryGetProperty("WorkspaceValidation", out var workspaceValidation) &&
                                         workspaceValidation.ValueKind == JsonValueKind.Object;
            var hasWinget = document.RootElement.TryGetProperty("Winget", out var winget) &&
                            winget.ValueKind == JsonValueKind.Object;
            return moduleConfigPath is not null || includesPackages || hasTools || hasGitHub || hasWorkspaceValidation || hasWinget
                ? new ReleaseBuildContract(
                    releaseConfigPath,
                    moduleConfigPath,
                    includesPackages,
                    moduleIncludesPackages,
                    RequiresUnifiedExecution: hasTools || hasGitHub || hasWorkspaceValidation || hasWinget)
                : null;
        }
        catch (Exception ex) when (
            ex is JsonException or
            InvalidOperationException or
            IOException or
            ArgumentException or
            NotSupportedException)
        {
            return null;
        }
    }

    private static bool IsModulePipelineConfig(string path)
    {
        return new ModulePipelineConfigurationService().TryLoad(path, out _);
    }

    private sealed record ReleaseBuildContract(
        string ConfigPath,
        string? ModuleConfigPath,
        bool IncludesPackages,
        bool ModuleIncludesPackages,
        bool RequiresUnifiedExecution);

    private static string? FindBuildScript(string directoryPath, string fileName, bool includeImmediateChildBuildFolders)
    {
        var directCandidate = Path.Combine(directoryPath, "Build", fileName);
        if (File.Exists(directCandidate))
        {
            return directCandidate;
        }

        if (!includeImmediateChildBuildFolders)
        {
            return null;
        }

        foreach (var childDirectory in Directory.EnumerateDirectories(directoryPath))
        {
            var nestedCandidate = Path.Combine(childDirectory, "Build", fileName);
            if (File.Exists(nestedCandidate))
            {
                return nestedCandidate;
            }
        }

        return null;
    }

    private static bool HasWebsiteSignals(string directoryPath)
        => File.Exists(Path.Combine(directoryPath, "build.ps1"))
           || Directory.Exists(Path.Combine(directoryPath, "Website"))
           || Directory.Exists(Path.Combine(directoryPath, "website"));

    private static ReleaseRepositoryKind DetermineKind(string? moduleBuildScript, string? projectBuildScript, bool hasWebsiteSignals)
    {
        if (moduleBuildScript is not null && projectBuildScript is not null)
        {
            return ReleaseRepositoryKind.Mixed;
        }

        if (moduleBuildScript is not null)
        {
            return ReleaseRepositoryKind.Module;
        }

        if (projectBuildScript is not null)
        {
            return ReleaseRepositoryKind.Library;
        }

        if (hasWebsiteSignals)
        {
            return ReleaseRepositoryKind.Website;
        }

        return ReleaseRepositoryKind.Unknown;
    }

    private static ReleaseWorkspaceKind DetermineWorkspaceKind(string directoryPath)
    {
        var leafName = Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (IsWorktree(directoryPath))
        {
            return ReleaseWorkspaceKind.Worktree;
        }

        if (leafName.Contains("-review", StringComparison.OrdinalIgnoreCase) || leafName.Contains("-pr", StringComparison.OrdinalIgnoreCase))
        {
            return ReleaseWorkspaceKind.ReviewClone;
        }

        if (leafName.StartsWith("_tmp", StringComparison.OrdinalIgnoreCase)
            || leafName.StartsWith("_backup", StringComparison.OrdinalIgnoreCase)
            || leafName.StartsWith("_test", StringComparison.OrdinalIgnoreCase))
        {
            return ReleaseWorkspaceKind.TemporaryClone;
        }

        return ReleaseWorkspaceKind.PrimaryRepository;
    }

    private static bool IsWorktree(string directoryPath)
    {
        var normalized = directoryPath.Replace('\\', '/');
        var leafName = Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return normalized.Contains("/_worktrees/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/_wt/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/.wt-", StringComparison.OrdinalIgnoreCase)
               || leafName.StartsWith(".wt-", StringComparison.OrdinalIgnoreCase);
    }
}

