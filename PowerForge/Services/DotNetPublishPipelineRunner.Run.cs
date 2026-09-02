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
        TrustedDotNetInstallationSnapshot? previousDotNetInstallationSnapshot = ActiveDotNetInstallationSnapshot.Value;
        TrustedNativeAotPathSnapshot? previousNativeAotPathSnapshot = ActiveNativeAotPathSnapshot.Value;
        string? previousGitExecutablePath = ActiveGitExecutablePath.Value;
        string? previousGitExecutableSha256 = ActiveGitExecutableSha256.Value;
        PublishProvenanceLease? previousGitExecutableLease = ActiveGitExecutableLease.Value;
        bool previousNativeAotPublish = ActiveNativeAotPublish.Value;
        bool previousStrictDotNetEnvironment = ActiveStrictDotNetEnvironment.Value;
        bool previousToolSnapshotScope = ActiveToolSnapshotScope.Value;
        ActiveDotNetExecutablePath.Value = null;
        ActiveDotNetExecutableSha256.Value = null;
        ActiveDotNetInstallationSnapshot.Value = null;
        ActiveNativeAotPathSnapshot.Value = null;
        ActiveGitExecutablePath.Value = null;
        ActiveGitExecutableSha256.Value = null;
        ActiveGitExecutableLease.Value = null;
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
        PublishProvenanceLease? manifestProvenanceLease = null;
        SourceProvenance? sharedPublishProvenance = null;
        var publishProvenanceByArtifact = new Dictionary<string, SourceProvenance>(StringComparer.OrdinalIgnoreCase);
        string[] plannedPublishGeneratedPaths = Array.Empty<string>();
        IReadOnlyDictionary<string, string> cleanTrackedGeneratedProvenanceState =
            new Dictionary<string, string>();
        string? manifestJson = null;
        string? manifestText = null;
        string? checksumsPath = null;
        string? runReportPath = null;
        string? runReportMarkdownPath = null;

        try
        {
            plannedPublishGeneratedPaths = ResolvePlannedPublishGeneratedPaths(plan);
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
                            if (manifestProvenanceLease is not null)
                            {
                                // A per-combination build is allowed to replace the previous combination's
                                // generated bin/obj inputs. Validate the completed publish boundary, then
                                // release and reacquire provenance after this build for the next publish.
                                manifestProvenanceLease.ValidateUnchanged();
                                sharedPublishProvenance?.ValidateCurrentSource();
                                manifestProvenanceLease.Dispose();
                                manifestProvenanceLease = null;
                                sharedPublishProvenance = null;
                            }

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
                            SourceProvenance? publishProvenance = null;
                            PublishProvenanceLease? provenanceLease = null;
                            if (requiresPublishProvenance)
                            {
                                if (sharedPublishProvenance is null)
                                {
                                    SourceProvenance initialProvenance =
                                        ReadPortableInventorySourceProvenance(
                                            plan,
                                            additionalGeneratedPaths: plannedPublishGeneratedPaths);
                                    PublishProvenanceLease candidateLease = PublishProvenanceLease.Create(
                                        PublishProvenanceLease.BuildGuardedPaths(
                                            initialProvenance.PublishInputFiles,
                                            initialProvenance.NoBuildPublishInputs));
                                    try
                                    {
                                        SourceProvenance confirmedProvenance =
                                            ReadPortableInventorySourceProvenance(
                                                plan,
                                                additionalGeneratedPaths: plannedPublishGeneratedPaths);
                                        candidateLease.EnsureCovers(PublishProvenanceLease.BuildGuardedPaths(
                                            confirmedProvenance.PublishInputFiles,
                                            confirmedProvenance.NoBuildPublishInputs));
                                        candidateLease.ValidateUnchanged();
                                        confirmedProvenance.ValidateCurrentSource();
                                        manifestProvenanceLease = candidateLease;
                                        sharedPublishProvenance = confirmedProvenance;
                                    }
                                    catch
                                    {
                                        candidateLease.Dispose();
                                        throw;
                                    }
                                }
                                else
                                {
                                    manifestProvenanceLease!.ValidateUnchanged();
                                    sharedPublishProvenance.ValidateCurrentSource();
                                }

                                publishProvenance = sharedPublishProvenance;
                                provenanceLease = manifestProvenanceLease;
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
                            DotNetPublishArtefactResult publishedArtefact = Publish(
                                plan,
                                step.TargetName!,
                                step.Framework ?? string.Empty,
                                step.Runtime!,
                                step.Style,
                                msiReservationOwner,
                                inputSnapshot,
                                provenanceLease,
                                publishProvenance,
                                plannedPublishGeneratedPaths,
                                out SourceProvenance? finalPublishProvenance);
                            artefacts.Add(publishedArtefact);
                            if (finalPublishProvenance is not null)
                            {
                                publishProvenanceByArtifact.Add(
                                    BuildArtifactProvenanceKey(publishedArtefact),
                                    finalPublishProvenance);
                            }
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
                            ValidateManifestProvenance(
                                manifestProvenanceLease,
                                sharedPublishProvenance,
                                GetVerifiedMsiVersionStateWrites(
                                    plan.ProjectRoot,
                                    cleanTrackedGeneratedProvenanceState,
                                    msiReservationOwner));
                            FinalizePortableEvidence(plan, artefacts, publishProvenanceByArtifact);
                            ValidateManifestProvenance(
                                manifestProvenanceLease,
                                sharedPublishProvenance,
                                GetVerifiedMsiVersionStateWrites(
                                    plan.ProjectRoot,
                                    cleanTrackedGeneratedProvenanceState,
                                    msiReservationOwner));
                            (manifestJson, manifestText, checksumsPath) = WriteManifestsWithProvenance(
                                plan,
                                artefacts,
                                storePackages,
                                msiBuilds,
                                cleanTrackedGeneratedPaths: null,
                                cleanTrackedGeneratedProvenanceState:
                                    cleanTrackedGeneratedProvenanceState,
                                msiReservationOwner:
                                    msiReservationOwner,
                                verifiedSourceProvenance:
                                    TryBuildManifestProvenance(artefacts, publishProvenanceByArtifact));
                            IReadOnlyDictionary<string, string> trackedManifestOutputState =
                                CaptureTrackedManifestOutputState(
                                    plan.ProjectRoot,
                                    cleanTrackedGeneratedProvenanceState,
                                    manifestJson,
                                    manifestText,
                                    checksumsPath);
                            ValidateManifestProvenance(
                                manifestProvenanceLease,
                                sharedPublishProvenance,
                                GetVerifiedMsiVersionStateWrites(
                                    plan.ProjectRoot,
                                    cleanTrackedGeneratedProvenanceState,
                                    msiReservationOwner)
                                .Concat(trackedManifestOutputState.Keys));
                            ValidateTrackedManifestOutputState(trackedManifestOutputState);
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
                manifestProvenanceLease?.Dispose();
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
                TrustedDotNetInstallationSnapshot? dotNetInstallationSnapshot = ActiveDotNetInstallationSnapshot.Value;
                TrustedNativeAotPathSnapshot? nativeAotPathSnapshot = ActiveNativeAotPathSnapshot.Value;
                PublishProvenanceLease? gitExecutableLease = ActiveGitExecutableLease.Value;
                try
                {
                    try
                    {
                        if (nativeAotPathSnapshot is not null)
                        {
                            try
                            {
                                nativeAotPathSnapshot.ValidateUnchanged(verifyHashes: true);
                            }
                            finally
                            {
                                nativeAotPathSnapshot.Dispose();
                            }
                        }
                    }
                    finally
                    {
                        if (dotNetInstallationSnapshot is not null)
                        {
                            try
                            {
                                dotNetInstallationSnapshot.ValidateUnchanged(verifyHashes: true);
                            }
                            finally
                            {
                                dotNetInstallationSnapshot.Dispose();
                            }
                        }
                    }
                }
                finally
                {
                    try
                    {
                        if (gitExecutableLease is not null)
                        {
                            try
                            {
                                gitExecutableLease.ValidateUnchanged();
                            }
                            finally
                            {
                                gitExecutableLease.Dispose();
                            }
                        }
                    }
                    finally
                    {
                        ClearMsiVersionStateWrites(msiReservationOwner);
                        _cancellationToken.Value = previousCancellationToken;
                        ActiveDotNetExecutablePath.Value = previousDotNetExecutablePath;
                        ActiveDotNetExecutableSha256.Value = previousDotNetExecutableSha256;
                        ActiveDotNetInstallationSnapshot.Value = previousDotNetInstallationSnapshot;
                        ActiveNativeAotPathSnapshot.Value = previousNativeAotPathSnapshot;
                        ActiveGitExecutablePath.Value = previousGitExecutablePath;
                        ActiveGitExecutableSha256.Value = previousGitExecutableSha256;
                        ActiveGitExecutableLease.Value = previousGitExecutableLease;
                        ActiveNativeAotPublish.Value = previousNativeAotPublish;
                        ActiveStrictDotNetEnvironment.Value = previousStrictDotNetEnvironment;
                        ActiveToolSnapshotScope.Value = previousToolSnapshotScope;
                    }
                }
            }
        }
    }

    private static string BuildArtifactProvenanceKey(DotNetPublishArtefactResult artefact)
        => string.Join(
            "|",
            artefact.Target,
            artefact.Framework,
            artefact.Runtime,
            artefact.Style.ToString());

    private static void ValidateManifestProvenance(
        PublishProvenanceLease? provenanceLease,
        SourceProvenance? provenance,
        IEnumerable<string>? additionalTrackedGeneratedPaths = null)
    {
        provenanceLease?.ValidateUnchanged();
        provenance?.ValidateCurrentSource(additionalTrackedGeneratedPaths);
    }

    internal static IReadOnlyDictionary<string, string> CaptureTrackedManifestOutputState(
        string projectRoot,
        IReadOnlyDictionary<string, string> cleanTrackedGeneratedProvenanceState,
        params string?[] outputPaths)
    {
        StringComparer comparer = IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var outputState = new Dictionary<string, string>(comparer);
        foreach (string outputPath in outputPaths
                     .Where(static path => !string.IsNullOrWhiteSpace(path))
                     .Select(path => Path.GetFullPath(
                         Path.IsPathRooted(path)
                             ? path!
                             : Path.Combine(projectRoot, path!)))
                     .Distinct(comparer))
        {
            if (!cleanTrackedGeneratedProvenanceState.ContainsKey(outputPath) || !File.Exists(outputPath))
                continue;
            using Stream stream = File.OpenRead(outputPath);
            outputState[outputPath] = ComputeSha256Hex(stream);
        }

        return outputState;
    }

    internal static void ValidateTrackedManifestOutputState(
        IReadOnlyDictionary<string, string> outputState)
    {
        foreach (KeyValuePair<string, string> output in outputState)
        {
            if (!File.Exists(output.Key))
                throw new InvalidOperationException($"Generated manifest output disappeared during provenance validation: {output.Key}");
            using Stream stream = File.OpenRead(output.Key);
            if (!string.Equals(ComputeSha256Hex(stream), output.Value, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Generated manifest output changed during provenance validation: {output.Key}");
            }
        }
    }

    internal static SourceProvenance? TryBuildManifestProvenance(
        IReadOnlyCollection<DotNetPublishArtefactResult> artefacts,
        IReadOnlyDictionary<string, SourceProvenance> publishProvenanceByArtifact)
    {
        DotNetPublishArtefactResult[] publishArtefacts = artefacts
            .Where(static artefact => artefact.Category == DotNetPublishArtefactCategory.Publish)
            .ToArray();
        if (publishArtefacts.Length == 0 || publishArtefacts.Any(artefact =>
                !publishProvenanceByArtifact.ContainsKey(BuildArtifactProvenanceKey(artefact))))
        {
            return null;
        }

        SourceProvenance[] provenances = publishArtefacts
            .Select(artefact => publishProvenanceByArtifact[BuildArtifactProvenanceKey(artefact)])
            .ToArray();
        SourceProvenance sharedProvenance = provenances[0];
        if (provenances.All(provenance => ReferenceEquals(provenance, sharedProvenance)))
            return sharedProvenance;

        string?[] revisions = provenances
            .Select(static provenance => provenance.Revision)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (revisions.Length > 1)
            throw new InvalidOperationException("Published artifacts do not share one verified source revision.");

        bool? dirty = provenances.Any(static provenance => provenance.Dirty == true)
            ? true
            : provenances.All(static provenance => provenance.Dirty == false)
                ? false
                : null;
        return new SourceProvenance(
            revisions.SingleOrDefault(),
            dirty,
            provenances.SelectMany(static provenance => provenance.DirtyPaths)
                .Distinct(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .ToArray(),
            provenances.SelectMany(static provenance => provenance.DirtyReasons)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            provenances.SelectMany(static provenance => provenance.NoBuildPublishInputs)
                .GroupBy(
                    static input => input.EvaluationKey + "\n" + input.FullPath,
                    StringComparer.Ordinal)
                .Select(static group => group.First())
                .ToArray(),
            provenances.SelectMany(static provenance => provenance.PublishInputFiles)
                .Distinct(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .ToArray(),
            validateCurrentSourceWithTrackedGeneratedPaths: additionalTrackedGeneratedPaths =>
            {
                foreach (SourceProvenance provenance in provenances)
                    provenance.ValidateCurrentSource(additionalTrackedGeneratedPaths);
            });
    }

}
