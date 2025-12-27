namespace PowerForge;

/// <summary>
/// Configuration segment that describes documentation paths (Docs folder and README path).
/// </summary>
public sealed class ConfigurationDocumentationSegment : IConfigurationSegment
{
    /// <inheritdoc />
    public string Type => "Documentation";

    /// <summary>Documentation configuration payload.</summary>
    public DocumentationConfiguration Configuration { get; set; } = new();
}

/// <summary>
/// Documentation configuration payload for <see cref="ConfigurationDocumentationSegment"/>.
/// </summary>
public sealed class DocumentationConfiguration
{
    /// <summary>Path to the folder where documentation will be created.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Path to the readme file that will be used for the documentation.</summary>
    public string PathReadme { get; set; } = string.Empty;
}

/// <summary>
/// Configuration segment that describes documentation generation behavior.
/// </summary>
public sealed class ConfigurationBuildDocumentationSegment : IConfigurationSegment
{
    /// <inheritdoc />
    public string Type => "BuildDocumentation";

    /// <summary>Build documentation configuration payload.</summary>
    public BuildDocumentationConfiguration Configuration { get; set; } = new();
}

/// <summary>
/// Build documentation configuration payload for <see cref="ConfigurationBuildDocumentationSegment"/>.
/// </summary>
public sealed class BuildDocumentationConfiguration
{
    /// <summary>Enable documentation generation.</summary>
    public bool Enable { get; set; }

    /// <summary>Remove files from docs folder before generating new docs.</summary>
    public bool StartClean { get; set; }

    /// <summary>Run a post-update step after generating new docs.</summary>
    public bool UpdateWhenNew { get; set; }

    /// <summary>Documentation tool selection.</summary>
    public DocumentationTool Tool { get; set; } = DocumentationTool.PowerForge;

    /// <summary>
    /// When enabled, generates external help in MAML XML format (for Get-Help) under a culture folder (default: <c>en-US</c>).
    /// </summary>
    public bool GenerateExternalHelp { get; set; } = true;

    /// <summary>
    /// Culture folder name used for external help output (default: <c>en-US</c>).
    /// </summary>
    public string ExternalHelpCulture { get; set; } = "en-US";

    /// <summary>
    /// Optional external help file name override. When empty, defaults to <c>&lt;ModuleName&gt;-help.xml</c>.
    /// </summary>
    public string ExternalHelpFileName { get; set; } = string.Empty;
}
