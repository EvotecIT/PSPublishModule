namespace PowerForge;

/// <summary>
/// Bridges a nested project/package workflow into a module pipeline progress host
/// while keeping every lane's item keys independent.
/// </summary>
internal sealed class ModulePipelineNestedProgressAdapter : IPowerForgeReleaseProgressReporterV2
{
    private readonly IModulePipelineProgressReporterV4 _reporter;
    private readonly string _keyPrefix;
    private readonly Dictionary<string, PowerForgeReleaseProgressItem> _items =
        new(StringComparer.OrdinalIgnoreCase);

    internal ModulePipelineNestedProgressAdapter(
        IModulePipelineProgressReporterV4 reporter,
        ModulePipelineStep parentStep)
    {
        _reporter = reporter ?? throw new ArgumentNullException(nameof(reporter));
        if (parentStep is null) throw new ArgumentNullException(nameof(parentStep));
        _keyPrefix = string.IsNullOrWhiteSpace(parentStep.Key)
            ? "package-lane"
            : parentStep.Key;
    }

    public void PhaseStarted(PowerForgeReleaseProgressPhase phase, int totalItems, string? detail = null)
    {
    }

    public void PhaseCompleted(PowerForgeReleaseProgressPhase phase, string? detail = null)
    {
    }

    public void PhaseFailed(PowerForgeReleaseProgressPhase phase, string? detail = null)
    {
    }

    public void ItemsPlanned(
        PowerForgeReleaseProgressPhase phase,
        IReadOnlyList<PowerForgeReleaseProgressItem> items)
    {
        if (items is null || items.Count == 0)
            return;

        var mapped = items
            .Where(static item => item is not null)
            .Select(Map)
            .ToArray();
        if (mapped.Length == 0)
            return;

        try
        {
            _reporter.ItemsPlanned(phase, mapped);
        }
        catch
        {
            // Progress is observational and must never change the build result.
        }
    }

    public void ItemUpdated(
        PowerForgeReleaseProgressItem item,
        PowerForgeReleaseProgressItemState state,
        string? detail = null)
    {
        if (item is null)
            return;

        try
        {
            _reporter.ItemUpdated(Map(item), state, detail);
        }
        catch
        {
            // Progress is observational and must never change the build result.
        }
    }

    private PowerForgeReleaseProgressItem Map(PowerForgeReleaseProgressItem item)
    {
        var sourceKey = $"{item.Phase}:{item.Key}";
        if (_items.TryGetValue(sourceKey, out var mapped))
        {
            CopyMutableValues(item, mapped);
            return mapped;
        }

        mapped = new PowerForgeReleaseProgressItem
        {
            Phase = item.Phase,
            Key = $"{_keyPrefix}:{item.Key}",
            Title = item.Title,
            Kind = item.Kind,
            Target = item.Target,
            GroupKey = item.GroupKey,
            GroupTitle = item.GroupTitle,
            GroupOrder = item.GroupOrder,
            CounterLabel = item.CounterLabel,
            Position = item.Position,
            Total = item.Total
        };
        CopyMutableValues(item, mapped);
        _items[sourceKey] = mapped;
        return mapped;
    }

    private static void CopyMutableValues(
        PowerForgeReleaseProgressItem source,
        PowerForgeReleaseProgressItem target)
    {
        target.ProgressValue = source.ProgressValue;
        target.ProgressMaximum = source.ProgressMaximum;
        target.Duration = source.Duration;
    }
}
