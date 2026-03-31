namespace PowerForge.Tests;

public sealed class PowerForgeReleaseRequestMapperTests
{
    [Fact]
    public void Build_AllowsToolsOnlyOverrideToDisableDefaults()
    {
        var defaults = new PowerForgeReleaseRequest
        {
            ToolsOnly = true
        };

        var request = PSPublishModule.PowerForgeReleaseRequestMapper.Build(
            @"C:\repo\.powerforge\project.release.json",
            defaults,
            new PSPublishModule.PowerForgeReleaseInvocationOptions
            {
                ToolsOnly = false
            });

        Assert.False(request.ToolsOnly);
    }

    [Fact]
    public void Build_PreservesToolsOnlyDefaultsWhenNotOverridden()
    {
        var defaults = new PowerForgeReleaseRequest
        {
            ToolsOnly = true
        };

        var request = PSPublishModule.PowerForgeReleaseRequestMapper.Build(
            @"C:\repo\.powerforge\project.release.json",
            defaults,
            new PSPublishModule.PowerForgeReleaseInvocationOptions());

        Assert.True(request.ToolsOnly);
    }
}
