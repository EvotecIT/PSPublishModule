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
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
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
        string? manifestJson = null;
        string? manifestText = null;
        string? checksumsPath = null;
        string? runReportPath = null;

        try
        {
            foreach (var step in plan.Steps ?? Array.Empty<DotNetPublishStep>())
            {
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
                            Build(plan, step.Runtime);
                            break;
                        case DotNetPublishStepKind.Publish:
                            artefacts.Add(Publish(plan, step.TargetName!, step.Framework ?? string.Empty, step.Runtime!, step.Style));
                            break;
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
                            msiBuilds.Add(BuildMsiPackage(plan, msiPrepares, step));
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
                            (manifestJson, manifestText, checksumsPath) = WriteManifests(plan, artefacts, storePackages, msiBuilds);
                            break;
                    }

                    progress.StepCompleted(step);
                    stepSucceeded = true;
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
            successResult.RunReportPath = runReportPath;
            return successResult;
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
            failedResult.RunReportPath = runReportPath;
            return failedResult;
        }
    }

}
