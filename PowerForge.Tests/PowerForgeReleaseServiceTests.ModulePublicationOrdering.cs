using System.Diagnostics;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void Execute_verified_github_recovery_skips_completed_registry_publication()
    {
        var root = CreateSandbox();
        try
        {
            var scriptPath = Path.Combine(root, "Build-Module.ps1");
            var releasePath = Path.Combine(root, "release.json");
            var moduleZip = Path.Combine(root, "SampleModule.v1.2.3.zip");
            var toolZip = Path.Combine(root, "SampleTool-1.2.3.zip");
            var toolExecutable = Path.Combine(root, "SampleTool");
            File.WriteAllText(scriptPath, "# module build");
            File.WriteAllText(releasePath, "{}");
            File.WriteAllText(moduleZip, "rebuilt module zip");
            File.WriteAllText(toolZip, "tool zip");
            File.WriteAllText(toolExecutable, "tool");

            var moduleCalls = new List<ModuleExecutionSnapshot>();
            var publishCalls = new List<GitHubReleasePublishRequest>();
            var service = CreateReleaseService(
                root,
                moduleCalls,
                new PowerForgeToolReleaseResult
                {
                    Success = true,
                    Artefacts =
                    [
                        new PowerForgeToolReleaseArtifactResult
                        {
                            Target = "SampleTool",
                            Version = "1.2.3",
                            ExecutablePath = toolExecutable,
                            ZipPath = toolZip
                        }
                    ]
                },
                publishGitHubRelease: request =>
                {
                    publishCalls.Add(request);
                    return new GitHubReleasePublishResult
                    {
                        Succeeded = true,
                        ReusedExistingRelease = true,
                        HtmlUrl = "https://github.com/EvotecIT/example/releases/tag/v1.2.3"
                    };
                },
                restorePublishedNuGetAssets: (_, _, _, _) => Array.Empty<string>(),
                restorePublishedModuleAssets: (source, moduleName, version, paths, _) =>
                {
                    Assert.Equal("https://www.powershellgallery.com/api/v2", source);
                    Assert.Equal("SampleModule", moduleName);
                    Assert.Equal("1.2.3", version);
                    Assert.Equal(moduleZip, Assert.Single(paths));
                    return [moduleZip];
                });
            var spec = CreateReleaseSpec(root, scriptPath);
            spec.Module!.ModuleName = "SampleModule";
            spec.Module.ArtifactPaths = [moduleZip];
            spec.GitHub = new PowerForgeReleaseGitHubOptions
            {
                Publish = true,
                VersionSource = PowerForgeReleaseVersionSource.Assets,
                Owner = "EvotecIT",
                Repository = "example",
                TokenEnvName = "PATH",
                Commitish = "0123456789abcdef0123456789abcdef01234567",
                ReuseExistingRelease = true,
                RequireExpectedExistingRelease = true,
                ExpectedExistingReleaseId = 42,
                RequirePublishedStableRelease = true,
                ReplaceExistingAssets = true,
                RequirePublishedNuGetAssets = true,
                RequirePublishedModuleAssets = true,
                PublishedModuleSource = "https://www.powershellgallery.com/api/v2"
            };
            var request = new PowerForgeReleaseRequest
            {
                ConfigPath = releasePath,
                ModuleRunMode = ConfigurationGateMode.Publish,
                PublishNuget = true
            };

            var result = service.Execute(spec, request);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(result.RegistryPublishingSkippedForVerifiedGitHubRecovery);
            Assert.False(request.PublishNuget);
            var module = Assert.Single(moduleCalls);
            Assert.Equal(ConfigurationGateMode.Publish, module.RunMode);
            Assert.False(module.IncludeModulePublishing);
            Assert.Single(publishCalls);
            Assert.Equal(
                moduleZip,
                Assert.Single(result.UnifiedGitHubRelease!.RecoveredPublishedModuleAssets));
            Assert.Null(result.ModulePublication);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ApplyVerifiedGitHubRecoveryPublishingOverrides_requires_complete_exact_byte_contract()
    {
        var request = new PowerForgeReleaseRequest
        {
            ModuleRunMode = ConfigurationGateMode.Publish,
            PublishProjectGitHub = true,
            PublishNuget = true
        };
        var gitHub = new PowerForgeReleaseGitHubOptions
        {
            Publish = true,
            ReuseExistingRelease = true,
            RequireExpectedExistingRelease = true,
            ExpectedExistingReleaseId = 42,
            RequirePublishedStableRelease = true,
            ReplaceExistingAssets = false,
            RequirePublishedNuGetAssets = true,
            RequirePublishedModuleAssets = true,
            PublishedModuleSource = "https://www.powershellgallery.com/api/v2"
        };
        var spec = new PowerForgeReleaseSpec { GitHub = gitHub };

        Assert.Throws<InvalidOperationException>(() =>
            PowerForgeReleaseService.ApplyVerifiedGitHubRecoveryPublishingOverrides(spec, request));
        Assert.True(request.PublishNuget);
        Assert.Null(request.ModuleIncludePublishing);

        gitHub.Commitish = "not-a-commit";
        Assert.Throws<InvalidOperationException>(() =>
            PowerForgeReleaseService.ApplyVerifiedGitHubRecoveryPublishingOverrides(spec, request));

        gitHub.Commitish = "0123456789abcdef0123456789abcdef01234567";
        Assert.Throws<InvalidOperationException>(() =>
            PowerForgeReleaseService.ApplyVerifiedGitHubRecoveryPublishingOverrides(spec, request));

        gitHub.ReplaceExistingAssets = true;
        gitHub.RequirePublishedModuleAssets = false;

        Assert.Throws<InvalidOperationException>(() =>
            PowerForgeReleaseService.ApplyVerifiedGitHubRecoveryPublishingOverrides(spec, request));
        Assert.True(request.PublishNuget);
        Assert.Null(request.ModuleIncludePublishing);

        gitHub.RequirePublishedModuleAssets = true;

        Assert.True(PowerForgeReleaseService.ApplyVerifiedGitHubRecoveryPublishingOverrides(spec, request));
        Assert.False(request.PublishNuget);
        Assert.False(request.ModuleIncludePublishing);
    }

    [Fact]
    public void ApplyVerifiedGitHubRecoveryPublishingOverrides_preserves_missing_registry_publication_before_first_github_release()
    {
        var request = new PowerForgeReleaseRequest
        {
            ModuleRunMode = ConfigurationGateMode.Publish,
            PublishProjectGitHub = true,
            PublishNuget = true
        };
        var gitHub = new PowerForgeReleaseGitHubOptions
        {
            Publish = true,
            Commitish = "0123456789abcdef0123456789abcdef01234567",
            RequirePublishedNuGetAssets = true,
            RequirePublishedModuleAssets = true,
            PublishedModuleSource = "https://www.powershellgallery.com/api/v2",
            RecoverPublishedRegistryAssetsBeforeGitHubRelease = true,
            PublishedModuleAlreadyExists = true
        };

        var allRegistriesSkipped = PowerForgeReleaseService.ApplyVerifiedGitHubRecoveryPublishingOverrides(
            new PowerForgeReleaseSpec { GitHub = gitHub },
            request);

        Assert.False(allRegistriesSkipped);
        Assert.True(request.PublishNuget);
        Assert.False(request.ModuleIncludePublishing);

        gitHub.PublishedModuleAlreadyExists = false;
        request.ModuleIncludePublishing = null;
        allRegistriesSkipped = PowerForgeReleaseService.ApplyVerifiedGitHubRecoveryPublishingOverrides(
            new PowerForgeReleaseSpec { GitHub = gitHub },
            request);

        Assert.False(allRegistriesSkipped);
        Assert.True(request.PublishNuget);
        Assert.Null(request.ModuleIncludePublishing);
    }

    [Fact]
    public void ApplyVerifiedGitHubRecoveryPublishingOverrides_rejects_incomplete_new_mode_flags()
    {
        var request = new PowerForgeReleaseRequest
        {
            ModuleRunMode = ConfigurationGateMode.Publish,
            PublishProjectGitHub = true,
            PublishNuget = true
        };
        var gitHub = new PowerForgeReleaseGitHubOptions
        {
            Publish = true,
            Commitish = "0123456789abcdef0123456789abcdef01234567",
            RecoverPublishedRegistryAssetsBeforeGitHubRelease = true
        };

        Assert.Throws<InvalidOperationException>(() =>
            PowerForgeReleaseService.ApplyVerifiedGitHubRecoveryPublishingOverrides(
                new PowerForgeReleaseSpec { GitHub = gitHub },
                request));

        gitHub.RecoverPublishedRegistryAssetsBeforeGitHubRelease = false;
        gitHub.PublishedModuleAlreadyExists = true;
        Assert.Throws<InvalidOperationException>(() =>
            PowerForgeReleaseService.ApplyVerifiedGitHubRecoveryPublishingOverrides(
                new PowerForgeReleaseSpec { GitHub = gitHub },
                request));
    }

    [Fact]
    public void Execute_rejects_unbound_direct_recovery_before_any_release_lane_runs()
    {
        var root = CreateSandbox();
        try
        {
            var scriptPath = Path.Combine(root, "Build-Module.ps1");
            var releasePath = Path.Combine(root, "release.json");
            File.WriteAllText(scriptPath, "# module build");
            File.WriteAllText(releasePath, "{}");
            var moduleCalls = new List<ModuleExecutionSnapshot>();
            var service = CreateReleaseService(
                root,
                moduleCalls,
                new PowerForgeToolReleaseResult { Success = true });
            var spec = CreateReleaseSpec(root, scriptPath);
            spec.GitHub = new PowerForgeReleaseGitHubOptions
            {
                Publish = true,
                ReuseExistingRelease = true,
                RequireExpectedExistingRelease = true,
                ExpectedExistingReleaseId = 42,
                RequirePublishedStableRelease = true,
                ReplaceExistingAssets = true,
                RequirePublishedNuGetAssets = true,
                RequirePublishedModuleAssets = true,
                PublishedModuleSource = "https://www.powershellgallery.com/api/v2"
            };

            var exception = Assert.Throws<InvalidOperationException>(() => service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ModuleRunMode = ConfigurationGateMode.Publish
                }));

            Assert.Contains("40-character commit SHA", exception.Message, StringComparison.Ordinal);
            Assert.Empty(moduleCalls);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_allows_basic_existing_release_reuse_without_registry_recovery_binding()
    {
        var root = CreateSandbox();
        try
        {
            var scriptPath = Path.Combine(root, "Build-Module.ps1");
            var releasePath = Path.Combine(root, "release.json");
            File.WriteAllText(scriptPath, "# module build");
            File.WriteAllText(releasePath, "{}");
            var moduleCalls = new List<ModuleExecutionSnapshot>();
            var service = CreateReleaseService(
                root,
                moduleCalls,
                new PowerForgeToolReleaseResult { Success = true });
            var spec = CreateReleaseSpec(root, scriptPath);
            spec.GitHub = new PowerForgeReleaseGitHubOptions
            {
                Publish = false,
                ReuseExistingRelease = true,
                ReplaceExistingAssets = true
            };

            var result = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ModuleRunMode = ConfigurationGateMode.Publish
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(2, moduleCalls.Count);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PublishBuiltReleaseOutputs_recovers_registry_payloads_before_creating_first_github_release()
    {
        var root = CreateSandbox();
        try
        {
            var releasePath = Path.Combine(root, "release.json");
            var packagePath = Path.Combine(root, "PowerForge.1.2.3.nupkg");
            var modulePath = Path.Combine(root, "SampleModule.v1.2.3.zip");
            File.WriteAllText(releasePath, "{}");
            File.WriteAllText(packagePath, "rebuilt package");
            File.WriteAllText(modulePath, "rebuilt module");
            var publishCalls = new List<GitHubReleasePublishRequest>();
            var nugetRecoveryCalls = 0;
            var moduleRecoveryCalls = 0;
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages must not rebuild."),
                planTools: (_, _, _) => throw new InvalidOperationException("Tools must not plan."),
                runTools: _ => throw new InvalidOperationException("Tools must not run."),
                publishGitHubRelease: request =>
                {
                    publishCalls.Add(request);
                    return new GitHubReleasePublishResult
                    {
                        Succeeded = true,
                        HtmlUrl = "https://github.com/EvotecIT/example/releases/tag/v1.2.3"
                    };
                },
                restorePublishedNuGetAssets: (_, version, paths, _) =>
                {
                    nugetRecoveryCalls++;
                    Assert.Equal("1.2.3", version);
                    Assert.Contains(packagePath, paths);
                    return [packagePath];
                },
                restorePublishedModuleAssets: (source, name, version, paths, _) =>
                {
                    moduleRecoveryCalls++;
                    Assert.Equal("https://www.powershellgallery.com/api/v2", source);
                    Assert.Equal("SampleModule", name);
                    Assert.Equal("1.2.3", version);
                    Assert.Equal(modulePath, Assert.Single(paths));
                    return [modulePath];
                });
            var built = new PowerForgeReleaseResult
            {
                Success = true,
                ModulePlan = new PowerForgeModuleReleasePlanSummary
                {
                    ModuleName = "SampleModule",
                    ModuleVersion = "1.2.3"
                },
                ReleaseAssets = [packagePath, modulePath],
                ReleaseAssetEntries =
                [
                    new PowerForgeReleaseAssetEntry
                    {
                        Path = packagePath,
                        Version = "1.2.3",
                        Category = PowerForgeReleaseAssetCategory.Package
                    },
                    new PowerForgeReleaseAssetEntry
                    {
                        Path = modulePath,
                        Version = "1.2.3",
                        Category = PowerForgeReleaseAssetCategory.Module
                    }
                ]
            };
            var spec = new PowerForgeReleaseSpec
            {
                Packages = new ProjectBuildConfiguration
                {
                    PublishSource = "https://api.nuget.org/v3/index.json"
                },
                GitHub = new PowerForgeReleaseGitHubOptions
                {
                    Publish = true,
                    VersionSource = PowerForgeReleaseVersionSource.Assets,
                    Owner = "EvotecIT",
                    Repository = "example",
                    TokenEnvName = "PATH",
                    Commitish = "0123456789abcdef0123456789abcdef01234567",
                    RequirePublishedNuGetAssets = true,
                    RequirePublishedModuleAssets = true,
                    PublishedModuleSource = "https://www.powershellgallery.com/api/v2",
                    RecoverPublishedRegistryAssetsBeforeGitHubRelease = true,
                    PublishedModuleAlreadyExists = true
                }
            };

            var result = service.PublishBuiltReleaseOutputs(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ResolvedReleaseVersion = "1.2.3"
                },
                built);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(1, nugetRecoveryCalls);
            Assert.Equal(1, moduleRecoveryCalls);
            var publish = Assert.Single(publishCalls);
            Assert.False(publish.ReuseExistingReleaseOnConflict);
            Assert.False(publish.ReplaceExistingAssets);
            Assert.Equal("0123456789abcdef0123456789abcdef01234567", publish.ExpectedTagCommitSha);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_defers_module_publication_until_tool_build_succeeds()
    {
        var root = CreateSandbox();
        try
        {
            var scriptPath = Path.Combine(root, "Build-Module.ps1");
            var releasePath = Path.Combine(root, "release.json");
            File.WriteAllText(scriptPath, "# module build");
            File.WriteAllText(releasePath, "{}");
            var moduleCalls = new List<ModuleExecutionSnapshot>();
            var service = CreateReleaseService(
                root,
                moduleCalls,
                new PowerForgeToolReleaseResult { Success = true });

            var result = service.Execute(
                CreateReleaseSpec(root, scriptPath),
                new PowerForgeReleaseRequest {
                    ConfigPath = releasePath,
                    ModuleRunMode = ConfigurationGateMode.Publish
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(2, moduleCalls.Count);
            Assert.Equal(ConfigurationGateMode.Build, moduleCalls[0].RunMode);
            Assert.False(moduleCalls[0].IncludeModulePublishing);
            Assert.Equal(ConfigurationGateMode.Publish, moduleCalls[1].RunMode);
            Assert.True(moduleCalls[1].IncludeModulePublishing);
            Assert.True(moduleCalls[1].NoDotnetBuild);
            Assert.True(moduleCalls[1].NoDotnetBuildWasSpecified);
            Assert.False(moduleCalls[0].ReuseStaging);
            Assert.False(moduleCalls[1].ReuseStaging);
            Assert.True(moduleCalls[1].SkipInstall);
            Assert.False(moduleCalls[1].IncludeProjectPackages);
            Assert.True(moduleCalls[0].RequireReusableOutput);
            Assert.True(moduleCalls[1].RequireReusableOutput);
            Assert.False(string.IsNullOrWhiteSpace(moduleCalls[0].StagingPath));
            Assert.Equal(moduleCalls[0].StagingPath, moduleCalls[1].StagingPath);
            Assert.False(Directory.Exists(moduleCalls[0].StagingPath));
            Assert.NotNull(result.Module);
            Assert.NotNull(result.ModulePublication);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ValidatesStagedReleaseBeforeDeferredModulePublication()
    {
        var root = CreateSandbox();
        try
        {
            var scriptPath = Path.Combine(root, "Build-Module.ps1");
            var validationPath = Path.Combine(root, "Test-Release.ps1");
            var releasePath = Path.Combine(root, "release.json");
            File.WriteAllText(scriptPath, "# module build");
            File.WriteAllText(validationPath, "# validation");
            File.WriteAllText(releasePath, "{}");
            var moduleCalls = new List<ModuleExecutionSnapshot>();
            var toolCompleted = false;
            var validationCalls = 0;
            var stageRoot = Path.Combine(root, "request-staged");
            var service = CreateReleaseService(
                root,
                moduleCalls,
                new PowerForgeToolReleaseResult { Success = true },
                onToolExecution: () => toolCompleted = true,
                runReleaseValidation: (_, context, _, _) =>
                {
                    validationCalls++;
                    Assert.True(toolCompleted);
                    Assert.Single(moduleCalls);
                    Assert.True(File.Exists(context.ReleaseManifestPath), context.ReleaseManifestPath);
                    Assert.Equal("1.2.3", context.ResolvedVersion);
                    Assert.Equal(stageRoot, context.StagingRoot);
                    return new PowerForgeReleaseValidationResult
                    {
                        Name = "release",
                        Succeeded = true,
                        ExitCode = 0
                    };
                });
            var spec = CreateReleaseSpec(root, scriptPath);
            spec.Module!.ModuleVersion = "1.2.3";
            spec.Validation = new PowerForgeReleaseValidationOptions
            {
                AfterStaging =
                [
                    new PowerForgeReleaseValidationAction
                    {
                        Name = "release",
                        FilePath = validationPath
                    }
                ]
            };

            var result = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ModuleRunMode = ConfigurationGateMode.Publish,
                    ModuleVersion = "1.2.3",
                    StageRoot = stageRoot
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(1, validationCalls);
            Assert.Equal(2, moduleCalls.Count);
            Assert.Single(result.ReleaseValidations);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ValidatesStagedReleaseBeforePublishingPerToolGitHubRelease()
    {
        var root = CreateSandbox();
        try
        {
            var releasePath = Path.Combine(root, "release.json");
            var validationPath = Path.Combine(root, "Test-Release.ps1");
            var toolZip = Path.Combine(root, "SampleTool-1.2.3.zip");
            var toolExecutable = Path.Combine(root, "SampleTool.exe");
            File.WriteAllText(releasePath, "{}");
            File.WriteAllText(validationPath, "# validation");
            File.WriteAllText(toolZip, "tool archive");
            File.WriteAllText(toolExecutable, "tool executable");
            var validationCompleted = false;
            var publicationCalls = 0;
            var service = CreateReleaseService(
                root,
                new List<ModuleExecutionSnapshot>(),
                new PowerForgeToolReleaseResult
                {
                    Success = true,
                    Artefacts =
                    [
                        new PowerForgeToolReleaseArtifactResult
                        {
                            Target = "SampleTool",
                            Version = "1.2.3",
                            ExecutablePath = toolExecutable,
                            ZipPath = toolZip
                        }
                    ]
                },
                publishGitHubRelease: _ =>
                {
                    Assert.True(validationCompleted);
                    publicationCalls++;
                    return new GitHubReleasePublishResult { Succeeded = true };
                },
                runReleaseValidation: (_, _, _, _) =>
                {
                    Assert.Equal(0, publicationCalls);
                    validationCompleted = true;
                    return new PowerForgeReleaseValidationResult
                    {
                        Name = "release",
                        Succeeded = true,
                        ExitCode = 0
                    };
                });
            var spec = CreateReleaseSpec(root, Path.Combine(root, "unused-Build-Module.ps1"));
            spec.Module = null;
            spec.Tools!.GitHub = new PowerForgeToolReleaseGitHubOptions
            {
                Publish = true,
                Owner = "EvotecIT",
                Repository = "Sample",
                TokenEnvName = "PATH",
                TagTemplate = "{Target}-v{Version}",
                ReleaseNameTemplate = "{Target} {Version}"
            };
            spec.Validation = new PowerForgeReleaseValidationOptions
            {
                AfterStaging =
                [
                    new PowerForgeReleaseValidationAction
                    {
                        Name = "release",
                        FilePath = validationPath
                    }
                ]
            };

            var result = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ToolsOnly = true,
                    StageRoot = Path.Combine(root, "staged")
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(validationCompleted);
            Assert.Equal(1, publicationCalls);
            Assert.Single(result.ToolGitHubReleases);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_FailedStagedReleaseValidationBlocksDeferredPublication()
    {
        var root = CreateSandbox();
        try
        {
            var scriptPath = Path.Combine(root, "Build-Module.ps1");
            var validationPath = Path.Combine(root, "Test-Release.ps1");
            var releasePath = Path.Combine(root, "release.json");
            File.WriteAllText(scriptPath, "# module build");
            File.WriteAllText(validationPath, "# validation");
            File.WriteAllText(releasePath, "{}");
            var moduleCalls = new List<ModuleExecutionSnapshot>();
            var service = CreateReleaseService(
                root,
                moduleCalls,
                new PowerForgeToolReleaseResult { Success = true },
                runReleaseValidation: (_, _, _, _) => new PowerForgeReleaseValidationResult
                {
                    Name = "release",
                    Succeeded = false,
                    ExitCode = 17,
                    StdErr = "release contract failed"
                });
            var spec = CreateReleaseSpec(root, scriptPath);
            spec.Module!.ModuleVersion = "1.2.3";
            spec.Outputs.Staging = new PowerForgeReleaseStagingOptions
            {
                RootPath = Path.Combine(root, "staged")
            };
            spec.Validation = new PowerForgeReleaseValidationOptions
            {
                AfterStaging =
                [
                    new PowerForgeReleaseValidationAction
                    {
                        Name = "release",
                        FilePath = validationPath
                    }
                ]
            };

            var result = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ModuleRunMode = ConfigurationGateMode.Publish,
                    ModuleVersion = "1.2.3"
                });

            Assert.False(result.Success);
            Assert.Contains("release contract failed", result.ErrorMessage, StringComparison.Ordinal);
            Assert.Single(moduleCalls);
            Assert.Null(result.ModulePublication);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_does_not_publish_module_after_tool_build_failure()
    {
        var root = CreateSandbox();
        try
        {
            var scriptPath = Path.Combine(root, "Build-Module.ps1");
            var releasePath = Path.Combine(root, "release.json");
            File.WriteAllText(scriptPath, "# module build");
            File.WriteAllText(releasePath, "{}");
            var moduleCalls = new List<ModuleExecutionSnapshot>();
            var service = CreateReleaseService(
                root,
                moduleCalls,
                new PowerForgeToolReleaseResult {
                    Success = false,
                    ErrorMessage = "Tool build failed."
                });

            var result = service.Execute(
                CreateReleaseSpec(root, scriptPath),
                new PowerForgeReleaseRequest {
                    ConfigPath = releasePath,
                    ModuleRunMode = ConfigurationGateMode.Publish
                });

            Assert.False(result.Success);
            Assert.Contains("Tool build failed", result.ErrorMessage, StringComparison.Ordinal);
            var build = Assert.Single(moduleCalls);
            Assert.Equal(ConfigurationGateMode.Build, build.RunMode);
            Assert.False(build.IncludeModulePublishing);
            Assert.Null(result.ModulePublication);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_build_mode_never_defers_or_publishes_module()
    {
        var root = CreateSandbox();
        try
        {
            var scriptPath = Path.Combine(root, "Build-Module.ps1");
            var releasePath = Path.Combine(root, "release.json");
            File.WriteAllText(scriptPath, "# module build");
            File.WriteAllText(releasePath, "{}");
            var moduleCalls = new List<ModuleExecutionSnapshot>();
            var service = CreateReleaseService(
                root,
                moduleCalls,
                new PowerForgeToolReleaseResult { Success = true });

            var result = service.Execute(
                CreateReleaseSpec(root, scriptPath),
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ModuleRunMode = ConfigurationGateMode.Build
                });

            Assert.True(result.Success, result.ErrorMessage);
            var build = Assert.Single(moduleCalls);
            Assert.Equal(ConfigurationGateMode.Build, build.RunMode);
            Assert.True(build.IncludeModulePublishing);
            Assert.Null(result.ModulePublication);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_post_build_source_guard_blocks_mutation_before_module_publication()
    {
        var root = CreateSandbox();
        try
        {
            var scriptPath = Path.Combine(root, "Build-Module.ps1");
            var releasePath = Path.Combine(root, "release.json");
            var trackedInput = Path.Combine(root, "tracked-input.txt");
            File.WriteAllText(scriptPath, "# module build");
            File.WriteAllText(releasePath, "{}");
            File.WriteAllText(trackedInput, "approved");
            RunGitForSourceGuard(root, "init");
            RunGitForSourceGuard(root, "config user.name \"PowerForge Tests\"");
            RunGitForSourceGuard(root, "config user.email \"powerforge-tests@example.invalid\"");
            RunGitForSourceGuard(root, "add .");
            RunGitForSourceGuard(root, "commit -m \"approved source\"");
            string revision = RunGitForSourceGuard(root, "rev-parse HEAD").Trim();

            var moduleCalls = new List<ModuleExecutionSnapshot>();
            var service = CreateReleaseService(
                root,
                moduleCalls,
                new PowerForgeToolReleaseResult { Success = true },
                request =>
                {
                    if (request.RunMode == ConfigurationGateMode.Build)
                        File.WriteAllText(trackedInput, "mutated during module build");
                });
            PowerForgeReleaseSpec spec = CreateReleaseSpec(root, scriptPath);
            spec.Tools = null;

            var exception = Assert.Throws<InvalidOperationException>(() => service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ModuleOnly = true,
                    ModuleRunMode = ConfigurationGateMode.Publish,
                    SourceRepositoryRoot = root,
                    ExpectedSourceRevision = revision,
                    SourceInputPaths = [releasePath]
                }));

            Assert.Contains("changed after the release build", exception.Message, StringComparison.OrdinalIgnoreCase);
            var build = Assert.Single(moduleCalls);
            Assert.Equal(ConfigurationGateMode.Build, build.RunMode);
            Assert.False(build.IncludeModulePublishing);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_post_build_source_guard_rechecks_after_tool_build_before_publication()
    {
        var root = CreateSandbox();
        try
        {
            var scriptPath = Path.Combine(root, "Build-Module.ps1");
            var releasePath = Path.Combine(root, "release.json");
            var trackedInput = Path.Combine(root, "tracked-input.txt");
            File.WriteAllText(scriptPath, "# module build");
            File.WriteAllText(releasePath, "{}");
            File.WriteAllText(trackedInput, "approved");
            RunGitForSourceGuard(root, "init");
            RunGitForSourceGuard(root, "config user.name \"PowerForge Tests\"");
            RunGitForSourceGuard(root, "config user.email \"powerforge-tests@example.invalid\"");
            RunGitForSourceGuard(root, "add .");
            RunGitForSourceGuard(root, "commit -m \"approved source\"");
            string revision = RunGitForSourceGuard(root, "rev-parse HEAD").Trim();
            int publicationAttempts = 0;

            var moduleCalls = new List<ModuleExecutionSnapshot>();
            var service = CreateReleaseService(
                root,
                moduleCalls,
                new PowerForgeToolReleaseResult { Success = true },
                publishGitHubRelease: _ =>
                {
                    publicationAttempts++;
                    return new GitHubReleasePublishResult { Succeeded = true };
                },
                onToolExecution: () => File.WriteAllText(trackedInput, "mutated during tool build"));
            PowerForgeReleaseSpec spec = CreateReleaseSpec(root, scriptPath);
            spec.Tools!.GitHub.Publish = true;

            var exception = Assert.Throws<InvalidOperationException>(() => service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ModuleRunMode = ConfigurationGateMode.Publish,
                    SourceRepositoryRoot = root,
                    ExpectedSourceRevision = revision,
                    SourceInputPaths = [releasePath]
                }));

            Assert.Contains("changed after the release build", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, publicationAttempts);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_post_build_source_guard_rejects_ignored_source_in_project_reference_graph()
    {
        var root = CreateSandbox();
        try
        {
            var releasePath = Path.Combine(root, "release.json");
            var appDirectory = Directory.CreateDirectory(Path.Combine(root, "App")).FullName;
            var libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "Library")).FullName;
            var appProject = Path.Combine(appDirectory, "App.csproj");
            var libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
            File.WriteAllText(releasePath, "{}");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(libraryProject, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "internal static class Library { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "Library/Generated.cs\n");
            RunGitForSourceGuard(root, "init");
            RunGitForSourceGuard(root, "config user.name \"PowerForge Tests\"");
            RunGitForSourceGuard(root, "config user.email \"powerforge-tests@example.invalid\"");
            RunGitForSourceGuard(root, "add .");
            RunGitForSourceGuard(root, "commit -m \"approved source\"");
            string revision = RunGitForSourceGuard(root, "rev-parse HEAD").Trim();
            File.WriteAllText(Path.Combine(libraryDirectory, "Generated.cs"), "internal static class Injected { }");
            int publicationAttempts = 0;

            var service = CreateReleaseService(
                root,
                new List<ModuleExecutionSnapshot>(),
                new PowerForgeToolReleaseResult { Success = true },
                publishGitHubRelease: _ =>
                {
                    publicationAttempts++;
                    return new GitHubReleasePublishResult { Succeeded = true };
                });
            PowerForgeReleaseSpec spec = CreateReleaseSpec(root, Path.Combine(root, "Build-Module.ps1"));
            spec.Module = null;
            spec.Tools!.GitHub.Publish = true;

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ToolsOnly = true,
                    SourceRepositoryRoot = root,
                    ExpectedSourceRevision = revision,
                    SourceInputPaths = [releasePath, appProject]
                }));

            Assert.Contains("changed after the release build", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, publicationAttempts);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_post_build_source_guard_is_invoked_by_package_publisher_after_package_build()
    {
        var root = CreateSandbox();
        try
        {
            var releasePath = Path.Combine(root, "release.json");
            var trackedInput = Path.Combine(root, "tracked-input.txt");
            File.WriteAllText(releasePath, "{}");
            File.WriteAllText(trackedInput, "approved");
            RunGitForSourceGuard(root, "init");
            RunGitForSourceGuard(root, "config user.name \"PowerForge Tests\"");
            RunGitForSourceGuard(root, "config user.email \"powerforge-tests@example.invalid\"");
            RunGitForSourceGuard(root, "add .");
            RunGitForSourceGuard(root, "commit -m \"approved source\"");
            string revision = RunGitForSourceGuard(root, "rev-parse HEAD").Trim();

            var moduleCalls = new List<ModuleExecutionSnapshot>();
            var service = CreateReleaseService(
                root,
                moduleCalls,
                new PowerForgeToolReleaseResult { Success = true },
                executePackages: (packageRequest, _, _) =>
                {
                    File.WriteAllText(trackedInput, "mutated during package build");
                    packageRequest.RemotePublishAttempted?.Invoke();
                    return new ProjectBuildHostExecutionResult { Success = true };
                });
            var spec = new PowerForgeReleaseSpec
            {
                Packages = new ProjectBuildConfiguration { PublishNuget = true }
            };

            var exception = Assert.Throws<InvalidOperationException>(() => service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    PackagesOnly = true,
                    PublishNuget = true,
                    SourceRepositoryRoot = root,
                    ExpectedSourceRevision = revision,
                    SourceInputPaths = [releasePath]
                }));

            Assert.Contains("changed after the release build", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_package_source_guard_rejects_ignored_evaluated_project_input()
    {
        var root = CreateSandbox();
        try
        {
            string releasePath = Path.Combine(root, "release.json");
            string projectPath = Path.Combine(root, "Package.csproj");
            string ignoredDirectory = Directory.CreateDirectory(Path.Combine(root, "ignored")).FullName;
            File.WriteAllText(releasePath, "{}");
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                  <ItemGroup><AdditionalFiles Include="ignored/rules.json" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, ".gitignore"), "ignored/\nbin/\nobj/\n");
            RunGitForSourceGuard(root, "init");
            RunGitForSourceGuard(root, "config user.name \"PowerForge Tests\"");
            RunGitForSourceGuard(root, "config user.email \"powerforge-tests@example.invalid\"");
            RunGitForSourceGuard(root, "add .");
            RunGitForSourceGuard(root, "commit -m \"approved source\"");
            string revision = RunGitForSourceGuard(root, "rev-parse HEAD").Trim();
            File.WriteAllText(Path.Combine(ignoredDirectory, "rules.json"), "{\"mutable\":true}");

            var service = CreateReleaseService(
                root,
                new List<ModuleExecutionSnapshot>(),
                new PowerForgeToolReleaseResult { Success = true },
                executePackages: (packageRequest, configuration, configPath) =>
                {
                    ProjectBuildPreparedContext prepared = new ProjectBuildPreparationService().Prepare(
                        configuration,
                        Path.GetDirectoryName(configPath)!,
                        planPath: null,
                        new ProjectBuildRequestedActions
                        {
                            Build = true,
                            PublishNuget = true
                        });
                    packageRequest.BuildSpecPrepared?.Invoke(prepared.Spec);
                    packageRequest.RemotePublishAttempted?.Invoke();
                    return new ProjectBuildHostExecutionResult { Success = true };
                });
            var spec = new PowerForgeReleaseSpec
            {
                Packages = new ProjectBuildConfiguration
                {
                    RootPath = root,
                    Build = true,
                    PublishNuget = true
                }
            };

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    PackagesOnly = true,
                    PublishNuget = true,
                    SourceRepositoryRoot = root,
                    ExpectedSourceRevision = revision,
                    SourceInputPaths = [releasePath]
                }));

            Assert.Contains("changed after the release build", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_post_build_source_guard_defers_and_allows_clean_module_publication()
    {
        var root = CreateSandbox();
        try
        {
            var scriptPath = Path.Combine(root, "Build-Module.ps1");
            var releasePath = Path.Combine(root, "release.json");
            File.WriteAllText(scriptPath, "# module build");
            File.WriteAllText(releasePath, "{}");
            RunGitForSourceGuard(root, "init");
            RunGitForSourceGuard(root, "config user.name \"PowerForge Tests\"");
            RunGitForSourceGuard(root, "config user.email \"powerforge-tests@example.invalid\"");
            RunGitForSourceGuard(root, "add .");
            RunGitForSourceGuard(root, "commit -m \"approved source\"");
            string revision = RunGitForSourceGuard(root, "rev-parse HEAD").Trim();

            var moduleCalls = new List<ModuleExecutionSnapshot>();
            var service = CreateReleaseService(
                root,
                moduleCalls,
                new PowerForgeToolReleaseResult { Success = true });
            PowerForgeReleaseSpec spec = CreateReleaseSpec(root, scriptPath);
            spec.Tools = null;

            PowerForgeReleaseResult result = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ModuleOnly = true,
                    ModuleRunMode = ConfigurationGateMode.Publish,
                    SourceRepositoryRoot = root,
                    ExpectedSourceRevision = revision,
                    SourceInputPaths = [releasePath]
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(2, moduleCalls.Count);
            Assert.Equal(ConfigurationGateMode.Build, moduleCalls[0].RunMode);
            Assert.Equal(ConfigurationGateMode.Publish, moduleCalls[1].RunMode);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_post_build_source_guard_allows_dirty_operator_file_outside_configured_module_source()
    {
        var root = CreateSandbox();
        try
        {
            string moduleRoot = Directory.CreateDirectory(Path.Combine(root, "Module")).FullName;
            string buildRoot = Directory.CreateDirectory(Path.Combine(root, "Build")).FullName;
            string moduleConfigPath = Path.Combine(root, "powerforge.json");
            string releasePath = Path.Combine(root, "release.json");
            string operatorScript = Path.Combine(buildRoot, "Build-Project.ps1");
            File.WriteAllText(Path.Combine(moduleRoot, "TestModule.psm1"), "function Get-Test { 'ok' }");
            File.WriteAllText(moduleConfigPath, """
                {
                  "Build": {
                    "Name": "TestModule",
                    "SourcePath": "Module",
                    "Version": "1.0.0"
                  }
                }
                """);
            File.WriteAllText(releasePath, "{}");
            File.WriteAllText(operatorScript, "param([string] $RunMode = 'Build')");
            RunGitForSourceGuard(root, "init");
            RunGitForSourceGuard(root, "config user.name \"PowerForge Tests\"");
            RunGitForSourceGuard(root, "config user.email \"powerforge-tests@example.invalid\"");
            RunGitForSourceGuard(root, "add .");
            RunGitForSourceGuard(root, "commit -m \"approved source\"");
            string revision = RunGitForSourceGuard(root, "rev-parse HEAD").Trim();
            File.WriteAllText(operatorScript, "param([string] $RunMode = 'Publish')");

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
                    ConfigPath = moduleConfigPath
                }
            };

            PowerForgeReleaseResult result = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ModuleOnly = true,
                    ModuleRunMode = ConfigurationGateMode.Publish,
                    SourceRepositoryRoot = root,
                    ExpectedSourceRevision = revision,
                    SourceInputPaths = [releasePath]
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(2, moduleCalls.Count);
            Assert.Equal(ConfigurationGateMode.Build, moduleCalls[0].RunMode);
            Assert.Equal(ConfigurationGateMode.Publish, moduleCalls[1].RunMode);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void BuildModuleFailureMessage_UsesStructuredFailureWithoutRepeatingStandardOutput()
    {
        var message = PowerForgeReleaseService.BuildModuleFailureMessage(
            @"C:\repo\powerforge.json",
            new ModuleBuildHostExecutionResult
            {
                ExitCode = 1,
                FailureMessage = "The module version is already published.",
                StandardOutput = "hundreds of lines of ordinary build output"
            });

        Assert.Contains("exit code 1", message, StringComparison.Ordinal);
        Assert.Contains("The module version is already published.", message, StringComparison.Ordinal);
        Assert.DoesNotContain("ordinary build output", message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildModuleFailureMessage_UsesBoundedStdoutTailWhenStructuredFailureIsUnavailable()
    {
        var message = PowerForgeReleaseService.BuildModuleFailureMessage(
            @"C:\repo\powerforge.json",
            new ModuleBuildHostExecutionResult
            {
                ExitCode = 1,
                StandardOutput = "\u001b[31m" + new string('x', 5_000) + "\u001b[0m\r\nActual module failure"
            });

        Assert.Contains("Actual module failure", message, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b[31m", message, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('x', 5_000), message, StringComparison.Ordinal);
        Assert.InRange(message.Length, 1, 4_200);
    }

    [Fact]
    public void Execute_deferred_json_module_publish_reuses_and_cleans_staging_directory()
    {
        var root = CreateSandbox();
        try
        {
            var moduleConfigPath = Path.Combine(root, "powerforge.json");
            var releasePath = Path.Combine(root, "release.json");
            File.WriteAllText(
                moduleConfigPath,
                """{ "Build": { "Name": "SampleModule", "SourcePath": ".", "Version": "1.0.0" }, "Segments": [] }""");
            File.WriteAllText(releasePath, "{}");
            var moduleCalls = new List<ModuleExecutionSnapshot>();
            var service = CreateReleaseService(
                root,
                moduleCalls,
                new PowerForgeToolReleaseResult { Success = true },
                request =>
                {
                    Assert.False(string.IsNullOrWhiteSpace(request.StagingPath));
                    var payloadPath = Path.Combine(request.StagingPath!, "Lib", "SampleModule.dll");
                    if (request.RunMode == ConfigurationGateMode.Build)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(payloadPath)!);
                        File.WriteAllText(payloadPath, "built payload");
                    }
                    else
                    {
                        Assert.True(File.Exists(payloadPath), payloadPath);
                    }
                });

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Module = new PowerForgeModuleReleaseOptions
                    {
                        RepositoryRoot = root,
                        ConfigPath = moduleConfigPath
                    },
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        ProjectRoot = root,
                        Targets =
                        [
                            new PowerForgeToolReleaseTarget
                            {
                                Name = "SampleTool",
                                ProjectPath = "SampleTool.csproj",
                                OutputName = "SampleTool"
                            }
                        ]
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ModuleRunMode = ConfigurationGateMode.Publish
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(2, moduleCalls.Count);
            Assert.Equal(moduleCalls[0].StagingPath, moduleCalls[1].StagingPath);
            Assert.Equal(moduleCalls[0].StagingPath, result.ModulePlan!.StagingPath);
            Assert.False(moduleCalls[0].ReuseStaging);
            Assert.True(moduleCalls[1].ReuseStaging);
            Assert.False(Directory.Exists(moduleCalls[0].StagingPath));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static PowerForgeReleaseSpec CreateReleaseSpec(
        string root,
        string scriptPath)
        => new()
        {
            Module = new PowerForgeModuleReleaseOptions {
                RepositoryRoot = root,
                ScriptPath = scriptPath
            },
            Tools = new PowerForgeToolReleaseSpec {
                ProjectRoot = root,
                Targets = [
                    new PowerForgeToolReleaseTarget {
                        Name = "SampleTool",
                        ProjectPath = "SampleTool.csproj",
                        OutputName = "SampleTool"
                    }
                ]
            }
        };

    private static PowerForgeReleaseService CreateReleaseService(
        string root,
        ICollection<ModuleExecutionSnapshot> moduleCalls,
        PowerForgeToolReleaseResult toolResult,
        Action<ModuleBuildHostBuildRequest>? onModuleExecution = null,
        Func<GitHubReleasePublishRequest, GitHubReleasePublishResult>? publishGitHubRelease = null,
        Func<string, string, IEnumerable<string>, CancellationToken, string[]>? restorePublishedNuGetAssets = null,
        Func<string, string, string, IEnumerable<string>, CancellationToken, string[]>? restorePublishedModuleAssets = null,
        Func<VirusTotalMonitorPublishRequest, CancellationToken, VirusTotalMonitorPublishResult>? publishVirusTotalMonitor = null,
        Action? onToolExecution = null,
        Func<ProjectBuildHostRequest, ProjectBuildConfiguration, string, ProjectBuildHostExecutionResult>? executePackages = null,
        Func<PowerForgeReleaseValidationAction, PowerForgeReleaseValidationContext, string, CancellationToken, PowerForgeReleaseValidationResult>? runReleaseValidation = null)
        => new(
            new NullLogger(),
            executePackages: executePackages ?? ((_, _, _) =>
                throw new InvalidOperationException("Packages should not run.")),
            planTools: (_, _, _) => new PowerForgeToolReleasePlan {
                ProjectRoot = root,
                Targets = [
                    new PowerForgeToolReleaseTargetPlan {
                        Name = "SampleTool",
                        Combinations = [new PowerForgeToolReleaseCombinationPlan()]
                    }
                ]
            },
            runTools: _ =>
            {
                onToolExecution?.Invoke();
                return toolResult;
            },
            loadDotNetToolsSpec: (_, _) =>
                throw new InvalidOperationException("DotNet tools should not run."),
            planDotNetTools: (_, _, _, _) =>
                throw new InvalidOperationException("DotNet tools should not run."),
            runDotNetTools: _ =>
                throw new InvalidOperationException("DotNet tools should not run."),
            publishGitHubRelease: publishGitHubRelease ?? (_ =>
                throw new InvalidOperationException("GitHub should not run.")),
            restorePublishedNuGetAssets: restorePublishedNuGetAssets,
            restorePublishedModuleAssets: restorePublishedModuleAssets,
            publishVirusTotalMonitor: publishVirusTotalMonitor,
            runReleaseValidation: runReleaseValidation,
            executeModuleBuild: (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                onModuleExecution?.Invoke(request);
                moduleCalls.Add(new ModuleExecutionSnapshot(
                    request.RunMode,
                    request.IncludeModulePublishing,
                    request.NoDotnetBuild,
                    request.NoDotnetBuildWasSpecified,
                    request.ReuseStaging,
                    request.SkipInstall,
                    request.IncludeProjectPackages,
                    request.StagingPath,
                    request.RequireReusableOutput));
                return new ModuleBuildHostExecutionResult { ExitCode = 0 };
            });

    private static string RunGitForSourceGuard(string workingDirectory, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Unable to start git.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {arguments} failed: {error}");
        return output;
    }

    private sealed record ModuleExecutionSnapshot(
        ConfigurationGateMode? RunMode,
        bool IncludeModulePublishing,
        bool NoDotnetBuild,
        bool NoDotnetBuildWasSpecified,
        bool ReuseStaging,
        bool SkipInstall,
        bool IncludeProjectPackages,
        string? StagingPath,
        bool RequireReusableOutput);
}
