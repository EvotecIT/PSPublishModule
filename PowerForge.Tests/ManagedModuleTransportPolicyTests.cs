using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleTransportPolicyTests
{
    [Fact]
    public void Resolve_AutoWithRepositorySource_UsesManagedTransport()
    {
        var decision = ManagedModuleTransportPolicy.Resolve(new ManagedModuleTransportPolicyInput
        {
            RequestedTransport = ModuleStateDeliveryTransport.Auto,
            HasRepositorySource = true
        });

        Assert.Equal(ModuleStateDeliveryTransport.Auto, decision.RequestedTransport);
        Assert.Equal(ModuleStateDeliveryTransport.ManagedModule, decision.EffectiveTransport);
        Assert.Contains("repository source", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(decision.CompatibilityFallbackIsProviderLimited);
    }

    [Fact]
    public void Resolve_AutoWithRegisteredRepositoryName_UsesCompatibilityTransport()
    {
        var decision = ManagedModuleTransportPolicy.Resolve(new ManagedModuleTransportPolicyInput
        {
            RequestedTransport = ModuleStateDeliveryTransport.Auto
        });

        Assert.Equal(ModuleStateDeliveryTransport.PrivateModule, decision.EffectiveTransport);
        Assert.Contains("registered repository name", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(decision.CompatibilityFallbackIsProviderLimited);
        Assert.True(decision.CompatibilityFallbackRequiresRepositorySource);
    }

    [Fact]
    public void Resolve_AutoWithSupportedPrivateProvider_UsesManagedTransport()
    {
        var decision = ManagedModuleTransportPolicy.Resolve(new ManagedModuleTransportPolicyInput
        {
            RequestedTransport = ModuleStateDeliveryTransport.Auto,
            UsesPrivateGalleryProvider = true,
            PrivateGalleryProvider = PrivateGalleryProvider.NuGet
        });

        Assert.Equal(ModuleStateDeliveryTransport.ManagedModule, decision.EffectiveTransport);
        Assert.Equal(ManagedModuleProviderSupportLevel.Supported, decision.ProviderSupport?.Level);
        Assert.Contains("Generic NuGet private feed", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_AutoWithPartialPrivateProviderWithoutManagedEvidence_UsesProviderLimitedCompatibilityTransport()
    {
        var decision = ManagedModuleTransportPolicy.Resolve(new ManagedModuleTransportPolicyInput
        {
            RequestedTransport = ModuleStateDeliveryTransport.Auto,
            UsesPrivateGalleryProvider = true,
            PrivateGalleryProvider = PrivateGalleryProvider.JFrog
        });

        Assert.Equal(ModuleStateDeliveryTransport.PrivateModule, decision.EffectiveTransport);
        Assert.Equal(ManagedModuleProviderSupportLevel.Partial, decision.ProviderSupport?.Level);
        Assert.True(decision.CompatibilityFallbackIsProviderLimited);
        Assert.False(decision.CompatibilityFallbackRequiresRepositorySource);
        Assert.Contains("OIDC", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_AutoWithPartialPrivateProviderAndRepositorySource_UsesManagedTransportWithLimitations()
    {
        var decision = ManagedModuleTransportPolicy.Resolve(new ManagedModuleTransportPolicyInput
        {
            RequestedTransport = ModuleStateDeliveryTransport.Auto,
            UsesPrivateGalleryProvider = true,
            PrivateGalleryProvider = PrivateGalleryProvider.JFrog,
            HasRepositorySource = true
        });

        Assert.Equal(ModuleStateDeliveryTransport.ManagedModule, decision.EffectiveTransport);
        Assert.Equal(ManagedModuleProviderSupportLevel.Partial, decision.ProviderSupport?.Level);
        Assert.False(decision.CompatibilityFallbackIsProviderLimited);
        Assert.Contains("JFrog", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldUseManagedPrivateGalleryPath_FollowsSameProviderEvidence()
    {
        Assert.True(ManagedModuleTransportPolicy.ShouldUseManagedPrivateGalleryPath(new ManagedModuleTransportPolicyInput
        {
            RequestedTransport = ModuleStateDeliveryTransport.Auto,
            UsesPrivateGalleryProvider = true,
            PrivateGalleryProvider = PrivateGalleryProvider.NuGet
        }));
        Assert.False(ManagedModuleTransportPolicy.ShouldUseManagedPrivateGalleryPath(new ManagedModuleTransportPolicyInput
        {
            RequestedTransport = ModuleStateDeliveryTransport.Auto,
            UsesPrivateGalleryProvider = true,
            PrivateGalleryProvider = PrivateGalleryProvider.JFrog
        }));
        Assert.True(ManagedModuleTransportPolicy.ShouldUseManagedPrivateGalleryPath(new ManagedModuleTransportPolicyInput
        {
            RequestedTransport = ModuleStateDeliveryTransport.Auto,
            UsesPrivateGalleryProvider = true,
            PrivateGalleryProvider = PrivateGalleryProvider.JFrog,
            HasRepositorySource = true
        }));
    }
}
