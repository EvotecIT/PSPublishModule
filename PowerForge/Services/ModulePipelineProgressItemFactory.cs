namespace PowerForge;

/// <summary>
/// Creates the canonical ordered progress items shared by direct module builds,
/// isolated-host transport, and unified release presentation.
/// </summary>
internal static class ModulePipelineProgressItemFactory
{
    /// <summary>
    /// Maps a module plan to the single title, target, kind, and ordinal model used
    /// by every progress host.
    /// </summary>
    internal static PowerForgeReleaseProgressItem[] Create(ModulePipelinePlan plan)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        var steps = ModulePipelineStep.Create(plan);
        var totals = steps
            .GroupBy(GetPhase)
            .ToDictionary(group => group.Key, group => group.Count());
        var positions = new Dictionary<PowerForgeReleaseProgressPhase, int>();
        return steps.Select(step =>
        {
            var phase = GetPhase(step);
            positions.TryGetValue(phase, out var position);
            position++;
            positions[phase] = position;
            return CreateItem(step, plan, phase, position, totals[phase]);
        }).ToArray();
    }

    private static PowerForgeReleaseProgressItem CreateItem(
        ModulePipelineStep step,
        ModulePipelinePlan plan,
        PowerForgeReleaseProgressPhase phase,
        int position,
        int total)
    {
        var presentation = ModulePipelineStepPresentation.Create(step, plan);
        var packageLane = phase == PowerForgeReleaseProgressPhase.Packages;
        return new PowerForgeReleaseProgressItem
        {
            Phase = phase,
            Key = step.Key,
            Title = presentation.Title,
            Kind = presentation.Kind.ToString(),
            Target = presentation.Target,
            GroupKey = packageLane ? "Packages:Lanes" : null,
            GroupTitle = packageLane ? "Package workflows" : null,
            GroupOrder = packageLane ? 0 : null,
            CounterLabel = packageLane ? "Lane" : null,
            Position = position,
            Total = total
        };
    }

    private static PowerForgeReleaseProgressPhase GetPhase(ModulePipelineStep step)
        => step.Kind == ModulePipelineStepKind.PackageBuild
            ? PowerForgeReleaseProgressPhase.Packages
            : PowerForgeReleaseProgressPhase.Module;
}
