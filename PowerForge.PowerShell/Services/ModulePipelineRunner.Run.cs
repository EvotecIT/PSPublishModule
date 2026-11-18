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

        using var versionCommit = ReleaseVersionCommitSession.Begin(
            _logger,
            plan.ProjectRoot,
            plan.Release?.Configuration?.CommitVersionChanges ?? ReleaseVersionCommitMode.Off,
            PublishesFromThisRun(plan));
        ModulePipelineResult result;
        try
        {
            result = RunPipeline(spec, plan, progress, preflightPrecomputedPlan);
        }
        catch
        {
            versionCommit.Complete(releaseSucceeded: false, plan.ModuleName, FormatReleaseVersion(plan));
            throw;
        }

        versionCommit.Complete(releaseSucceeded: true, plan.ModuleName, FormatReleaseVersion(result.Plan ?? plan));
        return result;
    }

    /// <summary>A publish run that has somewhere to publish; builds, plans and docs never commit.</summary>
    private static bool PublishesFromThisRun(ModulePipelinePlan plan)
        => plan.GateMode is null or ConfigurationGateMode.Publish &&
           (plan.Publishes?.Length ?? 0) > 0;

    private static string? FormatReleaseVersion(ModulePipelinePlan plan)
        => string.IsNullOrWhiteSpace(plan.ResolvedVersion)
            ? null
            : string.IsNullOrWhiteSpace(plan.PreRelease)
                ? plan.ResolvedVersion
                : $"{plan.ResolvedVersion}-{plan.PreRelease}";

    private ModulePipelineResult RunPipeline(
        ModulePipelineSpec spec,
        ModulePipelinePlan plan,
        IModulePipelineProgressReporter? progress,
        bool preflightPrecomputedPlan)
    {
        if (plan.GateMode == ConfigurationGateMode.Documentation)
            EnsureDocumentationGateConfigured(plan);

        if (preflightPrecomputedPlan && ShouldRefreshPrecomputedPlanAfterOnlineRequiredModulePreflight(spec, plan))
        {
            EnsureRequiredModuleOnlineResolutionToolInstalledIfNeededForRun(spec);
            plan = Plan(spec);
            if (plan.GateMode == ConfigurationGateMode.Documentation)
                EnsureDocumentationGateConfigured(plan);
        }

        var manifestRequiredModules = plan.RequiredModules ?? Array.Empty<RequiredModuleReference>();
        var packagingRequiredModules = plan.RequiredModulesForPackaging ?? Array.Empty<RequiredModuleReference>();
        var manifestExternalModuleDependencies = plan.ExternalModuleDependencies ?? Array.Empty<string>();

        // Without a live view (CI, redirected output, -Verbose) steps are reported as log lines with a heartbeat.
        var session = ModulePipelineExecutionSession.Create(
            plan,
            progress ?? new LoggingModulePipelineProgressReporter(_logger, ModulePipelineStep.Create(plan)));
        var cleanupStep = session.CleanupStep;

        var pipeline = ModuleBuildPipelineFactory.Create(_logger);
        var state = new ModulePipelineRunState(plan.BuildSpec.StagingPath);

        try
        {
            // Existing staging may contain an earlier or mixed payload. Its owning
            // compilation or release checkpoint restores commit context when recorded.
            plan.SourceCommitSha = plan.BuildSpec.ReuseStaging
                ? null
                : ReleaseSourceCommit.Capture(plan.ProjectRoot);
            if (plan.RequireReleaseSourceUnchanged)
                ValidateReleaseSourceUnchanged(plan, generatedOutputPaths: null, trackedGeneratedOutputPaths: null);
            if (plan.ReleaseCheckpoint && plan.BuildSpec.ReuseStaging && plan.GateMode == ConfigurationGateMode.Publish)
                return PublishFinalizedCheckpoint(spec, plan, session, pipeline, state);
            ExecutePreparationAndBuildPhases(plan, session, manifestRequiredModules, manifestExternalModuleDependencies, pipeline, state);
            var approvedForOutput = plan.ApprovedModules ?? Array.Empty<string>();
            var retainedApprovedModules = approvedForOutput.Except(
                ResolveApprovedModulesSatisfiedByFinalPayload(plan, state.BuildResult!,
                    Path.Combine(state.BuildResult!.StagingPath, $"{plan.ModuleName}.psm1"), approvedForOutput),
                StringComparer.OrdinalIgnoreCase).ToArray();
            if (retainedApprovedModules.Length > 0)
                throw new InvalidOperationException($"Approved modules cannot remain runtime dependencies of retained manifest-loaded files: {string.Join(", ", retainedApprovedModules)}. Merge those files into the root module or declare their runtime modules only as RequiredModule.");

            manifestRequiredModules = ResolveOutputRequiredModules(
                plan.RequiredModules,
                approvedForOutput);
            packagingRequiredModules = ResolveOutputRequiredModules(
                plan.RequiredModulesForPackaging,
                approvedForOutput);
            if (plan.GateMode == ConfigurationGateMode.Documentation)
            {
                ExecuteCompilationPhase(
                    plan,
                    session,
                    state,
                    manifestRequiredModules,
                    manifestExternalModuleDependencies);
                ExecuteDocumentationPhase(plan, session, session.Reporter, state);
                state.ProjectManifestSyncMessage = SyncBuildManifestToProjectRoot(
                    plan,
                    state.BuildResult,
                    syncGeneratedBootstrapper: false,
                    syncStagedExports: state.PowerShellCompilationResult is null);
                PersistPowerShellModuleCompilationCheckpoint(
                    plan,
                    state,
                    manifestRequiredModules,
                    manifestExternalModuleDependencies);
                return BuildPipelineResult(spec, plan, state);
            }

            ExecuteFormattingAndSigningPhases(plan, session, manifestRequiredModules, manifestExternalModuleDependencies, state);
            ExecuteTypeAcceleratorSurfaceReportPhase(plan, session, state);
            ExecuteDocumentationPhase(plan, session, session.Reporter, state);
            // Refresh the project-root manifest before validation or tests can abort the run so callers always see current metadata.
            state.ProjectManifestSyncMessage = SyncBuildManifestToProjectRoot(
                plan,
                state.BuildResult,
                syncGeneratedBootstrapper: state.PowerShellCompilationResult is null,
                syncStagedExports: state.PowerShellCompilationResult is null);
            PersistPowerShellModuleCompilationCheckpoint(
                plan,
                state,
                manifestRequiredModules,
                manifestExternalModuleDependencies);
            CaptureAuthorizedProjectManifest(plan, state);
            ExecuteValidationPhases(plan, session, state);
            ExecuteTestPhases(plan, session, state);
            ExecutePackagingPublishAndInstallPhases(spec, plan, session, packagingRequiredModules, pipeline, state);

            var result = BuildPipelineResult(spec, plan, state);
            CompleteSynchronizedReleaseCheckpoint(state);
            if (plan.ReleaseCheckpoint && !plan.DeleteGeneratedStagingAfterRun)
                SavePublicationCheckpoint(plan, state);
            return result;
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

            ExitSynchronizedReleaseCheckpointScope(state);
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
