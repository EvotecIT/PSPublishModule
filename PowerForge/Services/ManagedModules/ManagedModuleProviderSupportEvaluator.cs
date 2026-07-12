namespace PowerForge;

/// <summary>
/// Classifies managed module provider support so compatibility fallback decisions are explicit and testable.
/// </summary>
public static class ManagedModuleProviderSupportEvaluator
{
    /// <summary>
    /// Evaluates support from a managed repository descriptor.
    /// </summary>
    /// <param name="repository">Repository descriptor.</param>
    /// <returns>Provider-support evidence.</returns>
    public static ManagedModuleProviderSupport Evaluate(ManagedModuleRepository repository)
    {
        if (repository is null)
            throw new ArgumentNullException(nameof(repository));

        if (IsPowerShellScriptEndpoint(repository))
        {
            return Unsupported(
                "PowerShellGet script feed",
                "Script-source endpoints are not module publish endpoints; use the compatibility publishing tools or configure the module feed root.");
        }

        if (IsPowerShellGallery(repository))
        {
            return Supported("PowerShell Gallery");
        }

        if (IsAzureArtifacts(repository))
        {
            return Evaluate(PrivateGalleryProvider.AzureArtifacts);
        }

        return repository.Kind switch
        {
            ManagedModuleRepositoryKind.LocalFolder => Supported("Local folder feed"),
            ManagedModuleRepositoryKind.NuGetV3 => Supported("Generic NuGet v3 feed"),
            ManagedModuleRepositoryKind.NuGetV2 => Supported("NuGet v2 feed"),
            _ => Unsupported(
                repository.Kind.ToString(),
                "Repository kind is not supported by the managed module engine.")
        };
    }

    /// <summary>
    /// Evaluates support from a private-gallery provider.
    /// </summary>
    /// <param name="provider">Private-gallery provider.</param>
    /// <returns>Provider-support evidence.</returns>
    public static ManagedModuleProviderSupport Evaluate(PrivateGalleryProvider provider)
        => provider switch
        {
            PrivateGalleryProvider.NuGet => Supported(
                "Generic NuGet private feed"),
            PrivateGalleryProvider.AzureArtifacts => Partial(
                "Azure Artifacts",
                managedLifecycleSupported: true,
                "Explicit NuGet v3 feed URLs and static credentials are supported; credential-provider bootstrapping and repository registration remain compatibility/profile concerns."),
            PrivateGalleryProvider.JFrog => Partial(
                "JFrog/Artifactory",
                managedLifecycleSupported: true,
                "Explicit NuGet v3 endpoints and static credentials are supported; runtime OIDC or CLI credential exchange remains outside the managed path."),
            PrivateGalleryProvider.GitHubPackages => Expected(
                "GitHub Packages",
                managedLifecycleSupported: true,
                "GitHub Packages is treated as a generic NuGet v3 feed until live authentication and publish validation prove provider-specific parity."),
            _ => Unsupported(
                provider.ToString(),
                "Private-gallery provider is not supported by the managed module engine.")
        };

    private static bool IsPowerShellGallery(ManagedModuleRepository repository)
        => string.Equals(repository.Name, "PSGallery", StringComparison.OrdinalIgnoreCase) ||
           repository.Source.IndexOf("powershellgallery.com", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsPowerShellScriptEndpoint(ManagedModuleRepository repository)
    {
        if (!Uri.TryCreate(repository.Source, UriKind.Absolute, out var uri))
            return false;

        return uri.AbsolutePath.TrimEnd('/').EndsWith("/items/psscript", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAzureArtifacts(ManagedModuleRepository repository)
    {
        if (!Uri.TryCreate(repository.Source, UriKind.Absolute, out var uri))
            return false;

        return uri.Host.Equals("pkgs.dev.azure.com", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.EndsWith(".pkgs.visualstudio.com", StringComparison.OrdinalIgnoreCase);
    }

    private static ManagedModuleProviderSupport Supported(string provider)
        => new()
        {
            Provider = provider,
            Level = ManagedModuleProviderSupportLevel.Supported,
            ManagedLifecycleSupported = true,
            CompatibilityFallbackRecommended = false,
            Limitations = Array.Empty<string>()
        };

    private static ManagedModuleProviderSupport Partial(string provider, bool managedLifecycleSupported, params string[] limitations)
        => new()
        {
            Provider = provider,
            Level = ManagedModuleProviderSupportLevel.Partial,
            ManagedLifecycleSupported = managedLifecycleSupported,
            CompatibilityFallbackRecommended = true,
            Limitations = limitations
        };

    private static ManagedModuleProviderSupport Expected(string provider, bool managedLifecycleSupported, params string[] limitations)
        => new()
        {
            Provider = provider,
            Level = ManagedModuleProviderSupportLevel.Expected,
            ManagedLifecycleSupported = managedLifecycleSupported,
            CompatibilityFallbackRecommended = true,
            Limitations = limitations
        };

    private static ManagedModuleProviderSupport Unsupported(string provider, params string[] limitations)
        => new()
        {
            Provider = provider,
            Level = ManagedModuleProviderSupportLevel.Unsupported,
            ManagedLifecycleSupported = false,
            CompatibilityFallbackRecommended = true,
            Limitations = limitations
        };
}
