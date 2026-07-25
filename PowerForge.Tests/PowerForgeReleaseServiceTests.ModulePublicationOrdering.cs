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
            Assert.True(moduleCalls[1].SkipInstall);
            Assert.False(moduleCalls[1].IncludeProjectPackages);
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
        PowerForgeToolReleaseResult toolResult)
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
                moduleCalls.Add(new ModuleExecutionSnapshot(
                    request.RunMode,
                    request.IncludeModulePublishing,
                    request.NoDotnetBuild,
                    request.NoDotnetBuildWasSpecified,
                    request.SkipInstall,
                    request.IncludeProjectPackages));
                return new ModuleBuildHostExecutionResult { ExitCode = 0 };
            });

    private sealed record ModuleExecutionSnapshot(
        ConfigurationGateMode? RunMode,
        bool IncludeModulePublishing,
        bool NoDotnetBuild,
        bool NoDotnetBuildWasSpecified,
        bool SkipInstall,
        bool IncludeProjectPackages);
}
