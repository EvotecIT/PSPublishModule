using System.IO.Compression;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void Execute_PreservesSynthesizedScriptArchiveWithoutConfiguredStaging()
    {
        string root = CreateSandbox();
        try
        {
            const string moduleName = "Company.Tools";
            string buildScript = Path.Combine(root, "Build-Module.ps1");
            string releaseConfig = Path.Combine(root, "release.json");
            string scriptOutput = Path.Combine(root, "ScriptOutput");
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            File.WriteAllText(buildScript, "# module build");
            File.WriteAllText(releaseConfig, "{}");
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Tools should not plan."),
                runTools: _ => throw new InvalidOperationException("Tools should not run."),
                loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("DotNet tools should not load."),
                planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("DotNet tools should not plan."),
                runDotNetTools: _ => throw new InvalidOperationException("DotNet tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
                executeModuleBuild: (_, _) =>
                {
                    Directory.CreateDirectory(Path.Combine(scriptOutput, "support"));
                    File.WriteAllText(Path.Combine(scriptOutput, moduleName + ".ps1"), "'entry point'");
                    File.WriteAllText(Path.Combine(scriptOutput, "support", "helper.txt"), "helper");
                    return new ModuleBuildHostExecutionResult
                    {
                        ExitCode = 0,
                        ArtefactOutputs =
                        [
                            new PowerForgeModuleArtefactOutputSummary
                            {
                                Type = ArtefactType.Script,
                                OutputRoot = scriptOutput,
                                OutputPath = scriptOutput,
                                EntryPointRelativePath = moduleName + ".ps1"
                            }
                        ]
                    };
                });

            PowerForgeReleaseResult result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Module = new PowerForgeModuleReleaseOptions
                    {
                        RepositoryRoot = root,
                        ModuleName = moduleName,
                        ScriptPath = buildScript,
                        ModuleVersion = "1.2.3",
                        IncludesPackages = true,
                        ArtifactPaths = [scriptOutput]
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releaseConfig,
                    ModuleOnly = true,
                    ModuleRunMode = ConfigurationGateMode.Build
                });

            Assert.True(result.Success, result.ErrorMessage);
            string archivePath = Assert.Single(result.ModuleProducedAssets);
            Assert.StartsWith(Path.Combine(root, ".git", "powerforge"), archivePath, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(archivePath));
            Assert.Contains(archivePath, result.ReleaseAssets, StringComparer.OrdinalIgnoreCase);
            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            Assert.Contains(archive.Entries, entry => entry.FullName == moduleName + ".ps1");
            Assert.Contains(archive.Entries, entry => entry.FullName == "support/helper.txt");
        }
        finally
        {
            TryDelete(root);
        }
    }
}
