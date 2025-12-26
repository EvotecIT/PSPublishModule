namespace PowerForge;

/// <summary>
/// Result of creating a configured artefact output (packed/unpacked).
/// </summary>
public sealed class ArtefactBuildResult
{
    /// <summary>Artefact type that was created.</summary>
    public ArtefactType Type { get; }

    /// <summary>Optional artefact ID used for publish selection.</summary>
    public string? Id { get; }

    /// <summary>Full path to the artefact output (directory for Unpacked/Script, file for Packed/ScriptPacked).</summary>
    public string OutputPath { get; }

    /// <summary>Modules included in the artefact (main module + optional required modules).</summary>
    public ArtefactModuleEntry[] Modules { get; }

    /// <summary>Extra files/directories copied into the artefact output.</summary>
    public ArtefactCopyEntry[] CopiedItems { get; }

    /// <summary>
    /// Creates a new result instance.
    /// </summary>
    public ArtefactBuildResult(
        ArtefactType type,
        string? id,
        string outputPath,
        ArtefactModuleEntry[] modules,
        ArtefactCopyEntry[] copiedItems)
    {
        Type = type;
        Id = id;
        OutputPath = outputPath;
        Modules = modules ?? Array.Empty<ArtefactModuleEntry>();
        CopiedItems = copiedItems ?? Array.Empty<ArtefactCopyEntry>();
    }
}

/// <summary>
/// Represents a module folder that was included in an artefact.
/// </summary>
public sealed class ArtefactModuleEntry
{
    /// <summary>Name of the module.</summary>
    public string Name { get; }

    /// <summary>When true, this entry represents the main module being built.</summary>
    public bool IsMainModule { get; }

    /// <summary>Resolved module version when known (typically for required modules saved via PSResourceGet).</summary>
    public string? Version { get; }

    /// <summary>Destination path of the module folder in the artefact layout.</summary>
    public string Path { get; }

    /// <summary>
    /// Creates a new entry.
    /// </summary>
    public ArtefactModuleEntry(string name, bool isMainModule, string? version, string path)
    {
        Name = name;
        IsMainModule = isMainModule;
        Version = version;
        Path = path;
    }
}

/// <summary>
/// Represents a copied file or directory entry inside the artefact.
/// </summary>
public sealed class ArtefactCopyEntry
{
    /// <summary>Source path.</summary>
    public string Source { get; }

    /// <summary>Destination path.</summary>
    public string Destination { get; }

    /// <summary>Whether the copy was a directory copy.</summary>
    public bool IsDirectory { get; }

    /// <summary>
    /// Creates a new entry.
    /// </summary>
    public ArtefactCopyEntry(string source, string destination, bool isDirectory)
    {
        Source = source;
        Destination = destination;
        IsDirectory = isDirectory;
    }
}

