using System.Text.Json;
using PowerForge;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleaseBuildExecutionServiceTests
{
    [Fact]
    public async Task ExecuteAsync_CheckpointsUnifiedReleaseMetadataOutsideArtifactDirectories()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("ReleaseMetadataRepo");
        var buildDirectory = scope.CreateDirectory(Path.Combine("ReleaseMetadataRepo", "Build"));
        var metadataDirectory = scope.CreateDirectory(Path.Combine("ReleaseMetadataRepo", "Metadata"));
        var manifestPath = Path.Combine(metadataDirectory, "release.json");
        var checksumsPath = Path.Combine(metadataDirectory, "SHA256SUMS.txt");
        File.WriteAllText(manifestPath, "{}");
        File.WriteAllText(checksumsPath, "checksum");
        File.WriteAllText(Path.Combine(buildDirectory, "workspace.json"), "{}");
        var releaseConfig = Path.Combine(buildDirectory, "release.json");
        File.WriteAllText(
            releaseConfig,
            """{ "Outputs": {}, "WorkspaceValidation": { "ConfigPath": "workspace.json" } }""");
        var service = new ReleaseBuildExecutionService(
            new RepositoryCatalogScanner(),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(new ThrowingPowerShellRunner()),
            new ModuleBuildHostService(new ThrowingPowerShellRunner()),
            (configPath, _) => new PowerForgeReleaseResult
            {
                Success = true,
                ConfigPath = configPath,
                ReleaseManifestPath = manifestPath,
                ReleaseChecksumsPath = checksumsPath
            });

        var result = await service.ExecuteAsync(repositoryRoot);

        Assert.True(result.Succeeded);
        var adapter = Assert.Single(result.AdapterResults);
        Assert.Contains(manifestPath, adapter.ArtifactFiles);
        Assert.Contains(checksumsPath, adapter.ArtifactFiles);
    }

    [Fact]
    public async Task ExecuteAsync_DirectJsonModuleCheckpointsPackageLanesAndArtifacts()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("DirectJsonPackagesRepo");
        var packageDirectory = scope.CreateDirectory(
            Path.Combine("DirectJsonPackagesRepo", "PackageArtifacts"));
        var packagePath = Path.Combine(packageDirectory, "Direct.Library.1.0.0.nupkg");
        var zipPath = Path.Combine(packageDirectory, "Direct.Library.1.0.0.zip");
        File.WriteAllText(packagePath, "package");
        File.WriteAllText(zipPath, "zip");
        File.WriteAllText(
            Path.Combine(repositoryRoot, "Direct.Library.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Version>1.0.0</Version>
                <PackageId>Direct.Library</PackageId>
                <IsPackable>true</IsPackable>
              </PropertyGroup>
            </Project>
            """);
        var moduleConfig = Path.Combine(repositoryRoot, "powerforge.json");
        File.WriteAllText(
            moduleConfig,
            """
            {
              "Build": { "Name": "DirectJsonPackagesRepo", "SourcePath": "." },
              "Segments": [
                {
                  "Type": "PackageBuild",
                  "Configuration": {
                    "Name": "Direct packages",
                    "StagingPath": "PackageArtifacts",
                    "Build": true,
                    "PublishNuget": true
                  }
                }
              ]
            }
            """);
        var service = new ReleaseBuildExecutionService(
            new RepositoryCatalogScanner(),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(new ThrowingPowerShellRunner()),
            new ModuleBuildHostService(new CapturingPowerShellRunner(
                _ => new PowerShellRunResult(0, "ok", string.Empty, "pwsh"))));

        var result = await service.ExecuteAsync(repositoryRoot);

        Assert.True(result.Succeeded);
        var adapter = Assert.Single(result.AdapterResults);
        Assert.Contains(packageDirectory, adapter.ArtifactDirectories);
        Assert.Contains(packagePath, adapter.ArtifactFiles);
        Assert.Contains(zipPath, adapter.ArtifactFiles);
        var checkpoint = JsonSerializer.Deserialize<PowerForgeReleaseResult>(
            result.UnifiedReleaseStateJson!);
        var packagePlan = Assert.Single(checkpoint!.ModulePackagePlans);
        Assert.Equal("PackageBuild:0", packagePlan.Key);
        Assert.Equal("Direct packages", packagePlan.Name);
    }
}
