namespace PowerForge;

/// <summary>
/// Provider-support evidence used when deciding whether managed delivery can replace compatibility transport.
/// </summary>
public sealed class ManagedModuleProviderSupport
{
    /// <summary>
    /// Provider or repository source being assessed.
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Managed support level for the provider.
    /// </summary>
    public ManagedModuleProviderSupportLevel Level { get; set; }

    /// <summary>
    /// True when normal managed find, save, install, and update operations are supported for explicit endpoints.
    /// </summary>
    public bool ManagedLifecycleSupported { get; set; }

    /// <summary>
    /// True when compatibility transport should remain the default for this provider until the listed limitations are closed.
    /// </summary>
    public bool CompatibilityFallbackRecommended { get; set; }

    /// <summary>
    /// Provider-specific gaps that explain partial, expected, or unsupported status.
    /// </summary>
    public IReadOnlyList<string> Limitations { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Human-readable summary suitable for logs, verbose output, and benchmark reports.
    /// </summary>
    public string Summary
    {
        get
        {
            var provider = string.IsNullOrWhiteSpace(Provider) ? "Repository" : Provider;
            var summary = $"{provider}: {Level}";
            return Limitations.Count == 0
                ? summary
                : summary + " (" + string.Join("; ", Limitations) + ")";
        }
    }
}
