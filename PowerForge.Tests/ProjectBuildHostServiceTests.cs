using PowerForge;

namespace PowerForge.Tests;

public sealed class ProjectBuildHostServiceTests
{
    [Fact]
    public void Execute_WritesPlanAndUsesRequestedActionOverrides()
    {
        using var scope = new TemporaryDirectoryScope();
        var configDirectory = scope.CreateDirectory("Repo");
        var configPath = Path.Combine(configDirectory, "project.build.json");
        var planPath = Path.Combine(configDirectory, "artifacts", "plan.json");
        File.WriteAllText(
            configPath,
            """
            {
              "RootPath": ".",
              "Build": true,
              "PublishNuget": true,
              "PublishGitHub": true
            }
            """);

        DotNetRepositoryReleaseSpec? captured = null;
        var service = new ProjectBuildHostService(
            new NullLogger(),
            executeRelease: spec =>
            {
                captured = spec;
                return new DotNetRepositoryReleaseResult { Success = true, ResolvedVersion = "1.2.3" };
            },
            publishGitHub: null,
            validateGitHubPreflight: null);

        var result = service.Execute(new ProjectBuildHostRequest {
            ConfigPath = configPath,
            PlanOutputPath = planPath,
            ExecuteBuild = false,
            PlanOnly = true,
            UpdateVersions = false,
            Build = false,
            PublishNuget = false,
            PublishGitHub = false
        });

        Assert.True(result.Success);
        Assert.NotNull(captured);
        Assert.True(captured!.WhatIf);
        Assert.False(captured.Pack);
        Assert.False(captured.Publish);
        Assert.Equal(planPath, result.PlanOutputPath);
        Assert.True(File.Exists(planPath));
    }

    [Fact]
    public void Execute_RunsPlanThenBuildAndReturnsResolvedPaths()
    {
        using var scope = new TemporaryDirectoryScope();
        var configDirectory = scope.CreateDirectory("Repo");
        var outputDirectory = Path.Combine(configDirectory, "artifacts", "packages");
        var configPath = Path.Combine(configDirectory, "project.build.json");
        File.WriteAllText(
            configPath,
            """
            {
              "RootPath": ".",
              "OutputPath": "artifacts/packages",
              "Build": true,
              "PublishNuget": false,
              "PublishGitHub": false
            }
            """);

        var callIndex = 0;
        var service = new ProjectBuildHostService(
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
                var packagePath = Path.Combine(outputDirectory, "Example.1.0.0.nupkg");
                File.WriteAllText(packagePath, "pkg");
                return new DotNetRepositoryReleaseResult {
                    Success = true,
                    Projects = {
                        new DotNetRepositoryProjectResult {
                            ProjectName = "Example",
                            IsPackable = true,
                            NewVersion = "1.0.0",
                            Packages = { packagePath }
                        }
                    }
                };
            },
            publishGitHub: null,
            validateGitHubPreflight: null);

        var result = service.Execute(new ProjectBuildHostRequest {
            ConfigPath = configPath,
            ExecuteBuild = true,
            PlanOnly = false,
            UpdateVersions = false,
            Build = true,
            PublishNuget = false,
            PublishGitHub = false
        });

        Assert.Equal(2, callIndex);
        Assert.True(result.Success);
        Assert.Equal(configDirectory, result.RootPath);
        Assert.Equal(outputDirectory, result.OutputPath);
        Assert.Single(result.Result.Release!.Projects);
        Assert.Single(result.Result.Release.Projects[0].Packages);
    }

    private sealed class TemporaryDirectoryScope : IDisposable
    {
        public TemporaryDirectoryScope()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
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
}
