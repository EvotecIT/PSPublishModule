namespace PowerForge;

/// <summary>
/// Central policy for choosing managed module delivery versus compatibility transport.
/// </summary>
public static class ManagedModuleTransportPolicy
{
    /// <summary>
    /// Resolves the effective transport and explanation for a delivery request.
    /// </summary>
    /// <param name="input">Transport policy input.</param>
    /// <returns>Resolved transport decision.</returns>
    public static ManagedModuleTransportDecision Resolve(ManagedModuleTransportPolicyInput input)
    {
        if (input is null)
            throw new ArgumentNullException(nameof(input));

        if (input.RequestedTransport == ModuleStateDeliveryTransport.ManagedModule)
        {
            var support = input.UsesPrivateGalleryProvider
                ? ManagedModuleProviderSupportEvaluator.Evaluate(input.PrivateGalleryProvider)
                : null;
            return Managed(input.RequestedTransport, CreateExplicitManagedReason(support), support);
        }

        if (input.RequestedTransport != ModuleStateDeliveryTransport.Auto)
        {
            return new ManagedModuleTransportDecision
            {
                RequestedTransport = input.RequestedTransport,
                EffectiveTransport = input.RequestedTransport,
                Reason = "Transport was requested explicitly."
            };
        }

        if (input.UsesPrivateGalleryProvider)
        {
            var support = ManagedModuleProviderSupportEvaluator.Evaluate(input.PrivateGalleryProvider);
            if (ShouldUseManagedPrivateGalleryPath(input, support))
                return Managed(input.RequestedTransport, CreateAutoManagedProviderReason(input, support), support);

            return new ManagedModuleTransportDecision
            {
                RequestedTransport = input.RequestedTransport,
                EffectiveTransport = ModuleStateDeliveryTransport.PrivateModule,
                Reason = "Auto selected compatibility transport because " + FormatProviderFallbackReason(support),
                ProviderSupport = support,
                CompatibilityFallbackIsProviderLimited = true
            };
        }

        if (input.UsesMicrosoftArtifactRegistry)
        {
            return new ManagedModuleTransportDecision
            {
                RequestedTransport = input.RequestedTransport,
                EffectiveTransport = ModuleStateDeliveryTransport.PrivateModule,
                Reason = "Auto selected compatibility transport because Microsoft Artifact Registry registration is currently provided by the existing repository workflow.",
                CompatibilityFallbackIsProviderLimited = true
            };
        }

        if (!input.HasRepositorySource)
        {
            return new ManagedModuleTransportDecision
            {
                RequestedTransport = input.RequestedTransport,
                EffectiveTransport = ModuleStateDeliveryTransport.PrivateModule,
                Reason = "Auto selected compatibility transport because the repository input resolved to a registered repository name rather than a repository source URI or local feed path."
            };
        }

        return Managed(
            input.RequestedTransport,
            "Auto selected managed transport because a repository source URI or local feed path was resolved.",
            providerSupport: null);
    }

    /// <summary>
    /// Returns true when a private-gallery request can skip compatibility registration and use managed delivery directly.
    /// </summary>
    /// <param name="input">Transport policy input.</param>
    /// <returns>True when the managed private-gallery path should be used.</returns>
    public static bool ShouldUseManagedPrivateGalleryPath(ManagedModuleTransportPolicyInput input)
    {
        if (input is null)
            throw new ArgumentNullException(nameof(input));

        var support = input.UsesPrivateGalleryProvider
            ? ManagedModuleProviderSupportEvaluator.Evaluate(input.PrivateGalleryProvider)
            : null;
        return ShouldUseManagedPrivateGalleryPath(input, support);
    }

    private static bool ShouldUseManagedPrivateGalleryPath(
        ManagedModuleTransportPolicyInput input,
        ManagedModuleProviderSupport? support)
    {
        if (input.RequestedTransport == ModuleStateDeliveryTransport.ManagedModule)
            return true;
        if (input.RequestedTransport != ModuleStateDeliveryTransport.Auto)
            return false;
        if (!input.UsesPrivateGalleryProvider)
            return false;

        return support?.Level == ManagedModuleProviderSupportLevel.Supported ||
               input.HasManagedOnlyOptions ||
               input.HasRepositorySource;
    }

    private static ManagedModuleTransportDecision Managed(
        ModuleStateDeliveryTransport requestedTransport,
        string reason,
        ManagedModuleProviderSupport? providerSupport)
        => new()
        {
            RequestedTransport = requestedTransport,
            EffectiveTransport = ModuleStateDeliveryTransport.ManagedModule,
            Reason = reason,
            ProviderSupport = providerSupport
        };

    private static string CreateExplicitManagedReason(ManagedModuleProviderSupport? providerSupport)
        => providerSupport is null
            ? "Transport was requested explicitly."
            : providerSupport.CompatibilityFallbackRecommended
                ? "Managed transport selected explicitly for " + providerSupport.Summary + "."
                : "Managed transport selected because " + providerSupport.Provider + " is supported by the managed engine.";

    private static string CreateAutoManagedProviderReason(
        ManagedModuleTransportPolicyInput input,
        ManagedModuleProviderSupport providerSupport)
    {
        if (providerSupport.Level == ManagedModuleProviderSupportLevel.Supported)
            return "Auto selected managed transport because " + providerSupport.Provider + " is supported by the managed engine.";

        if (input.HasManagedOnlyOptions)
            return "Auto selected managed transport because managed-only options were requested for " + providerSupport.Summary + ".";

        return "Auto selected managed transport because a repository source URI or local feed path was resolved for " + providerSupport.Summary + ".";
    }

    private static string FormatProviderFallbackReason(ManagedModuleProviderSupport providerSupport)
    {
        if (providerSupport.Level == ManagedModuleProviderSupportLevel.Supported)
            return providerSupport.Provider + " is supported, but repository registration and access probing are running through the existing provider workflow.";

        var limitations = providerSupport.Limitations.Count == 0
            ? "provider support is incomplete"
            : string.Join("; ", providerSupport.Limitations);
        return providerSupport.Provider + " managed support is " + providerSupport.Level + ": " + limitations;
    }
}
