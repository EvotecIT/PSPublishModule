namespace PowerForge;

/// <summary>
/// Configures opt-in release source and provenance protections for a module pipeline.
/// </summary>
public sealed class ConfigurationReleaseProtectionSegment : IConfigurationSegment
{
    /// <inheritdoc />
    public string Type => "ReleaseProtection";

    /// <summary>Release protection settings. Every protection is disabled by default.</summary>
    public ReleaseProtectionConfiguration Configuration { get; set; } = new();
}

/// <summary>
/// Selects release protections that should be enforced by a module pipeline.
/// </summary>
public sealed class ReleaseProtectionConfiguration
{
    /// <summary>
    /// Requires release inputs to come from a clean Git source snapshot when the pipeline is planned.
    /// </summary>
    public bool RequireCleanSource { get; set; }

    /// <summary>
    /// Requires release inputs to remain unchanged after planning. This also requires a clean source snapshot.
    /// </summary>
    public bool RequireSourceUnchanged { get; set; }

    /// <summary>
    /// Embeds signed source provenance in eligible signed GitHub module artefacts. This also requires a clean,
    /// unchanged source snapshot.
    /// </summary>
    public bool GenerateProvenance { get; set; }
}
