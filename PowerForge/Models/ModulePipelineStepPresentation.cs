namespace PowerForge;

/// <summary>
/// Describes the host-neutral title and target shown for a module pipeline step.
/// </summary>
internal sealed class ModulePipelineStepPresentation
{
    private ModulePipelineStepPresentation(
        string title,
        string target,
        ModulePipelineStepKind kind)
    {
        Title = title;
        Target = target;
        Kind = kind;
    }

    /// <summary>Concise action title shared by direct and unified build hosts.</summary>
    internal string Title { get; }

    /// <summary>Optional semantic destination or artifact target.</summary>
    internal string Target { get; }

    /// <summary>Pipeline kind used by presentation hosts to select an icon.</summary>
    internal ModulePipelineStepKind Kind { get; }

    /// <summary>
    /// Creates the canonical display data for a planned module step.
    /// </summary>
    internal static ModulePipelineStepPresentation Create(
        ModulePipelineStep step,
        ModulePipelinePlan plan)
    {
        if (step is null) throw new ArgumentNullException(nameof(step));
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        var title = step.Kind switch
        {
            ModulePipelineStepKind.Artefact => "Pack artefact",
            ModulePipelineStepKind.Publish => "Publish",
            ModulePipelineStepKind.Install => "Install",
            ModulePipelineStepKind.Cleanup => "Cleanup staging",
            _ => step.Title
        };

        var target = step.Kind switch
        {
            ModulePipelineStepKind.Artefact => FormatArtefactTarget(step.ArtefactSegment),
            ModulePipelineStepKind.Publish => FormatPublishTarget(step.PublishSegment),
            ModulePipelineStepKind.Install => $"{plan.InstallStrategy}, keep {plan.InstallKeepVersions}",
            _ => string.Empty
        };

        return new ModulePipelineStepPresentation(title, target, step.Kind);
    }

    private static string FormatArtefactTarget(ConfigurationArtefactSegment? segment)
    {
        if (segment is null)
            return string.Empty;

        var id = segment.Configuration?.ID;
        var label = segment.ArtefactType.ToString();
        return string.IsNullOrWhiteSpace(id) ? label : $"{label} ({id})";
    }

    private static string FormatPublishTarget(ConfigurationPublishSegment? segment)
    {
        if (segment is null)
            return string.Empty;

        var configuration = segment.Configuration ?? new PublishConfiguration();
        var repositoryName = configuration.Repository?.Name ?? configuration.RepositoryName;
        var qualifiers = new[] { configuration.ID, repositoryName }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        return qualifiers.Length == 0
            ? configuration.Destination.ToString()
            : $"{configuration.Destination} ({string.Join(", ", qualifiers)})";
    }
}
