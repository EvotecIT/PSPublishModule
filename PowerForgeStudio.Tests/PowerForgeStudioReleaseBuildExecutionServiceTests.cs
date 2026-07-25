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

    [Fact]
    public async Task ExecuteAsync_UsesDiscoveredJsonModuleConfigWithoutLegacyScript()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("JsonModuleRepo");
        var buildDirectory = scope.CreateDirectory(Path.Combine("JsonModuleRepo", "Build"));
        File.WriteAllText(Path.Combine(buildDirectory, "Build-Project.ps1"), "# unified entry point");
        var moduleConfig = Path.Combine(repositoryRoot, "powerforge.json");
        var moduleRoot = scope.CreateDirectory(Path.Combine("JsonModuleRepo", "src", "JsonModuleRepo"));
        var configuredArtifactRoot = Path.Combine(moduleRoot, "out");
        var configuredArtifact = Path.Combine(configuredArtifactRoot, "package-v1.0.0");
        var staleArtifact = Directory.CreateDirectory(Path.Combine(configuredArtifactRoot, "package-v0.9.0")).FullName;
        File.WriteAllText(Path.Combine(staleArtifact, "JsonModuleRepo.zip"), "stale");
        File.SetLastWriteTimeUtc(Path.Combine(staleArtifact, "JsonModuleRepo.zip"), DateTime.UtcNow.AddDays(-1));
        File.WriteAllText(
            moduleConfig,
            """
            {
              "SchemaVersion": 1,
              "Build": { "Name": "JsonModuleRepo", "SourcePath": "src/JsonModuleRepo" },
              "Segments": [
                { "Type": "Packed", "Configuration": { "Enabled": true, "Path": "out/package-<TagModuleVersionWithPreRelease>" } }
              ]
            }
            """);
        File.WriteAllText(
            Path.Combine(buildDirectory, "release.json"),
            """
            {
              "Module": {
                "RepositoryRoot": "..",
                "ConfigPath": "powerforge.json"
              },
              "Packages": {
                "RootPath": "..",
                "Build": true
              }
            }
            """);

        PowerShellRunRequest? captured = null;
        var moduleRunner = new CapturingPowerShellRunner(request =>
        {
            captured = request;
            Directory.CreateDirectory(configuredArtifact);
            File.WriteAllText(Path.Combine(configuredArtifact, "JsonModuleRepo.zip"), "artifact");
            return new PowerShellRunResult(0, "ok", string.Empty, "pwsh");
        });
        var service = new ReleaseBuildExecutionService(
            new RepositoryCatalogScanner(),
            new ProjectBuildHostService(
                new NullLogger(),
                executeRelease: _ => new DotNetRepositoryReleaseResult { Success = true },
                publishGitHub: null,
                validateGitHubPreflight: null),
            new ProjectBuildCommandHostService(new ThrowingPowerShellRunner()),
            new ModuleBuildHostService(moduleRunner));

        var result = await service.ExecuteAsync(repositoryRoot);

        Assert.True(result.Succeeded);
        Assert.Contains(result.AdapterResults, adapter => adapter.AdapterKind == ReleaseBuildAdapterKind.ProjectBuild);
        var adapter = Assert.Single(result.AdapterResults, adapter => adapter.AdapterKind == ReleaseBuildAdapterKind.ModuleBuild);
        Assert.Equal(ReleaseBuildAdapterKind.ModuleBuild, adapter.AdapterKind);
        Assert.NotNull(captured);
        Assert.Contains($"ConfigPath = '{moduleConfig}'", captured!.CommandText!, StringComparison.Ordinal);
        Assert.DoesNotContain("$buildScriptPath =", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$moduleBuildArguments['BuildFramework'] = 'auto'", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$moduleBuildArguments['RunMode'] = 'Build'", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$moduleBuildArguments['IncludeProjectPackages'] = $false", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$moduleBuildArguments['SkipInstall'] = $true", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$moduleBuildArguments['NoSign'] = $true", captured.CommandText!, StringComparison.Ordinal);
        Assert.True(captured.PreferPwsh);
        Assert.Equal(8, captured.RequiredRuntimeMajor);
        Assert.Contains(configuredArtifact, adapter.ArtifactDirectories);
        Assert.Contains(Path.Combine(configuredArtifact, "JsonModuleRepo.zip"), adapter.ArtifactFiles);
        Assert.DoesNotContain(Path.Combine(staleArtifact, "JsonModuleRepo.zip"), adapter.ArtifactFiles);
    }

    [Fact]
    public async Task ExecuteAsync_UnifiedRelease_UsesSharedEngineAndCapturesToolArtifacts()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("UnifiedToolRepo");
        var buildDirectory = scope.CreateDirectory(Path.Combine("UnifiedToolRepo", "Build"));
        var releaseConfig = Path.Combine(buildDirectory, "release.json");
        File.WriteAllText(
            releaseConfig,
            """
            {
              "Tools": {
                "ProjectRoot": "..",
                "Targets": []
              },
              "GitHub": {
                "Publish": true,
                "Owner": "EvotecIT",
                "Repository": "UnifiedToolRepo"
              }
            }
            """);
        var toolDirectory = scope.CreateDirectory(Path.Combine("UnifiedToolRepo", "Artifacts", "Tool"));
        var zipPath = Path.Combine(toolDirectory, "UnifiedToolRepo-win-x64.zip");
        File.WriteAllText(zipPath, "zip");
        string? capturedConfig = null;
        var service = new ReleaseBuildExecutionService(
            new RepositoryCatalogScanner(),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(new ThrowingPowerShellRunner()),
            new ModuleBuildHostService(new ThrowingPowerShellRunner()),
            configPath =>
            {
                capturedConfig = configPath;
                return new PowerForgeReleaseResult {
                    Success = true,
                    ToolPlan = new PowerForgeToolReleasePlan(),
                    Tools = new PowerForgeToolReleaseResult {
                        Success = true,
                        Artefacts = [
                            new PowerForgeToolReleaseArtifactResult {
                                Target = "UnifiedToolRepo",
                                OutputPath = toolDirectory,
                                ZipPath = zipPath
                            }
                        ]
                    }
                };
            });

        var result = await service.ExecuteAsync(repositoryRoot);

        Assert.True(result.Succeeded);
        Assert.Equal(releaseConfig, capturedConfig);
        Assert.NotNull(result.UnifiedReleaseStateJson);
        var adapter = Assert.Single(result.AdapterResults);
        Assert.Equal(ReleaseBuildAdapterKind.ToolBuild, adapter.AdapterKind);
        Assert.Contains(toolDirectory, adapter.ArtifactDirectories);
        Assert.Contains(zipPath, adapter.ArtifactFiles);
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

    private sealed class CapturingPowerShellRunner(Func<PowerShellRunRequest, PowerShellRunResult> execute) : IPowerShellRunner
    {
        public PowerShellRunResult Run(PowerShellRunRequest request) => execute(request);
    }
}
