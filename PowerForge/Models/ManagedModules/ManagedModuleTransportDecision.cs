namespace PowerForge;

/// <summary>
/// Resolved module delivery transport with the evidence used to explain the choice.
/// </summary>
public sealed class ManagedModuleTransportDecision
{
    /// <summary>
    /// Requested delivery transport.
    /// </summary>
    public ModuleStateDeliveryTransport RequestedTransport { get; set; }

    /// <summary>
    /// Effective delivery transport after Auto policy is applied.
    /// </summary>
    public ModuleStateDeliveryTransport EffectiveTransport { get; set; }

    /// <summary>
    /// Human-readable reason suitable for verbose output and result objects.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Provider support evidence used by the decision, when provider-specific.
    /// </summary>
    public ManagedModuleProviderSupport? ProviderSupport { get; set; }

    /// <summary>
    /// True when compatibility remains selected because provider support or provider workflow parity is incomplete.
    /// </summary>
    public bool CompatibilityFallbackIsProviderLimited { get; set; }
}
