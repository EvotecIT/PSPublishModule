using System.IO;
using System.Linq;

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
        var reporter = session.Reporter;
        var stageStep = session.StageStep;
        var buildStep = session.BuildStep;
        var manifestStep = session.ManifestStep;
        var docsExtractStep = session.DocsExtractStep;
        var docsWriteStep = session.DocsWriteStep;
        var docsMamlStep = session.DocsMamlStep;
        var formatStagingStep = session.FormatStagingStep;
        var formatProjectStep = session.FormatProjectStep;
        var signStep = session.SignStep;
        var cleanupStep = session.CleanupStep;

        var pipeline = ModuleBuildPipelineFactory.Create(_logger);
        var state = new ModulePipelineRunState(plan.BuildSpec.StagingPath);

        try
        {
            state.DependencyInstallResults = EnsureBuildDependenciesInstalledIfNeeded(plan);
            SyncSourceProjectVersionIfRequested(plan);

            ModuleBuildPipeline.StagingResult staged;
            session.Start(stageStep);
            try
            {
                staged = pipeline.StageToStaging(plan.BuildSpec);
                state.Staged = staged;
                state.StagingPathForCleanup = staged.StagingPath;
                session.Done(stageStep);
            }
            catch (Exception ex)
            {
                session.Fail(stageStep, ex);
                state.StagingPathForCleanup ??= plan.BuildSpec.StagingPath;
                throw;
            }

            ModuleBuildResult buildResult;
            session.Start(buildStep);
            try
            {
                buildResult = pipeline.BuildInStaging(plan.BuildSpec, staged.StagingPath);
                state.BuildResult = buildResult;
                session.Done(buildStep);
            }
            catch (Exception ex)
            {
                session.Fail(buildStep, ex);
                throw;
            }

            state.MergeExecution = plan.BuildSpec.RefreshManifestOnly ? MergeExecutionResult.None : ApplyMerge(plan, buildResult);
            var mergedScripts = state.MergedScripts;
            if (!plan.BuildSpec.RefreshManifestOnly)
                ApplyPlaceholders(plan, buildResult);

            session.Start(manifestStep);
            try
            {
                RefreshManifestFromPlan(plan, buildResult, manifestRequiredModules, manifestExternalModuleDependencies);

                if (plan.Delivery is not null && plan.Delivery.Enable)
                {
                    ApplyDeliveryMetadata(buildResult.ManifestPath, plan.Delivery);

                    if (plan.Delivery.GenerateInstallCommand || plan.Delivery.GenerateUpdateCommand)
                    {
                        var generator = new DeliveryCommandGenerator(_logger);
                        var generated = generator.Generate(buildResult.StagingPath, plan.ModuleName, plan.Delivery);

                        if (generated.Length > 0)
                        {
                            try
                            {
                                var publicFolder = Path.Combine(buildResult.StagingPath, "Public");
                                if (Directory.Exists(publicFolder))
                                {
                                    var scripts = Directory.GetFiles(publicFolder, "*.ps1", SearchOption.AllDirectories);
                                    var functions = ScriptFunctionExportDetector.DetectScriptFunctions(scripts);
                                    BuildServices.SetManifestExports(buildResult.ManifestPath, functions, cmdlets: null, aliases: null);
                                }

                                if (mergedScripts)
                                    SyncMergedPsm1WithGeneratedScripts(buildResult.ManifestPath, buildResult.StagingPath, plan.ModuleName, generated.Select(g => g.ScriptPath));
                            }
                            catch (Exception ex)
                            {
                                _logger.Warn($"Failed to update manifest exports after generating delivery commands. Error: {ex.Message}");
                                if (_logger.IsVerbose) _logger.Verbose(ex.ToString());
                            }
                        }
                    }
                }

                if (!mergedScripts && !plan.BuildSpec.RefreshManifestOnly)
                    TryRegenerateBootstrapperFromManifest(buildResult, plan.ModuleName, plan.BuildSpec.ExportAssemblies);

                session.Done(manifestStep);
            }
            catch (Exception ex)
            {
                session.Fail(manifestStep, ex);
                throw;
            }

            if (plan.Documentation is not null && plan.DocumentationBuild?.Enable == true)
            {
                try
                {
                    state.DocumentationResult = _hostedOperations.BuildDocumentation(
                        moduleName: plan.ModuleName,
                        stagingPath: buildResult.StagingPath,
                        moduleManifestPath: buildResult.ManifestPath,
                        documentation: plan.Documentation,
                        buildDocumentation: plan.DocumentationBuild!,
                        progress: reporter,
                        extractStep: docsExtractStep,
                        writeStep: docsWriteStep,
                        externalHelpStep: docsMamlStep);

                    if (state.DocumentationResult is not null && !state.DocumentationResult.Succeeded)
                        throw new InvalidOperationException($"Documentation generation failed. {state.DocumentationResult.ErrorMessage}");

                    // Legacy: "UpdateWhenNew" historically updated documentation in the project folder.
                    // When enabled, keep the repo Docs/Readme.md and external help in sync (not just staging).
                    if (state.DocumentationResult is not null &&
                        state.DocumentationResult.Succeeded &&
                        plan.DocumentationBuild?.UpdateWhenNew == true)
                    {
                        try
                        {
                            SyncGeneratedDocumentationToProjectRoot(plan, state.DocumentationResult);
                        }
                        catch (Exception ex)
                        {
                            _logger.Warn($"Failed to update project docs folder. Error: {ex.Message}");
                            if (_logger.IsVerbose) _logger.Verbose(ex.ToString());
                        }
                    }
                }
                catch (Exception ex)
                {
                    session.Fail(docsExtractStep, ex);
                    session.Fail(docsWriteStep, ex);
                    session.Fail(docsMamlStep, ex);
                    throw;
                }
            }

            if (plan.Formatting is not null)
            {
                var formattingPipeline = new FormattingPipeline(_logger);       

                session.Start(formatStagingStep);
                try
                {
                    state.FormattingStagingResults = FormatPowerShellTree(
                        rootPath: buildResult.StagingPath,
                        moduleName: plan.ModuleName,
                        manifestPath: buildResult.ManifestPath,
                        includeMergeFormatting: true,
                        formatting: plan.Formatting,
                        pipeline: formattingPipeline);

                    var stagingFmt = FormattingSummary.FromResults(state.FormattingStagingResults);
                    if (stagingFmt.Status == CheckStatus.Fail)
                    {
                        LogFormattingIssues(buildResult.StagingPath, state.FormattingStagingResults, "staging root");
                        throw new InvalidOperationException(
                            BuildFormattingFailureMessage("staging root", buildResult.StagingPath, stagingFmt, state.FormattingStagingResults));
                    }
                    session.Done(formatStagingStep);
                }
                catch (Exception ex)
                {
                    session.Fail(formatStagingStep, ex);
                    throw;
                }

                if (plan.Formatting.Options.UpdateProjectRoot)
                {
                    session.Start(formatProjectStep);
                    try
                    {
                        var projectManifest = Path.Combine(plan.ProjectRoot, $"{plan.ModuleName}.psd1");
                        state.FormattingProjectResults = FormatPowerShellTree(
                            rootPath: plan.ProjectRoot,
                            moduleName: plan.ModuleName,
                            manifestPath: projectManifest,
                            includeMergeFormatting: false,
                            formatting: plan.Formatting,
                            pipeline: formattingPipeline);

                        var projectFmt = FormattingSummary.FromResults(state.FormattingProjectResults);
                        if (projectFmt.Status == CheckStatus.Fail)
                        {
                            LogFormattingIssues(plan.ProjectRoot, state.FormattingProjectResults, "project root");
                            throw new InvalidOperationException(
                                BuildFormattingFailureMessage("project root", plan.ProjectRoot, projectFmt, state.FormattingProjectResults));
                        }
                        session.Done(formatProjectStep);
                    }
                    catch (Exception ex)
                    {
                        session.Fail(formatProjectStep, ex);
                        throw;
                    }
                }
            }

            try
            {
                RefreshManifestFromPlan(plan, buildResult, manifestRequiredModules, manifestExternalModuleDependencies);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Post-format manifest patch failed. {ex.Message}");
                if (_logger.IsVerbose) _logger.Verbose(ex.ToString());
            }

            if (plan.SignModule)
            {
                session.Start(signStep);
                try
                {
                    state.SigningResult = SignBuiltModuleOutput(
                        moduleName: plan.ModuleName,
                        rootPath: buildResult.StagingPath,
                        signing: plan.Signing);
                    session.Done(signStep);
                }
                catch (Exception ex)
                {
                    session.Fail(signStep, ex);
                    throw;
                }
            }

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
