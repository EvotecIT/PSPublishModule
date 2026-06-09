using PowerForge;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Portfolio;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioReleaseBuildExecutionServiceTests
{
    [Fact]
    public async Task ExecuteAsync_UsesSharedProjectBuildHostServiceForProjectBuilds()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("LibraryRepo");
        var buildDirectory = scope.CreateDirectory(Path.Combine("LibraryRepo", "Build"));
        var buildScriptPath = Path.Combine(buildDirectory, "Build-Project.ps1");
        var configPath = Path.Combine(buildDirectory, "project.build.json");
        var outputDirectory = Path.Combine(repositoryRoot, "artifacts", "packages");

        File.WriteAllText(buildScriptPath, "# test");
        File.WriteAllText(
            configPath,
            """
            {
              "RootPath": "..",
              "OutputPath": "artifacts/packages",
              "Build": true
            }
            """);

        var callIndex = 0;
        var projectBuildHostService = new ProjectBuildHostService(
            new NullLogger(),
            executeRelease: spec =>
            {
                callIndex++;
                if (callIndex == 1)
                {
                    Assert.True(spec.WhatIf);
                    return new DotNetRepositoryReleaseResult { Success = true };
                }

                Assert.False(spec.WhatIf);
                Directory.CreateDirectory(outputDirectory);
                var packagePath = Path.Combine(outputDirectory, "LibraryRepo.1.0.0.nupkg");
                File.WriteAllText(packagePath, "pkg");
                return new DotNetRepositoryReleaseResult {
                    Success = true,
                    Projects = {
                        new DotNetRepositoryProjectResult {
                            ProjectName = "LibraryRepo",
                            IsPackable = true,
                            NewVersion = "1.0.0",
                            Packages = { packagePath }
                        }
                    }
                };
            },
            publishGitHub: null,
            validateGitHubPreflight: null);
        var service = new ReleaseBuildExecutionService(
            new RepositoryCatalogScanner(),
            projectBuildHostService,
            new ProjectBuildCommandHostService(new ThrowingPowerShellRunner()),
            new ModuleBuildHostService(new ThrowingPowerShellRunner()));

        var result = await service.ExecuteAsync(repositoryRoot);

        Assert.True(result.Succeeded);
        Assert.Equal(2, callIndex);
        var adapter = Assert.Single(result.AdapterResults);
        Assert.Equal(ReleaseBuildAdapterKind.ProjectBuild, adapter.AdapterKind);
        Assert.Contains(outputDirectory, adapter.ArtifactDirectories);
        Assert.Contains(adapter.ArtifactFiles, path => path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class TemporaryDirectoryScope : IDisposable
    {
        public TemporaryDirectoryScope()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "PowerForgeStudioTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public string CreateDirectory(string relativePath)
        {
            var path = Path.Combine(RootPath, relativePath);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(RootPath, recursive: true); } catch { }
        }
    }

    private sealed class ThrowingPowerShellRunner : IPowerShellRunner
    {
        public PowerShellRunResult Run(PowerShellRunRequest request)
            => throw new InvalidOperationException("PowerShell should not be used for project builds when shared host service is available.");
    }
}
