using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePublisherRequiredModulesTests
{
    [Fact]
    public void DoesVersionMatchRequiredModule_ExactRequiredVersion()
    {
        var required = new ManifestEditor.RequiredModule(
            moduleName: "PSSharedGoods",
            requiredVersion: "0.0.312");

        Assert.True(ModulePublisher.DoesVersionMatchRequiredModule(required, "0.0.312"));
        Assert.False(ModulePublisher.DoesVersionMatchRequiredModule(required, "0.0.313"));
    }

    [Fact]
    public void DoesVersionMatchRequiredModule_MinimumVersion()
    {
        var required = new ManifestEditor.RequiredModule(
            moduleName: "PSSharedGoods",
            moduleVersion: "0.0.312");

        Assert.True(ModulePublisher.DoesVersionMatchRequiredModule(required, "0.0.312"));
        Assert.True(ModulePublisher.DoesVersionMatchRequiredModule(required, "0.0.400"));
        Assert.False(ModulePublisher.DoesVersionMatchRequiredModule(required, "0.0.311"));
    }

    [Fact]
    public void DoesVersionMatchRequiredModule_RangeVersion()
    {
        var required = new ManifestEditor.RequiredModule(
            moduleName: "PSSharedGoods",
            moduleVersion: "0.0.300",
            maximumVersion: "0.0.350");

        Assert.True(ModulePublisher.DoesVersionMatchRequiredModule(required, "0.0.300"));
        Assert.True(ModulePublisher.DoesVersionMatchRequiredModule(required, "0.0.350"));
        Assert.False(ModulePublisher.DoesVersionMatchRequiredModule(required, "0.0.299"));
        Assert.False(ModulePublisher.DoesVersionMatchRequiredModule(required, "0.0.351"));
    }

    [Fact]
    public void HasMatchingRequiredModuleVersion_ReturnsTrueWhenAnyVersionMatches()
    {
        var required = new ManifestEditor.RequiredModule(
            moduleName: "PSSharedGoods",
            moduleVersion: "0.0.312");

        var result = ModulePublisher.HasMatchingRequiredModuleVersion(
            required,
            new[] { "0.0.200", "0.0.312", "0.0.313" });

        Assert.True(result);
    }
}
