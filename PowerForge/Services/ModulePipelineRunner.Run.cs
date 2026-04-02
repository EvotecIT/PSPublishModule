namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private static readonly IScriptFunctionExportDetector ScriptFunctionExportDetector = new PowerShellScriptFunctionExportDetector();

    /// <summary>
    /// Executes the pipeline described by <paramref name="spec"/>.
    /// </summary>
    public ModulePipelineResult Run(ModulePipelineSpec spec)
    {
        var plan = Plan(spec);
        return Run(spec, plan, progress: null);
    }

    /// <summary>
    /// Executes the pipeline described by <paramref name="spec"/> using a precomputed <paramref name="plan"/>.
    /// </summary>
    public ModulePipelineResult Run(ModulePipelineSpec spec, ModulePipelinePlan plan, IModulePipelineProgressReporter? progress = null)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        var manifestRequiredModules = ResolveOutputRequiredModules(plan.RequiredModules, plan.MergeMissing, plan.ApprovedModules);
        var packagingRequiredModules = ResolveOutputRequiredModules(plan.RequiredModulesForPackaging, plan.MergeMissing, plan.ApprovedModules);
        var manifestExternalModuleDependencies = plan.ExternalModuleDependencies ?? Array.Empty<string>();

        var session = ModulePipelineExecutionSession.Create(plan, progress);
        var cleanupStep = session.CleanupStep;

        var pipeline = ModuleBuildPipelineFactory.Create(_logger);
        var state = new ModulePipelineRunState(plan.BuildSpec.StagingPath);

        try
        {
            ExecutePreparationAndBuildPhases(plan, session, manifestRequiredModules, manifestExternalModuleDependencies, pipeline, state);
            ExecuteDocumentationPhase(plan, session, session.Reporter, state);
            ExecuteFormattingAndSigningPhases(plan, session, manifestRequiredModules, manifestExternalModuleDependencies, state);
            ExecuteValidationPhases(plan, session, state);
            ExecuteTestPhases(plan, session, state);
            ExecutePackagingPublishAndInstallPhases(spec, plan, session, packagingRequiredModules, pipeline, state);

            state.ProjectManifestSyncMessage = SyncBuildManifestToProjectRoot(plan);

            return BuildPipelineResult(spec, plan, state);
        }
        catch (Exception ex)
        {
            state.PipelineFailure = ex;
            throw;
        }
        finally
        {
            if (plan.DeleteGeneratedStagingAfterRun)
            {
                session.Start(cleanupStep);
                try { DeleteDirectoryWithRetries(state.StagingPathForCleanup); }
                catch (Exception ex) { _logger.Warn($"Failed to delete staging folder: {ex.Message}"); }
                session.Done(cleanupStep);
            }

            if (state.PipelineFailure is not null)
                session.NotifySkippedOnFailure();
        }
    }

}
