namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private void SynchronizeModuleVersionAndCheckpoint(
        ModulePipelinePlan plan,
        ModulePipelineExecutionSession session,
        ModulePipelineRunState state)
        => session.Run(session.VersionSynchronizationStep, () =>
        {
            SynchronizeModuleVersionFromReleaseSource(plan, state);
            InitializeSynchronizedReleaseCheckpoint(plan, state);
            PreflightSynchronizedPackageGitHubRetrySafety(plan, state, onlyAvailableResults: true);
        });

    private void ExecuteTypeAcceleratorSurfaceReportPhase(
        ModulePipelinePlan plan,
        ModulePipelineExecutionSession session,
        ModulePipelineRunState state)
    {
        var buildResult = state.RequireBuildResult();
        if (plan.BuildSpec.RefreshManifestOnly)
            return;

        session.Run(session.TypeAcceleratorReportStep, () => TryWriteTypeAcceleratorSurfaceReport(plan, buildResult, state));
    }
}
