namespace PowerForge;

/// <summary>
/// Configuration segment that sets the high-level run mode for a module pipeline.
/// </summary>
public sealed class ConfigurationGateSegment : IConfigurationSegment
{
    /// <inheritdoc />
    public string Type => "Gate";

    /// <summary>Gate configuration payload.</summary>
    public GateConfiguration Configuration { get; set; } = new();
}

/// <summary>
/// Run-mode configuration for module build orchestration.
/// </summary>
public sealed class GateConfiguration
{
    /// <summary>
    /// High-level mode that constrains which configured phases may execute.
    /// </summary>
    public ConfigurationGateMode Mode { get; set; }
}

/// <summary>
/// High-level module pipeline run modes.
/// </summary>
public enum ConfigurationGateMode
{
    /// <summary>Only refresh manifest metadata and skip build, package, signing, artefact, publish, and install phases.</summary>
    Manifest,

    /// <summary>Generate command Markdown and external help without validation, tests, signing, artefacts, publishing, or install phases.</summary>
    Documentation,

    /// <summary>Build module and package lanes locally, but suppress publishing for this run.</summary>
    Build,

    /// <summary>Allow configured build and publish phases to execute.</summary>
    Publish
}
