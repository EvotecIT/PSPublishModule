namespace PowerForge;

/// <summary>
/// Policy used to decide whether an installed module version came from an acceptable managed source.
/// </summary>
public sealed class ManagedModuleSourcePolicy
{
    /// <summary>
    /// Require a managed module receipt to exist for the installed version.
    /// </summary>
    public bool RequireManagedReceipt { get; set; } = true;

    /// <summary>
    /// Require the receipt repository name to match the requested repository name.
    /// </summary>
    public bool RequireRepositoryNameMatch { get; set; } = true;

    /// <summary>
    /// Require the receipt repository source to match the requested repository source.
    /// </summary>
    public bool RequireRepositorySourceMatch { get; set; } = true;
}
