namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private void ExecuteCompilationPhase(
        ModulePipelinePlan plan,
        ModulePipelineExecutionSession session,
        ModulePipelineRunState state,
        RequiredModuleReference[] manifestRequiredModules,
        string[] manifestExternalModuleDependencies)
    {
        if (plan.BuildSpec.PowerShellCompilation?.Enabled != true) return;
        session.Start(session.CompilationStep);
        try
        {
            ApplyPowerShellModuleCompilation(plan, state, manifestRequiredModules, manifestExternalModuleDependencies);
            session.Done(session.CompilationStep);
        }
        catch (Exception ex)
        {
            session.Fail(session.CompilationStep, ex);
            throw;
        }
    }

    private void ApplyPowerShellModuleCompilation(
        ModulePipelinePlan plan,
        ModulePipelineRunState state,
        RequiredModuleReference[] manifestRequiredModules,
        string[] manifestExternalModuleDependencies)
    {
        var configuration = plan.BuildSpec.PowerShellCompilation;
        if (configuration?.Enabled != true) return;

        var releaseContract = PowerShellModuleCompilationReleaseContract.Create(
            plan,
            manifestRequiredModules,
            manifestExternalModuleDependencies);
        var integrator = new PowerShellModuleCompilationIntegrator();
        var integrated = plan.BuildSpec.ReuseStaging
            ? integrator.Restore(state.RequireBuildResult(), configuration, releaseContract, plan.Signing)
            : integrator.Compile(state.RequireBuildResult(), configuration, releaseContract);
        if (plan.BuildSpec.ReuseStaging)
            plan.SourceCommitSha = integrated.CompilationResult.SourceCommitSha;
        else
            integrated.CompilationResult.SourceCommitSha = plan.SourceCommitSha;
        state.BuildResult = integrated.BuildResult;
        state.PowerShellCompilationResult = integrated.CompilationResult;
        state.PowerShellCompilationReleaseContract = releaseContract;
        _logger.Info(
            $"Compiled staged PowerShell module as {configuration.Mode} binary module: " +
            $"{integrated.CompilationResult.CompiledUnits}/{integrated.CompilationResult.TotalUnits} typed units " +
            $"({integrated.CompilationResult.CoveragePercentage:F2}%).");
    }

    private static void PersistPowerShellModuleCompilationCheckpoint(
        ModulePipelinePlan plan,
        ModulePipelineRunState state,
        RequiredModuleReference[] manifestRequiredModules,
        string[] manifestExternalModuleDependencies)
    {
        if (state.PowerShellCompilationResult is null) return;
        state.PowerShellCompilationReleaseContract = PowerShellModuleCompilationReleaseContract.Create(
            plan,
            manifestRequiredModules,
            manifestExternalModuleDependencies);
        new PowerShellModuleCompilationIntegrator().PersistCheckpoint(
            state.PowerShellCompilationResult,
            plan.BuildSpec.PowerShellCompilation
            ?? throw new InvalidOperationException("PowerShell compilation configuration is unavailable."),
            state.PowerShellCompilationReleaseContract
            ?? throw new InvalidOperationException("PowerShell compilation release contract is unavailable."),
            state.SigningResult,
            plan.Signing);
        state.BuildResult!.FinalizedPayloadFiles = state.PowerShellCompilationResult.FinalizedPayloadFiles;
        CaptureFinalizedModulePayloadIntegrity(state);
    }
}
