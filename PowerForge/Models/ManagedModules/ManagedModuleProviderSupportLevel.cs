namespace PowerForge;

/// <summary>
/// Describes how completely the managed module engine supports a repository provider.
/// </summary>
public enum ManagedModuleProviderSupportLevel
{
    /// <summary>
    /// Managed lifecycle support is implemented and covered by current contracts.
    /// </summary>
    Supported,

    /// <summary>
    /// Managed lifecycle support works for explicit repository endpoints and static credentials, but some provider-specific bootstrap or auth behavior still needs compatibility tooling.
    /// </summary>
    Partial,

    /// <summary>
    /// The provider should work as a generic NuGet endpoint, but still needs live provider-specific validation before compatibility fallback can be retired.
    /// </summary>
    Expected,

    /// <summary>
    /// The provider is not supported by the managed engine.
    /// </summary>
    Unsupported
}
