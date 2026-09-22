using PowerForge;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Orchestrator.Host;
using PowerForgeStudio.Orchestrator.Portfolio;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioRepositoryPlanPreviewServiceTests
{
    [Fact]
    public async Task PopulatePlanPreviewAsync_InvalidJsonBuildContract_FailsPreview()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("InvalidModuleRepo");
        var moduleConfig = Path.Combine(repositoryRoot, "powerforge.json");
        File.WriteAllText(moduleConfig, """{ "Build": { "Name": "MissingSource" } }""");
        var service = new RepositoryPlanPreviewService(
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(new ThrowingPowerShellRunner()),
            new ModuleBuildHostService(new ThrowingPowerShellRunner()));
        var item = new RepositoryPortfolioItem(
            new RepositoryCatalogEntry(
                Name: "InvalidModuleRepo",
                RootPath: repositoryRoot,
                RepositoryKind: ReleaseRepositoryKind.Module,
                WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
                ModuleBuildScriptPath: moduleConfig,
                ProjectBuildScriptPath: null,
                IsWorktree: false,
                HasWebsiteSignals: false),
            new RepositoryGitSnapshot(true, "main", "origin/main", 0, 0, 0, 0),
            new RepositoryReadiness(RepositoryReadinessKind.Ready, "Ready"));

        var result = await service.PopulatePlanPreviewAsync([item], new PlanPreviewOptions { MaxRepositories = 1 });

        var preview = Assert.Single(Assert.Single(result).PlanResults!);
        Assert.Equal(RepositoryPlanStatus.Failed, preview.Status);
        Assert.Contains("Build.SourcePath", preview.ErrorTail, StringComparison.Ordinal);
    }

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
            executeRelease: spec => new DotNetRepositoryReleaseResult
            {
                Success = true,
                ResolvedVersion = "1.0.0",
                Projects =
                [
                    new DotNetRepositoryProjectResult
                    {
                        ProjectName = "LibraryRepo",
                        PackageId = "LibraryRepo",
                        IsPackable = true,
                        NewVersion = "1.0.0",
                        Packages = [Path.Combine(repositoryRoot, "Artifacts", "LibraryRepo.1.0.0.nupkg")]
                    }
                ]
            },
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
        Assert.Contains(plan.Actions, action => action.Action == "Build and pack project" && action.Target == "LibraryRepo");
        Assert.Contains(plan.Actions, action => action.Action == "Create NuGet package");
    }

    [Fact]
    public async Task PopulatePlanPreviewAsync_NegativeLimit_TreatsWorkspaceAsUnlimited()
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
            executeRelease: spec => new DotNetRepositoryReleaseResult
            {
                Success = true,
                ResolvedVersion = "1.0.0",
                Projects =
                [
                    new DotNetRepositoryProjectResult
                    {
                        ProjectName = "LibraryRepo",
                        PackageId = "LibraryRepo",
                        IsPackable = true,
                        NewVersion = "1.0.0"
                    }
                ]
            },
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

        var result = await service.PopulatePlanPreviewAsync([item], new PlanPreviewOptions { MaxRepositories = -1 });

        var updated = Assert.Single(result);
        var plan = Assert.Single(updated.PlanResults!);
        Assert.Equal(RepositoryPlanStatus.Succeeded, plan.Status);
    }

    [Fact]
    public async Task PopulatePlanPreviewAsync_UnifiedContract_UsesSharedReleasePlan()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("UnifiedRepo");
        var buildDirectory = scope.CreateDirectory(Path.Combine("UnifiedRepo", "Build"));
        var releaseConfig = Path.Combine(buildDirectory, "release.json");
        File.WriteAllText(releaseConfig, """{ "Tools": { "Targets": [] }, "GitHub": { "Publish": true } }""");
        string? capturedConfig = null;
        PowerForgeReleaseRequest? capturedRequest = null;
        var service = new RepositoryPlanPreviewService(
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(new ThrowingPowerShellRunner()),
            new ModuleBuildHostService(new ThrowingPowerShellRunner()),
            (configPath, request) =>
            {
                capturedConfig = configPath;
                capturedRequest = request;
                return new PowerForgeReleaseResult { Success = true };
            });
        var item = new RepositoryPortfolioItem(
            new RepositoryCatalogEntry(
                "UnifiedRepo",
                repositoryRoot,
                ReleaseRepositoryKind.Library,
                ReleaseWorkspaceKind.PrimaryRepository,
                null,
                null,
                false,
                false,
                releaseConfig),
            new RepositoryGitSnapshot(true, "main", "origin/main", 0, 0, 0, 0),
            new RepositoryReadiness(RepositoryReadinessKind.Ready, "Ready"));

        var result = await service.PopulatePlanPreviewAsync([item], new PlanPreviewOptions { MaxRepositories = 1 });

        Assert.Equal(releaseConfig, capturedConfig);
        Assert.NotNull(capturedRequest);
        Assert.True(capturedRequest!.PlanOnly);
        Assert.False(capturedRequest.PublishNuget);
        Assert.False(capturedRequest.PublishProjectGitHub);
        Assert.False(capturedRequest.PublishToolGitHub);
        Assert.Equal(ConfigurationGateMode.Build, capturedRequest.ModuleRunMode);
        Assert.True(capturedRequest.ModuleNoSign);
        Assert.True(capturedRequest.ModuleSkipInstall);
        Assert.False(capturedRequest.SubmitWinget);
        var preview = Assert.Single(Assert.Single(result).PlanResults!);
        Assert.Equal(RepositoryPlanAdapterKind.UnifiedRelease, preview.AdapterKind);
        Assert.Equal(RepositoryPlanStatus.Succeeded, preview.Status);
        Assert.Equal(releaseConfig, preview.PlanPath);
    }

    [Fact]
    public async Task PlanRepositoryAsync_LegacyProjectAdapterRejectsEmptyPlanJson()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("LegacyProjectRepo");
        var buildScript = Path.Combine(repositoryRoot, "Build-Project.ps1");
        File.WriteAllText(buildScript, "# test");
        var service = new RepositoryPlanPreviewService(
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(new EmptyPlanPowerShellRunner()),
            new ModuleBuildHostService(new ThrowingPowerShellRunner()));
        var repository = new RepositoryCatalogEntry(
            "LegacyProjectRepo",
            repositoryRoot,
            ReleaseRepositoryKind.Library,
            ReleaseWorkspaceKind.PrimaryRepository,
            null,
            buildScript,
            false,
            false);

        var result = Assert.Single(await service.PlanRepositoryAsync(repository));

        Assert.Equal(RepositoryPlanStatus.Failed, result.Status);
        Assert.Null(result.PlanPath);
        Assert.Contains("did not contain any buildable projects", result.ErrorTail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlanRepositoryAsync_RejectsPlanWhosePreflightFailedDespiteZeroExitCode()
    {
        using var scope = new TemporaryDirectoryScope();
        var name = "FailedScriptPlan" + Guid.NewGuid().ToString("N")[..8];
        var repositoryRoot = scope.CreateDirectory(name);
        var buildScript = Path.Combine(repositoryRoot, "Build-Project.ps1");
        File.WriteAllText(buildScript, "# test");
        var planPath = PowerForgeStudioHostPaths.GetPlansFilePath(name, RepositoryPlanAdapterKind.ProjectPlan.ToString(), "project.plan.json");
        try
        {
            var service = new RepositoryPlanPreviewService(
                new ProjectBuildHostService(),
                new ProjectBuildCommandHostService(new EmptyPlanPowerShellRunner("""{"Success":false,"Projects":[{"ProjectName":"FailedProject","IsPackable":true}]}""")),
                new ModuleBuildHostService(new ThrowingPowerShellRunner()));
            var repository = new RepositoryCatalogEntry(name, repositoryRoot, ReleaseRepositoryKind.Library,
                ReleaseWorkspaceKind.PrimaryRepository, null, buildScript, false, false);

            var result = Assert.Single(await service.PlanRepositoryAsync(repository));

            Assert.Equal(RepositoryPlanStatus.Failed, result.Status);
            Assert.Null(result.PlanPath);
            Assert.Contains("failed build preflight", result.ErrorTail, StringComparison.Ordinal);
        }
        finally
        {
            var repositoryPlanDirectory = Directory.GetParent(Path.GetDirectoryName(planPath)!)!.FullName;
            if (Directory.Exists(repositoryPlanDirectory))
                Directory.Delete(repositoryPlanDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task PlanRepositoryAsync_DoesNotAcceptAnOlderPlanWhenScriptWritesNothing()
    {
        using var scope = new TemporaryDirectoryScope();
        var name = "ScriptOnly" + Guid.NewGuid().ToString("N")[..8];
        var repositoryRoot = scope.CreateDirectory(name);
        var buildScript = Path.Combine(repositoryRoot, "Build-Project.ps1");
        File.WriteAllText(buildScript, "# test");
        var planPath = PowerForgeStudioHostPaths.GetPlansFilePath(name, RepositoryPlanAdapterKind.ProjectPlan.ToString(), "project.plan.json");
        File.WriteAllText(planPath, """{"Success":true,"Projects":[{"ProjectName":"OldPlan","IsPackable":true}]}""");
        try
        {
            var service = new RepositoryPlanPreviewService(
                new ProjectBuildHostService(),
                new ProjectBuildCommandHostService(new NoPlanPowerShellRunner()),
                new ModuleBuildHostService(new ThrowingPowerShellRunner()));
            var repository = new RepositoryCatalogEntry(name, repositoryRoot, ReleaseRepositoryKind.Library,
                ReleaseWorkspaceKind.PrimaryRepository, null, buildScript, false, false);

            var result = Assert.Single(await service.PlanRepositoryAsync(repository));

            Assert.Equal(RepositoryPlanStatus.Failed, result.Status);
            Assert.Null(result.PlanPath);
            Assert.False(File.Exists(planPath));
        }
        finally
        {
            var repositoryPlanDirectory = Directory.GetParent(Path.GetDirectoryName(planPath)!)!.FullName;
            Directory.Delete(repositoryPlanDirectory, recursive: true);
        }
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

    private sealed class EmptyPlanPowerShellRunner : IPowerShellRunner
    {
        private readonly string _planJson;

        public EmptyPlanPowerShellRunner(string planJson = "{}") => _planJson = planJson;

        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            const string marker = "-PlanPath '";
            var command = request.CommandText ?? string.Empty;
            Assert.Contains("Build-Project.ps1' -Plan:$true -Build:$false -PublishNuget:$false -PublishGitHub:$false -UpdateVersions:$false",
                command, StringComparison.Ordinal);
            Assert.DoesNotContain("Invoke-ProjectBuild -Plan", command, StringComparison.Ordinal);
            var start = command.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0);
            start += marker.Length;
            var end = command.IndexOf('\'', start);
            Assert.True(end > start);
            var planPath = command[start..end].Replace("''", "'", StringComparison.Ordinal);
            Directory.CreateDirectory(Path.GetDirectoryName(planPath)!);
            File.WriteAllText(planPath, _planJson);
            return new PowerShellRunResult(0, "plan written", string.Empty, "test-pwsh");
        }
    }

    private sealed class NoPlanPowerShellRunner : IPowerShellRunner
    {
        public PowerShellRunResult Run(PowerShellRunRequest request)
            => new(0, "script returned without writing a plan", string.Empty, "test-pwsh");
    }
}
