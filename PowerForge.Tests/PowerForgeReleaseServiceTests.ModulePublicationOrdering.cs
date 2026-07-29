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
                Token = "token",
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
        Func<string, string, string, IEnumerable<string>, CancellationToken, string[]>? restorePublishedModuleAssets = null)
        => new(
            new NullLogger(),
            executePackages: (_, _, _) =>
                throw new InvalidOperationException("Packages should not run."),
            planTools: (_, _, _) => new PowerForgeToolReleasePlan {
                ProjectRoot = root,
                Targets = [
                    new PowerForgeToolReleaseTargetPlan {
                        Name = "SampleTool",
                        Combinations = [new PowerForgeToolReleaseCombinationPlan()]
                    }
                ]
            },
            runTools: _ => toolResult,
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
