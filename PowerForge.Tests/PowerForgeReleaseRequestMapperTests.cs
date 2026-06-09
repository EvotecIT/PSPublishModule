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

    [Fact]
    public void Build_DoesNotMutateDefaultsInput()
    {
        var defaults = new PowerForgeReleaseRequest
        {
            ToolsOnly = true,
            PublishToolGitHub = true,
            WorkspaceEnableFeatures = new[] { "chat" },
            InstallerMsBuildProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ProductName"] = "Original"
            }
        };

        var request = PSPublishModule.PowerForgeReleaseRequestMapper.Build(
            @"C:\repo\.powerforge\project.release.json",
            defaults,
            new PSPublishModule.PowerForgeReleaseInvocationOptions
            {
                ToolsOnly = false,
                WorkspaceEnableFeatures = new[] { "tools" },
                InstallerMsBuildProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ProductName"] = "Override"
                }
            });

        Assert.False(request.ToolsOnly);
        Assert.True(defaults.ToolsOnly);
        Assert.Equal(new[] { "chat" }, defaults.WorkspaceEnableFeatures);
        Assert.Equal("Original", defaults.InstallerMsBuildProperties["ProductName"]);
    }
}
