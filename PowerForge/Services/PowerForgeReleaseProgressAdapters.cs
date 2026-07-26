namespace PowerForge;

internal sealed class ProjectBuildReleaseProgressAdapter : IProjectBuildProgressReporter
{
    private readonly IPowerForgeReleaseProgressReporterV2 _release;
    private readonly PowerForgeReleaseProgressPhase _releasePhase;
    private readonly Dictionary<ProjectBuildProgressPhase, PowerForgeReleaseProgressItem> _items = new();

    internal ProjectBuildReleaseProgressAdapter(
        IPowerForgeReleaseProgressReporterV2 release,
        PowerForgeReleaseProgressPhase releasePhase)
    {
        _release = release;
        _releasePhase = releasePhase;
    }

    public void PhaseStarted(ProjectBuildProgressPhase phase, int totalItems, string? detail = null)
    {
        var item = GetOrCreate(phase);
        _release.ItemUpdated(item, PowerForgeReleaseProgressItemState.Started, WithCount(detail, 0, totalItems));
    }

    public void PhaseUpdated(
        ProjectBuildProgressPhase phase,
        int completedItems,
        int totalItems,
        string? detail = null)
    {
        var item = GetOrCreate(phase);
        _release.ItemUpdated(
            item,
            PowerForgeReleaseProgressItemState.Started,
            WithCount(detail, completedItems, totalItems));
    }

    public void PhaseCompleted(ProjectBuildProgressPhase phase, string? detail = null)
        => _release.ItemUpdated(GetOrCreate(phase), PowerForgeReleaseProgressItemState.Completed, detail);

    public void PhaseFailed(ProjectBuildProgressPhase phase, string? detail = null)
        => _release.ItemUpdated(GetOrCreate(phase), PowerForgeReleaseProgressItemState.Failed, detail);

    private PowerForgeReleaseProgressItem GetOrCreate(ProjectBuildProgressPhase phase)
    {
        if (_items.TryGetValue(phase, out var item))
            return item;

        item = new PowerForgeReleaseProgressItem
        {
            Phase = _releasePhase,
            Key = $"project:{phase}",
            Title = GetTitle(phase),
            Kind = phase.ToString()
        };
        _items[phase] = item;
        _release.ItemsPlanned(_releasePhase, new[] { item });
        return item;
    }

    private static string GetTitle(ProjectBuildProgressPhase phase)
        => phase switch
        {
            ProjectBuildProgressPhase.Plan => "Prepare project build plan",
            ProjectBuildProgressPhase.Versioning => "Resolve project versions",
            ProjectBuildProgressPhase.PackageBuild => "Build packages and archives",
            ProjectBuildProgressPhase.PackageSigning => "Sign NuGet packages",
            ProjectBuildProgressPhase.NuGetPublish => "Publish NuGet packages",
            ProjectBuildProgressPhase.GitHubPublish => "Publish project GitHub release",
            _ => phase.ToString()
        };

    private static string? WithCount(string? detail, int completed, int total)
    {
        if (total <= 0)
            return detail;

        var count = $"{Math.Max(0, completed)}/{total}";
        return string.IsNullOrWhiteSpace(detail) ? count : $"{count} — {detail}";
    }
}

internal sealed class DotNetPublishReleaseProgressAdapter : IDotNetPublishProgressReporter
{
    private readonly IPowerForgeReleaseProgressReporterV2 _release;
    private readonly IReadOnlyDictionary<string, PowerForgeReleaseProgressItem> _items;

    internal DotNetPublishReleaseProgressAdapter(
        IPowerForgeReleaseProgressReporterV2 release,
        DotNetPublishPlan plan)
    {
        _release = release;
        var steps = plan.Steps ?? Array.Empty<DotNetPublishStep>();
        var items = steps.Select((step, index) => new PowerForgeReleaseProgressItem
        {
            Phase = PowerForgeReleaseProgressPhase.Tools,
            Key = step.Key,
            Title = BuildTitle(step),
            Kind = step.Kind.ToString(),
            Position = index + 1,
            Total = steps.Length
        }).ToArray();
        _items = items.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
        _release.ItemsPlanned(PowerForgeReleaseProgressPhase.Tools, items);
    }

    public void StepStarting(DotNetPublishStep step)
        => Update(step, PowerForgeReleaseProgressItemState.Started);

    public void StepCompleted(DotNetPublishStep step)
        => Update(step, PowerForgeReleaseProgressItemState.Completed);

    public void StepFailed(DotNetPublishStep step, Exception error)
        => Update(step, PowerForgeReleaseProgressItemState.Failed, error.Message);

    private void Update(
        DotNetPublishStep step,
        PowerForgeReleaseProgressItemState state,
        string? detail = null)
    {
        if (step is null || !_items.TryGetValue(step.Key, out var item))
            return;

        _release.ItemUpdated(item, state, detail);
    }

    private static string BuildTitle(DotNetPublishStep step)
    {
        var dimensions = new[]
        {
            step.TargetName,
            step.Framework,
            step.Runtime,
            step.Style?.ToString()
        }.Where(value => !string.IsNullOrWhiteSpace(value));
        var suffix = string.Join(", ", dimensions);
        return string.IsNullOrWhiteSpace(suffix)
            ? step.Title
            : $"{step.Title} ({suffix})";
    }
}
