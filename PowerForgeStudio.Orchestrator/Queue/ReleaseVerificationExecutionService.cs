using System.Net.Http;
using PowerForge;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Host;
using PowerForgeStudio.Orchestrator.Portfolio;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed class ReleaseVerificationExecutionService : IReleaseVerificationExecutionService, IDisposable
{
    private readonly ReleaseQueueCheckpointSerializer _checkpointSerializer = new();
    private readonly ReleaseQueueTargetProjectionService _targetProjectionService = new();
    private readonly PublishVerificationHostService _verificationHostService;
    private readonly bool _ownsVerificationHostService;

    public ReleaseVerificationExecutionService()
        : this(new PublishVerificationHostService(), ownsVerificationHostService: true)
    {
    }

    internal ReleaseVerificationExecutionService(
        HttpClient httpClient,
        PowerShellRepositoryResolver powerShellRepositoryResolver)
        : this(
            new PublishVerificationHostService(httpClient, powerShellRepositoryResolver),
            ownsVerificationHostService: false)
    {
    }

    internal ReleaseVerificationExecutionService(
        PublishVerificationHostService verificationHostService)
        : this(verificationHostService, ownsVerificationHostService: false)
    {
    }

    internal ReleaseVerificationExecutionService(
        PublishVerificationHostService verificationHostService,
        bool ownsVerificationHostService)
    {
        _verificationHostService = verificationHostService ?? throw new ArgumentNullException(nameof(verificationHostService));
        _ownsVerificationHostService = ownsVerificationHostService;
    }

    public void Dispose()
    {
        if (_ownsVerificationHostService)
        {
            _verificationHostService.Dispose();
        }
    }

    public IReadOnlyList<ReleaseVerificationTarget> BuildPendingTargets(IEnumerable<ReleaseQueueItem> queueItems)
    {
        return _targetProjectionService.BuildTargetsByKey(
            queueItems,
            ReleaseQueueStage.Verify,
            TryDeserializePublishResult,
            static (_, publishResult) => publishResult.Receipts.Select(receipt => new ReleaseVerificationTarget(
                RootPath: receipt.RootPath,
                RepositoryName: receipt.RepositoryName,
                AdapterKind: receipt.AdapterKind,
                TargetName: receipt.TargetName,
                TargetKind: receipt.TargetKind,
                Destination: receipt.Destination,
                SourcePath: receipt.SourcePath)),
            static target => (target.RootPath, target.AdapterKind, target.TargetName, target.TargetKind, target.Destination, target.SourcePath));
    }

    public async Task<ReleaseVerificationExecutionResult> ExecuteAsync(ReleaseQueueItem queueItem, CancellationToken cancellationToken = default)
        => await ExecuteAsync(queueItem, cancellationToken, progress: null).ConfigureAwait(false);

    public async Task<ReleaseVerificationExecutionResult> ExecuteAsync(
        ReleaseQueueItem queueItem,
        CancellationToken cancellationToken,
        IReleaseArtifactProgressSink? progress)
    {
        ArgumentNullException.ThrowIfNull(queueItem);

        var publishResult = TryDeserializePublishResult(queueItem);
        if (publishResult is null)
        {
            return new ReleaseVerificationExecutionResult(
                RootPath: queueItem.RootPath,
                Succeeded: false,
                Summary: "Verification checkpoint could not be read from queue state.",
                SourceCheckpointStateJson: queueItem.CheckpointStateJson,
                Receipts: [
                    FailedReceipt(queueItem.RootPath, queueItem.RepositoryName, "Verify", "Queue checkpoint", null, "Queue state is missing the publish checkpoint.", "Checkpoint")
                ]);
        }

        if (publishResult.Receipts.Count == 0)
        {
            return new ReleaseVerificationExecutionResult(
                RootPath: queueItem.RootPath,
                Succeeded: false,
                Summary: "Verification cannot run because no publish receipts were captured.",
                SourceCheckpointStateJson: queueItem.CheckpointStateJson,
                Receipts: [
                    FailedReceipt(queueItem.RootPath, queueItem.RepositoryName, "Verify", "Publish receipts", null, "No publish receipts were captured for verification.", "Publish")
                ]);
        }

        var signingResult = _checkpointSerializer.TryDeserialize<ReleaseSigningExecutionResult>(
            publishResult.SourceCheckpointStateJson);

        for (var index = 0; index < publishResult.Receipts.Count; index++)
        {
            var planned = publishResult.Receipts[index];
            await progress.ReportAsync(
                ReleaseQueueStage.Verify,
                planned.TargetName,
                planned.SourcePath,
                "Planned",
                completedItems: 0,
                totalItems: publishResult.Receipts.Count,
                $"Recorded {planned.TargetKind} destination ready for verification.",
                CancellationToken.None).ConfigureAwait(false);
        }

        var receipts = new List<ReleaseVerificationReceipt>(publishResult.Receipts.Count);
        for (var index = 0; index < publishResult.Receipts.Count; index++)
        {
            var receipt = publishResult.Receipts[index];
            await progress.ReportAsync(
                ReleaseQueueStage.Verify,
                receipt.TargetName,
                receipt.SourcePath,
                "Checking",
                index,
                publishResult.Receipts.Count,
                $"Checking the recorded {receipt.TargetKind} destination.",
                CancellationToken.None).ConfigureAwait(false);
            ReleaseVerificationReceipt result;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                result = await VerifyReceiptAsync(receipt, signingResult?.Receipts, cancellationToken).ConfigureAwait(false);
                // Some hosts return a failed probe when cancelled instead of throwing.
                if (result.Status != ReleaseVerificationReceiptStatus.Verified)
                    cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Exception exception)
            {
                var cancelled = cancellationToken.IsCancellationRequested || exception is OperationCanceledException;
                var failed = FailedReceipt(receipt.RootPath, receipt.RepositoryName, receipt.AdapterKind,
                    receipt.TargetName, receipt.Destination,
                    cancelled ? "Verification was cancelled before this check completed."
                        : "Verification was interrupted before this check completed. Retry verification to check remote state.",
                    receipt.TargetKind);
                receipts.Add(failed);
                var progressSaved = await TryReportTerminalProgressAsync(
                    progress,
                    ReleaseQueueStage.Verify,
                    receipt.TargetName,
                    receipt.SourcePath,
                    cancelled ? "Cancelled" : "Failed",
                    index + 1,
                    publishResult.Receipts.Count,
                    failed.Summary,
                    CancellationToken.None).ConfigureAwait(false);
                var failedResult = ReleaseQueueExecutionResultFactory.CreateVerificationResult(queueItem, receipts) with
                {
                    WasCancelled = cancelled,
                    Summary = cancelled ? "Verification cancelled; completed check results retained."
                        : "Verification interrupted; completed check results retained."
                };
                return progressSaved ? failedResult : ProgressPersistenceFailure(queueItem, receipts, cancelled);
            }

            receipts.Add(result);
            var terminalSaved = await TryReportTerminalProgressAsync(
                progress,
                ReleaseQueueStage.Verify,
                receipt.TargetName,
                receipt.SourcePath,
                result.Status.ToString(),
                index + 1,
                publishResult.Receipts.Count,
                result.Summary,
                CancellationToken.None).ConfigureAwait(false);
            if (!terminalSaved)
                return ProgressPersistenceFailure(queueItem, receipts, cancelled: false);
        }

        return ReleaseQueueExecutionResultFactory.CreateVerificationResult(queueItem, receipts);
    }

    private static async Task<bool> TryReportTerminalProgressAsync(
        IReleaseArtifactProgressSink? progress,
        ReleaseQueueStage stage,
        string itemName,
        string? itemPath,
        string state,
        int completedItems,
        int totalItems,
        string detail,
        CancellationToken cancellationToken)
    {
        try
        {
            await progress.ReportAsync(stage, itemName, itemPath, state, completedItems, totalItems, detail, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static ReleaseVerificationExecutionResult ProgressPersistenceFailure(
        ReleaseQueueItem queueItem,
        IReadOnlyList<ReleaseVerificationReceipt> receipts,
        bool cancelled)
        => new(
            queueItem.RootPath,
            false,
            "A verification check completed, but its progress update could not be saved. Completed receipts were retained; retry verification to finish the remaining targets.",
            queueItem.CheckpointStateJson,
            receipts)
        {
            WasCancelled = cancelled
        };

    private async Task<ReleaseVerificationReceipt> VerifyReceiptAsync(
        ReleasePublishReceipt publishReceipt,
        IReadOnlyList<ReleaseSigningReceipt>? signingReceipts,
        CancellationToken cancellationToken)
    {
        if (publishReceipt.Status == ReleasePublishReceiptStatus.Skipped)
        {
            return SkippedReceipt(
                publishReceipt,
                string.IsNullOrWhiteSpace(publishReceipt.Summary)
                    ? "Publish was intentionally skipped, so verification is not required."
                    : publishReceipt.Summary);
        }

        if (publishReceipt.Status != ReleasePublishReceiptStatus.Published)
        {
            return FailedReceipt(
                publishReceipt.RootPath,
                publishReceipt.RepositoryName,
                publishReceipt.AdapterKind,
                publishReceipt.TargetName,
                publishReceipt.Destination,
                $"Publish receipt status was {publishReceipt.Status}; verification cannot pass.",
                publishReceipt.TargetKind);
        }

        return publishReceipt.TargetKind switch
        {
            "GitHub" => await VerifyGitHubAsync(publishReceipt, cancellationToken),
            "NuGet" => await VerifyNuGetAsync(publishReceipt, signingReceipts, cancellationToken),
            "ModulePackages" => await VerifyModulePackageAsync(publishReceipt, signingReceipts, cancellationToken),
            "PowerShellRepository" => await VerifyPowerShellRepositoryAsync(publishReceipt, cancellationToken),
            _ => FailedReceipt(publishReceipt.RootPath, publishReceipt.RepositoryName, publishReceipt.AdapterKind,
                publishReceipt.TargetName, publishReceipt.Destination,
                $"Verification is not available for published {publishReceipt.TargetKind} targets. Delivery remains unverified.", publishReceipt.TargetKind)
        };
    }

    private async Task<ReleaseVerificationReceipt> VerifyModulePackageAsync(
        ReleasePublishReceipt publishReceipt,
        IReadOnlyList<ReleaseSigningReceipt>? signingReceipts,
        CancellationToken cancellationToken)
    {
        if (publishReceipt.HasPackageIdentity &&
            publishReceipt.SourcePath?.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) == true)
            return await VerifyNuGetAsync(publishReceipt, signingReceipts, cancellationToken, "NuGet").ConfigureAwait(false);

        if (publishReceipt.TargetName.EndsWith(" GitHub release", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(publishReceipt.Destination, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            return await VerifyGitHubAsync(publishReceipt, cancellationToken, "GitHub").ConfigureAwait(false);

        return FailedReceipt(publishReceipt.RootPath, publishReceipt.RepositoryName, publishReceipt.AdapterKind,
            publishReceipt.TargetName, publishReceipt.Destination,
            "The module-owned package receipt does not identify a verifiable NuGet package or GitHub release.",
            publishReceipt.TargetKind);
    }

    private async Task<ReleaseVerificationReceipt> VerifyGitHubAsync(
        ReleasePublishReceipt publishReceipt,
        CancellationToken cancellationToken,
        string? hostTargetKind = null)
    {
        var result = await VerifyWithHostAsync(publishReceipt, cancellationToken, hostTargetKind: hostTargetKind);
        return MapReceipt(publishReceipt, result);
    }

    private async Task<ReleaseVerificationReceipt> VerifyNuGetAsync(
        ReleasePublishReceipt publishReceipt,
        IReadOnlyList<ReleaseSigningReceipt>? signingReceipts,
        CancellationToken cancellationToken,
        string? hostTargetKind = null)
    {
        string? destinationOverride = null;
        if (publishReceipt.DestinationCredentialsOmitted && publishReceipt.PublicRegistry is null)
        {
            destinationOverride = TryResolveCurrentNuGetDestination(publishReceipt);
            if (destinationOverride is null)
                return FailedReceipt(publishReceipt.RootPath, publishReceipt.RepositoryName, publishReceipt.AdapterKind,
                    publishReceipt.TargetName, publishReceipt.Destination,
                    "The saved NuGet destination omitted URL credentials or query values. The matching current project configuration is unavailable, so remote verification could not run.",
                    publishReceipt.TargetKind);
        }
        var expectedDigest = signingReceipts?.FirstOrDefault(signing =>
            signing.Status == ReleaseSigningReceiptStatus.Signed &&
            string.Equals(signing.ArtifactPath, publishReceipt.SourcePath,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
            (string.Equals(signing.AdapterKind, publishReceipt.AdapterKind, StringComparison.OrdinalIgnoreCase) ||
             (publishReceipt.TargetKind == "ModulePackages" &&
              string.Equals(signing.AdapterKind, "ModuleBuild", StringComparison.OrdinalIgnoreCase))))?.ContentSha256;
        var result = await VerifyWithHostAsync(publishReceipt, cancellationToken, destinationOverride, expectedDigest, hostTargetKind);
        return MapReceipt(publishReceipt, result);
    }

    private static string? TryResolveCurrentNuGetDestination(ReleasePublishReceipt receipt)
    {
        if (!Directory.Exists(receipt.RootPath)) return null;
        try
        {
            var repository = new RepositoryCatalogScanner().InspectRepository(receipt.RootPath);
            var publisher = new ProjectBuildPublishHostService();
            if (string.Equals(receipt.AdapterKind, "ProjectBuild", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(repository.ProjectBuildScriptPath)) return null;
                var configPath = RepositoryPlanPreviewService.ResolveProjectConfigPath(repository.ProjectBuildScriptPath, receipt.RootPath);
                if (string.IsNullOrWhiteSpace(configPath)) return null;
                var current = publisher.ResolvePublishDestinationForVerification(configPath);
                return MatchesSavedDestination(current, receipt.Destination) ? current : null;
            }

            if (!string.Equals(receipt.TargetKind, "ModulePackages", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(receipt.AdapterKind, "UnifiedRelease", StringComparison.OrdinalIgnoreCase)) return null;

            IReadOnlyList<ModulePackageReleaseLane> lanes;
            if (!string.IsNullOrWhiteSpace(repository.UnifiedReleaseConfigPath))
            {
                var spec = PowerForgeReleaseService.LoadConfiguration(repository.UnifiedReleaseConfigPath!);
                lanes = ModulePackageReleaseCheckpointService.ResolveLanes(repository.UnifiedReleaseConfigPath!, spec);
            }
            else if (string.Equals(Path.GetExtension(repository.ModuleBuildScriptPath), ".json", StringComparison.OrdinalIgnoreCase))
            {
                var context = new ModulePipelineConfigurationService().Load(repository.ModuleBuildScriptPath!);
                lanes = ModulePackageReleaseCheckpointService.ResolveLanes(context);
            }
            else return null;

            var matches = lanes.Where(static lane => lane.PublishNuget)
                .Select(lane => lane.Reference is not null
                    ? publisher.ResolvePublishDestinationForVerification(lane.Reference, lane.ConfigPath)
                    : publisher.ResolvePublishDestinationForVerification(lane.Inline!))
                .Where(current => MatchesSavedDestination(current, receipt.Destination))
                .Distinct(StringComparer.Ordinal)
                .Take(2)
                .ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool MatchesSavedDestination(string current, string? saved)
        => StudioOutputSanitizer.DestinationCredentialsOmitted(current) &&
           string.Equals(StudioOutputSanitizer.SanitizeDestination(current), saved, StringComparison.Ordinal);

    private async Task<ReleaseVerificationReceipt> VerifyPowerShellRepositoryAsync(ReleasePublishReceipt publishReceipt, CancellationToken cancellationToken)
    {
        var result = await VerifyWithHostAsync(publishReceipt, cancellationToken);
        return MapReceipt(publishReceipt, result);
    }

    private ReleasePublishExecutionResult? TryDeserializePublishResult(ReleaseQueueItem queueItem)
        => _checkpointSerializer.TryDeserialize<ReleasePublishExecutionResult>(queueItem.CheckpointStateJson);

    private static ReleaseVerificationReceipt VerifiedReceipt(ReleasePublishReceipt publishReceipt, string summary)
        => ReleaseQueueReceiptFactory.CreateVerificationReceipt(
            publishReceipt,
            ReleaseVerificationReceiptStatus.Verified,
            summary);

    private static ReleaseVerificationReceipt SkippedReceipt(ReleasePublishReceipt publishReceipt, string summary)
        => ReleaseQueueReceiptFactory.CreateVerificationReceipt(
            publishReceipt,
            ReleaseVerificationReceiptStatus.Skipped,
            summary);

    private static ReleaseVerificationReceipt FailedReceipt(string rootPath, string repositoryName, string adapterKind, string targetName, string? destination, string summary, string? targetKind = null)
        => ReleaseQueueReceiptFactory.FailedVerificationReceipt(rootPath, repositoryName, adapterKind, targetName, destination, summary, targetKind);

    private async Task<PublishVerificationResult> VerifyWithHostAsync(
        ReleasePublishReceipt publishReceipt,
        CancellationToken cancellationToken,
        string? destinationOverride = null,
        string? expectedContentSha256 = null,
        string? hostTargetKind = null)
        => await _verificationHostService.VerifyAsync(new PublishVerificationRequest {
            RootPath = publishReceipt.RootPath,
            RepositoryName = publishReceipt.RepositoryName,
            AdapterKind = publishReceipt.AdapterKind,
            TargetName = publishReceipt.TargetName,
            TargetKind = hostTargetKind ?? publishReceipt.TargetKind,
            Destination = destinationOverride ?? publishReceipt.Destination,
            SourcePath = publishReceipt.SourcePath,
            PackageId = publishReceipt.PackageId,
            PackageVersion = publishReceipt.PackageVersion,
            ExpectedContentSha256 = expectedContentSha256
        }, cancellationToken).ConfigureAwait(false);

    private static ReleaseVerificationReceipt MapReceipt(ReleasePublishReceipt publishReceipt, PublishVerificationResult result)
        => result.Status switch
        {
            PublishVerificationStatus.Verified => VerifiedReceipt(publishReceipt, result.Summary),
            _ => FailedReceipt(
                publishReceipt.RootPath,
                publishReceipt.RepositoryName,
                publishReceipt.AdapterKind,
                publishReceipt.TargetName,
                publishReceipt.Destination,
                result.Summary,
                publishReceipt.TargetKind)
        };
}
