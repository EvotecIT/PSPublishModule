using System.Text.Json;
using PowerForge;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Orchestrator.Portfolio;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioReviewedPlanTests
{
    [Fact]
    public async Task DirectModuleJsonPlannerReturnsReviewedActionsWithoutPowerShell()
    {
        var root = Path.Combine(Path.GetTempPath(), $"studio-module-plan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "src", "Example.Module"));
        var config = Path.Combine(root, "powerforge.json");
        try
        {
            await File.WriteAllTextAsync(config,
                """
                {
                  "Build": { "Name": "Example.Module", "SourcePath": "src/Example.Module", "Version": "1.2.3" },
                  "Install": { "Enabled": false },
                  "Segments": [
                    { "Type": "Packed", "Configuration": { "Enabled": true, "Path": "out/Example.Module.zip" } }
                  ]
                }
                """);
            var repository = new RepositoryCatalogEntry(
                "Example.Module", root, ReleaseRepositoryKind.Module, ReleaseWorkspaceKind.PrimaryRepository,
                config, null, false, false);

            var result = Assert.Single(await new RepositoryPlanPreviewService().PlanRepositoryAsync(repository));

            Assert.Equal("Module JSON configuration validated and reviewed plan generated.", result.Summary);
            Assert.Contains(result.Actions, action => action.Action == "Stage to staging" && action.Target == "Example.Module");
            Assert.Contains(result.Actions, action => action.Action == "Pack Packed");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ModuleProjectionUsesCanonicalOrderAndBuildOnlyOverridesWithoutSecretPayloads()
    {
        const string secret = "studio-super-secret";
        var root = Path.Combine(Path.GetTempPath(), $"studio-canonical-module-plan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "src", "Example.Module"));
        var config = Path.Combine(root, "powerforge.json");
        try
        {
            await File.WriteAllTextAsync(config,
                $$"""
                {
                  "Build": { "Name": "Example.Module", "SourcePath": "src/Example.Module", "Version": "2.4.0" },
                  "Install": { "Enabled": true },
                  "Segments": [
                    { "Type": "Packed", "Configuration": { "Enabled": true, "ArtefactName": "Example.Module.zip" } },
                    { "Type": "Execute", "Configuration": { "Name": "Prepare dependencies", "At": "BeforeDependencies", "InlineScript": "Write-Output '{{secret}}'", "Environment": { "TOKEN": "{{secret}}" } } },
                    { "Type": "GalleryNuget", "Configuration": { "Enabled": true, "Destination": "PowerShellGallery", "RepositoryName": "PSGallery", "ApiKey": "{{secret}}" } }
                  ]
                }
                """);
            var repository = new RepositoryCatalogEntry(
                "Example.Module", root, ReleaseRepositoryKind.Module, ReleaseWorkspaceKind.PrimaryRepository,
                config, null, false, false);
            var result = Assert.Single(await new RepositoryPlanPreviewService().PlanRepositoryAsync(repository));
            var actions = result.Actions;

            var actionNames = actions.Select(action => action.Action).ToList();
            Assert.Equal("Run action (Prepare dependencies)", actionNames[0]);
            Assert.True(actionNames.IndexOf("Run action (Prepare dependencies)") <
                        actionNames.IndexOf("Stage to staging"));
            Assert.Contains(actions, action => action.Lane == "Artifact" && action.Action == "Pack Packed");
            Assert.Contains(actions, action => action.Lane == "PowerShell" && action.Detail.Contains("contents and environment hidden", StringComparison.Ordinal));
            Assert.DoesNotContain(actions, action => action.Lane is "Release" or "Install" or "Sign");
            Assert.DoesNotContain(secret, JsonSerializer.Serialize(actions), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ProjectProjectionShowsNugetSymbolsAndZipOutputs()
    {
        var execution = new ProjectBuildHostExecutionResult
        {
            Result = new ProjectBuildResult
            {
                Release = new DotNetRepositoryReleaseResult
                {
                    ResolvedVersion = "3.2.1",
                    Projects =
                    [
                        new DotNetRepositoryProjectResult
                        {
                            ProjectName = "Example.Core",
                            PackageId = "Example.Core",
                            IsPackable = true,
                            NewVersion = "3.2.1",
                            Packages = [@"C:\artifacts\Example.Core.3.2.1.nupkg"],
                            SymbolPackages = [@"C:\artifacts\Example.Core.3.2.1.snupkg"],
                            ReleaseZipPath = @"C:\artifacts\Example.Core.3.2.1.zip"
                        }
                    ]
                }
            }
        };

        var actions = RepositoryPlanActionProjectionService.FromProject(execution);

        Assert.Collection(actions,
            action => Assert.Equal("Build and pack project", action.Action),
            action => Assert.Equal("Create NuGet package", action.Action),
            action => Assert.Equal("Create symbols package", action.Action),
            action => Assert.Equal("Create release archive", action.Action));
    }

    [Fact]
    public void UnifiedProjectionShowsExecutableZipMsiMsixCommandAndWingetWithoutArguments()
    {
        const string secret = "hook-secret-value";
        var result = new PowerForgeReleaseResult
        {
            Success = true,
            DotNetToolPlan = new DotNetPublishPlan
            {
                Steps =
                [
                    new DotNetPublishStep { Kind = DotNetPublishStepKind.Publish, Title = "Publish desktop host", TargetName = "Studio", Framework = "net10.0", Runtime = "win-x64" },
                    new DotNetPublishStep { Kind = DotNetPublishStepKind.Bundle, Title = "Create portable ZIP", BundleId = "portable", BundleZipPath = @"C:\artifacts\Studio.zip" },
                    new DotNetPublishStep { Kind = DotNetPublishStepKind.MsiBuild, Title = "Build Windows installer", InstallerId = "desktop-msi", InstallerOutputPath = @"C:\artifacts\Studio.msi" },
                    new DotNetPublishStep { Kind = DotNetPublishStepKind.StorePackage, Title = "Build Store package", StorePackageId = "desktop-store", StorePackageOutputPath = @"C:\artifacts\Studio.msix" },
                    new DotNetPublishStep { Kind = DotNetPublishStepKind.CommandHook, Title = "Run release hook", HookId = "prepare", HookCommand = "pwsh", HookArguments = ["-Token", secret] }
                ]
            },
            WingetSubmissionPlan = new PowerForgeWingetSubmissionPlan
            {
                Enabled = true,
                Mode = PowerForgeWingetSubmissionMode.Update,
                Entries =
                [
                    new PowerForgeWingetSubmissionEntryPlan
                    {
                        PackageIdentifier = "Evotec.PowerForgeStudio",
                        PackageVersion = "1.0.0",
                        ManifestPath = @"C:\manifests\Evotec.PowerForgeStudio.yaml",
                        Arguments = ["--token", secret],
                        RedactedArguments = ["--token", "***"]
                    }
                ]
            }
        };

        var actions = RepositoryPlanActionProjectionService.FromUnified(result);

        Assert.Contains(actions, action => action.Lane == "Executable");
        Assert.Contains(actions, action => action.Lane == "ZIP");
        Assert.Contains(actions, action => action.Lane == "MSI");
        Assert.Contains(actions, action => action.Lane == "MSIX");
        Assert.Contains(actions, action => action.Lane == "Command" && action.Detail.Contains("arguments hidden", StringComparison.Ordinal));
        Assert.Contains(actions, action => action.Lane == "WinGet" && action.Detail.Contains("deferred", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(actions), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnifiedModulePreviewUsesCanonicalBuildOnlyModulePlan()
    {
        var root = Path.Combine(Path.GetTempPath(), $"studio-unified-module-plan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "Build"));
        var moduleConfig = Path.Combine(root, "powerforge.json");
        var releaseConfig = Path.Combine(root, "Build", "release.json");
        try
        {
            await File.WriteAllTextAsync(moduleConfig,
                """
                {
                  "Build": { "Name": "Unified.Module", "SourcePath": ".", "Version": "1.0.0" },
                  "Install": { "Enabled": true },
                  "Segments": [
                    { "Type": "Execute", "Configuration": { "Name": "Prepare unified assets", "At": "BeforeBuild", "InlineScript": "Write-Output 'unified-secret-value'" } },
                    { "Type": "Packed", "Configuration": { "Enabled": true, "ArtefactName": "Unified.Module.zip" } },
                    { "Type": "GalleryNuget", "Configuration": { "Enabled": true, "Destination": "PowerShellGallery", "RepositoryName": "PSGallery", "ApiKey": "unified-secret-value" } }
                  ]
                }
                """);
            await File.WriteAllTextAsync(releaseConfig,
                """{ "Module": { "RepositoryRoot": "..", "ConfigPath": "powerforge.json" } }""");
            var repository = new RepositoryCatalogEntry(
                "Unified.Module",
                root,
                ReleaseRepositoryKind.Mixed,
                ReleaseWorkspaceKind.PrimaryRepository,
                null,
                null,
                false,
                false,
                releaseConfig);

            var result = Assert.Single(await new RepositoryPlanPreviewService().PlanRepositoryAsync(repository));

            Assert.Equal(RepositoryPlanStatus.Succeeded, result.Status);
            Assert.Contains(result.Actions, action => action.Action == "Run action (Prepare unified assets)");
            Assert.Contains(result.Actions, action => action.Action == "Pack Packed");
            Assert.DoesNotContain(result.Actions, action => action.Lane is "Release" or "Install" or "Sign");
            Assert.DoesNotContain("unified-secret-value", JsonSerializer.Serialize(result.Actions), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ProjectPlanFileProjectionSupportsLegacyPowerShellOutputAndBoundsRows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"studio-plan-{Guid.NewGuid():N}.json");
        try
        {
            var release = new DotNetRepositoryReleaseResult
            {
                ResolvedVersion = "1.0.0",
                Projects = Enumerable.Range(1, 250).Select(index => new DotNetRepositoryProjectResult
                {
                    ProjectName = $"Project{index}",
                    PackageId = $"Package{index}",
                    IsPackable = true
                }).ToList()
            };
            File.WriteAllText(path, JsonSerializer.Serialize(release));

            var actions = RepositoryPlanActionProjectionService.FromProjectPlanFile(path);

            Assert.Equal(200, actions.Count);
            Assert.Equal(1, actions[0].Sequence);
            Assert.Equal(200, actions[^1].Sequence);
            Assert.Equal("Additional actions omitted", actions[^1].Action);
            Assert.Equal("51 more action(s)", actions[^1].Target);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{ not-json }")]
    public void ProjectPlanFileProjectionRejectsMalformedOrEmptyPlans(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"studio-invalid-plan-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, json);
            Assert.Throws<InvalidDataException>(() => RepositoryPlanActionProjectionService.FromProjectPlanFile(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void PlanResultExposesMutuallyExclusiveStatusStatesForUiStyling()
    {
        var succeeded = new RepositoryPlanResult(
            RepositoryPlanAdapterKind.ProjectPlan,
            RepositoryPlanStatus.Succeeded,
            "ok",
            null,
            0,
            0);
        var failed = succeeded with { Status = RepositoryPlanStatus.Failed };

        Assert.True(succeeded.IsSucceeded);
        Assert.False(succeeded.IsFailed);
        Assert.False(succeeded.IsNeutral);
        Assert.True(failed.IsFailed);
        Assert.False(failed.IsSucceeded);
        Assert.False(failed.IsNeutral);
    }
}
