using ConsoleMonster.Testing;
using PowerForge.ConsoleUi;

namespace PowerForge.Tests;

[Collection(ConsoleCapture.Collection)]
public sealed class ProjectBuildProgressLedgerTests
{
    // A finished run prints the complete ledger: every planned row with the time it took, not only what fitted the live area.
    [Fact]
    public void StandaloneConsole_PrintsEveryItemWithItsDurationAfterSuccess()
    {
        ProjectBuildWorkflowResult? result = null;
        var output = ConsoleCapture.Final(view => result = ProjectBuildConsoleUi.RunInteractive(view,
            new ProjectBuildConsolePlan { ConfigPath = "project.build.json", RootPath = "repository", Build = true },
            progress =>
            {
                var detailed = Assert.IsAssignableFrom<IProjectBuildProgressReporterV2>(progress);
                var items = Enumerable.Range(1, 85).Select(index => new ProjectBuildProgressItem
                {
                    Phase = ProjectBuildProgressPhase.PackageBuild,
                    Key = $"package:{index}",
                    Title = $"Project.{index:00}",
                    Position = index,
                    Total = 85
                }).ToArray();
                detailed.ItemsPlanned(ProjectBuildProgressPhase.PackageBuild, items);
                foreach (var item in items)
                {
                    detailed.ItemUpdated(item, ProjectBuildProgressItemState.Started, "building");
                    item.Duration = TimeSpan.FromSeconds(item.Position);
                    detailed.ItemUpdated(item, ProjectBuildProgressItemState.Completed, "1 package, 1 archive");
                }
                return new ProjectBuildWorkflowResult { Result = new ProjectBuildResult { Success = true } };
            }));

        Assert.True(result!.Result.Success);
        Assert.Contains("Project 01/85 Project.01", output, StringComparison.Ordinal);
        Assert.Contains("Project 85/85 Project.85", output, StringComparison.Ordinal);
        Assert.Contains("1 package, 1 archive - " + ConsoleMonster.Widgets.NumberFormat.Duration(TimeSpan.FromSeconds(85)), output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "Project 06/06")]
    [InlineData(false, "Project 05/06")]
    public void StandaloneConsole_PreservesExplicitCounterInTerminalPhaseState(bool success, string expectedCounter)
    {
        var output = ConsoleCapture.Final(view => ProjectBuildConsoleUi.RunInteractive(view,
            new ProjectBuildConsolePlan { ConfigPath = "project.build.json", RootPath = "repository", Build = true },
            progress =>
            {
                progress.PhaseStarted(ProjectBuildProgressPhase.PackageBuild, 6, "packing");
                progress.PhaseUpdated(ProjectBuildProgressPhase.PackageBuild, 5, 6, "packing");
                if (success) progress.PhaseCompleted(ProjectBuildProgressPhase.PackageBuild, "complete");
                else progress.PhaseFailed(ProjectBuildProgressPhase.PackageBuild, "failed");
                return new ProjectBuildWorkflowResult { Result = new ProjectBuildResult { Success = success } };
            }));

        Assert.Contains(expectedCounter, output, StringComparison.Ordinal);
    }

    [Fact]
    public void StandaloneConsole_PrintsTheLedgerBeforeRethrowingFailure()
    {
        var expected = new InvalidOperationException("synthetic failure");
        Exception? actual = null;
        var output = ConsoleCapture.Final(view => actual = Record.Exception(() => ProjectBuildConsoleUi.RunInteractive(view,
            new ProjectBuildConsolePlan { ConfigPath = "project.build.json", RootPath = "repository", Build = true },
            progress =>
            {
                var detailed = Assert.IsAssignableFrom<IProjectBuildProgressReporterV2>(progress);
                var item = new ProjectBuildProgressItem
                {
                    Phase = ProjectBuildProgressPhase.PackageBuild,
                    Key = "package:Sample.Project",
                    Title = "Sample.Project",
                    Position = 1,
                    Total = 2
                };
                detailed.ItemsPlanned(ProjectBuildProgressPhase.PackageBuild, [item]);
                detailed.ItemUpdated(item, ProjectBuildProgressItemState.Started, "building");
                item.Duration = TimeSpan.FromSeconds(2);
                detailed.ItemUpdated(item, ProjectBuildProgressItemState.Completed, "1 package, 1 archive");
                throw expected;
            })));

        Assert.Same(expected, actual);
        Assert.Contains("Sample.Project", output, StringComparison.Ordinal);
        Assert.Contains("1 package, 1 archive - " + ConsoleMonster.Widgets.NumberFormat.Duration(TimeSpan.FromSeconds(2)), output, StringComparison.Ordinal);
        Assert.Contains("skipped after failure", output, StringComparison.Ordinal);
    }

