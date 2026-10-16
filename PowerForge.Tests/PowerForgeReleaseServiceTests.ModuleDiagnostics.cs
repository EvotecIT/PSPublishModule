namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Theory]
    [InlineData(ConfigurationGateMode.Build)]
    [InlineData(ConfigurationGateMode.Publish)]
    public void Execute_ModuleOnly_ReplaysDiagnosticsWithoutAReleaseCheckpoint(ConfigurationGateMode mode)
    {
        var root = CreateSandbox();
        try
        {
            var script = Path.Combine(root, "Build-Module.ps1");
            File.WriteAllText(script, "# synthetic module host boundary");
            var logger = new BufferedLogger();
            var service = new PowerForgeReleaseService(
                logger,
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages must not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Tools must not plan."),
                runTools: _ => throw new InvalidOperationException("Tools must not run."),
                loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("Tools must not load."),
                planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("Tools must not plan."),
                runDotNetTools: _ => throw new InvalidOperationException("Tools must not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub must not publish."),
                executeModuleBuild: (request, _) =>
                {
                    Assert.False(request.ReleaseCheckpoint);
                    Assert.Equal(mode, request.RunMode);
                    return new ModuleBuildHostExecutionResult
                    {
                        ExitCode = 0, StandardOutput = "Complete child output.", StandardError = "Complete child recovery instructions."
                    };
                });
            var result = service.Execute(new PowerForgeReleaseSpec
            {
                Module = new PowerForgeModuleReleaseOptions
                {
                    RepositoryRoot = root, ModuleName = "Sample", ModuleVersion = "1.0.0", ScriptPath = script
                }
            }, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "release.json"), ModuleOnly = true, ModuleRunMode = mode
            });
            Assert.True(result.Success, result.ErrorMessage);
            Assert.Single(logger.Entries, entry => entry.Level == "info" && entry.Message == "Complete child output.");
            Assert.Single(logger.Entries, entry => entry.Level == "warn" && entry.Message == "Complete child recovery instructions.");
        }
        finally { TryDelete(root); }
    }
}
