using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Orchestrator.Catalog;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioRepositoryCatalogScannerTests
{
    [Fact]
    public void InspectRepository_ForwardSlashWorktreePath_IsDetectedAsWorktree()
    {
        using var scope = new TemporaryDirectoryScope();
        var worktreePath = scope.CreateRepository("_worktrees/PSPublishModule-pr-176");

        var scanner = new RepositoryCatalogScanner();
        var entry = scanner.InspectRepository(worktreePath.Replace('\\', '/'));

        Assert.True(entry.IsWorktree);
        Assert.Equal(ReleaseWorkspaceKind.Worktree, entry.WorkspaceKind);
    }

    [Fact]
    public void InspectRepository_UnifiedReleaseModuleConfig_IsDetectedWithoutLegacyScript()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryPath = scope.CreateRepository("JsonModuleRepo");
        var buildPath = Path.Combine(repositoryPath, "Build");
        var legacyScript = Path.Combine(buildPath, "Build-Module.ps1");
        File.Delete(legacyScript);
        File.WriteAllText(Path.Combine(buildPath, "Build-Project.ps1"), "# unified entry point");
        var moduleConfig = Path.Combine(repositoryPath, "powerforge.json");
        Directory.CreateDirectory(Path.Combine(repositoryPath, "src", "JsonModuleRepo"));
        File.WriteAllText(moduleConfig, """{ "SchemaVersion": 1, "Build": { "Name": "JsonModuleRepo", "SourcePath": "src/JsonModuleRepo" } }""");
        File.WriteAllText(
            Path.Combine(buildPath, "release.json"),
            """
            {
              "Module": {
                "RepositoryRoot": "..",
                "ConfigPath": "powerforge.json"
              }
            }
            """);

        var entry = new RepositoryCatalogScanner().InspectRepository(repositoryPath);

        Assert.Equal(ReleaseRepositoryKind.Module, entry.RepositoryKind);
        Assert.Equal(moduleConfig, entry.ModuleBuildScriptPath);
        Assert.Null(entry.ProjectBuildScriptPath);
        Assert.True(entry.IsReleaseManaged);
    }

    [Fact]
    public void InspectRepository_UnifiedReleaseWithPackages_PreservesBothBuildContracts()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryPath = scope.CreateRepository("UnifiedRepo");
        var buildPath = Path.Combine(repositoryPath, "Build");
        File.Delete(Path.Combine(buildPath, "Build-Module.ps1"));
        var moduleRoot = Directory.CreateDirectory(Path.Combine(repositoryPath, "src", "UnifiedRepo")).FullName;
        var moduleConfig = Path.Combine(repositoryPath, "powerforge.json");
        File.WriteAllText(
            moduleConfig,
            $$"""{ "SchemaVersion": 1, "Build": { "Name": "UnifiedRepo", "SourcePath": "{{moduleRoot.Replace("\\", "\\\\")}}" } }""");
        var releaseConfig = Path.Combine(buildPath, "release.json");
        File.WriteAllText(
            releaseConfig,
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

        var entry = new RepositoryCatalogScanner().InspectRepository(repositoryPath);

        Assert.Equal(ReleaseRepositoryKind.Mixed, entry.RepositoryKind);
        Assert.Equal(moduleConfig, entry.ModuleBuildScriptPath);
        Assert.Equal(releaseConfig, entry.ProjectBuildScriptPath);
        Assert.Equal(
            releaseConfig,
            PowerForgeStudio.Orchestrator.Portfolio.RepositoryPlanPreviewService.ResolveProjectConfigPath(
                entry.ProjectBuildScriptPath!,
                repositoryPath));
        Assert.Null(entry.UnifiedReleaseConfigPath);
    }

    [Fact]
    public void InspectRepository_ToolsAndTopLevelGitHub_RetainsUnifiedReleaseContract()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryPath = scope.CreateRepository("UnifiedToolRepo");
        var buildPath = Path.Combine(repositoryPath, "Build");
        File.Delete(Path.Combine(buildPath, "Build-Module.ps1"));
        var releaseConfig = Path.Combine(buildPath, "release.json");
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

        var entry = new RepositoryCatalogScanner().InspectRepository(repositoryPath);

        Assert.True(entry.IsReleaseManaged);
        Assert.Equal(releaseConfig, entry.UnifiedReleaseConfigPath);
        Assert.Equal(releaseConfig, entry.PrimaryBuildScriptPath);
    }

    [Fact]
    public void InspectRepository_WorkspaceValidation_RetainsUnifiedReleaseContract()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryPath = scope.CreateRepository("ValidatedRepo");
        var buildPath = Path.Combine(repositoryPath, "Build");
        File.Delete(Path.Combine(buildPath, "Build-Module.ps1"));
        var releaseConfig = Path.Combine(buildPath, "release.json");
        File.WriteAllText(
            releaseConfig,
            """
            {
              "Packages": {
                "RootPath": "..",
                "Build": true
              },
              "WorkspaceValidation": {
                "ConfigPath": "workspace.validation.json"
              }
            }
            """);

        var entry = new RepositoryCatalogScanner().InspectRepository(repositoryPath);

        Assert.True(entry.IsReleaseManaged);
        Assert.Equal(releaseConfig, entry.UnifiedReleaseConfigPath);
        Assert.Equal(releaseConfig, entry.PrimaryBuildScriptPath);
    }

    [Fact]
    public void InspectRepository_UnifiedContract_PrefersDeclaredJsonModuleOverLegacyScript()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryPath = scope.CreateRepository("UnifiedModuleRepo");
        var buildPath = Path.Combine(repositoryPath, "Build");
        var moduleConfig = Path.Combine(repositoryPath, "powerforge.json");
        Directory.CreateDirectory(Path.Combine(repositoryPath, "src", "UnifiedModuleRepo"));
        File.WriteAllText(moduleConfig, """{ "Build": { "Name": "UnifiedModuleRepo", "SourcePath": "src/UnifiedModuleRepo" } }""");
        var releaseConfig = Path.Combine(buildPath, "release.json");
        File.WriteAllText(
            releaseConfig,
            """
            {
              "Module": {
                "RepositoryRoot": "..",
                "ConfigPath": "powerforge.json"
              },
              "Tools": {
                "ProjectRoot": "..",
                "Targets": []
              }
            }
            """);

        var entry = new RepositoryCatalogScanner().InspectRepository(repositoryPath);

        Assert.Equal(releaseConfig, entry.UnifiedReleaseConfigPath);
        Assert.Equal(moduleConfig, entry.ModuleBuildScriptPath);
    }

    [Fact]
    public void InspectRepository_Winget_RetainsUnifiedReleaseContract()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryPath = scope.CreateRepository("WingetRepo");
        var buildPath = Path.Combine(repositoryPath, "Build");
        File.Delete(Path.Combine(buildPath, "Build-Module.ps1"));
        var releaseConfig = Path.Combine(buildPath, "release.json");
        File.WriteAllText(
            releaseConfig,
            """
            {
              "Winget": {
                "Enabled": true,
                "Submit": true,
                "Packages": []
              }
            }
            """);

        var entry = new RepositoryCatalogScanner().InspectRepository(repositoryPath);

        Assert.True(entry.IsReleaseManaged);
        Assert.Equal(releaseConfig, entry.UnifiedReleaseConfigPath);
        Assert.Equal(releaseConfig, entry.PrimaryBuildScriptPath);
    }

    [Fact]
    public void InspectRepository_ModuleOwnedPackages_SuppressesOuterPackageContract()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryPath = scope.CreateRepository("ModuleOwnedPackagesRepo");
        var buildPath = Path.Combine(repositoryPath, "Build");
        File.Delete(Path.Combine(buildPath, "Build-Module.ps1"));
        Directory.CreateDirectory(Path.Combine(repositoryPath, "src", "ModuleOwnedPackagesRepo"));
        var moduleConfig = Path.Combine(repositoryPath, "powerforge.json");
        File.WriteAllText(
            moduleConfig,
            """{ "Build": { "Name": "ModuleOwnedPackagesRepo", "SourcePath": "src/ModuleOwnedPackagesRepo" } }""");
        File.WriteAllText(
            Path.Combine(buildPath, "release.json"),
            """
            {
              "Module": {
                "RepositoryRoot": "..",
                "ConfigPath": "powerforge.json",
                "IncludesPackages": true
              },
              "Packages": {
                "RootPath": "..",
                "Build": true
              }
            }
            """);

        var entry = new RepositoryCatalogScanner().InspectRepository(repositoryPath);

        Assert.Equal(ReleaseRepositoryKind.Module, entry.RepositoryKind);
        Assert.Equal(moduleConfig, entry.ModuleBuildScriptPath);
        Assert.Null(entry.ProjectBuildScriptPath);
    }

    [Fact]
    public void InspectRepository_InvalidModuleConfig_IsNotDetected()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryPath = scope.CreateRepository("InvalidJsonModuleRepo");
        var buildPath = Path.Combine(repositoryPath, "Build");
        File.Delete(Path.Combine(buildPath, "Build-Module.ps1"));
        File.WriteAllText(Path.Combine(repositoryPath, "powerforge.json"), """{ "SchemaVersion": 1, "Build": { "Name": "MissingSource" } }""");

        var entry = new RepositoryCatalogScanner().InspectRepository(repositoryPath);

        Assert.Null(entry.ModuleBuildScriptPath);
    }

    [Fact]
    public void Scan_InvalidReleaseModuleMetadata_DoesNotAbortOtherRepositories()
    {
        using var scope = new TemporaryDirectoryScope();
        var invalidRepository = scope.CreateRepository("InvalidReleaseRepo");
        File.Delete(Path.Combine(invalidRepository, "Build", "Build-Module.ps1"));
        File.WriteAllText(
            Path.Combine(invalidRepository, "Build", "release.json"),
            """{ "Module": { "RepositoryRoot": 42, "ConfigPath": { "file": "powerforge.json" } } }""");
        var validRepository = scope.CreateRepository("ValidModuleRepo");

        var entries = new RepositoryCatalogScanner().Scan(scope.RootPath);

        Assert.Equal(2, entries.Count);
        Assert.Null(entries.Single(entry => entry.Name == "InvalidReleaseRepo").ModuleBuildScriptPath);
        Assert.NotNull(entries.Single(entry => entry.Name == "ValidModuleRepo").ModuleBuildScriptPath);
    }

    [Fact]
    public void Scan_NullArtifactConfiguration_DoesNotAbortOtherRepositories()
    {
        using var scope = new TemporaryDirectoryScope();
        var invalidRepository = scope.CreateRepository("InvalidArtifactRepo");
        File.Delete(Path.Combine(invalidRepository, "Build", "Build-Module.ps1"));
        File.WriteAllText(
            Path.Combine(invalidRepository, "powerforge.json"),
            """
            {
              "Build": { "Name": "InvalidArtifactRepo", "SourcePath": "." },
              "Segments": [
                { "Type": "Packed", "Configuration": null }
              ]
            }
            """);
        var validRepository = scope.CreateRepository("ValidModuleRepo");

        var entries = new RepositoryCatalogScanner().Scan(scope.RootPath);

        Assert.Equal(2, entries.Count);
        Assert.Null(entries.Single(entry => entry.Name == "InvalidArtifactRepo").ModuleBuildScriptPath);
        Assert.NotNull(entries.Single(entry => entry.Name == "ValidModuleRepo").ModuleBuildScriptPath);
    }

    private sealed class TemporaryDirectoryScope : IDisposable
    {
        public TemporaryDirectoryScope()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "PowerForgeStudioTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public string CreateRepository(string relativePath)
        {
            var path = Path.Combine(RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.Combine(path, "Build"));
            File.WriteAllText(Path.Combine(path, "Build", "Build-Module.ps1"), "# test");
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
