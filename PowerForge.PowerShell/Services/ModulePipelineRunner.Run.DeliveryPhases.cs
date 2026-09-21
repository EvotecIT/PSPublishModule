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
                            var deliveredSigningResult = CreateDeliveredSigningResult(
                                state.SigningResult,
                                buildResult.StagingPath,
                                module.Path);
                            ValidateDeliveredBinaryDependencies(plan, module.Path);
                            _ = PowerShellModuleCompilationIntegrator.FinalizeDeliveredCanonicalManifest(
                                module.Path,
                                module.Name,
                                deliveredSigningResult,
                                plan.Signing);
                            ValidateDeliveredBinaryDependencies(plan, module.Path);
                        }
                    }
                    state.ArtefactResults.Add(result);
                    session.Done(step);
                }
                catch (Exception ex)
                {
                    session.Fail(step, ex);
                    throw;
                }
            }
            // Later configured artefacts may populate a shared DoNotClear output root.
            // Capture the completed set before user actions can change it.
            RefreshFinalizedArtefactIntegrity(plan, state);
        }
        ExecuteActions(ModulePipelineActionStage.AfterArtefacts, plan, session, state);
        ValidateFinalizedModulePayloadIntegrity(state);
        ValidateDeliveredArtefactIntegrity(plan, state);

        ExecutePackageBuildsAfterModule(plan, session, state);
        // Project builds can add sibling outputs under a loose artefact root. Preserve
        // the bytes and inventory of artefact-owned paths before accepting those additions.
        ValidateFinalizedOwnedArtefactIntegrity(state, plan.SignModule);
        RefreshFinalizedArtefactIntegrity(plan, state);
        ValidateRequestedReleaseVersion(plan, state);

        var publishingEnabled = plan.GateMode is null or ConfigurationGateMode.Publish;
        if (publishingEnabled)
        {
            ExecuteActions(ModulePipelineActionStage.BeforePublish, plan, session, state);
            ValidateFinalizedModulePayloadIntegrity(state);
            ValidateDeliveredArtefactIntegrity(plan, state);
            ExecutePublishOperations(plan, session, buildResult, state);
            // Package publication can rebuild sibling outputs beneath a DoNotClear
            // artefact root. Keep the delivered paths fixed before accepting those
            // trusted outputs; AfterPublish actions must still match this snapshot.
            ValidateFinalizedOwnedArtefactIntegrity(state, plan.SignModule);
            RefreshFinalizedArtefactIntegrity(plan, state);
            ExecuteActions(ModulePipelineActionStage.AfterPublish, plan, session, state);
            ValidateFinalizedModulePayloadIntegrity(state);
            ValidateDeliveredArtefactIntegrity(plan, state);
        }
        else
        {
            SkipActions(ModulePipelineActionStage.BeforePublish, plan, session);
            ExecutePublishOperations(plan, session, buildResult, state);
            SkipActions(ModulePipelineActionStage.AfterPublish, plan, session);
        }
        state.ReleaseCoordinationResult ??= PrepareUnifiedReleaseAssets(plan, state, publishId: null);

        ExecuteActions(ModulePipelineActionStage.BeforeInstall, plan, session, state);
        ValidateFinalizedModulePayloadIntegrity(state);
        ValidateDeliveredArtefactIntegrity(plan, state);
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
                ValidateDeliveredBinaryDependencies(plan, installPackagePath);
                var expectedSignedInstallSourcePaths = CaptureExpectedSignedInstallSourcePaths(
                    state.SigningResult,
                    buildResult.StagingPath,
                    installPackagePath);

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
                ModuleSigningResult? installPackageSigningResult = null;
                ModuleSigningResult? deliveredInstallSigningResult = null;
                var validateInstalledTransactionally =
                    plan.InstallStrategy == InstallationStrategy.AutoRevision &&
                    (plan.SignModule || ShouldValidateBinaryDependencies(plan));
                state.InstallResult = pipeline.InstallFromStagingWithManifestFinalizer(
                        installSpec,
                        plan.SignModule
                            ? (manifestPath, _) => installPackageSigningResult = SignChangedInstallManifest(
                                plan,
                                manifestPath,
                                buildResult.StagingPath,
                                state.SigningResult)
                            : null,
                        validateInstalledPaths: validateInstalledTransactionally
                            ? installedPaths =>
                            {
                                foreach (var installedPath in installedPaths)
                                    ValidateDeliveredBinaryDependencies(plan, installedPath);
                                if (plan.SignModule)
                                {
                                    deliveredInstallSigningResult = ValidateAndFinalizeSignedInstall(
                                        plan,
                                        buildResult.StagingPath,
                                        installPackagePath,
                                        installedPaths,
                                        expectedSignedInstallSourcePaths,
                                        state.SigningResult,
                                        installPackageSigningResult);
                                }
                                foreach (var installedPath in installedPaths)
                                    ValidateDeliveredBinaryDependencies(plan, installedPath);
                            }
                            : null,
                        requireAllDestinationRoots: validateInstalledTransactionally,
                        validatePreparedDestination: path => ValidateDeliveredBinaryDependencies(plan, path),
                        validateCommittedDestination: plan.InstallStrategy == InstallationStrategy.Exact
                            ? installedPath =>
                            {
                                ValidateDeliveredBinaryDependencies(plan, installedPath);
                                if (plan.SignModule)
                                {
                                    var signedResult = ValidateAndFinalizeSignedInstall(
                                        plan,
                                        buildResult.StagingPath,
                                        installPackagePath,
                                        new[] { installedPath },
                                        expectedSignedInstallSourcePaths,
                                        state.SigningResult,
                                        installPackageSigningResult);
                                    if (signedResult is not null)
                                    {
                                        deliveredInstallSigningResult = deliveredInstallSigningResult is null
                                            ? signedResult
                                            : AggregateSigningResults(deliveredInstallSigningResult, signedResult);
                                    }
                                }
                            }
                            : null);
                if (deliveredInstallSigningResult is not null)
                    state.SigningResult = AggregateSigningResults(state.SigningResult, deliveredInstallSigningResult);
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
        ValidateFinalizedModulePayloadIntegrity(state);
        ValidateDeliveredArtefactIntegrity(plan, state);
        if (state.InstallResult is not null)
        {
            foreach (string installedPath in state.InstallResult.InstalledPaths)
                ValidateDeliveredBinaryDependencies(plan, installedPath);
        }
    }
}