    // A completed phase must not be reopened by late metadata from the package execution.
    [Fact]
    public void StandaloneConsole_DoesNotReopenACompletedPhase()
    {
        var output = ConsoleCapture.Final(view => ProjectBuildConsoleUi.RunInteractive(view,
            new ProjectBuildConsolePlan { RootPath = "source", PlanOnly = true },
            progress =>
            {
                progress.PhaseStarted(ProjectBuildProgressPhase.Plan, 2);
                progress.PhaseCompleted(ProjectBuildProgressPhase.Plan, "plan prepared");
                progress.PhaseStarted(ProjectBuildProgressPhase.Plan, 2, "stale metadata start");
                progress.PhaseUpdated(ProjectBuildProgressPhase.Plan, 1, 2, "stale metadata update");
                return new ProjectBuildWorkflowResult { Result = new ProjectBuildResult { Success = true } };
            }));

        Assert.True(System.Text.RegularExpressions.Regex.IsMatch(output, @"\[01/01\] \[OK\] Prepare build plan\s+Step 02/02 - plan prepared"), output);
    }

    // Unified release: module steps and nested package stages stay in their planned order under their phase,
    // each counted within its own stage.
    [Fact]
    public void UnifiedReleaseConsole_ListsModuleStepsAndNestedPackageStagesInOrder()
    {
        var spec = new PowerForgeReleaseSpec
        {
            Module = new PowerForgeModuleReleaseOptions { ModuleName = "SampleProgress", ModuleVersion = "1.0.0", IncludesPackages = true }
        };
        var moduleItems = new[]
        {
            new PowerForgeReleaseProgressItem { Phase = PowerForgeReleaseProgressPhase.Module, Key = "build:stage", Title = "Stage to staging", Kind = ModulePipelineStepKind.Build.ToString(), Position = 1, Total = 3 },
            new PowerForgeReleaseProgressItem { Phase = PowerForgeReleaseProgressPhase.Module, Key = "artefact:unpacked", Title = "Pack artefact", Target = "Unpacked (Local)", Kind = ModulePipelineStepKind.Artefact.ToString(), Position = 2, Total = 3 },
            new PowerForgeReleaseProgressItem { Phase = PowerForgeReleaseProgressPhase.Module, Key = "publish:gallery", Title = "Publish", Target = "PowerShellGallery", Kind = ModulePipelineStepKind.Publish.ToString(), Position = 3, Total = 3 }
        };

        PowerForgeReleaseResult? result = null;
        var output = ConsoleCapture.Final(view => result = PowerForgeReleaseConsoleUi.RunInteractive(view, spec,
            new PowerForgeReleaseRequest { ConfigPath = "release.json" },
            progress =>
            {
                var detailed = Assert.IsAssignableFrom<IPowerForgeReleaseProgressReporterV2>(progress);
                progress.PhaseStarted(PowerForgeReleaseProgressPhase.Module, moduleItems.Length);
                detailed.ItemsPlanned(PowerForgeReleaseProgressPhase.Module, moduleItems);
                detailed.ItemUpdated(moduleItems[2], PowerForgeReleaseProgressItemState.Started, "publishing");
                detailed.ItemUpdated(moduleItems[0], PowerForgeReleaseProgressItemState.Completed, "staged");
                detailed.ItemUpdated(moduleItems[1], PowerForgeReleaseProgressItemState.Completed, "packed");
                detailed.ItemUpdated(moduleItems[2], PowerForgeReleaseProgressItemState.Completed, "published");
                progress.PhaseCompleted(PowerForgeReleaseProgressPhase.Module, "complete");

                var project = new ProjectBuildReleaseProgressAdapter(detailed, PowerForgeReleaseProgressPhase.Packages);
                foreach (var phase in new[]
                         {
                             ProjectBuildProgressPhase.Versioning,
                             ProjectBuildProgressPhase.PackageBuild,
                             ProjectBuildProgressPhase.PackageSigning,
                             ProjectBuildProgressPhase.NuGetPublish,
                             ProjectBuildProgressPhase.GitHubPublish
                         })
                {
                    var item = new ProjectBuildProgressItem
                    {
                        Phase = phase,
                        Key = $"{phase}:one",
                        Title = $"{phase} item",
                        Kind = phase == ProjectBuildProgressPhase.GitHubPublish ? "GitHubAsset" : phase.ToString(),
                        Position = 1,
                        Total = 2
                    };
                    project.PhaseStarted(phase, 2);
                    project.ItemsPlanned(phase, [item]);
                    project.ItemUpdated(item, ProjectBuildProgressItemState.Completed, "complete");
                    project.PhaseCompleted(phase);
                }

                return new PowerForgeReleaseResult { Success = true };
            }), width: 110);

        Assert.True(result!.Success);
        GoldenFile.Match(GoldenPath("UnifiedRelease.Ledger.txt"), ConsoleCapture.Ledger(output));
    }

