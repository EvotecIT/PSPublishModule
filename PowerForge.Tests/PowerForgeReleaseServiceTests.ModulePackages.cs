namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void Execute_ModuleOwnedPackages_PlansOuterPackageLaneWithoutExecutingIt()
    {
        var root = CreateSandbox();
        try
        {
            var buildScript = Path.Combine(root, "Module", "Build", "Build-Module.ps1");
            Directory.CreateDirectory(Path.GetDirectoryName(buildScript)!);
            File.WriteAllText(buildScript, "# test build script");

            var packageCalls = 0;
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (request, _, configPath) =>
                {
                    packageCalls++;
                    return new ProjectBuildHostExecutionResult
                    {
                        ConfigPath = configPath,
                        Success = request.PlanOnly == true && !request.ExecuteBuild
                    };
                },
                planTools: (_, _, _) => throw new InvalidOperationException("Tools should not run."),
                runTools: _ => throw new InvalidOperationException("Tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Module = new PowerForgeModuleReleaseOptions
                    {
                        RepositoryRoot = ".",
                        IncludesPackages = true
                    },
                    Packages = new ProjectBuildConfiguration
                    {
                        RootPath = "."
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true,
                    ModuleRunMode = ConfigurationGateMode.Publish
                });

            Assert.True(result.Success);
            Assert.NotNull(result.ModulePlan);
            Assert.Equal(ConfigurationGateMode.Publish, result.ModulePlan!.RunMode);
            Assert.True(result.ModulePlan.IncludesPackages);
            Assert.True(result.ModulePlan.IncludesProjectPackages);
            Assert.Equal(1, packageCalls);
            Assert.NotNull(result.Packages);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ModuleOnly_DisablesModuleOwnedProjectPackages()
    {
        var root = CreateSandbox();
        try
        {
            var buildScript = Path.Combine(root, "Module", "Build", "Build-Module.ps1");
            Directory.CreateDirectory(Path.GetDirectoryName(buildScript)!);
            File.WriteAllText(buildScript, "# test build script");

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Module-only must not run package planning."),
                planTools: (_, _, _) => throw new InvalidOperationException("Tools should not run."),
                runTools: _ => throw new InvalidOperationException("Tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Module = new PowerForgeModuleReleaseOptions
                    {
                        RepositoryRoot = ".",
                        IncludesPackages = true
                    },
                    Packages = new ProjectBuildConfiguration
                    {
                        RootPath = ".",
                        PublishNuget = true
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true,
                    ModuleOnly = true,
                    ModuleRunMode = ConfigurationGateMode.Publish
                });

            Assert.True(result.Success);
            Assert.NotNull(result.ModulePlan);
            Assert.True(result.ModulePlan!.IncludesPackages);
            Assert.False(result.ModulePlan.IncludesProjectPackages);
            Assert.Null(result.Packages);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_PackagesOnly_RunsOuterPackageLaneForModuleOwnedPackages()
    {
        var root = CreateSandbox();
        try
        {
            var packageCalls = 0;
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (request, _, configPath) =>
                {
                    packageCalls++;
                    return new ProjectBuildHostExecutionResult
                    {
                        ConfigPath = configPath,
                        Success = request.PlanOnly == true
                    };
                },
                planTools: (_, _, _) => throw new InvalidOperationException("Tools should not run."),
                runTools: _ => throw new InvalidOperationException("Tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Module = new PowerForgeModuleReleaseOptions
                    {
                        RepositoryRoot = ".",
                        IncludesPackages = true
                    },
                    Packages = new ProjectBuildConfiguration
                    {
                        RootPath = "."
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true,
                    PackagesOnly = true
                });

            Assert.True(result.Success);
            Assert.Equal(1, packageCalls);
            Assert.Null(result.ModulePlan);
            Assert.NotNull(result.Packages);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Execute_ModuleOwnedPackagePublishSwitch_InfersPublishGate(
        bool publishNuget,
        bool publishProjectGitHub)
    {
        var root = CreateSandbox();
        try
        {
            var buildScript = Path.Combine(root, "Module", "Build", "Build-Module.ps1");
            Directory.CreateDirectory(Path.GetDirectoryName(buildScript)!);
            File.WriteAllText(buildScript, "# test build script");

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (request, _, configPath) => new ProjectBuildHostExecutionResult
                {
                    ConfigPath = configPath,
                    Success = request.PlanOnly == true && !request.ExecuteBuild
                },
                planTools: (_, _, _) => throw new InvalidOperationException("Tools should not run."),
                runTools: _ => throw new InvalidOperationException("Tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Module = new PowerForgeModuleReleaseOptions
                    {
                        RepositoryRoot = ".",
                        IncludesPackages = true,
                        Framework = "net10.0"
                    },
                    Packages = new ProjectBuildConfiguration
                    {
                        RootPath = "."
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true,
                    PublishNuget = publishNuget,
                    PublishProjectGitHub = publishProjectGitHub
                });

            Assert.True(result.Success);
            Assert.NotNull(result.ModulePlan);
            Assert.Equal(ConfigurationGateMode.Publish, result.ModulePlan!.RunMode);
            Assert.Equal("net10.0", result.ModulePlan.Framework);
            Assert.NotNull(result.Packages);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData(true, false, null, null, ConfigurationGateMode.Publish)]
    [InlineData(false, true, null, null, ConfigurationGateMode.Publish)]
    [InlineData(true, false, false, null, ConfigurationGateMode.Build)]
    [InlineData(false, true, null, false, ConfigurationGateMode.Build)]
    public void Execute_ModuleOwnedPackageConfiguration_UsesEffectivePublishSetting(
        bool configuredNuget,
        bool configuredGitHub,
        bool? requestedNuget,
        bool? requestedProjectGitHub,
        ConfigurationGateMode expectedRunMode)
    {
        var root = CreateSandbox();
        try
        {
            var buildScript = Path.Combine(root, "Module", "Build", "Build-Module.ps1");
            Directory.CreateDirectory(Path.GetDirectoryName(buildScript)!);
            File.WriteAllText(buildScript, "# test build script");

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (request, _, configPath) => new ProjectBuildHostExecutionResult
                {
                    ConfigPath = configPath,
                    Success = request.PlanOnly == true && !request.ExecuteBuild
                },
                planTools: (_, _, _) => throw new InvalidOperationException("Tools should not run."),
                runTools: _ => throw new InvalidOperationException("Tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Module = new PowerForgeModuleReleaseOptions
                    {
                        RepositoryRoot = ".",
                        IncludesPackages = true
                    },
                    Packages = new ProjectBuildConfiguration
                    {
                        RootPath = ".",
                        PublishNuget = configuredNuget,
                        PublishGitHub = configuredGitHub
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true,
                    PublishNuget = requestedNuget,
                    PublishProjectGitHub = requestedProjectGitHub
                });

            Assert.True(result.Success);
            Assert.NotNull(result.ModulePlan);
            Assert.Equal(expectedRunMode, result.ModulePlan!.RunMode);
            Assert.NotNull(result.Packages);
        }
        finally
        {
            TryDelete(root);
        }
    }
}
