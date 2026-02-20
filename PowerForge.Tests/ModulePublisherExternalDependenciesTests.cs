using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePublisherExternalDependenciesTests
{
    [Fact]
    public void NormalizeExternalModuleDependenciesForRepositoryPublish_FiltersInboxDependenciesForPsGallery()
    {
        var dependencies = new[]
        {
            "Microsoft.PowerShell.Utility",
            " PSSharedGoods ",
            "Microsoft.PowerShell.Management",
            "PSSharedGoods",
            "Microsoft.PowerShell.Diagnostics"
        };

        var result = ModulePublisher.NormalizeExternalModuleDependenciesForRepositoryPublish(
            PublishTool.PSResourceGet,
            "PSGallery",
            dependencies);

        Assert.Equal(new[] { "PSSharedGoods" }, result.Filtered);
        Assert.Equal(
            new[]
            {
                "Microsoft.PowerShell.Utility",
                "Microsoft.PowerShell.Management",
                "Microsoft.PowerShell.Diagnostics"
            },
            result.Removed);
    }

    [Fact]
    public void NormalizeExternalModuleDependenciesForRepositoryPublish_DoesNotFilterForPowerShellGet()
    {
        var dependencies = new[]
        {
            "Microsoft.PowerShell.Utility",
            "PSSharedGoods"
        };

        var result = ModulePublisher.NormalizeExternalModuleDependenciesForRepositoryPublish(
            PublishTool.PowerShellGet,
            "PSGallery",
            dependencies);

        Assert.Equal(new[] { "Microsoft.PowerShell.Utility", "PSSharedGoods" }, result.Filtered);
        Assert.Empty(result.Removed);
    }

    [Fact]
    public void NormalizeExternalModuleDependenciesForRepositoryPublish_DoesNotFilterForCustomRepository()
    {
        var dependencies = new[]
        {
            "Microsoft.PowerShell.Utility",
            "PSSharedGoods"
        };

        var result = ModulePublisher.NormalizeExternalModuleDependenciesForRepositoryPublish(
            PublishTool.PSResourceGet,
            "InternalRepo",
            dependencies);

        Assert.Equal(new[] { "Microsoft.PowerShell.Utility", "PSSharedGoods" }, result.Filtered);
        Assert.Empty(result.Removed);
    }
}