    private static string Ledger(string output) => output.Substring(output.IndexOf("[01/", StringComparison.Ordinal));

    private static string GoldenPath(string name, [System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
        => Path.Combine(Path.GetDirectoryName(sourceFile)!, "Golden", "ConsoleMonster", name);

    [Theory]
    [InlineData(1, 9, "01/09")]
    [InlineData(1, 24, "01/24")]
    [InlineData(20, 24, "20/24")]
    [InlineData(1, 100, "001/100")]
    public void ProgressCounterFormatter_UsesOneStableWidthPerScope(
        int position,
        int total,
        string expected)
        => Assert.Equal(expected, ProgressCounterFormatter.Format(position, total));

    [Theory]
    [InlineData(ProjectBuildProgressPhase.Plan, "Step")]
    [InlineData(ProjectBuildProgressPhase.Versioning, "Project")]
    [InlineData(ProjectBuildProgressPhase.PackageBuild, "Project")]
    [InlineData(ProjectBuildProgressPhase.PackageSigning, "Package")]
    [InlineData(ProjectBuildProgressPhase.NuGetPublish, "Package")]
    [InlineData(ProjectBuildProgressPhase.GitHubPublish, "Release")]
    public void ProjectBuildCounterScope_MatchesTheCountedResource(
        ProjectBuildProgressPhase phase,
        string expected)
        => Assert.Equal(expected, ProgressCounterFormatter.GetProjectBuildScope(phase));


    [Fact]
    public void Execute_ReportsPerProjectPackageEventsAndDurations()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N")));
        try
        {
            WriteProject(root.FullName, "Sample.First");
            WriteProject(root.FullName, "Sample.Second");
            var progress = new RecordingProjectBuildProgress();

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(
                new DotNetRepositoryReleaseSpec
                {
                    RootPath = root.FullName,
                    Configuration = "Release",
                    OutputPath = Path.Combine(root.FullName, "packages"),
                    Pack = true,
                    Publish = false,
                    UpdateVersions = false,
                    WhatIf = true,
                    CreateReleaseZip = false
                },
                signAssemblies: null,
                validateAssemblySigning: null,
                progress);

            Assert.True(result.Success, result.ErrorMessage);
            var versionItems = progress.Planned
                .Where(item => item.Phase == ProjectBuildProgressPhase.Versioning)
                .ToArray();
            var packageItems = progress.Planned
                .Where(item => item.Phase == ProjectBuildProgressPhase.PackageBuild)
                .ToArray();
            Assert.Equal(
                new[] { "Sample.First", "Sample.Second" },
                packageItems.Select(item => item.Title).OrderBy(title => title, StringComparer.Ordinal));
            Assert.Equal(new[] { 1, 2 }, packageItems.Select(item => item.Position).OrderBy(position => position));
            Assert.All(packageItems, item => Assert.Equal(2, item.Total));
            Assert.Equal(2, versionItems.Length);
            Assert.All(versionItems, versionItem => Assert.Contains(progress.Updates, update =>
                update.Item.Key == versionItem.Key &&
                update.State == ProjectBuildProgressItemState.Completed));
            Assert.Equal(2, progress.Updates.Count(update =>
                update.Item.Phase == ProjectBuildProgressPhase.PackageBuild &&
                update.State == ProjectBuildProgressItemState.Started));
            Assert.Equal(2, progress.Updates.Count(update =>
                update.Item.Phase == ProjectBuildProgressPhase.PackageBuild &&
                update.State == ProjectBuildProgressItemState.Completed));
            Assert.All(
                progress.Updates.Where(update =>
                    update.Item.Phase == ProjectBuildProgressPhase.PackageBuild &&
                    update.State == ProjectBuildProgressItemState.Completed),
                update =>
                {
                    Assert.NotNull(update.Item.Duration);
                    Assert.True(update.Item.Duration >= TimeSpan.Zero);
                    Assert.Contains("package(s)", update.Detail, StringComparison.Ordinal);
                    Assert.Contains("planned", update.Detail, StringComparison.Ordinal);
                });
            Assert.All(result.Projects.Where(project => project.IsPackable), project =>
                Assert.True(project.PackageBuildDuration >= TimeSpan.Zero));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Execute_ReportsEverySignedPackageAsDurableWork(bool timeout)
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N")));
        try
        {
            WriteProject(root.FullName, "Sample.Signed");
            if (timeout)
            {
                WriteProject(root.FullName, "Sample.Second");
                WriteProject(root.FullName, "Sample.Third");
            }
            var progress = new RecordingProjectBuildProgress();
            DotNetRepositoryReleaseService.PackageSigningHandler signer = (
                IReadOnlyList<string> packages,
                DotNetRepositoryReleaseSpec spec,
                string sha256,
                Action<string, int, DotNetNuGetSignResult?>? signingProgress,
                out string[] failedPackages,
                out string error) =>
            {
                for (var index = 0; index < packages.Count; index++)
                {
                    var package = packages[index];
                    signingProgress?.Invoke(package, 1, null);
                    if (timeout && index == 1)
                    {
                        signingProgress?.Invoke(package, 0,
                            new DotNetNuGetSignResult(-1, "", "timed out", "dotnet", TimeSpan.FromSeconds(3), true, "timed out"));
                        failedPackages = packages.Skip(index).ToArray();
                        error = "Signing timed out; remaining packages were not attempted.";
                        return false;
                    }
                    signingProgress?.Invoke(package, 0,
                        new DotNetNuGetSignResult(0, "", "", "dotnet", TimeSpan.FromMilliseconds(42), false, null));
                }
                failedPackages = Array.Empty<string>();
                error = string.Empty;
                return packages.Count > 0;
            };
            var service = new DotNetRepositoryReleaseService(
                new NullLogger(),
                signer,
                (_, _) => "ABCDEF");

            var result = service.Execute(
                new DotNetRepositoryReleaseSpec
                {
                    RootPath = root.FullName,
                    Configuration = "Release",
                    OutputPath = Path.Combine(root.FullName, "packages"),
                    Pack = true,
                    Publish = false,
                    UpdateVersions = false,
                    CreateReleaseZip = false,
                    CertificateThumbprint = "ABC123",
                    SignAssemblies = false,
                    SignPackages = true
                },
                signAssemblies: null,
                validateAssemblySigning: null,
                progress);

            Assert.Equal(!timeout, result.Success);
            var signingItems = progress.Planned
                .Where(item => item.Phase == ProjectBuildProgressPhase.PackageSigning)
                .ToArray();
            Assert.Equal(timeout ? 3 : 1, signingItems.Length);
            var signingItem = signingItems[0];
            Assert.EndsWith(".nupkg", signingItem.Title, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(progress.Updates, update =>
                update.Item.Key == signingItem.Key &&
                update.State == ProjectBuildProgressItemState.Started &&
                update.Detail == "Signing and timestamping");
            var completed = Assert.Single(progress.Updates, update =>
                update.Item.Key == signingItem.Key &&
                update.State == ProjectBuildProgressItemState.Completed);
            Assert.Equal("signed", completed.Detail);
            Assert.Equal(TimeSpan.FromMilliseconds(42), completed.Item.Duration);
            if (timeout)
            {
                var failed = Assert.Single(progress.Updates, update =>
                    update.Item.Key == signingItems[1].Key && update.State == ProjectBuildProgressItemState.Failed);
                Assert.Equal(TimeSpan.FromSeconds(3), failed.Item.Duration);
                var skipped = Assert.Single(progress.Updates, update =>
                    update.Item.Key == signingItems[2].Key && update.State == ProjectBuildProgressItemState.Skipped);
                Assert.Null(skipped.Item.Duration);
                Assert.Contains("not attempted", skipped.Detail);
                Assert.DoesNotContain(progress.Updates, update =>
                    update.Item.Key == signingItems[2].Key && update.State == ProjectBuildProgressItemState.Started);
            }
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Execute_MsBuildBatchStartsEveryProjectBeforeCompletingAnyAndIncludesBatchTime()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N")));
        try
        {
            WriteProject(root.FullName, "Sample.First");
            WriteProject(root.FullName, "Sample.Second");
            var progress = new RecordingProjectBuildProgress();

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(
                new DotNetRepositoryReleaseSpec
                {
                    RootPath = root.FullName,
                    Configuration = "Release",
                    OutputPath = Path.Combine(root.FullName, "packages"),
                    Pack = true,
                    PackStrategy = DotNetRepositoryPackStrategy.MSBuild,
                    Publish = false,
                    UpdateVersions = false,
                    CreateReleaseZip = false
                },
                signAssemblies: null,
                validateAssemblySigning: null,
                progress);

            Assert.True(result.Success, result.ErrorMessage);
            var packageUpdates = progress.Updates
                .Where(update => update.Item.Phase == ProjectBuildProgressPhase.PackageBuild)
                .ToList();
            var firstTerminal = packageUpdates.FindIndex(update =>
                update.State is ProjectBuildProgressItemState.Completed or ProjectBuildProgressItemState.Failed);
            Assert.Equal(2, packageUpdates.Take(firstTerminal).Count(update =>
                update.State == ProjectBuildProgressItemState.Started &&
                update.Detail == "building in MSBuild batch"));
            Assert.Equal(2, packageUpdates.Take(firstTerminal).Count(update =>
                update.State == ProjectBuildProgressItemState.Started &&
                update.Detail == "MSBuild batch complete; awaiting package collection" &&
                update.Item.Duration > TimeSpan.Zero));
            Assert.All(
                result.Projects.Where(project => project.IsPackable),
                project => Assert.True(project.PackageBuildDuration > TimeSpan.Zero));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }


    private static void WriteProject(string rootPath, string name)
    {
        var directory = Directory.CreateDirectory(Path.Combine(rootPath, name));
        File.WriteAllText(
            Path.Combine(directory.FullName, $"{name}.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <VersionPrefix>1.0.0</VersionPrefix>
                <IsPackable>true</IsPackable>
              </PropertyGroup>
            </Project>
            """);
    }

    private sealed class RecordingProjectBuildProgress : IProjectBuildProgressReporterV2
    {
        public List<ProjectBuildProgressItem> Planned { get; } = new();
        public List<(ProjectBuildProgressItem Item, ProjectBuildProgressItemState State, string? Detail)> Updates { get; } = new();

        public void PhaseStarted(ProjectBuildProgressPhase phase, int totalItems, string? detail = null) { }
        public void PhaseUpdated(ProjectBuildProgressPhase phase, int completedItems, int totalItems, string? detail = null) { }
        public void PhaseCompleted(ProjectBuildProgressPhase phase, string? detail = null) { }
        public void PhaseFailed(ProjectBuildProgressPhase phase, string? detail = null) { }

        public void ItemsPlanned(
            ProjectBuildProgressPhase phase,
            IReadOnlyList<ProjectBuildProgressItem> items)
            => Planned.AddRange(items);

        public void ItemUpdated(
            ProjectBuildProgressItem item,
            ProjectBuildProgressItemState state,
            string? detail = null)
            => Updates.Add((
                new ProjectBuildProgressItem
                {
                    Phase = item.Phase,
                    Key = item.Key,
                    Title = item.Title,
                    Kind = item.Kind,
                    Position = item.Position,
                    Total = item.Total,
                    Duration = item.Duration
                },
                state,
                detail));
    }

}
