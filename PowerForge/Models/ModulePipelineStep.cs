namespace PowerForge;

/// <summary>
/// High-level pipeline step kinds used for progress reporting and UI rendering.
/// </summary>
public enum ModulePipelineStepKind
{
    /// <summary>Build module into staging.</summary>
    Build = 0,
    /// <summary>Generate documentation (markdown + external help).</summary>
    Documentation = 1,
    /// <summary>Create an artefact output (packed/unpacked).</summary>
    Artefact = 2,
    /// <summary>Publish to a repository or GitHub.</summary>
    Publish = 3,
    /// <summary>Install the module into module roots.</summary>
    Install = 4,
    /// <summary>Delete generated staging directory.</summary>
    Cleanup = 5,
}

/// <summary>
/// Represents a planned pipeline step (for progress reporting and UI rendering).
/// </summary>
public sealed class ModulePipelineStep
{
    /// <summary>Step kind.</summary>
    public ModulePipelineStepKind Kind { get; }

    /// <summary>
    /// Stable key for the step within a single planned run. Intended for mapping UI tasks.
    /// </summary>
    public string Key { get; }

    /// <summary>Human-readable step title.</summary>
    public string Title { get; }

    /// <summary>Optional artefact segment associated with the step.</summary>
    public ConfigurationArtefactSegment? ArtefactSegment { get; }

    /// <summary>Optional publish segment associated with the step.</summary>
    public ConfigurationPublishSegment? PublishSegment { get; }

    private ModulePipelineStep(
        ModulePipelineStepKind kind,
        string key,
        string title,
        ConfigurationArtefactSegment? artefactSegment = null,
        ConfigurationPublishSegment? publishSegment = null)
    {
        Kind = kind;
        Key = key ?? string.Empty;
        Title = title ?? string.Empty;
        ArtefactSegment = artefactSegment;
        PublishSegment = publishSegment;
    }

    /// <summary>
    /// Creates the ordered list of steps for a given pipeline plan.
    /// </summary>
    public static ModulePipelineStep[] Create(ModulePipelinePlan plan)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        var steps = new List<ModulePipelineStep>();

        // 1) Build (always)
        steps.Add(new ModulePipelineStep(
            kind: ModulePipelineStepKind.Build,
            key: "build",
            title: "Build to staging"));

        // 2) Docs
        if (plan.Documentation is not null && plan.DocumentationBuild?.Enable == true)
        {
            steps.Add(new ModulePipelineStep(
                kind: ModulePipelineStepKind.Documentation,
                key: "docs",
                title: "Generate docs"));
        }

        // 3) Artefacts
        if (plan.Artefacts is { Length: > 0 })
        {
            for (int i = 0; i < plan.Artefacts.Length; i++)
            {
                var a = plan.Artefacts[i];
                if (a is null) continue;

                var id = a.Configuration?.ID;
                var label = $"Pack {a.ArtefactType}";
                if (!string.IsNullOrWhiteSpace(id)) label += $" ({id})";        

                var key = $"artefact:{i + 1:00}:{a.ArtefactType}:{(id ?? string.Empty)}";
                steps.Add(new ModulePipelineStep(
                    kind: ModulePipelineStepKind.Artefact,
                    key: key,
                    title: label,
                    artefactSegment: a));
            }
        }

        // 4) Publishes
        if (plan.Publishes is { Length: > 0 })
        {
            for (int i = 0; i < plan.Publishes.Length; i++)
            {
                var p = plan.Publishes[i];
                if (p is null) continue;

                var cfg = p.Configuration ?? new PublishConfiguration();       
                var id = cfg.ID;
                var repoName = cfg.Repository?.Name ?? cfg.RepositoryName;      

                var label = $"Publish {cfg.Destination}";
                if (!string.IsNullOrWhiteSpace(repoName)) label += $" ({repoName})";
                if (!string.IsNullOrWhiteSpace(id)) label += $" [{id}]";

                var key = $"publish:{i + 1:00}:{cfg.Destination}:{(id ?? string.Empty)}";
                steps.Add(new ModulePipelineStep(
                    kind: ModulePipelineStepKind.Publish,
                    key: key,
                    title: label,
                    publishSegment: p));
            }
        }

        // 5) Install
        if (plan.InstallEnabled)
        {
            steps.Add(new ModulePipelineStep(
                kind: ModulePipelineStepKind.Install,
                key: "install",
                title: $"Install ({plan.InstallStrategy}, keep {plan.InstallKeepVersions})"));
        }

        // 6) Cleanup staging
        if (plan.DeleteGeneratedStagingAfterRun)
        {
            steps.Add(new ModulePipelineStep(
                kind: ModulePipelineStepKind.Cleanup,
                key: "cleanup",
                title: "Cleanup staging"));
        }

        return steps.ToArray();
    }
}
