namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void Execute_BuildModeWithVirusTotalEnabled_DoesNotResolveSecretOrUpload()
    {
        var root = CreateSandbox();
        var environmentName = $"POWERFORGE_TEST_VT_{Guid.NewGuid():N}";
        try
        {
            Environment.SetEnvironmentVariable(environmentName, null);
            var scriptPath = Path.Combine(root, "Build-Module.ps1");
            var releasePath = Path.Combine(root, "release.json");
            File.WriteAllText(scriptPath, "# module build");
            File.WriteAllText(releasePath, "{}");
            var moduleCalls = new List<ModuleExecutionSnapshot>();
            var uploadCalls = 0;
            var service = CreateReleaseService(
                root,
                moduleCalls,
                new PowerForgeToolReleaseResult { Success = true },
                publishVirusTotalMonitor: (_, _) =>
                {
                    uploadCalls++;
                    return new VirusTotalMonitorPublishResult { Success = true };
                });
            var spec = new PowerForgeReleaseSpec
            {
                Module = new PowerForgeModuleReleaseOptions
                {
                    RepositoryRoot = root,
                    ScriptPath = scriptPath
                },
                VirusTotal = new PowerForgeVirusTotalOptions
                {
                    Enabled = true,
                    ProjectName = "Example",
                    ApiKeyEnvName = environmentName,
                    ArtifactKinds = [VirusTotalArtifactKind.PowerShellModule]
                }
            };

            var result = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ModuleRunMode = ConfigurationGateMode.Build
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Single(moduleCalls);
            Assert.Equal(0, uploadCalls);
            Assert.Null(result.VirusTotalMonitor);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentName, null);
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_OrdinaryBuildWithVirusTotal_DoesNotHashExistingModuleArtifacts()
    {
        var root = CreateSandbox();
        try
        {
            var scriptPath = Path.Combine(root, "Build-Module.ps1");
            var releasePath = Path.Combine(root, "release.json");
            var artifactPath = Path.Combine(root, "Existing.zip");
            File.WriteAllText(scriptPath, "# module build");
            File.WriteAllText(releasePath, "{}");
            File.WriteAllText(artifactPath, "existing artifact");
            var moduleCalls = new List<ModuleExecutionSnapshot>();
            var service = CreateReleaseService(
                root,
                moduleCalls,
                new PowerForgeToolReleaseResult { Success = true });
            var spec = new PowerForgeReleaseSpec
            {
                Module = new PowerForgeModuleReleaseOptions
                {
                    RepositoryRoot = root,
                    ScriptPath = scriptPath,
                    ArtifactPaths = [artifactPath]
                },
                VirusTotal = new PowerForgeVirusTotalOptions
                {
                    Enabled = true,
                    ApiKeyEnvName = "POWERFORGE_TEST_UNUSED_" + Guid.NewGuid().ToString("N"),
                    ArtifactKinds = [VirusTotalArtifactKind.PowerShellModule]
                }
            };

            using var lockedArtifact = new FileStream(
                artifactPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            var result = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ModuleRunMode = ConfigurationGateMode.Build
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Single(moduleCalls);
            Assert.Empty(result.ModuleProducedAssets);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ShouldPublishVirusTotalMonitor_OnlyAllowsPublicationExecution()
    {
        var spec = new PowerForgeReleaseSpec
        {
            Module = new PowerForgeModuleReleaseOptions(),
            VirusTotal = new PowerForgeVirusTotalOptions
            {
                Enabled = true,
                ArtifactKinds = [VirusTotalArtifactKind.PowerShellModule]
            }
        };

        Assert.False(PowerForgeReleaseService.ShouldPublishVirusTotalMonitor(
            spec,
            new PowerForgeReleaseRequest { ModuleRunMode = ConfigurationGateMode.Build },
            explicitAppleAction: false,
            runModule: true,
            runPackages: false,
            runTools: false,
            publishUnifiedGitHub: false));
        Assert.False(PowerForgeReleaseService.ShouldPublishVirusTotalMonitor(
            spec,
            new PowerForgeReleaseRequest { ModuleRunMode = ConfigurationGateMode.Publish },
            explicitAppleAction: true,
            runModule: true,
            runPackages: false,
            runTools: false,
            publishUnifiedGitHub: false));
        Assert.True(PowerForgeReleaseService.ShouldPublishVirusTotalMonitor(
            spec,
            new PowerForgeReleaseRequest { ModuleRunMode = ConfigurationGateMode.Publish },
            explicitAppleAction: false,
            runModule: true,
            runPackages: false,
            runTools: false,
            publishUnifiedGitHub: false));

        var wingetSpec = new PowerForgeReleaseSpec
        {
            Winget = new PowerForgeReleaseWingetOptions { Enabled = true, Submit = true },
            VirusTotal = new PowerForgeVirusTotalOptions
            {
                Enabled = true,
                ArtifactKinds = [VirusTotalArtifactKind.MsiPackage]
            }
        };
        Assert.True(PowerForgeReleaseService.ShouldPublishVirusTotalMonitor(
            wingetSpec,
            new PowerForgeReleaseRequest(),
            explicitAppleAction: false,
            runModule: false,
            runPackages: false,
            runTools: true,
            publishUnifiedGitHub: false));
        Assert.False(PowerForgeReleaseService.ShouldPublishVirusTotalMonitor(
            wingetSpec,
            new PowerForgeReleaseRequest { SubmitWinget = false },
            explicitAppleAction: false,
            runModule: false,
            runPackages: false,
            runTools: true,
            publishUnifiedGitHub: false));

        Assert.False(PowerForgeReleaseService.ShouldCaptureVirusTotalModuleArtifactProvenance(
            spec,
            new PowerForgeReleaseRequest
            {
                ModuleRunMode = ConfigurationGateMode.Build,
                PublishNuget = true
            },
            runModule: true));
        Assert.True(PowerForgeReleaseService.ShouldCaptureVirusTotalModuleArtifactProvenance(
            spec,
            new PowerForgeReleaseRequest
            {
                ModuleRunMode = ConfigurationGateMode.Build,
                CaptureModuleArtifactProvenance = true
            },
            runModule: true));
        Assert.True(PowerForgeReleaseService.ShouldCaptureVirusTotalModuleArtifactProvenance(
            spec,
            new PowerForgeReleaseRequest { ModuleRunMode = ConfigurationGateMode.Publish },
            runModule: true));
    }
}
