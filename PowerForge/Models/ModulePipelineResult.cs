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
    /// Creates a new result instance.
    /// </summary>
    public ModulePipelineResult(ModulePipelinePlan plan, ModuleBuildResult buildResult, ModuleInstallerResult? installResult)
    {
        Plan = plan;
        BuildResult = buildResult;
        InstallResult = installResult;
    }
}

