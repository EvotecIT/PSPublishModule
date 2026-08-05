namespace PowerForge.Tests;

public sealed class PowerForgeReleaseProgressAdaptersTests
{
    [Fact]
    public void ProjectBuildAdapter_DerivesNestedGroupsFromTheOwningReleasePhase()
    {
        var release = new RecordingReleaseProgress();
        var adapter = new ProjectBuildReleaseProgressAdapter(
            release,
            PowerForgeReleaseProgressPhase.Versioning);
        var item = new ProjectBuildProgressItem
        {
            Phase = ProjectBuildProgressPhase.Versioning,
            Key = "ProjectA",
            Title = "ProjectA",
            Position = 1,
            Total = 1
        };

        adapter.ItemsPlanned(ProjectBuildProgressPhase.Versioning, [item]);

        var planned = Assert.Single(release.Planned);
        Assert.Equal("Versioning:Versioning", planned.GroupKey);
        Assert.StartsWith(PowerForgeReleaseProgressPhase.Versioning.ToString(), planned.GroupKey, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectBuildAdapter_ForwardsExistingPhaseContractAsDetailedReleaseItems()
    {
        var release = new RecordingReleaseProgress();
        var adapter = new ProjectBuildReleaseProgressAdapter(
            release,
            PowerForgeReleaseProgressPhase.Packages);

        adapter.PhaseStarted(ProjectBuildProgressPhase.PackageBuild, 4, "Packing Alpha");
        adapter.PhaseUpdated(ProjectBuildProgressPhase.PackageBuild, 2, 4, "Packing Beta");
        adapter.PhaseCompleted(ProjectBuildProgressPhase.PackageBuild, "4 packages");

        var item = Assert.Single(release.Planned);
        Assert.Equal(PowerForgeReleaseProgressPhase.Packages, item.Phase);
        Assert.Equal("Build packages and archives", item.Title);
        Assert.Collection(
            release.Updates,
            update =>
            {
                Assert.Equal(PowerForgeReleaseProgressItemState.Started, update.State);
                Assert.Contains("Project 00/04", update.Detail, StringComparison.Ordinal);
            },
            update =>
            {
                Assert.Equal(PowerForgeReleaseProgressItemState.Started, update.State);
                Assert.Contains("Project 02/04", update.Detail, StringComparison.Ordinal);
            },
            update =>
            {
                Assert.Equal(PowerForgeReleaseProgressItemState.Completed, update.State);
                Assert.Contains("Project 04/04", update.Detail, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void ProjectBuildAdapter_PreservesLastCountWhenPhaseFails()
    {
        var release = new RecordingReleaseProgress();
        var adapter = new ProjectBuildReleaseProgressAdapter(
            release,
            PowerForgeReleaseProgressPhase.Packages);

        adapter.PhaseStarted(ProjectBuildProgressPhase.PackageBuild, 4, "Packing Alpha");
        adapter.PhaseUpdated(ProjectBuildProgressPhase.PackageBuild, 2, 4, "Packing Beta");
        adapter.PhaseFailed(ProjectBuildProgressPhase.PackageBuild, "Packing failed");

        var failure = release.Updates[^1];
        Assert.Equal(PowerForgeReleaseProgressItemState.Failed, failure.State);
        Assert.Contains("Project 02/04", failure.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectBuildAdapter_ForwardsPerProjectTimingAndState()
    {
        var release = new RecordingReleaseProgress();
        var adapter = new ProjectBuildReleaseProgressAdapter(
            release,
            PowerForgeReleaseProgressPhase.Packages);
        var item = new ProjectBuildProgressItem
        {
            Phase = ProjectBuildProgressPhase.PackageBuild,
            Key = "package:Sample.Project",
            Title = "Sample.Project",
            Kind = nameof(ProjectBuildProgressPhase.PackageBuild),
            Position = 7,
            Total = 85
        };

        adapter.ItemsPlanned(ProjectBuildProgressPhase.PackageBuild, new[] { item });
        adapter.ItemUpdated(item, ProjectBuildProgressItemState.Started, "building");
        item.Duration = TimeSpan.FromSeconds(42);
        adapter.ItemUpdated(item, ProjectBuildProgressItemState.Completed, "1 package, 1 archive");

        var mapped = Assert.Single(release.Planned);
        Assert.Equal(PowerForgeReleaseProgressPhase.Packages, mapped.Phase);
        Assert.Equal("Sample.Project", mapped.Title);
        Assert.Equal(7, mapped.Position);
        Assert.Equal(85, mapped.Total);
        Assert.Equal(TimeSpan.FromSeconds(42), mapped.Duration);
        Assert.Collection(
            release.Updates,
            update => Assert.Equal(PowerForgeReleaseProgressItemState.Started, update.State),
            update =>
            {
                Assert.Equal(PowerForgeReleaseProgressItemState.Completed, update.State);
                Assert.Equal(TimeSpan.FromSeconds(42), update.Item.Duration);
            });
    }

    [Fact]
    public void DotNetPublishAdapter_ForwardsPlannedMatrixAndStepState()
    {
        var release = new RecordingReleaseProgress();
        var step = new DotNetPublishStep
        {
            Key = "publish:PowerForge:linux-x64",
            Kind = DotNetPublishStepKind.Publish,
            Title = "Publish PowerForge",
            TargetName = "PowerForge",
            Framework = "net10.0",
            Runtime = "linux-x64",
            Style = DotNetPublishStyle.Portable
        };
        var plan = new DotNetPublishPlan { Steps = new[] { step } };
        var adapter = new DotNetPublishReleaseProgressAdapter(release, plan);

        adapter.StepStarting(step);
        adapter.StepCompleted(step);

        var item = Assert.Single(release.Planned);
        Assert.Equal(1, item.Position);
        Assert.Equal(1, item.Total);
        Assert.Contains("PowerForge, net10.0, linux-x64", item.Title, StringComparison.Ordinal);
        Assert.Equal(
            new[]
            {
                PowerForgeReleaseProgressItemState.Started,
                PowerForgeReleaseProgressItemState.Completed
            },
            release.Updates.Select(update => update.State));
    }

    [Fact]
    public void GitHubReleaseAdapter_ForwardsAssetBytesAndTerminalState()
    {
        var release = new RecordingReleaseProgress();
        var adapter = new GitHubReleaseProgressAdapter(release);
        var assetPath = Path.Combine(Path.GetTempPath(), "PowerForge.Build.zip");

        adapter.Report(new GitHubReleaseAssetProgress
        {
            FilePath = assetPath,
            FileName = "PowerForge.Build.zip",
            Position = 4,
            TotalAssets = 24,
            State = GitHubReleaseAssetProgressState.Planned
        });
        adapter.Report(new GitHubReleaseAssetProgress
        {
            FilePath = assetPath,
            FileName = "PowerForge.Build.zip",
            Position = 4,
            TotalAssets = 24,
            State = GitHubReleaseAssetProgressState.Uploading,
            BytesTransferred = 25,
            TotalBytes = 100
        });
        adapter.Report(new GitHubReleaseAssetProgress
        {
            FilePath = assetPath,
            FileName = "PowerForge.Build.zip",
            Position = 4,
            TotalAssets = 24,
            State = GitHubReleaseAssetProgressState.Uploaded,
            BytesTransferred = 100,
            TotalBytes = 100
        });

        var item = Assert.Single(release.Planned);
        Assert.Equal(PowerForgeReleaseProgressPhase.GitHub, item.Phase);
        Assert.Equal(4, item.Position);
        Assert.Equal(24, item.Total);
        Assert.Equal(100, item.ProgressMaximum);
        Assert.Equal(100, item.ProgressValue);
        Assert.Collection(
            release.Updates,
            update =>
            {
                Assert.Equal(PowerForgeReleaseProgressItemState.Started, update.State);
                Assert.Contains("/", update.Detail, StringComparison.Ordinal);
            },
            update => Assert.Equal(PowerForgeReleaseProgressItemState.Completed, update.State));
    }

    [Fact]
    public void ProjectGitHubAdapter_ForwardsAssetsThroughThePackageLane()
    {
        var release = new RecordingReleaseProgress();
        var project = new ProjectBuildReleaseProgressAdapter(
            release,
            PowerForgeReleaseProgressPhase.Packages);
        var assetPath = Path.Combine(Path.GetTempPath(), "Sample.1.0.0.zip");
        var adapter = new ProjectBuildGitHubProgressAdapter(project, [assetPath]);

        adapter.Report(new GitHubReleaseAssetProgress
        {
            FilePath = assetPath,
            FileName = "Sample.1.0.0.zip",
            Position = 1,
            TotalAssets = 1,
            State = GitHubReleaseAssetProgressState.Uploading,
            BytesTransferred = 512,
            TotalBytes = 1024
        });
        adapter.Report(new GitHubReleaseAssetProgress
        {
            FilePath = assetPath,
            FileName = "Sample.1.0.0.zip",
            Position = 1,
            TotalAssets = 1,
            State = GitHubReleaseAssetProgressState.Uploaded,
            BytesTransferred = 1024,
            TotalBytes = 1024
        });

        var item = Assert.Single(release.Planned);
        Assert.Equal(PowerForgeReleaseProgressPhase.Packages, item.Phase);
        Assert.Equal("GitHubAsset", item.Kind);
        Assert.Equal("Sample.1.0.0.zip", item.Title);
        Assert.Collection(
            release.Updates,
            update =>
            {
                Assert.Equal(PowerForgeReleaseProgressItemState.Started, update.State);
                Assert.Contains("512 B / 1 KB", update.Detail, StringComparison.Ordinal);
            },
            update => Assert.Equal(PowerForgeReleaseProgressItemState.Completed, update.State));
    }

    [Fact]
    public void ProjectGitHubAdapter_UsesOneGlobalSequenceForSameNamedAssets()
    {
        var release = new RecordingReleaseProgress();
        var project = new ProjectBuildReleaseProgressAdapter(
            release,
            PowerForgeReleaseProgressPhase.Packages);
        var firstPath = Path.Combine(Path.GetTempPath(), "ProjectA", "Package.zip");
        var secondPath = Path.Combine(Path.GetTempPath(), "ProjectB", "Package.zip");
        var adapter = new ProjectBuildGitHubProgressAdapter(project, [firstPath, secondPath]);

        foreach (var path in new[] { firstPath, secondPath })
        {
            adapter.Report(new GitHubReleaseAssetProgress
            {
                FilePath = path,
                FileName = "Package.zip",
                Position = 1,
                TotalAssets = 1,
                State = GitHubReleaseAssetProgressState.Uploaded
            });
        }

        Assert.Collection(
            release.Planned,
            first =>
            {
                Assert.Equal(1, first.Position);
                Assert.Equal(2, first.Total);
            },
            second =>
            {
                Assert.Equal(2, second.Position);
                Assert.Equal(2, second.Total);
            });
        Assert.Equal(2, release.Planned.Select(static item => item.Key).Distinct(StringComparer.Ordinal).Count());
    }

    private sealed class RecordingReleaseProgress : IPowerForgeReleaseProgressReporterV2
    {
        public List<PowerForgeReleaseProgressItem> Planned { get; } = new();

        public List<(PowerForgeReleaseProgressItem Item, PowerForgeReleaseProgressItemState State, string? Detail)> Updates { get; } = new();

        public void PhaseStarted(PowerForgeReleaseProgressPhase phase, int totalItems, string? detail = null) { }

        public void PhaseCompleted(PowerForgeReleaseProgressPhase phase, string? detail = null) { }

        public void PhaseFailed(PowerForgeReleaseProgressPhase phase, string? detail = null) { }

        public void ItemsPlanned(
            PowerForgeReleaseProgressPhase phase,
            IReadOnlyList<PowerForgeReleaseProgressItem> items)
            => Planned.AddRange(items);

        public void ItemUpdated(
            PowerForgeReleaseProgressItem item,
            PowerForgeReleaseProgressItemState state,
            string? detail = null)
            => Updates.Add((item, state, detail));
    }
}

public sealed class ProcessRunnerStreamingTests
{
    [Fact]
    public async Task RunAsync_CapturesOutputAndForwardsLines()
    {
        var lines = new List<string>();
        var result = await new ProcessRunner().RunAsync(new ProcessRunRequest(
            "dotnet",
            Directory.GetCurrentDirectory(),
            new[] { "--version" },
            TimeSpan.FromSeconds(30),
            environmentVariables: null,
            captureOutput: true,
            captureError: true,
            outputLineReceived: lines.Add,
            errorLineReceived: null));

        Assert.True(result.Succeeded, result.StdErr);
        var line = Assert.Single(lines);
        Assert.Contains(line, result.StdOut, StringComparison.Ordinal);
    }
}
