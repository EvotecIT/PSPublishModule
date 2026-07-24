using System.Text.Json;
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
        var releaseModuleBuildConfig = moduleBuildScript is null
            ? FindReleaseModuleBuildConfig(directoryPath, includeImmediateChildBuildFolders)
            : null;
        var moduleBuildConfig = moduleBuildScript is null
            ? releaseModuleBuildConfig ?? FindDirectModuleBuildConfig(directoryPath)
            : null;
        var moduleBuildInput = moduleBuildScript ?? moduleBuildConfig;
        var projectBuildScript = releaseModuleBuildConfig is null
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
            HasWebsiteSignals: hasWebsiteSignals);
    }

    private static string? FindReleaseModuleBuildConfig(string directoryPath, bool includeImmediateChildBuildFolders)
    {
        foreach (var releaseConfigPath in EnumerateBuildFiles(directoryPath, "release.json", includeImmediateChildBuildFolders))
        {
            var moduleConfigPath = ResolveModuleConfigFromRelease(releaseConfigPath);
            if (moduleConfigPath is not null)
            {
                return moduleConfigPath;
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

    private static string? ResolveModuleConfigFromRelease(string releaseConfigPath)
    {
        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(releaseConfigPath),
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (!document.RootElement.TryGetProperty("Module", out var module) ||
                module.ValueKind != JsonValueKind.Object ||
                !module.TryGetProperty("ConfigPath", out var configPathElement))
            {
                return null;
            }

            var configuredPath = configPathElement.GetString();
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                return null;
            }

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
            return IsModulePipelineConfig(fullPath) ? fullPath : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsModulePipelineConfig(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(path),
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return document.RootElement.TryGetProperty("Segments", out var segments) &&
                   segments.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

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

