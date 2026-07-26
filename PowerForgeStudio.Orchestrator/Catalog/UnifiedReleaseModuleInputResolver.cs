using PowerForge;

namespace PowerForgeStudio.Orchestrator.Catalog;

/// <summary>
/// Resolves the effective module recipe declared by a unified release contract.
/// </summary>
internal static class UnifiedReleaseModuleInputResolver
{
    internal static UnifiedReleaseModuleInputPaths Resolve(
        string releaseConfigPath,
        PowerForgeModuleReleaseOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseConfigPath);
        ArgumentNullException.ThrowIfNull(options);

        var releaseDirectory = Path.GetDirectoryName(Path.GetFullPath(releaseConfigPath))
                               ?? Directory.GetCurrentDirectory();
        var repositoryRoot = string.IsNullOrWhiteSpace(options.RepositoryRoot)
            ? releaseDirectory
            : PathTokenProtection.GetFullPath(releaseDirectory, options.RepositoryRoot!);
        if (!string.IsNullOrWhiteSpace(options.ConfigPath))
        {
            return new UnifiedReleaseModuleInputPaths(
                PathTokenProtection.GetFullPath(repositoryRoot, options.ConfigPath!),
                ScriptPath: null);
        }

        var configuredScript = string.IsNullOrWhiteSpace(options.ScriptPath)
            ? Path.Combine("Module", "Build", "Build-Module.ps1")
            : options.ScriptPath!;
        return new UnifiedReleaseModuleInputPaths(
            ConfigPath: null,
            PathTokenProtection.GetFullPath(repositoryRoot, configuredScript));
    }
}

internal sealed record UnifiedReleaseModuleInputPaths(
    string? ConfigPath,
    string? ScriptPath);
