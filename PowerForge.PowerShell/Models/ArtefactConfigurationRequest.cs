namespace PowerForge;

/// <summary>
/// Input used by <see cref="ArtefactConfigurationFactory"/> to create an artefact configuration segment.
/// </summary>
public sealed class ArtefactConfigurationRequest
{
    /// <summary>Artefact type to generate.</summary>
    public ArtefactType Type { get; set; }

    /// <summary>Whether Enable was explicitly provided.</summary>
    public bool EnableSpecified { get; set; }
    /// <summary>Enable artefact creation.</summary>
    public bool Enable { get; set; }

    /// <summary>Whether IncludeTagName was explicitly provided.</summary>
    public bool IncludeTagNameSpecified { get; set; }
    /// <summary>Include tag name in output names.</summary>
    public bool IncludeTagName { get; set; }

    /// <summary>Artefact root path.</summary>
    public string? Path { get; set; }
    /// <summary>Path where the main module or required modules are copied.</summary>
    public string? ModulesPath { get; set; }
    /// <summary>Path where required modules are copied.</summary>
    public string? RequiredModulesPath { get; set; }
    /// <summary>Repository used to download required modules.</summary>
    public string? RequiredModulesRepository { get; set; }
    /// <summary>Tool used when downloading required modules.</summary>
    public ModuleSaveTool? RequiredModulesTool { get; set; }
    /// <summary>Source used to resolve required modules.</summary>
    public RequiredModulesSource? RequiredModulesSource { get; set; }
    /// <summary>Whether AddRequiredModules was explicitly provided.</summary>
    public bool AddRequiredModulesSpecified { get; set; }
    /// <summary>Enable required modules copying.</summary>
    public bool AddRequiredModules { get; set; }

    /// <summary>Repository credential username for required modules.</summary>
    public string? RequiredModulesCredentialUserName { get; set; }
    /// <summary>Repository credential secret for required modules.</summary>
    public string? RequiredModulesCredentialSecret { get; set; }
    /// <summary>Path to a file containing the repository credential secret.</summary>
    public string? RequiredModulesCredentialSecretFilePath { get; set; }

    /// <summary>Directory mappings to copy.</summary>
    public ArtefactCopyMapping[]? CopyDirectories { get; set; }
    /// <summary>Whether CopyDirectoriesRelative was explicitly provided.</summary>
    public bool CopyDirectoriesRelativeSpecified { get; set; }
    /// <summary>Whether destination directories are relative.</summary>
    public bool CopyDirectoriesRelative { get; set; }

    /// <summary>File mappings to copy.</summary>
    public ArtefactCopyMapping[]? CopyFiles { get; set; }
    /// <summary>Whether CopyFilesRelative was explicitly provided.</summary>
    public bool CopyFilesRelativeSpecified { get; set; }
    /// <summary>Whether destination files are relative.</summary>
    public bool CopyFilesRelative { get; set; }

    /// <summary>Whether DoNotClear was explicitly provided.</summary>
    public bool DoNotClearSpecified { get; set; }
    /// <summary>Do not clear the output directory before creating the artefact.</summary>
    public bool DoNotClear { get; set; }

    /// <summary>Artefact file name override.</summary>
    public string? ArtefactName { get; set; }
    /// <summary>Script artefact file name override.</summary>
    public string? ScriptName { get; set; }
    /// <summary>Artefact identifier.</summary>
    public string? ID { get; set; }

    /// <summary>Inline pre-merge script text.</summary>
    public string? PreScriptMergeText { get; set; }
    /// <summary>Inline post-merge script text.</summary>
    public string? PostScriptMergeText { get; set; }
    /// <summary>Path to pre-merge script file.</summary>
    public string? PreScriptMergePath { get; set; }
    /// <summary>Path to post-merge script file.</summary>
    public string? PostScriptMergePath { get; set; }
}
