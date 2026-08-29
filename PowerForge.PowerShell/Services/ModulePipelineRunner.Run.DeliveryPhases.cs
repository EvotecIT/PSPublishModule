namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private void ExecutePackagingPublishAndInstallPhases(
        ModulePipelineSpec spec,
        ModulePipelinePlan plan,
        ModulePipelineExecutionSession session,
        RequiredModuleReference[] packagingRequiredModules,
        ModuleBuildPipeline pipeline,
        ModulePipelineRunState state)
    {
        var buildResult = state.RequireBuildResult();

        ValidateFinalizedModulePayloadIntegrity(state);
        ValidateReleaseArtefactOutputPathConflicts(plan, state);
        ExecuteActions(ModulePipelineActionStage.BeforeArtefacts, plan, session, state);
        ValidateFinalizedModulePayloadIntegrity(state);
        if (plan.Artefacts is { Length: > 0 })
        {
            var builder = new ArtefactBuilder(_logger);
            foreach (var artefact in plan.Artefacts)
            {
                var step = session.GetArtefactStep(artefact);
                session.Start(step);
                try
                {
                    ArtefactBuildResult result = builder.BuildWithFinalizer(
                        segment: artefact,
                        projectRoot: plan.ProjectRoot,
                        stagingPath: buildResult.StagingPath,
                        moduleName: plan.ModuleName,
                        moduleVersion: plan.ResolvedVersion,
                        preRelease: plan.PreRelease,
                        requiredModules: packagingRequiredModules,
                        information: plan.Information,
                        delivery: plan.Delivery,
                        includeScriptFolders: !state.PackageWithoutScriptFolders,
                        finalizedPayloadFiles: buildResult.FinalizedPayloadFiles,
                        finalizePackedArtefact: context => plan.SignModule
                            ? FinalizeSignedPackedArtefact(plan, state, context)
                            : FinalizeUnsignedPackedArtefact(plan, state, context));
                    if (result.Type == ArtefactType.Unpacked)
                    {
                        foreach (var module in result.Modules.Where(static module => module.IsMainModule))
                        {
                            _ = PowerShellModuleCompilationIntegrator.FinalizeDeliveredCanonicalManifest(
                                module.Path,
                                module.Name,
                                state.SigningResult,
                                plan.Signing);
                        }
                    }
                    state.ArtefactResults.Add(result);
                    CaptureFinalizedPackedArtefactIntegrity(plan, state, result);
                    session.Done(step);
                }
                catch (Exception ex)
                {
                    session.Fail(step, ex);
                    throw;
                }
            }
        }
        ExecuteActions(ModulePipelineActionStage.AfterArtefacts, plan, session, state);
        ValidateFinalizedModulePayloadIntegrity(state);
        ValidateFinalizedPackedArtefactIntegrity(state);

        ExecutePackageBuildsAfterModule(plan, session, state);
        ValidateRequestedReleaseVersion(plan, state);

        ExecuteActions(ModulePipelineActionStage.BeforePublish, plan, session, state);
        ValidateFinalizedModulePayloadIntegrity(state);
        ValidateFinalizedPackedArtefactIntegrity(state);
        ExecutePublishOperations(plan, session, buildResult, state);
        ExecuteActions(ModulePipelineActionStage.AfterPublish, plan, session, state);
        state.ReleaseCoordinationResult ??= PrepareUnifiedReleaseAssets(plan, state, publishId: null);

        ExecuteActions(ModulePipelineActionStage.BeforeInstall, plan, session, state);
        ValidateFinalizedModulePayloadIntegrity(state);
        if (plan.InstallEnabled)
        {
            session.Start(session.InstallStep);
            string? installPackagePath = null;
            try
            {
                installPackagePath = Path.Combine(Path.GetTempPath(), "PowerForge", "install", $"{plan.ModuleName}_{Guid.NewGuid():N}");
                Directory.CreateDirectory(installPackagePath);
                ArtefactBuilder.CopyModulePackageForInstall(
                    buildResult.StagingPath,
                    installPackagePath,
                    plan.Information,
                    plan.Delivery,
                    includeScriptFolders: !state.PackageWithoutScriptFolders,
                    finalizedPayloadFiles: buildResult.FinalizedPayloadFiles);

                var installSpec = new ModuleInstallSpec
                {
                    Name = plan.ModuleName,
                    Version = plan.ResolvedVersion,
                    StagingPath = installPackagePath,
                    Strategy = plan.InstallStrategy,
                    KeepVersions = plan.InstallKeepVersions,
                    Roots = plan.InstallRoots,
                    UpdateManifestToResolvedVersion = spec.Install?.UpdateManifestToResolvedVersion ?? true,
                    LegacyFlatHandling = plan.InstallLegacyFlatHandling,
                    PreserveVersions = plan.InstallPreserveVersions
                };
                state.InstallResult = pipeline.InstallFromStaging(installSpec);
                foreach (var installedPath in state.InstallResult.InstalledPaths)
                {
                    _ = PowerShellModuleCompilationIntegrator.FinalizeDeliveredCanonicalManifest(
                        installedPath,
                        plan.ModuleName,
                        state.SigningResult,
                        plan.Signing);
                }
                session.Done(session.InstallStep);
            }
            catch (Exception ex)
            {
                session.Fail(session.InstallStep, ex);
                throw;
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(installPackagePath))
                {
                    try { DeleteDirectoryWithRetries(installPackagePath); }
                    catch (Exception ex) { _logger.Warn($"Failed to delete install package folder: {ex.Message}"); }
                }
            }
        }
        ExecuteActions(ModulePipelineActionStage.AfterInstall, plan, session, state);
    }
}
