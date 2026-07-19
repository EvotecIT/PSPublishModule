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

    [Fact]
    public void Build_MapsCompactAppleActionOverrides()
    {
        var request = PSPublishModule.PowerForgeReleaseRequestMapper.Build(
            "/repo/powerforge.release.json",
            defaults: null,
            new PSPublishModule.PowerForgeReleaseInvocationOptions
            {
                AppleAction = PowerForgeAppleReleaseAction.Upload,
                AppleActionConfirmed = true,
                AppleResume = false,
                AppleWaitForProcessing = true,
                AppleProcessingTimeoutSeconds = 900,
                ApplePollIntervalSeconds = 15,
                AppleSummaryOnly = true
            });

        Assert.Equal(PowerForgeAppleReleaseAction.Upload, request.AppleAction);
        Assert.True(request.AppleActionConfirmed);
        Assert.False(request.AppleResume);
        Assert.True(request.AppleWaitForProcessing);
        Assert.Equal(900, request.AppleProcessingTimeoutSeconds);
        Assert.Equal(15, request.ApplePollIntervalSeconds);
        Assert.True(request.AppleSummaryOnly);
    }
}
