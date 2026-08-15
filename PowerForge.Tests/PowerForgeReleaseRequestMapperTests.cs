namespace PowerForge.Tests;

public sealed class PowerForgeReleaseRequestMapperTests
{
    [Fact]
    public void AppStoreConnectResultLimiterHonorsTheCmdletMaximum()
    {
        var limited = PSPublishModule.AppStoreConnectCommandSupport.LimitResults(
            new[] { "first", "second", "third" },
            limit: 2);

        Assert.Equal(new[] { "first", "second" }, limited);
    }

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
                AppleMarketingVersion = "1.6",
                AppleSourceCommit = "0123456789abcdef0123456789abcdef01234567",
                AppleExpectedPlanSha256 = "abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd",
                AppleShipTestFlightTargets = ["CasaRay iOS", "CasaRay Mac"],
                AppleShipAppStoreTargets = ["CasaRay iOS"],
                AppleShipReuseRemoteScreenshots = false,
                AppleActionConfirmed = true,
                AppleAdoptExistingBuild = true,
                AppleResume = false,
                AppleWaitForProcessing = true,
                AppleProcessingTimeoutSeconds = 900,
                ApplePollIntervalSeconds = 15,
                AppleSummaryOnly = true
            });

        Assert.Equal(PowerForgeAppleReleaseAction.Upload, request.AppleAction);
        Assert.Equal("1.6", request.AppleMarketingVersion);
        Assert.Equal("0123456789abcdef0123456789abcdef01234567", request.AppleSourceCommit);
        Assert.True(request.RequireImmutableAppleSourceSnapshot);
        Assert.Equal("abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd", request.AppleExpectedPlanSha256);
        Assert.Equal(["CasaRay iOS", "CasaRay Mac"], request.AppleShipTestFlightTargets);
        Assert.Equal(["CasaRay iOS"], request.AppleShipAppStoreTargets);
        Assert.False(request.AppleShipReuseRemoteScreenshots);
        Assert.True(request.AppleActionConfirmed);
        Assert.True(request.AppleAdoptExistingBuild);
        Assert.False(request.AppleResume);
        Assert.True(request.AppleWaitForProcessing);
        Assert.Equal(900, request.AppleProcessingTimeoutSeconds);
        Assert.Equal(15, request.ApplePollIntervalSeconds);
        Assert.True(request.AppleSummaryOnly);
    }

    [Fact]
    public void Build_PreservesImmutableAppleSourceSnapshotFromDefaults()
    {
        var request = PSPublishModule.PowerForgeReleaseRequestMapper.Build(
            "/repo/powerforge.release.json",
            new PowerForgeReleaseRequest
            {
                RequireImmutableAppleSourceSnapshot = true
            },
            new PSPublishModule.PowerForgeReleaseInvocationOptions());

        Assert.True(request.RequireImmutableAppleSourceSnapshot);
    }

    [Fact]
    public void Build_MapsModuleRunMode()
    {
        var request = PSPublishModule.PowerForgeReleaseRequestMapper.Build(
            "/repo/powerforge.release.json",
            defaults: null,
            new PSPublishModule.PowerForgeReleaseInvocationOptions
            {
                ModuleFramework = "net10.0",
                ModuleRunMode = ConfigurationGateMode.Publish
            });

        Assert.Equal("net10.0", request.ModuleFramework);
        Assert.Equal(ConfigurationGateMode.Publish, request.ModuleRunMode);
    }

    [Fact]
    public void Build_MapsStandaloneReleaseVersion()
    {
        var request = PSPublishModule.PowerForgeReleaseRequestMapper.Build(
            "/repo/powerforge.release.json",
            defaults: null,
            new PSPublishModule.PowerForgeReleaseInvocationOptions
            {
                ToolsOnly = true,
                ReleaseVersion = "3.0.80"
            });

        Assert.True(request.ToolsOnly);
        Assert.Equal("3.0.80", request.ReleaseVersion);
    }

    [Fact]
    public void Build_MapsModuleReleaseOverrides()
    {
        var request = PSPublishModule.PowerForgeReleaseRequestMapper.Build(
            "/repo/powerforge.release.json",
            defaults: null,
            new PSPublishModule.PowerForgeReleaseInvocationOptions
            {
                ModuleTimeoutSeconds = 10800,
                ModuleCertificateThumbprint = "ABC123",
                ModuleSkipInstall = true,
                ModuleSignIncludeBinaries = true,
                ModuleSignIncludeInternals = false,
                ModuleSignIncludeExe = true,
                ModuleDiagnosticsBaselinePath = ".powerforge/diagnostics.json",
                ModuleGenerateDiagnosticsBaseline = false,
                ModuleUpdateDiagnosticsBaseline = true,
                ModuleFailOnNewDiagnostics = true,
                ModuleFailOnDiagnosticsSeverity = "Error"
            });

        Assert.Equal(10800, request.ModuleTimeoutSeconds);
        Assert.Equal("ABC123", request.ModuleCertificateThumbprint);
        Assert.True(request.ModuleSkipInstall);
        Assert.True(request.ModuleSignIncludeBinaries);
        Assert.False(request.ModuleSignIncludeInternals);
        Assert.True(request.ModuleSignIncludeExe);
        Assert.Equal(".powerforge/diagnostics.json", request.ModuleDiagnosticsBaselinePath);
        Assert.False(request.ModuleGenerateDiagnosticsBaseline);
        Assert.True(request.ModuleUpdateDiagnosticsBaseline);
        Assert.True(request.ModuleFailOnNewDiagnostics);
        Assert.Equal("Error", request.ModuleFailOnDiagnosticsSeverity);
    }
}
