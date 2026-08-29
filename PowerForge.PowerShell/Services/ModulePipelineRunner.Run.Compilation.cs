namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private void ApplyPowerShellModuleCompilation(ModulePipelinePlan plan, ModulePipelineRunState state)
    {
        var configuration = plan.BuildSpec.PowerShellCompilation;
        if (configuration?.Enabled != true) return;

        var integrator = new PowerShellModuleCompilationIntegrator();
        var integrated = plan.BuildSpec.ReuseStaging
            ? integrator.Restore(state.RequireBuildResult(), configuration)
            : integrator.Compile(state.RequireBuildResult(), configuration);
        state.BuildResult = integrated.BuildResult;
        state.PowerShellCompilationResult = integrated.CompilationResult;
        _logger.Info(
            $"Compiled staged PowerShell module as {configuration.Mode} binary module: " +
            $"{integrated.CompilationResult.CompiledUnits}/{integrated.CompilationResult.TotalUnits} typed units " +
            $"({integrated.CompilationResult.CoveragePercentage:F2}%).");
    }

    private static void PersistPowerShellModuleCompilationCheckpoint(ModulePipelinePlan plan, ModulePipelineRunState state)
    {
        if (state.PowerShellCompilationResult is null) return;
        new PowerShellModuleCompilationIntegrator().PersistCheckpoint(
            state.PowerShellCompilationResult,
            plan.BuildSpec.PowerShellCompilation
            ?? throw new InvalidOperationException("PowerShell compilation configuration is unavailable."));
    }
}
