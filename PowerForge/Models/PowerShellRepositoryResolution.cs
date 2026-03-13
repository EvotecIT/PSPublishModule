namespace PowerForge;

/// <summary>
/// Resolved PowerShell repository metadata used for verification and publish probes.
/// </summary>
public sealed class PowerShellRepositoryResolution
{
    /// <summary>
    /// Repository name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Repository source URI when available.
    /// </summary>
    public string? SourceUri { get; set; }

    /// <summary>
    /// Repository publish URI when available.
    /// </summary>
    public string? PublishUri { get; set; }

    /// <summary>
    /// Preferred display/source URI for diagnostics.
    /// </summary>
    public string DisplaySource => SourceUri ?? PublishUri ?? Name;
}
