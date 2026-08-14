namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private static string[] ResolveGeneratedModuleProvenancePaths(
        PowerForgeModuleReleaseOptions? options,
        string releaseConfigPath,
        PowerForgeReleaseRequest request,
        bool runModule)
    {
        if (!runModule ||
            options is null ||
            request.ModuleRunMode != ConfigurationGateMode.Publish ||
            string.IsNullOrWhiteSpace(request.EffectiveConfigurationPath))
        {
            return Array.Empty<string>();
        }

        string configDirectory = Path.GetDirectoryName(releaseConfigPath) ?? Directory.GetCurrentDirectory();
        string repositoryRoot = Path.GetFullPath(Path.IsPathRooted(options.RepositoryRoot)
            ? options.RepositoryRoot!
            : Path.Combine(configDirectory, string.IsNullOrWhiteSpace(options.RepositoryRoot) ? "." : options.RepositoryRoot!));
        string manifestPath = string.IsNullOrWhiteSpace(options.ManifestPath)
            ? Path.Combine(repositoryRoot, "Module", "PSPublishModule.psd1")
            : Path.GetFullPath(Path.IsPathRooted(options.ManifestPath)
                ? options.ManifestPath!
                : Path.Combine(repositoryRoot, options.ManifestPath!));
        string moduleDirectory = Path.GetDirectoryName(manifestPath)
            ?? throw new InvalidOperationException("Module manifest must have a parent directory.");

        return new[]
            {
                Path.Combine(moduleDirectory, PublishedRegistryProvenanceValidator.ModuleProvenanceFileName),
                Path.Combine(moduleDirectory, PowerForgeModuleSourceAttestationWriter.FileName)
            }
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .ToArray();
    }
}
