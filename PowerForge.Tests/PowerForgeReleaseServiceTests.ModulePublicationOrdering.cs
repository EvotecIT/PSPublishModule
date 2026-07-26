namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
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
        Action<ModuleBuildHostBuildRequest>? onModuleExecution = null)
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
            publishGitHubRelease: _ =>
                throw new InvalidOperationException("GitHub should not run."),
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
