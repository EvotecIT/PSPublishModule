namespace PowerForge;

/// <summary>
/// Describes a complete on-disk artefact layout immediately before final delivery.
/// </summary>
public sealed class PackedArtefactFinalizationContext
{
    /// <summary>Kind of artefact whose completed layout is being finalized.</summary>
    public ArtefactType ArtefactType { get; }

    /// <summary>Root directory containing the layout to finalize.</summary>
    public string RootPath { get; }

    /// <summary>Full path to the primary module directory within <see cref="RootPath"/>.</summary>
    public string MainModulePath { get; }

    /// <summary>Full path to the primary module manifest, or an empty value for script artefacts.</summary>
    public string ManifestPath { get; }

    /// <summary>Primary executable module or script file within <see cref="MainModulePath"/>.</summary>
    public string EntryPointPath { get; }

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
        : this(
            ArtefactType.Packed,
            rootPath,
            mainModulePath,
            manifestPath,
            manifestPath,
            outputPath,
            moduleName,
            version)
    {
    }

    /// <summary>Creates an artefact finalization context for a completed module or script layout.</summary>
    public PackedArtefactFinalizationContext(
        ArtefactType artefactType,
        string rootPath,
        string mainModulePath,
        string manifestPath,
        string entryPointPath,
        string outputPath,
        string moduleName,
        string version)
    {
        ArtefactType = artefactType;
        RootPath = rootPath;
        MainModulePath = mainModulePath;
        ManifestPath = manifestPath;
        EntryPointPath = entryPointPath;
        OutputPath = outputPath;
        ModuleName = moduleName;
        Version = version;
    }
}
