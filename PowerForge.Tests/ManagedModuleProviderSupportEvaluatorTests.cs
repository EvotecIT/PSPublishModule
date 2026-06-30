namespace PowerForge.Tests;

public sealed class ManagedModuleProviderSupportEvaluatorTests
{
    [Fact]
    public void EvaluateRepository_ClassifiesSupportedSources()
    {
        var local = ManagedModuleProviderSupportEvaluator.Evaluate(new ManagedModuleRepository("Local", @"C:\Modules"));
        var gallery = ManagedModuleProviderSupportEvaluator.Evaluate(new ManagedModuleRepository("PSGallery", "https://www.powershellgallery.com/api/v3/index.json"));
        var generic = ManagedModuleProviderSupportEvaluator.Evaluate(new ManagedModuleRepository("Private", "https://nuget.example.test/v3/index.json"));

        Assert.Equal(ManagedModuleProviderSupportLevel.Supported, local.Level);
        Assert.Equal(ManagedModuleProviderSupportLevel.Supported, gallery.Level);
        Assert.Equal(ManagedModuleProviderSupportLevel.Supported, generic.Level);
        Assert.False(local.CompatibilityFallbackRecommended);
        Assert.True(generic.ManagedLifecycleSupported);
    }

    [Fact]
    public void EvaluateRepository_ClassifiesNuGetV2AsPartialBecauseManagedPublishNeedsV3OrLocal()
    {
        var support = ManagedModuleProviderSupportEvaluator.Evaluate(new ManagedModuleRepository("Legacy", "https://nuget.example.test/api/v2"));

        Assert.Equal(ManagedModuleProviderSupportLevel.Partial, support.Level);
        Assert.True(support.ManagedLifecycleSupported);
        Assert.True(support.CompatibilityFallbackRecommended);
        Assert.Contains(support.Limitations, limitation => limitation.Contains("publishing", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(PrivateGalleryProvider.AzureArtifacts, ManagedModuleProviderSupportLevel.Partial, "credential-provider")]
    [InlineData(PrivateGalleryProvider.JFrog, ManagedModuleProviderSupportLevel.Partial, "OIDC")]
    [InlineData(PrivateGalleryProvider.GitHubPackages, ManagedModuleProviderSupportLevel.Expected, "live authentication")]
    public void EvaluateProvider_RecordsExplicitProviderLimitations(
        PrivateGalleryProvider provider,
        ManagedModuleProviderSupportLevel expectedLevel,
        string expectedLimitation)
    {
        var support = ManagedModuleProviderSupportEvaluator.Evaluate(provider);

        Assert.Equal(expectedLevel, support.Level);
        Assert.True(support.ManagedLifecycleSupported);
        Assert.True(support.CompatibilityFallbackRecommended);
        Assert.Contains(support.Limitations, limitation => limitation.Contains(expectedLimitation, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EvaluateProvider_ClassifiesGenericNuGetAsSupported()
    {
        var support = ManagedModuleProviderSupportEvaluator.Evaluate(PrivateGalleryProvider.NuGet);

        Assert.Equal(ManagedModuleProviderSupportLevel.Supported, support.Level);
        Assert.True(support.ManagedLifecycleSupported);
        Assert.False(support.CompatibilityFallbackRecommended);
        Assert.Empty(support.Limitations);
    }
}
