using PowerForge.ConsoleShared;
using Spectre.Console;

namespace PowerForge.Tests;

public sealed class ProjectBuildProgressLedgerTests
{
    [Fact]
    public void ProgressLedger_RegistersCompletePlanAndRetainsCompleteTimedHistory()
    {
        using var writer = new StringWriter();
        var console = CreateConsole(writer, height: 12);
        SpectreProgressLedger? ledger = null;

        SpectreProgressDisplay.Run(
            console,
            [
                new TaskDescriptionColumn { Alignment = Justify.Left },
                new ProgressBarColumn(),
                new PercentageColumn(),
                new ElapsedTimeColumn()
            ],
            context =>
            {
                ledger = new SpectreProgressLedger(context);
                var items = Enumerable.Range(1, 85)
                    .Select(index => new SpectreProgressLedgerItem
                    {
                        Key = $"package:{index}",
                        GroupKey = "PackageBuild",
                        GroupTitle = "Build packages and archives",
                        GroupOrder = 1,
                        Title = $"Project.{index:00}",
                        Position = index,
                        Total = 85
                    })
                    .ToArray();

                ledger.Plan(items);
                Assert.Equal(85, ledger.VisibleTaskCount);

                foreach (var item in items)
                {
                    ledger.Update(item, SpectreProgressLedgerState.Started, "building");
                    Assert.Equal(85, ledger.VisibleTaskCount);

                    item.Duration = TimeSpan.FromSeconds(item.Position);
                    ledger.Update(item, SpectreProgressLedgerState.Completed, "1 package, 1 archive");
                    Assert.Equal(85, ledger.VisibleTaskCount);
                    if (item.Position == items.Length)
                    {
                        var stoppedAt = ledger.GetVisibleElapsedTime(item.Key);
                        Thread.Sleep(25);
                        Assert.Equal(stoppedAt, ledger.GetVisibleElapsedTime(item.Key));
                    }
                }

                Assert.Equal(85, ledger.GetItemCount("PackageBuild"));
                Assert.Equal(1d, ledger.GetCompletionRatio("PackageBuild"), 5);
                Assert.Equal(85, ledger.VisibleTaskCount);
                ledger.ClearLiveTasks();
                Assert.Equal(0, ledger.VisibleTaskCount);
            });

        var snapshots = Assert.IsAssignableFrom<IReadOnlyList<SpectreProgressLedgerSnapshot>>(
            ledger!.GetSnapshots());
        Assert.Equal(85, snapshots.Count);
        Assert.All(snapshots, snapshot => Assert.Equal(SpectreProgressLedgerState.Completed, snapshot.State));
        Assert.Equal(TimeSpan.FromSeconds(1), snapshots[0].Duration);
        Assert.Equal(TimeSpan.FromSeconds(85), snapshots[^1].Duration);

        SpectreProgressLedger.WriteLedger(console, snapshots, "Project build details");
        var output = writer.ToString();
        Assert.Contains("Project.01", output, StringComparison.Ordinal);
        Assert.Contains("Project.85", output, StringComparison.Ordinal);
        Assert.Contains("01:25.000", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgressLedger_UpdatesFullPlanTopToBottomWithoutMovingRows()
    {
        using var writer = new StringWriter();
        var console = CreateConsole(writer, height: 12);
        IReadOnlyList<string>? finalTaskDescriptions = null;

        SpectreProgressDisplay.Run(
            console,
            SpectreBuildProgressColumns.CreateStandard(),
            context =>
            {
                var ledger = new SpectreProgressLedger(context);
                var items = Enumerable.Range(1, 85)
                    .Select(index => new SpectreProgressLedgerItem
                    {
                        Key = $"package:{index}",
                        GroupKey = "PackageBuild",
                        GroupTitle = "Build packages and archives",
                        GroupOrder = 1,
                        Title = $"Project.{index:00}",
                        Position = index,
                        Total = 85
                    })
                    .ToArray();

                ledger.Plan(items);
                ledger.Update(items[6], SpectreProgressLedgerState.Started, "building");
                ledger.Update(items[3], SpectreProgressLedgerState.Started, "building");
                Assert.Equal(85, ledger.VisibleTaskCount);
            },
            tasks => finalTaskDescriptions = tasks.Select(task => task.Description).ToArray());

        Assert.NotNull(finalTaskDescriptions);
        Assert.Equal(85, finalTaskDescriptions!.Count);
        Assert.Contains("Project.01", finalTaskDescriptions[0], StringComparison.Ordinal);
        Assert.Contains("Project.04", finalTaskDescriptions[3], StringComparison.Ordinal);
        Assert.Contains("Project.07", finalTaskDescriptions[6], StringComparison.Ordinal);
        Assert.Contains("Project.85", finalTaskDescriptions[^1], StringComparison.Ordinal);
        var output = writer.ToString();
        Assert.Contains("Project.01", output, StringComparison.Ordinal);
        Assert.Contains("Project.07", output, StringComparison.Ordinal);
        Assert.Contains("Project.85", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgressLedger_RendersSharedDetailedTitleTargetKindAndTimingLayout()
    {
        using var writer = new StringWriter();
        var console = CreateConsole(writer, height: 12);
        var presentation = new SpectreProgressPresentation(viewportWidth: 140, unicode: false);
        IReadOnlyList<string>? finalTaskDescriptions = null;

        SpectreProgressDisplay.Run(
            console,
            presentation.CreateColumns(),
            context =>
            {
                var ledger = new SpectreProgressLedger(context, presentation);
                var item = new SpectreProgressLedgerItem
                {
                    Key = "artefact:unpacked",
                    GroupKey = "Module",
                    GroupTitle = "Build PowerShell module",
                    GroupOrder = 1,
                    Title = "Pack artefact",
                    Target = "Unpacked",
                    CounterLabel = "Module",
                    Kind = ModulePipelineStepKind.Artefact.ToString(),
                    Position = 1,
                    Total = 2
                };

                ledger.Plan([item]);
                ledger.Update(item, SpectreProgressLedgerState.Started, "packing");
            },
            tasks => finalTaskDescriptions = tasks.Select(task => task.Description).ToArray());

        var description = Assert.Single(finalTaskDescriptions!);
        Assert.StartsWith("Module 01/02 Pack artefact", description, StringComparison.Ordinal);
        Assert.Contains("packing", description, StringComparison.Ordinal);
        Assert.EndsWith("Unpacked", description, StringComparison.Ordinal);
        Assert.Contains("PK", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void UnifiedReleaseConsole_UsesSharedModuleRowsInCanonicalOrder()
    {
        using var writer = new StringWriter();
        var console = CreateConsole(writer, height: 20);
        var spec = new PowerForgeReleaseSpec
        {
            Module = new PowerForgeModuleReleaseOptions
            {
                ModuleName = "SampleProgress",
                ModuleVersion = "1.0.0"
            }
        };
        var request = new PowerForgeReleaseRequest
        {
            ConfigPath = "release.json",
            ModuleOnly = true
        };
        var items = new[]
        {
            new PowerForgeReleaseProgressItem
            {
                Phase = PowerForgeReleaseProgressPhase.Module,
                Key = "build:stage",
                Title = "Stage to staging",
                Kind = ModulePipelineStepKind.Build.ToString(),
                Position = 1,
                Total = 3
            },
            new PowerForgeReleaseProgressItem
            {
                Phase = PowerForgeReleaseProgressPhase.Module,
                Key = "artefact:unpacked",
                Title = "Pack artefact",
                Target = "Unpacked (Local)",
                Kind = ModulePipelineStepKind.Artefact.ToString(),
                Position = 2,
                Total = 3
            },
            new PowerForgeReleaseProgressItem
            {
                Phase = PowerForgeReleaseProgressPhase.Module,
                Key = "publish:gallery",
                Title = "Publish",
                Target = "PowerShellGallery",
                Kind = ModulePipelineStepKind.Publish.ToString(),
                Position = 3,
                Total = 3
            }
        };

        var result = SpectrePowerForgeReleaseConsoleUi.RunInteractive(
            console,
            spec,
            request,
            progress =>
            {
                var detailed = Assert.IsAssignableFrom<IPowerForgeReleaseProgressReporterV2>(progress);
                progress.PhaseStarted(PowerForgeReleaseProgressPhase.Module, items.Length);
                detailed.ItemsPlanned(PowerForgeReleaseProgressPhase.Module, items);
                detailed.ItemUpdated(items[2], PowerForgeReleaseProgressItemState.Started, "publishing");
                detailed.ItemUpdated(items[0], PowerForgeReleaseProgressItemState.Completed, "staged");
                detailed.ItemUpdated(items[1], PowerForgeReleaseProgressItemState.Completed, "packed");
                detailed.ItemUpdated(items[2], PowerForgeReleaseProgressItemState.Completed, "published");
                progress.PhaseCompleted(PowerForgeReleaseProgressPhase.Module, "complete");
                return new PowerForgeReleaseResult { Success = true };
            });

        Assert.True(result.Success);
        var output = writer.ToString();
        var first = output.LastIndexOf("Module 01/03 Stage to staging", StringComparison.Ordinal);
        var second = output.LastIndexOf("Module 02/03 Pack artefact", StringComparison.Ordinal);
        var third = output.LastIndexOf("Module 03/03 Publish", StringComparison.Ordinal);
        Assert.True(first >= 0, output);
        Assert.True(second > first, output);
        Assert.True(third > second, output);
        Assert.Contains("Unpacked (Local)", output, StringComparison.Ordinal);
        Assert.Contains("PowerShellGallery", output, StringComparison.Ordinal);
        Assert.Contains("Phase 01/01", output, StringComparison.Ordinal);
    }

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
    public void StandaloneConsole_WritesDetailedLedgerAfterSuccess()
    {
        using var writer = new StringWriter();
        var console = CreateConsole(writer, height: 12);

        var result = SpectreProjectBuildConsoleUi.RunInteractive(
            console,
            new ProjectBuildConsolePlan
            {
                ConfigPath = "project.build.json",
                RootPath = "repository",
                Build = true
            },
            progress =>
            {
                var detailed = Assert.IsAssignableFrom<IProjectBuildProgressReporterV2>(progress);
                var item = new ProjectBuildProgressItem
                {
                    Phase = ProjectBuildProgressPhase.PackageBuild,
                    Key = "package:Sample.Project",
                    Title = "Sample.Project",
                    Position = 1,
                    Total = 1,
                    Duration = TimeSpan.FromSeconds(2)
                };
                detailed.ItemsPlanned(ProjectBuildProgressPhase.PackageBuild, [item]);
                detailed.ItemUpdated(item, ProjectBuildProgressItemState.Started, "building");
                detailed.ItemUpdated(item, ProjectBuildProgressItemState.Completed, "1 package, 1 archive");
                return new ProjectBuildWorkflowResult
                {
                    Result = new ProjectBuildResult { Success = true }
                };
            });

        Assert.True(result.Result.Success);
        var output = writer.ToString();
        Assert.Contains("Project build details", output, StringComparison.Ordinal);
        Assert.Contains("Sample.Project", output, StringComparison.Ordinal);
        Assert.Contains("00:02.000", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "Project 06/06")]
    [InlineData(false, "Project 05/06")]
    public void StandaloneConsole_PreservesExplicitCounterInTerminalPhaseState(
        bool success,
        string expectedCounter)
    {
        using var writer = new StringWriter();
        var console = CreateConsole(writer, height: 12);

        var result = SpectreProjectBuildConsoleUi.RunInteractive(
            console,
            new ProjectBuildConsolePlan
            {
                ConfigPath = "project.build.json",
                RootPath = "repository",
                Build = true
            },
            progress =>
            {
                progress.PhaseStarted(ProjectBuildProgressPhase.PackageBuild, 6, "packing");
                progress.PhaseUpdated(ProjectBuildProgressPhase.PackageBuild, 5, 6, "packing");
                if (success) {
                    progress.PhaseCompleted(ProjectBuildProgressPhase.PackageBuild, "complete");
                }
                else {
                    progress.PhaseFailed(ProjectBuildProgressPhase.PackageBuild, "failed");
                }

                return new ProjectBuildWorkflowResult
                {
                    Result = new ProjectBuildResult { Success = success }
                };
            });

        Assert.Equal(success, result.Result.Success);
        Assert.Contains(expectedCounter, writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void StandaloneConsole_WritesDetailedLedgerBeforeRethrowingFailure()
    {
        using var writer = new StringWriter();
        var console = CreateConsole(writer, height: 12);
        var expected = new InvalidOperationException("synthetic failure");

        var actual = Assert.Throws<InvalidOperationException>(() =>
            SpectreProjectBuildConsoleUi.RunInteractive(
                console,
                new ProjectBuildConsolePlan
                {
                    ConfigPath = "project.build.json",
                    RootPath = "repository",
                    Build = true
                },
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
                }));

        Assert.Same(expected, actual);
        var output = writer.ToString();
        Assert.Contains("Project build details", output, StringComparison.Ordinal);
        Assert.Contains("Sample.Project", output, StringComparison.Ordinal);
        Assert.Contains("00:02.000", output, StringComparison.Ordinal);
    }

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
            Assert.Equal(
                new[] { "Sample.First", "Sample.Second" },
                progress.Planned.Select(item => item.Title).OrderBy(title => title, StringComparer.Ordinal));
            Assert.Equal(new[] { 1, 2 }, progress.Planned.Select(item => item.Position).OrderBy(position => position));
            Assert.All(progress.Planned, item => Assert.Equal(2, item.Total));
            Assert.Equal(2, progress.Updates.Count(update => update.State == ProjectBuildProgressItemState.Started));
            Assert.Equal(2, progress.Updates.Count(update => update.State == ProjectBuildProgressItemState.Completed));
            Assert.All(
                progress.Updates.Where(update => update.State == ProjectBuildProgressItemState.Completed),
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
            var firstTerminal = progress.Updates.FindIndex(update =>
                update.State is ProjectBuildProgressItemState.Completed or ProjectBuildProgressItemState.Failed);
            Assert.Equal(2, progress.Updates.Take(firstTerminal).Count(update =>
                update.State == ProjectBuildProgressItemState.Started &&
                update.Detail == "building in MSBuild batch"));
            Assert.Equal(2, progress.Updates.Take(firstTerminal).Count(update =>
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

    private static IAnsiConsole CreateConsole(TextWriter writer, int height)
        => AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new TerminalConsoleOutput(writer, height),
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.Yes
        });

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

    private sealed class TerminalConsoleOutput : IAnsiConsoleOutput
    {
        public TerminalConsoleOutput(TextWriter writer, int height)
        {
            Writer = writer;
            Height = height;
        }

        public TextWriter Writer { get; }
        public bool IsTerminal => true;
        public int Width => 140;
        public int Height { get; }
        public void SetEncoding(System.Text.Encoding encoding) { }
    }
}
