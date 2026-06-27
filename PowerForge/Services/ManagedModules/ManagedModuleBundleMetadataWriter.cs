using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Writes reusable offline bundle metadata for managed save operations.
/// </summary>
public sealed class ManagedModuleBundleMetadataWriter
{
    /// <summary>
    /// Creates bundle metadata from install results.
    /// </summary>
    /// <param name="results">Save/install results to describe.</param>
    /// <returns>Bundle metadata.</returns>
    public ManagedModuleBundleMetadata Create(IEnumerable<ManagedModuleInstallResult> results)
    {
        if (results is null)
            throw new ArgumentNullException(nameof(results));

        var entries = results
            .Where(static result => result is not null)
            .SelectMany(result => Expand(result, dependencyOf: null))
            .GroupBy(static entry => entry.ModulePath, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.Version, ManagedModuleVersionComparer.Instance)
            .ToArray();

        return new ManagedModuleBundleMetadata
        {
            CreatedUtc = DateTimeOffset.UtcNow,
            ModuleRoot = entries.Select(static entry => Path.GetDirectoryName(Path.GetDirectoryName(entry.ModulePath) ?? string.Empty))
                .FirstOrDefault(static root => !string.IsNullOrWhiteSpace(root)) ?? string.Empty,
            Modules = entries
        };
    }

    /// <summary>
    /// Writes bundle metadata as JSON.
    /// </summary>
    /// <param name="path">Metadata file path.</param>
    /// <param name="results">Save/install results to describe.</param>
    /// <returns>Metadata that was written.</returns>
    public ManagedModuleBundleMetadata Write(string path, IEnumerable<ManagedModuleInstallResult> results)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Metadata path is required.", nameof(path));

        var metadata = Create(results);
        var fullPath = Path.GetFullPath(path.Trim().Trim('"'));
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory!);

        File.WriteAllText(fullPath, JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
        return metadata;
    }

    private static IEnumerable<ManagedModuleBundleEntry> Expand(ManagedModuleInstallResult result, string? dependencyOf)
    {
        yield return new ManagedModuleBundleEntry
        {
            Name = result.Name,
            Version = result.Version,
            Status = result.Status,
            RepositoryName = result.RepositoryName,
            RepositorySource = result.RepositorySource,
            ModulePath = result.ModulePath,
            PackagePath = result.Download?.PackagePath,
            PackageSha256 = result.Download?.PackageSha256,
            DependencyOf = dependencyOf
        };

        foreach (var dependency in result.DependencyResults)
        {
            foreach (var entry in Expand(dependency, result.Name))
                yield return entry;
        }
    }
}
