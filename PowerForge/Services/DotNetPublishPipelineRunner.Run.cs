using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    /// <summary>
    /// Executes the provided <paramref name="plan"/>.
    /// </summary>
    public DotNetPublishResult Run(DotNetPublishPlan plan, IDotNetPublishProgressReporter? progress)
        => Run(plan, progress, CancellationToken.None);

    /// <summary>
    /// Executes the provided <paramref name="plan"/> and cancels the active child process when requested.
    /// </summary>
    public DotNetPublishResult Run(
        DotNetPublishPlan plan,
        IDotNetPublishProgressReporter? progress,
        CancellationToken cancellationToken)
        => RunWithReservationOwner(plan, progress, CreateMsiReservationOwner(), cancellationToken);

    internal DotNetPublishResult RunWithReservationOwner(
        DotNetPublishPlan plan,
        IDotNetPublishProgressReporter? progress,
        string msiReservationOwner,
        CancellationToken cancellationToken = default)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (string.IsNullOrWhiteSpace(msiReservationOwner))
            throw new ArgumentException("MSI reservation owner cannot be empty.", nameof(msiReservationOwner));
        cancellationToken.ThrowIfCancellationRequested();
        var previousCancellationToken = _cancellationToken.Value;
        string? previousDotNetExecutablePath = ActiveDotNetExecutablePath.Value;
        string? previousDotNetExecutableSha256 = ActiveDotNetExecutableSha256.Value;
        string? previousGitExecutablePath = ActiveGitExecutablePath.Value;
        string? previousGitExecutableSha256 = ActiveGitExecutableSha256.Value;
        bool previousNativeAotPublish = ActiveNativeAotPublish.Value;
        bool previousStrictDotNetEnvironment = ActiveStrictDotNetEnvironment.Value;
        bool previousToolSnapshotScope = ActiveToolSnapshotScope.Value;
        ActiveDotNetExecutablePath.Value = null;
        ActiveDotNetExecutableSha256.Value = null;
        ActiveGitExecutablePath.Value = null;
        ActiveGitExecutableSha256.Value = null;
        ActiveToolSnapshotScope.Value = true;
        _cancellationToken.Value = cancellationToken;
        progress ??= NullDotNetPublishProgressReporter.Instance;

        var runStartedUtc = DateTimeOffset.UtcNow;
        var runStopwatch = Stopwatch.StartNew();
        var artefacts = new List<DotNetPublishArtefactResult>();
        var msiPrepares = new List<DotNetPublishMsiPrepareResult>();
        var msiBuilds = new List<DotNetPublishMsiBuildResult>();
        var storePackages = new List<DotNetPublishStorePackageResult>();
        var benchmarkGates = new List<DotNetPublishBenchmarkGateResult>();
        var benchmarkExtracts = new Dictionary<string, DotNetPublishBenchmarkExtractionResult>(StringComparer.OrdinalIgnoreCase);
        var stepReports = new List<DotNetPublishRunReportStep>();
        IReadOnlyDictionary<string, string> cleanTrackedGeneratedProvenanceState =
            new Dictionary<string, string>();
        string? manifestJson = null;
        string? manifestText = null;
        string? checksumsPath = null;
        string? runReportPath = null;
        string? runReportMarkdownPath = null;

        try
        {
            ValidateExplicitDotNetEnvironmentVariables(plan.EnvironmentVariables);
            ValidateNativeAotEnvironmentVariables(plan);
            ValidateTrackedGeneratedProvenancePaths(plan);
            ActiveNativeAotPublish.Value = PlanUsesNativeAot(plan);
            ActiveStrictDotNetEnvironment.Value = plan.NoBuildInPublish ||
                (plan.Targets ?? Array.Empty<DotNetPublishTargetPlan>())
                .Any(static target => target?.Publish?.Sign?.Enabled == true);
            if ((plan.Steps ?? Array.Empty<DotNetPublishStep>())
                .Any(step => step.Kind == DotNetPublishStepKind.Manifest))
            {
                cleanTrackedGeneratedProvenanceState = CaptureCleanTrackedGeneratedProvenanceState(
                    plan.ProjectRoot,
                    EnumerateTrackedGeneratedProvenancePaths(
                        plan,
                        Array.Empty<DotNetPublishMsiBuildResult>()));
            }

            foreach (var step in plan.Steps ?? Array.Empty<DotNetPublishStep>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress.StepStarting(step);
                var stepStartedUtc = DateTimeOffset.UtcNow;
                var stepStopwatch = Stopwatch.StartNew();
                string? stepError = null;
                var stepSucceeded = false;
                try
                {
                    switch (step.Kind)
                    {
                        case DotNetPublishStepKind.CommandHook:
                            RunCommandHook(plan, step);
                            break;
                        case DotNetPublishStepKind.Restore:
                            Restore(plan, step.Runtime);
                            break;
                        case DotNetPublishStepKind.Clean:
                            Clean(plan);
                            break;
                        case DotNetPublishStepKind.Build:
                            if ((plan.Targets ?? Array.Empty<DotNetPublishTargetPlan>())
                                .Any(static target => target?.Publish?.Sign?.Enabled == true))
                            {
                                _ = ReadPortableInventorySourceProvenance(plan);
                            }
                            Build(plan, step);
                            break;
                        case DotNetPublishStepKind.Publish:
                        {
                            // Bind provenance to the project-reference bytes available immediately
                            // after BeforeTargetPublish hooks and make the real no-build publish consume
                            // private snapshots of the bytes proven by the detached rebuild.
                            bool requiresPublishProvenance = plan.NoBuildInPublish ||
                                (plan.Targets ?? Array.Empty<DotNetPublishTargetPlan>()).Any(target =>
                                    target is not null &&
                                    target.Name.Equals(step.TargetName, StringComparison.OrdinalIgnoreCase) &&
                                    target.Publish?.Sign?.Enabled == true);
                            SourceProvenance? publishProvenance = requiresPublishProvenance
                                ? ReadPortableInventorySourceProvenance(plan)
                                : null;
                            using PublishProvenanceLease? provenanceLease = requiresPublishProvenance
                                ? PublishProvenanceLease.Create(PublishProvenanceLease.BuildGuardedPaths(
                                    publishProvenance!.PublishInputFiles,
                                    publishProvenance.NoBuildPublishInputs,
                                    plan.NoBuildInPublish))
                                : null;
                            if (provenanceLease is not null)
                            {
                                SourceProvenance confirmedProvenance =
                                    ReadPortableInventorySourceProvenance(plan);
                                provenanceLease.EnsureCovers(PublishProvenanceLease.BuildGuardedPaths(
                                    confirmedProvenance.PublishInputFiles,
                                    confirmedProvenance.NoBuildPublishInputs,
                                    plan.NoBuildInPublish));
                                provenanceLease.ValidateUnchanged();
                                publishProvenance = confirmedProvenance;
                            }
                            using NoBuildPublishInputSnapshot? inputSnapshot =
                                requiresPublishProvenance
                                    ? CreateNoBuildPublishInputSnapshot(
                                        plan,
                                        step.TargetName!,
                                        step.Framework ?? string.Empty,
                                        step.Runtime!,
                                        step.Style,
                                        publishProvenance!)
                                    : null;
                            artefacts.Add(Publish(
                                plan,
                                step.TargetName!,
                                step.Framework ?? string.Empty,
                                step.Runtime!,
                                step.Style,
                                msiReservationOwner,
                                inputSnapshot,
                                provenanceLease));
                            break;
                        }
                        case DotNetPublishStepKind.Bundle:
                            artefacts.Add(BuildBundle(plan, artefacts, step));
                            break;
                        case DotNetPublishStepKind.ServiceLifecycle:
                            RunServiceLifecycleStep(plan, artefacts, step);
                            break;
                        case DotNetPublishStepKind.MsiPrepare:
                            msiPrepares.Add(PrepareMsiPackage(plan, artefacts, step));
                            break;
                        case DotNetPublishStepKind.MsiBuild:
                            msiBuilds.Add(BuildMsiPackage(plan, msiPrepares, step, msiReservationOwner));
                            break;
                        case DotNetPublishStepKind.MsiSign:
                            SignMsiPackage(plan, msiBuilds, step);
                            break;
                        case DotNetPublishStepKind.StorePackage:
                            storePackages.Add(BuildStorePackage(plan, step));
                            break;
                        case DotNetPublishStepKind.BenchmarkExtract:
                            RunBenchmarkExtractStep(plan, benchmarkExtracts, step);
                            break;
                        case DotNetPublishStepKind.BenchmarkGate:
                            benchmarkGates.Add(RunBenchmarkGateStep(plan, benchmarkExtracts, step));
                            break;
                        case DotNetPublishStepKind.Manifest:
                        {
                            FinalizePortableEvidence(plan, artefacts);
                            (manifestJson, manifestText, checksumsPath) = WriteManifestsWithProvenance(
                                plan,
                                artefacts,
                                storePackages,
                                msiBuilds,
                                cleanTrackedGeneratedPaths: null,
                                cleanTrackedGeneratedProvenanceState:
                                    cleanTrackedGeneratedProvenanceState,
                                msiReservationOwner:
                                    msiReservationOwner);
                            break;
                        }
                    }

                    progress.StepCompleted(step);
                    stepSucceeded = true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    stepError = ex.GetBaseException().Message;
                    progress.StepFailed(step, ex);
                    throw new DotNetPublishStepException(step, ex);
                }
                finally
                {
                    stepStopwatch.Stop();
                    stepReports.Add(new DotNetPublishRunReportStep
                    {
                        Key = step.Key ?? string.Empty,
                        Kind = step.Kind,
                        Title = step.Title ?? string.Empty,
                        StartedUtc = stepStartedUtc,
                        FinishedUtc = DateTimeOffset.UtcNow,
                        DurationMs = stepStopwatch.ElapsedMilliseconds,
                        Succeeded = stepSucceeded,
                        ErrorMessage = stepError
                    });
                }
            }

            runStopwatch.Stop();
            var successResult = new DotNetPublishResult
            {
                Succeeded = true,
                Artefacts = artefacts.ToArray(),
                MsiPrepares = msiPrepares.ToArray(),
                MsiBuilds = msiBuilds.ToArray(),
                StorePackages = storePackages.ToArray(),
                BenchmarkGates = benchmarkGates.ToArray(),
                ManifestJsonPath = manifestJson,
                ManifestTextPath = manifestText,
                ChecksumsPath = checksumsPath
            };

            runReportPath = TryWriteRunReport(
                plan,
                successResult,
                stepReports,
                runStartedUtc,
                runStopwatch.Elapsed);
            runReportMarkdownPath = TryWriteRunReportMarkdown(
                plan,
                successResult,
                stepReports,
                runStartedUtc,
                runStopwatch.Elapsed);
            successResult.RunReportPath = runReportPath;
            successResult.RunReportMarkdownPath = runReportMarkdownPath;
            return successResult;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            runStopwatch.Stop();
            var failure = BuildFailure(plan, ex, out var errorMessage);

            _logger.Error(errorMessage);
            if (_logger.IsVerbose) _logger.Verbose(ex.ToString());
            var failedResult = new DotNetPublishResult
            {
                Succeeded = false,
                ErrorMessage = errorMessage,
                Failure = failure,
                Artefacts = artefacts.ToArray(),
                MsiPrepares = msiPrepares.ToArray(),
                MsiBuilds = msiBuilds.ToArray(),
                StorePackages = storePackages.ToArray(),
                BenchmarkGates = benchmarkGates.ToArray(),
                ManifestJsonPath = manifestJson,
                ManifestTextPath = manifestText,
                ChecksumsPath = checksumsPath
            };

            runReportPath = TryWriteRunReport(
                plan,
                failedResult,
                stepReports,
                runStartedUtc,
                runStopwatch.Elapsed);
            runReportMarkdownPath = TryWriteRunReportMarkdown(
                plan,
                failedResult,
                stepReports,
                runStartedUtc,
                runStopwatch.Elapsed);
            failedResult.RunReportPath = runReportPath;
            failedResult.RunReportMarkdownPath = runReportMarkdownPath;
            return failedResult;
        }
        finally
        {
            try
            {
                foreach (var version in plan.MsiVersions.Values)
                {
                    if (!ReleaseMsiVersionStateReservation(version, msiReservationOwner))
                    {
                        _logger.Warn(
                            $"MSI version reservation for '{version.Version}' in '{version.StatePath}' " +
                            "could not be released. A later overwrite rebuild may require retrying after the state file is available.");
                    }
                }
            }
            finally
            {
                ClearMsiVersionStateWrites(msiReservationOwner);
                _cancellationToken.Value = previousCancellationToken;
                ActiveDotNetExecutablePath.Value = previousDotNetExecutablePath;
                ActiveDotNetExecutableSha256.Value = previousDotNetExecutableSha256;
                ActiveGitExecutablePath.Value = previousGitExecutablePath;
                ActiveGitExecutableSha256.Value = previousGitExecutableSha256;
                ActiveNativeAotPublish.Value = previousNativeAotPublish;
                ActiveStrictDotNetEnvironment.Value = previousStrictDotNetEnvironment;
                ActiveToolSnapshotScope.Value = previousToolSnapshotScope;
            }
        }
    }

}
