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
        return steps
            .Select((step, index) => CreateItem(step, plan, index + 1, steps.Length))
            .ToArray();
    }

    private static PowerForgeReleaseProgressItem CreateItem(
        ModulePipelineStep step,
        ModulePipelinePlan plan,
        int position,
        int total)
    {
        var presentation = ModulePipelineStepPresentation.Create(step, plan);
        return new PowerForgeReleaseProgressItem
        {
            Phase = PowerForgeReleaseProgressPhase.Module,
            Key = step.Key,
            Title = presentation.Title,
            Kind = presentation.Kind.ToString(),
            Target = presentation.Target,
            Position = position,
            Total = total
        };
    }
}
