using PowerForge;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleaseBuildExecutionServiceTests
{
    [Fact]
    public async Task ExecuteAsync_checkpoints_complete_script_exported_module_configuration()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("ScriptModuleRepo");
        var buildDirectory = scope.CreateDirectory(Path.Combine("ScriptModuleRepo", "Build"));
        var scriptPath = Path.Combine(repositoryRoot, "Build-Module.ps1");
        File.WriteAllText(scriptPath, "# module recipe");
        var releaseConfig = Path.Combine(buildDirectory, "release.json");
        File.WriteAllText(
            releaseConfig,
            """{ "Module": { "RepositoryRoot": "..", "ScriptPath": "Build-Module.ps1" } }""");
        string? expectedFingerprint = null;
        var moduleHost = new ModuleBuildHostService(new CapturingPowerShellRunner(request =>
        {
            var marker = "$targetJson = '";
            var start = request.CommandText?.IndexOf(marker, StringComparison.Ordinal) ?? -1;
            if (start < 0)
                return new PowerShellRunResult(1, string.Empty, "Export path missing.", "pwsh");
            start += marker.Length;
            var end = request.CommandText!.IndexOf('\'', start);
            var outputPath = request.CommandText[start..end];
            File.WriteAllText(
                outputPath,
                """
                {
                  "Build": {
                    "Name": "Sample",
                    "SourcePath": "."
                  },
                  "Segments": [
                    {
                      "Type": "Packed",
                      "ArtefactType": "Packed",
                      "Configuration": {
                        "Enabled": true,
                        "Path": "Artifacts/Packed",
                        "ID": "ToGitHub"
                      }
                    },
                    {
                      "Type": "GalleryNuget",
                      "Configuration": {
                        "Destination": "PowerShellGallery",
                        "Enabled": true,
                        "RepositoryName": "PSGallery"
                      }
                    }
                  ]
                }
                """);
            expectedFingerprint =
                UnifiedReleaseConfigFingerprint.ComputeModuleConfig(outputPath);
            return new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh");
        }));
        var service = new ReleaseBuildExecutionService(
            new RepositoryCatalogScanner(),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(new ThrowingPowerShellRunner()),
            moduleHost,
            (_, _) => new PowerForgeReleaseResult {
                Success = true,
                ConfigPath = releaseConfig,
                ModulePlan = new PowerForgeModuleReleasePlanSummary {
                    ModuleName = "Sample",
                    ScriptPath = scriptPath
                },
                Module = new ModuleBuildHostExecutionResult { ExitCode = 0 }
            });

        var result = await service.ExecuteAsync(repositoryRoot);

        Assert.True(result.Succeeded);
        Assert.Equal(expectedFingerprint, result.ModuleExportedConfigSha256);
    }

    [Fact]
    public async Task ExecuteAsync_checkpoints_only_planned_top_level_packages_for_signing()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("PackageCheckpointRepo");
        var buildDirectory = scope.CreateDirectory(Path.Combine("PackageCheckpointRepo", "Build"));
        var artifactDirectory = scope.CreateDirectory(
            Path.Combine("PackageCheckpointRepo", "Artifacts"));
        var releaseConfig = Path.Combine(buildDirectory, "release.json");
        File.WriteAllText(
            releaseConfig,
            """{ "Packages": { "RootPath": "..", "Build": true } }""");
        var currentPackage = Path.Combine(artifactDirectory, "Sample.1.0.0.nupkg");
        var stalePackage = Path.Combine(artifactDirectory, "Sample.0.9.0.nupkg");
        File.WriteAllText(currentPackage, "current");
        File.WriteAllText(stalePackage, "stale");
        var service = new ReleaseBuildExecutionService(
            new RepositoryCatalogScanner(),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(new ThrowingPowerShellRunner()),
            new ModuleBuildHostService(new ThrowingPowerShellRunner()),
            (_, _) => new PowerForgeReleaseResult {
                Success = true,
                ConfigPath = releaseConfig,
                Packages = new ProjectBuildHostExecutionResult {
                    Success = true,
                    RootPath = repositoryRoot,
                    OutputPath = artifactDirectory,
                    Result = new ProjectBuildResult {
                        Success = true,
                        Release = new DotNetRepositoryReleaseResult {
                            Success = true,
                            Projects = {
                                new DotNetRepositoryProjectResult {
                                    ProjectName = "Sample",
                                    PackageId = "Sample",
                                    NewVersion = "1.0.0",
                                    Packages = { currentPackage }
                                }
                            }
                        }
                    }
                }
            });

        var result = await service.ExecuteAsync(repositoryRoot);

        Assert.True(result.Succeeded);
        var adapter = Assert.Single(result.AdapterResults);
        Assert.Contains(currentPackage, adapter.ArtifactFiles);
        Assert.DoesNotContain(stalePackage, adapter.ArtifactFiles);
    }

    [Fact]
    public async Task ExecuteAsync_package_only_release_uses_unified_checkpoint_path()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("PackageOnlyRepo");
        var buildDirectory = scope.CreateDirectory(Path.Combine("PackageOnlyRepo", "Build"));
        var releaseConfig = Path.Combine(buildDirectory, "release.json");
        File.WriteAllText(
            releaseConfig,
            """{ "Packages": { "RootPath": "..", "Build": true } }""");
        var unifiedCalls = 0;
        var service = new ReleaseBuildExecutionService(
            new RepositoryCatalogScanner(),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(new ThrowingPowerShellRunner()),
            new ModuleBuildHostService(new ThrowingPowerShellRunner()),
            (configPath, _) =>
            {
                unifiedCalls++;
                Assert.Equal(releaseConfig, configPath);
                return new PowerForgeReleaseResult {
                    Success = true,
                    ConfigPath = configPath,
                    Packages = new ProjectBuildHostExecutionResult {
                        Success = true,
                        ConfigPath = configPath,
                        RootPath = repositoryRoot
                    }
                };
            });

        var result = await service.ExecuteAsync(repositoryRoot);

        Assert.True(result.Succeeded);
        Assert.Equal(1, unifiedCalls);
        Assert.False(string.IsNullOrWhiteSpace(result.UnifiedReleaseStateJson));
        Assert.False(string.IsNullOrWhiteSpace(result.UnifiedReleaseConfigSha256));
        Assert.Equal(ReleaseBuildAdapterKind.ProjectBuild, Assert.Single(result.AdapterResults).AdapterKind);
    }

    [Fact]
    public async Task ExecuteAsync_rejects_release_contract_drift_during_the_build()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("DriftDuringBuildRepo");
        var buildDirectory = scope.CreateDirectory(Path.Combine("DriftDuringBuildRepo", "Build"));
        var releaseConfig = Path.Combine(buildDirectory, "release.json");
        File.WriteAllText(releaseConfig, """{ "GitHub": { "Publish": false, "Repository": "Original" } }""");
        var service = new ReleaseBuildExecutionService(
            new RepositoryCatalogScanner(),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(new ThrowingPowerShellRunner()),
            new ModuleBuildHostService(new ThrowingPowerShellRunner()),
            (_, _) =>
            {
                File.WriteAllText(releaseConfig, """{ "GitHub": { "Publish": false, "Repository": "Changed" } }""");
                return new PowerForgeReleaseResult { Success = true, ConfigPath = releaseConfig };
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExecuteAsync(repositoryRoot));

        Assert.Contains("changed while the build was running", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_captures_module_owned_package_outputs_in_the_build_checkpoint()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("ModulePackagesRepo");
        var buildDirectory = scope.CreateDirectory(Path.Combine("ModulePackagesRepo", "Build"));
        var packageDirectory = scope.CreateDirectory(Path.Combine("ModulePackagesRepo", "PackageArtifacts"));
        var moduleConfig = Path.Combine(repositoryRoot, "powerforge.json");
        var releaseConfig = Path.Combine(buildDirectory, "release.json");
        var packagePath = Path.Combine(packageDirectory, "Sample.Library.1.0.0.nupkg");
        var zipPath = Path.Combine(packageDirectory, "Sample.Library.1.0.0.zip");
        File.WriteAllText(packagePath, "package");
        File.WriteAllText(zipPath, "zip");
        File.WriteAllText(
            moduleConfig,
            """
            {
              "Build": { "Name": "Sample", "SourcePath": "." },
              "Segments": [
                {
                  "Type": "PackageBuild",
                  "Configuration": {
                    "Name": "Sample packages",
                    "StagingPath": "PackageArtifacts",
                    "Build": true,
                    "PublishNuget": true
                  }
                }
              ]
            }
            """);
        File.WriteAllText(
            releaseConfig,
            """{ "Module": { "RepositoryRoot": "..", "ConfigPath": "powerforge.json", "IncludesPackages": true } }""");
        var service = new ReleaseBuildExecutionService(
            new RepositoryCatalogScanner(),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(new ThrowingPowerShellRunner()),
            new ModuleBuildHostService(new ThrowingPowerShellRunner()),
            (_, _) => new PowerForgeReleaseResult {
                Success = true,
                ConfigPath = releaseConfig,
                ModulePlan = new PowerForgeModuleReleasePlanSummary {
                    ConfigPath = moduleConfig,
                    IncludesPackages = true
                },
                Module = new ModuleBuildHostExecutionResult {
                    ExitCode = 0,
                    Executable = "pwsh"
                }
            });

        var result = await service.ExecuteAsync(repositoryRoot);

        Assert.True(result.Succeeded);
        var adapter = Assert.Single(result.AdapterResults);
        Assert.Equal(ReleaseBuildAdapterKind.ModuleBuild, adapter.AdapterKind);
        Assert.Contains(packageDirectory, adapter.ArtifactDirectories);
        Assert.Contains(packagePath, adapter.ArtifactFiles);
        Assert.Contains(zipPath, adapter.ArtifactFiles);
    }
}
