namespace PowerForge;

/// <summary>
/// Configuration segment that describes an artefact output.
/// </summary>
public sealed class ConfigurationArtefactSegment : IConfigurationSegment
{
    /// <summary>Artefact kind.</summary>
    public ArtefactType ArtefactType { get; set; }

    /// <inheritdoc />
    public string Type => ArtefactType.ToString();

    /// <summary>Artefact configuration payload.</summary>
    public ArtefactConfiguration Configuration { get; set; } = new();
}

/// <summary>
/// Artefact configuration payload for <see cref="ConfigurationArtefactSegment"/>.
/// </summary>
public sealed class ArtefactConfiguration
{
    /// <summary>Enable artefact creation.</summary>
    public bool? Enabled { get; set; }

    /// <summary>Include tag name in artefact name.</summary>
    public bool? IncludeTagName { get; set; }

    /// <summary>Path where artefact will be created.</summary>
    public string? Path { get; set; }

    /// <summary>Required modules configuration (copied into artefact).</summary>
    public ArtefactRequiredModulesConfiguration RequiredModules { get; set; } = new();

    /// <summary>Directories to copy to artefact (Source/Destination).</summary>
    public ArtefactCopyMapping[]? DirectoryOutput { get; set; }

    /// <summary>Whether destination directories should be relative to artefact root.</summary>
    public bool? DestinationDirectoriesRelative { get; set; }

    /// <summary>Files to copy to artefact (Source/Destination).</summary>
    public ArtefactCopyMapping[]? FilesOutput { get; set; }

    /// <summary>Whether destination files should be relative to artefact root.</summary>
    public bool? DestinationFilesRelative { get; set; }

    /// <summary>Do not clear artefact output directory before creating artefact.</summary>
    public bool? DoNotClear { get; set; }

    /// <summary>Artefact file name override.</summary>
    public string? ArtefactName { get; set; }

    /// <summary>Script name used by script artefacts.</summary>
    public string? ScriptName { get; set; }

    /// <summary>Optional ID of the artefact (used by publish configuration).</summary>
    public string? ID { get; set; }

    /// <summary>Script text added at the beginning of the script (Script/ScriptPacked).</summary>
    public string? PreScriptMerge { get; set; }

    /// <summary>Script text added at the end of the script (Script/ScriptPacked).</summary>
    public string? PostScriptMerge { get; set; }
}

/// <summary>
/// Required modules configuration payload inside <see cref="ArtefactConfiguration"/>.
/// </summary>
public sealed class ArtefactRequiredModulesConfiguration
{
    /// <summary>Enable copying required modules.</summary>
    public bool? Enabled { get; set; }

    /// <summary>Path where required modules will be copied to.</summary>
    public string? Path { get; set; }

    /// <summary>Path where main module (or required module) will be copied to.</summary>
    public string? ModulesPath { get; set; }

    /// <summary>
    /// Tool used when downloading required modules (Save-PSResource/Save-Module).
    /// When empty, PowerForge chooses the best available tool at runtime (prefer PSResourceGet, fall back to PowerShellGet).
    /// </summary>
    public ModuleSaveTool? Tool { get; set; }

    /// <summary>
    /// Source used to resolve required modules (local copy vs download).
    /// When empty, PowerForge uses locally available modules only.
    /// </summary>
    public RequiredModulesSource? Source { get; set; }

    /// <summary>
    /// Repository name used when downloading required modules (Save-PSResource / Save-Module).
    /// When empty, the default PSResourceGet behavior is used (PowerForge defaults to PSGallery).
    /// </summary>
    public string? Repository { get; set; }

    /// <summary>Optional credential used for repository access when downloading required modules.</summary>
    public RepositoryCredential? Credential { get; set; }
}

/// <summary>
/// Represents a copy mapping entry for artefact output.
/// </summary>
public sealed class ArtefactCopyMapping
{
    /// <summary>Source path.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Destination path.</summary>
    public string Destination { get; set; } = string.Empty;
}
