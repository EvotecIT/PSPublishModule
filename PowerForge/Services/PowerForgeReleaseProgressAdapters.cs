namespace PowerForge;

internal sealed class ProjectBuildReleaseProgressAdapter : IProjectBuildProgressReporterV2
{
    private readonly IPowerForgeReleaseProgressReporterV2 _release;
    private readonly PowerForgeReleaseProgressPhase _releasePhase;
    private readonly Dictionary<ProjectBuildProgressPhase, PowerForgeReleaseProgressItem> _items = new();
    private readonly Dictionary<string, PowerForgeReleaseProgressItem> _projectItems = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ProjectBuildProgressPhase, (int Completed, int Total)> _phaseCounts = new();

    internal ProjectBuildReleaseProgressAdapter(
        IPowerForgeReleaseProgressReporterV2 release,
        PowerForgeReleaseProgressPhase releasePhase)
    {
        _release = release;
        _releasePhase = releasePhase;
    }

    public void PhaseStarted(ProjectBuildProgressPhase phase, int totalItems, string? detail = null)
    {
        RememberCount(phase, 0, totalItems);
        var item = GetOrCreate(phase);
        _release.ItemUpdated(item, PowerForgeReleaseProgressItemState.Started, WithCount(phase, detail, 0, totalItems));
    }

    public void PhaseUpdated(
        ProjectBuildProgressPhase phase,
        int completedItems,
        int totalItems,
        string? detail = null)
    {
        RememberCount(phase, completedItems, totalItems);
        var item = GetOrCreate(phase);
        _release.ItemUpdated(
            item,
            PowerForgeReleaseProgressItemState.Started,
            WithCount(phase, detail, completedItems, totalItems));
    }

    public void PhaseCompleted(ProjectBuildProgressPhase phase, string? detail = null)
    {
        var count = GetTerminalCount(phase, completed: true);
        _release.ItemUpdated(
            GetOrCreate(phase),
            PowerForgeReleaseProgressItemState.Completed,
            WithCount(phase, detail, count.Completed, count.Total));
    }

    public void PhaseFailed(ProjectBuildProgressPhase phase, string? detail = null)
    {
        var count = GetTerminalCount(phase, completed: false);
        _release.ItemUpdated(
            GetOrCreate(phase),
            PowerForgeReleaseProgressItemState.Failed,
            WithCount(phase, detail, count.Completed, count.Total));
    }

    public void ItemsPlanned(
        ProjectBuildProgressPhase phase,
        IReadOnlyList<ProjectBuildProgressItem> items)
    {
        if (items is null || items.Count == 0)
            return;

        var mapped = items
            .Where(item => item is not null)
            .Select(item => MapItem(item))
            .ToArray();
        _release.ItemsPlanned(_releasePhase, mapped);
    }

    public void ItemUpdated(
        ProjectBuildProgressItem item,
        ProjectBuildProgressItemState state,
        string? detail = null)
    {
        if (item is null)
            return;

        var mapped = MapItem(item);
        mapped.Duration = item.Duration;
        _release.ItemUpdated(
            mapped,
            state switch
            {
                ProjectBuildProgressItemState.Started => PowerForgeReleaseProgressItemState.Started,
                ProjectBuildProgressItemState.Completed => PowerForgeReleaseProgressItemState.Completed,
                ProjectBuildProgressItemState.Failed => PowerForgeReleaseProgressItemState.Failed,
                ProjectBuildProgressItemState.Skipped => PowerForgeReleaseProgressItemState.Skipped,
                _ => PowerForgeReleaseProgressItemState.Planned
            },
            detail);
    }

    private PowerForgeReleaseProgressItem GetOrCreate(ProjectBuildProgressPhase phase)
    {
        if (_items.TryGetValue(phase, out var item))
            return item;

        item = new PowerForgeReleaseProgressItem
        {
            Phase = _releasePhase,
            Key = $"project:{phase}",
            Title = GetTitle(phase),
            Kind = phase.ToString(),
            GroupKey = GetGroupKey(phase),
            GroupTitle = GetTitle(phase),
            GroupOrder = GetGroupOrder(phase),
            CounterLabel = GetCounterLabel(phase)
        };
        _items[phase] = item;
        _release.ItemsPlanned(_releasePhase, new[] { item });
        return item;
    }

    private PowerForgeReleaseProgressItem MapItem(ProjectBuildProgressItem item)
    {
        var key = $"project:{item.Phase}:{item.Key}";
        if (_projectItems.TryGetValue(key, out var mapped))
        {
            mapped.Duration = item.Duration;
            return mapped;
        }

        mapped = new PowerForgeReleaseProgressItem
        {
            Phase = _releasePhase,
            Key = key,
            Title = item.Title,
            Kind = item.Kind,
            GroupKey = GetGroupKey(item.Phase),
            GroupTitle = GetTitle(item.Phase),
            GroupOrder = GetGroupOrder(item.Phase),
            CounterLabel = GetCounterLabel(item.Phase),
            Position = item.Position,
            Total = item.Total,
            Duration = item.Duration
        };
        _projectItems[key] = mapped;
        return mapped;
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

    private string GetGroupKey(ProjectBuildProgressPhase phase)
        => $"{_releasePhase}:{phase}";

    private static int GetGroupOrder(ProjectBuildProgressPhase phase)
        => phase switch
        {
            ProjectBuildProgressPhase.Plan => 10,
            ProjectBuildProgressPhase.Versioning => 20,
            ProjectBuildProgressPhase.PackageBuild => 30,
            ProjectBuildProgressPhase.PackageSigning => 40,
            ProjectBuildProgressPhase.NuGetPublish => 50,
            ProjectBuildProgressPhase.GitHubPublish => 60,
            _ => 90
        };

    private static string GetCounterLabel(ProjectBuildProgressPhase phase)
        => phase switch
        {
            ProjectBuildProgressPhase.PackageSigning => "Package",
            ProjectBuildProgressPhase.NuGetPublish => "Package",
            ProjectBuildProgressPhase.GitHubPublish => "Asset",
            _ => "Project"
        };

    private void RememberCount(
        ProjectBuildProgressPhase phase,
        int completed,
        int total)
    {
        if (total <= 0) {
            return;
        }

        _phaseCounts[phase] = (Math.Min(Math.Max(0, completed), total), total);
    }

    private (int Completed, int Total) GetTerminalCount(
        ProjectBuildProgressPhase phase,
        bool completed)
    {
        if (!_phaseCounts.TryGetValue(phase, out var count)) {
            return (0, 0);
        }

        return completed ? (count.Total, count.Total) : count;
    }

    private static string? WithCount(
        ProjectBuildProgressPhase phase,
        string? detail,
        int completed,
        int total)
    {
        if (total <= 0)
            return detail;

        var count = ProgressCounterFormatter.Format(
            ProgressCounterFormatter.GetProjectBuildScope(phase),
            completed,
            total);
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
