namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    /// <summary>
    /// Executes the pipeline described by <paramref name="spec"/>.
    /// </summary>
    public ModulePipelineResult Run(ModulePipelineSpec spec)
    {
        EnsureDocumentationGateConfigured(spec);
        EnsureRequiredModuleOnlineResolutionToolInstalledIfNeededForRun(spec);
        var plan = Plan(spec);
        return Run(spec, plan, progress: null, preflightPrecomputedPlan: false);
    }

    /// <summary>
    /// Executes the pipeline described by <paramref name="spec"/> using a precomputed <paramref name="plan"/>.
    /// </summary>
    public ModulePipelineResult Run(ModulePipelineSpec spec, ModulePipelinePlan plan, IModulePipelineProgressReporter? progress = null)
        => Run(spec, plan, progress, preflightPrecomputedPlan: true);

    private ModulePipelineResult Run(
        ModulePipelineSpec spec,
        ModulePipelinePlan plan,
        IModulePipelineProgressReporter? progress,
        bool preflightPrecomputedPlan)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        if (plan.GateMode == ConfigurationGateMode.Documentation)
            EnsureDocumentationGateConfigured(plan);

        if (preflightPrecomputedPlan && ShouldRefreshPrecomputedPlanAfterOnlineRequiredModulePreflight(spec, plan))
        {
            EnsureRequiredModuleOnlineResolutionToolInstalledIfNeededForRun(spec);
            plan = Plan(spec);
            if (plan.GateMode == ConfigurationGateMode.Documentation)
                EnsureDocumentationGateConfigured(plan);
        }

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
            if (plan.GateMode == ConfigurationGateMode.Documentation)
            {
                ExecuteDocumentationPhase(plan, session, session.Reporter, state);
                state.ProjectManifestSyncMessage = SyncBuildManifestToProjectRoot(plan, state.BuildResult, syncGeneratedBootstrapper: false);
                return BuildPipelineResult(spec, plan, state);
            }

            ExecuteFormattingAndSigningPhases(plan, session, manifestRequiredModules, manifestExternalModuleDependencies, state);
            ExecuteTypeAcceleratorSurfaceReportPhase(plan, state);
            ExecuteDocumentationPhase(plan, session, session.Reporter, state);
            // Refresh the project-root manifest before validation or tests can abort the run so callers always see current metadata.
            state.ProjectManifestSyncMessage = SyncBuildManifestToProjectRoot(plan, state.BuildResult);
            ExecuteValidationPhases(plan, session, state);
            ExecuteTestPhases(plan, session, state);
            ExecutePackagingPublishAndInstallPhases(spec, plan, session, packagingRequiredModules, pipeline, state);

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

    private static void EnsureDocumentationGateConfigured(ModulePipelinePlan plan)
    {
        if (plan.Documentation is not null && plan.DocumentationBuild?.Enable == true)
        {
            EnsureDocumentationPathIsSafe(plan.ProjectRoot, plan.Documentation);
            return;
        }

        throw new InvalidOperationException("Gate mode Documentation requires an enabled Documentation and BuildDocumentation configuration.");
    }

    private static void EnsureDocumentationGateConfigured(ModulePipelineSpec spec)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));

        var documentationGate = false;
        DocumentationConfiguration? documentation = null;
        var hasEnabledDocumentationBuild = false;

        foreach (var segment in spec.Segments ?? Array.Empty<IConfigurationSegment>())
        {
            switch (segment)
            {
                case ConfigurationGateSegment gate:
                    documentationGate = gate.Configuration.Mode == ConfigurationGateMode.Documentation;
                    break;
                case ConfigurationDocumentationSegment docs:
                    documentation = docs.Configuration;
                    break;
                case ConfigurationBuildDocumentationSegment buildDocs:
                    hasEnabledDocumentationBuild = buildDocs.Configuration?.Enable == true;
                    break;
            }
        }

        if (!documentationGate)
            return;

        if (documentation is not null && hasEnabledDocumentationBuild)
        {
            var sourcePath = spec.Build?.SourcePath ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(sourcePath))
                EnsureDocumentationPathIsSafe(sourcePath, documentation);

            return;
        }

        throw new InvalidOperationException("Gate mode Documentation requires an enabled Documentation and BuildDocumentation configuration.");
    }

    private static void EnsureDocumentationPathIsSafe(string projectRoot, DocumentationConfiguration documentation)
    {
        if (documentation is null)
            throw new ArgumentNullException(nameof(documentation));

        NormalizeProjectPathForStaging(projectRoot, documentation.Path, rejectProjectRoot: true);
    }

}
