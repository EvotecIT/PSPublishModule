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
        var releaseModuleBuildInput = releaseContract?.ModuleBuildInputPath;
        var discoveredModuleBuildInput = releaseModuleBuildInput ?? (moduleBuildScript is null
            ? FindDirectModuleBuildConfig(directoryPath)
            : null);
        var moduleBuildInput = discoveredModuleBuildInput ?? moduleBuildScript;
        var projectBuildScript = releaseContract?.IncludesPackages == true &&
                                 !releaseContract.ModuleIncludesPackages
            ? releaseContract.ConfigPath
            : releaseModuleBuildInput is null
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
        foreach (var releaseConfigPath in EnumerateReleaseConfigFiles(directoryPath, includeImmediateChildBuildFolders))
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
        foreach (var relativePath in new[]
        {
            "powerforge.json",
            Path.Combine("Build", "powerforge.json"),
            Path.Combine(".powerforge", "powerforge.json")
        })
        {
            var candidate = Path.Combine(directoryPath, relativePath);
            if (IsModulePipelineConfig(candidate))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateReleaseConfigFiles(
        string directoryPath,
        bool includeImmediateChildBuildFolders)
    {
        foreach (var candidate in EnumerateReleaseConfigCandidates(directoryPath))
        {
            if (File.Exists(candidate))
                yield return candidate;
        }

        if (!includeImmediateChildBuildFolders)
        {
            yield break;
        }

        foreach (var childDirectory in Directory.EnumerateDirectories(directoryPath))
        {
            foreach (var nestedCandidate in EnumerateReleaseConfigCandidates(childDirectory))
            {
                if (File.Exists(nestedCandidate))
                    yield return nestedCandidate;
            }
        }
    }

    private static IEnumerable<string> EnumerateReleaseConfigCandidates(string directoryPath)
    {
        yield return Path.Combine(directoryPath, "powerforge.release.json");
        yield return Path.Combine(directoryPath, ".powerforge", "release.json");
        yield return Path.Combine(directoryPath, "Build", "release.json");
        yield return Path.Combine(directoryPath, "release.json");
    }

    private static ReleaseBuildContract? ResolveReleaseBuildContract(string releaseConfigPath)
    {
        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(releaseConfigPath),
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var includesPackages = TryGetPropertyIgnoreCase(document.RootElement, "Packages", out var packages) &&
                                   packages.ValueKind == JsonValueKind.Object;
            string? moduleBuildInputPath = null;
            var moduleIncludesPackages = false;
            var hasModule = TryGetPropertyIgnoreCase(document.RootElement, "Module", out var module) &&
                            module.ValueKind == JsonValueKind.Object;
            if (hasModule)
            {
                moduleIncludesPackages = TryGetPropertyIgnoreCase(module, "IncludesPackages", out var includesPackagesElement) &&
                                         includesPackagesElement.ValueKind == JsonValueKind.True;
                var moduleOptions = new PowerForgeModuleReleaseOptions();
                if (TryGetPropertyIgnoreCase(module, "RepositoryRoot", out var rootElement))
                    moduleOptions.RepositoryRoot = rootElement.GetString();
                if (TryGetPropertyIgnoreCase(module, "ConfigPath", out var configPathElement))
                    moduleOptions.ConfigPath = configPathElement.GetString();
                if (TryGetPropertyIgnoreCase(module, "ScriptPath", out var scriptPathElement))
                    moduleOptions.ScriptPath = scriptPathElement.GetString();
                var moduleInput = UnifiedReleaseModuleInputResolver.Resolve(releaseConfigPath, moduleOptions);
                // Keep the declared release contract even if its module input later
                // becomes missing or invalid. Plan/build/publish must report that
                // failure instead of silently downgrading to an unmanaged shape.
                moduleBuildInputPath = moduleInput.ConfigPath ?? moduleInput.ScriptPath;
            }

            var hasTools = TryGetPropertyIgnoreCase(document.RootElement, "Tools", out var tools) &&
                           tools.ValueKind == JsonValueKind.Object;
            var hasGitHub = TryGetPropertyIgnoreCase(document.RootElement, "GitHub", out var gitHub) &&
                            gitHub.ValueKind == JsonValueKind.Object;
            var hasWorkspaceValidation = TryGetPropertyIgnoreCase(document.RootElement, "WorkspaceValidation", out var workspaceValidation) &&
                                         workspaceValidation.ValueKind == JsonValueKind.Object;
            var hasWinget = TryGetPropertyIgnoreCase(document.RootElement, "Winget", out var winget) &&
                            winget.ValueKind == JsonValueKind.Object;
            var hasAppleApps = TryGetPropertyIgnoreCase(document.RootElement, "AppleApps", out var appleApps) &&
                               appleApps.ValueKind == JsonValueKind.Object;
            var hasVirusTotal = TryGetPropertyIgnoreCase(document.RootElement, "VirusTotal", out var virusTotal) &&
                                virusTotal.ValueKind == JsonValueKind.Object;
            return hasModule || includesPackages || hasTools || hasGitHub || hasWorkspaceValidation || hasWinget || hasAppleApps || hasVirusTotal
                ? new ReleaseBuildContract(
                    releaseConfigPath,
                    moduleBuildInputPath,
                    includesPackages,
                    moduleIncludesPackages,
                    RequiresUnifiedExecution: hasModule || includesPackages || hasTools || hasGitHub || hasWorkspaceValidation || hasWinget || hasAppleApps || hasVirusTotal)
                : null;
        }
        catch (Exception ex) when (
            ex is JsonException or
            InvalidOperationException or
            IOException or
            ArgumentException or
            NotSupportedException)
        {
            // Preserve the discovered release contract even when it cannot be parsed.
            // Planning and execution must surface the configuration error rather than
            // silently falling back to incomplete module/project adapters.
            return new ReleaseBuildContract(
                releaseConfigPath,
                ModuleBuildInputPath: null,
                IncludesPackages: false,
                ModuleIncludesPackages: false,
                RequiresUnifiedExecution: true);
        }
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static bool IsModulePipelineConfig(string path)
    {
        return new ModulePipelineConfigurationService().TryLoad(path, out _);
    }

    private sealed record ReleaseBuildContract(
        string ConfigPath,
        string? ModuleBuildInputPath,
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

