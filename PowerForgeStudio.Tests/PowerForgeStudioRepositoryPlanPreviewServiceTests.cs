using PowerForge;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Orchestrator.Portfolio;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioRepositoryPlanPreviewServiceTests
{
    [Fact]
    public void ResolveProjectConfigPath_UsesSiblingConfigForNestedBuildScript()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("MixedRepo");
        var nestedBuildDirectory = scope.CreateDirectory(Path.Combine("MixedRepo", "src", "ReleaseHost", "Build"));
        var nestedScriptPath = Path.Combine(nestedBuildDirectory, "Build-Project.ps1");
        var nestedConfigPath = Path.Combine(nestedBuildDirectory, "project.build.json");

        File.WriteAllText(nestedScriptPath, "# test");
        File.WriteAllText(nestedConfigPath, "{ }");

        var resolved = RepositoryPlanPreviewService.ResolveProjectConfigPath(nestedScriptPath, repositoryRoot);

        Assert.Equal(nestedConfigPath, resolved);
    }

    [Fact]
    public void ResolveProjectConfigPath_FallsBackToRootBuildConfigWhenSiblingMissing()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("LibraryRepo");
        var rootBuildDirectory = scope.CreateDirectory(Path.Combine("LibraryRepo", "Build"));
        var nestedBuildDirectory = scope.CreateDirectory(Path.Combine("LibraryRepo", "tools", "Build"));
        var nestedScriptPath = Path.Combine(nestedBuildDirectory, "Build-Project.ps1");
        var rootConfigPath = Path.Combine(rootBuildDirectory, "project.build.json");

        File.WriteAllText(nestedScriptPath, "# test");
        File.WriteAllText(rootConfigPath, "{ }");

        var resolved = RepositoryPlanPreviewService.ResolveProjectConfigPath(nestedScriptPath, repositoryRoot);

        Assert.Equal(rootConfigPath, resolved);
    }

    [Fact]
    public async Task PopulatePlanPreviewAsync_UsesSharedProjectBuildHostServiceForProjectPlans()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("LibraryRepo");
        var buildDirectory = scope.CreateDirectory(Path.Combine("LibraryRepo", "Build"));
        var buildScriptPath = Path.Combine(buildDirectory, "Build-Project.ps1");
        var configPath = Path.Combine(buildDirectory, "project.build.json");

        File.WriteAllText(buildScriptPath, "# test");
        File.WriteAllText(
            configPath,
            """
            {
              "RootPath": ".",
              "Build": true
            }
            """);

        var projectBuildHostService = new ProjectBuildHostService(
            new NullLogger(),
            executeRelease: spec => new DotNetRepositoryReleaseResult { Success = true, ResolvedVersion = "1.0.0" },
            publishGitHub: null,
            validateGitHubPreflight: null);
        var service = new RepositoryPlanPreviewService(
            projectBuildHostService,
            new ProjectBuildCommandHostService(new ThrowingPowerShellRunner()),
            new ModuleBuildHostService(new ThrowingPowerShellRunner()));

        var item = new RepositoryPortfolioItem(
            new RepositoryCatalogEntry(
                Name: "LibraryRepo",
                RootPath: repositoryRoot,
                RepositoryKind: ReleaseRepositoryKind.Library,
                WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                ModuleBuildScriptPath: null,
                ProjectBuildScriptPath: buildScriptPath,
                IsWorktree: false,
                HasWebsiteSignals: false),
            new RepositoryGitSnapshot(true, "main", "origin/main", 0, 0, 0, 0),
            new RepositoryReadiness(RepositoryReadinessKind.Ready, "Ready"));

        var result = await service.PopulatePlanPreviewAsync([item], new PlanPreviewOptions { MaxRepositories = 1 });

        var updated = Assert.Single(result);
        var plan = Assert.Single(updated.PlanResults!);
        Assert.Equal(RepositoryPlanAdapterKind.ProjectPlan, plan.AdapterKind);
        Assert.Equal(RepositoryPlanStatus.Succeeded, plan.Status);
        Assert.NotNull(plan.PlanPath);
        Assert.True(File.Exists(plan.PlanPath!));
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
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }

    private sealed class ThrowingPowerShellRunner : IPowerShellRunner
    {
        public PowerShellRunResult Run(PowerShellRunRequest request)
            => throw new InvalidOperationException("PowerShell should not be used for project plan preview when shared host service is available.");
    }
}
