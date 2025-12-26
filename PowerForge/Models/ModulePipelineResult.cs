namespace PowerForge;

/// <summary>
/// Result of executing a <see cref="ModulePipelineSpec"/>.
/// </summary>
public sealed class ModulePipelineResult
{
    /// <summary>
    /// Planned execution details.
    /// </summary>
    public ModulePipelinePlan Plan { get; }

    /// <summary>
    /// Build result produced by <see cref="ModuleBuildPipeline.BuildToStaging"/>.
    /// </summary>
    public ModuleBuildResult BuildResult { get; }

    /// <summary>
    /// Install result when install was enabled; otherwise null.
    /// </summary>
    public ModuleInstallerResult? InstallResult { get; }

    /// <summary>
    /// Documentation result when documentation generation was enabled; otherwise null.
    /// </summary>
    public DocumentationBuildResult? DocumentationResult { get; }

    /// <summary>
    /// Publish results produced during the run.
    /// </summary>
    public ModulePublishResult[] PublishResults { get; }

    /// <summary>
    /// Artefact results produced during the run.
    /// </summary>
    public ArtefactBuildResult[] ArtefactResults { get; }

    /// <summary>
    /// Creates a new result instance.
    /// </summary>
    public ModulePipelineResult(
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        ModuleInstallerResult? installResult,
        DocumentationBuildResult? documentationResult,
        ModulePublishResult[] publishResults,
        ArtefactBuildResult[] artefactResults)
    {
        Plan = plan;
        BuildResult = buildResult;
        InstallResult = installResult;
        DocumentationResult = documentationResult;
        PublishResults = publishResults ?? Array.Empty<ModulePublishResult>();
        ArtefactResults = artefactResults ?? Array.Empty<ArtefactBuildResult>();
    }
}
