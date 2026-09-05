namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
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
