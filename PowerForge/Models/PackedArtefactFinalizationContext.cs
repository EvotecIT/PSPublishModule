namespace PowerForge;

/// <summary>
/// Describes the complete on-disk layout immediately before a packed artefact is archived.
/// </summary>
public sealed class PackedArtefactFinalizationContext
{
    /// <summary>Root directory whose contents become the packed archive.</summary>
    public string RootPath { get; }

    /// <summary>Full path to the primary module directory within <see cref="RootPath"/>.</summary>
    public string MainModulePath { get; }

    /// <summary>Full path to the primary module manifest.</summary>
    public string ManifestPath { get; }

    /// <summary>Final archive output path.</summary>
    public string OutputPath { get; }

    /// <summary>Primary module name.</summary>
    public string ModuleName { get; }

    /// <summary>Resolved module version, including the prerelease label when configured.</summary>
    public string Version { get; }

    /// <summary>Creates a packed artefact finalization context.</summary>
    public PackedArtefactFinalizationContext(
        string rootPath,
        string mainModulePath,
        string manifestPath,
        string outputPath,
        string moduleName,
        string version)
    {
        RootPath = rootPath;
        MainModulePath = mainModulePath;
        ManifestPath = manifestPath;
        OutputPath = outputPath;
        ModuleName = moduleName;
        Version = version;
    }
}
