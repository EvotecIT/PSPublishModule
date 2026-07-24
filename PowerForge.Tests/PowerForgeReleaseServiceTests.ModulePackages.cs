namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void Execute_CoordinatedVersions_RunPackagesFirstAndApplyHighestVersionToModule()
    {
        var root = CreateSandbox();
        try
        {
            var buildScript = Path.Combine(root, "Module", "Build", "Build-Module.ps1");
            var manifestPath = Path.Combine(root, "Module", "PSPublishModule.psd1");
            Directory.CreateDirectory(Path.GetDirectoryName(buildScript)!);
            File.WriteAllText(buildScript, "# test build script");
            File.WriteAllText(manifestPath, "@{ ModuleVersion = '3.0.75' }");

            ProjectBuildHostRequest? capturedRequest = null;
            string? toolReleaseVersion = null;
            var progress = new RecordingReleaseProgress();
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (packageRequest, _, configPath) =>
                {
                    capturedRequest = packageRequest;
                    var execution = new ProjectBuildHostExecutionResult
                    {
                        ConfigPath = configPath,
                        Success = true
                    };
                    execution.Result.Release = new DotNetRepositoryReleaseResult { ResolvedVersion = "3.1.0" };
                    execution.Result.Release.ResolvedVersionsByProject["PowerForge"] = "3.1.0";
                    execution.Result.Release.ResolvedVersionsByProject["PowerForge.Cli"] = "9.0.0";
                    return execution;
                },
                planTools: (_, _, toolRequest) =>
                {
                    toolReleaseVersion = toolRequest.ResolvedReleaseVersion;
                    return new PowerForgeToolReleasePlan
                    {
                        Targets =
                        [
                            new PowerForgeToolReleaseTargetPlan
                            {
                                Combinations = [new PowerForgeToolReleaseCombinationPlan()]
                            }
                        ]
                    };
                },
                runTools: _ => throw new InvalidOperationException("Tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Module = new PowerForgeModuleReleaseOptions
                    {
                        RepositoryRoot = ".",
                        ManifestPath = "Module/PSPublishModule.psd1",
                        ModuleVersion = "3.0.X",
                        SynchronizeVersionWithPackages = true,
                        VersionPrimaryProject = "PowerForge"
                    },
                    Packages = new ProjectBuildConfiguration
                    {
                        RootPath = ".",
                        GitHubPrimaryProject = "PowerForge.Cli"
                    },
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        Targets = [new PowerForgeToolReleaseTarget { Name = "PowerForge" }]
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true,
                    Progress = progress
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.NotNull(capturedRequest);
            Assert.Equal("3.0.76", capturedRequest!.ReleaseVersionFloor);
            Assert.Equal("PowerForge", capturedRequest.ReleaseVersionFloorProject);
            Assert.True(capturedRequest.PlanOnly);
            Assert.False(capturedRequest.ExecuteBuild);
            Assert.False(capturedRequest.PublishNuget);
            Assert.Equal("3.1.0", result.ModulePlan!.ModuleVersion);
            Assert.Null(result.ModulePlan.PreReleaseTag);
            Assert.Equal("3.1.0", toolReleaseVersion);
            Assert.Equal(
                new[]
                {
                    "start:Versioning",
                    "complete:Versioning",
                    "start:Module",
                    "complete:Module",
                    "start:Tools",
                    "complete:Tools"
                },
                progress.Events);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private sealed class RecordingReleaseProgress : IPowerForgeReleaseProgressReporter
    {
        public List<string> Events { get; } = new();

        public void PhaseStarted(PowerForgeReleaseProgressPhase phase, int totalItems, string? detail = null)
            => Events.Add($"start:{phase}");

        public void PhaseCompleted(PowerForgeReleaseProgressPhase phase, string? detail = null)
            => Events.Add($"complete:{phase}");

        public void PhaseFailed(PowerForgeReleaseProgressPhase phase, string? detail = null)
            => Events.Add($"fail:{phase}");
    }

    [Fact]
    public void Execute_CoordinatedVersions_DoesNotPublishPackagesBeforeModuleFailure()
    {
        var root = CreateSandbox();
        try
        {
            var buildScript = Path.Combine(root, "Module", "Build", "Build-Module.ps1");
            var manifestPath = Path.Combine(root, "Module", "PSPublishModule.psd1");
            Directory.CreateDirectory(Path.GetDirectoryName(buildScript)!);
            File.WriteAllText(buildScript, "throw 'module build failed'");
            File.WriteAllText(manifestPath, "@{ ModuleVersion = '3.0.75' }");

            var packageRequests = new List<ProjectBuildHostRequest>();
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (packageRequest, _, configPath) =>
                {
                    packageRequests.Add(packageRequest);
                    var execution = new ProjectBuildHostExecutionResult
                    {
                        ConfigPath = configPath,
                        Success = true
                    };
                    execution.Result.Release = new DotNetRepositoryReleaseResult { ResolvedVersion = "3.0.76" };
                    execution.Result.Release.ResolvedVersionsByProject["PowerForge"] = "3.0.76";
                    return execution;
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
                        ScriptPath = "Module/Build/Build-Module.ps1",
                        ManifestPath = "Module/PSPublishModule.psd1",
                        ModuleVersion = "3.0.X",
                        SynchronizeVersionWithPackages = true,
                        VersionPrimaryProject = "PowerForge"
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
                    PublishNuget = true,
                    ModuleNoDotnetBuild = true,
                    ModuleFramework = "net8.0"
                });

            Assert.False(result.Success);
            var packagePlan = Assert.Single(packageRequests);
            Assert.True(packagePlan.PlanOnly);
            Assert.False(packagePlan.ExecuteBuild);
            Assert.False(packagePlan.PublishNuget);
            Assert.Contains("module build failed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_CoordinatedVersions_RejectsTwoPackageOwners()
    {
        var root = CreateSandbox();
        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() => new PowerForgeReleaseService(new NullLogger()).Execute(
                new PowerForgeReleaseSpec
                {
                    Module = new PowerForgeModuleReleaseOptions
                    {
                        RepositoryRoot = ".",
                        IncludesPackages = true,
                        SynchronizeVersionWithPackages = true,
                        VersionPrimaryProject = "PowerForge"
                    },
                    Packages = new ProjectBuildConfiguration { RootPath = "." }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true
                }));

            Assert.Contains("cannot be combined", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

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
