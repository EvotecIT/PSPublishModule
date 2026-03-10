using System.Text.Json;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

[Trait("Category", "Smoke")]
public sealed class PowerForgeStudioSmokeHarnessTests
{
    private static readonly SemaphoreSlim SmokeEnvironmentLock = new(1, 1);

    [Fact(Skip = "Live smoke harness is opt-in; use Build/Smoke-ReleaseOpsStudio.ps1 with explicit environment flags.")]
    public async Task LocalSmokePath_ExercisesBuildAndRetryStages()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var repositoryIdentity = ResolveRepositoryIdentity(repositoryRoot);
        var buildService = new ReleaseBuildExecutionService();
        var checkpointReader = new ReleaseBuildCheckpointReader();
        var signingService = new ReleaseSigningExecutionService();
        var publishService = new ReleasePublishExecutionService();
        var verificationService = new ReleaseVerificationExecutionService();
        var queueRunner = new ReleaseQueueRunner();
        var realSigningEnabled = IsRealSigningEnabled();
        var signThumbprint = Environment.GetEnvironmentVariable("RELEASE_OPS_STUDIO_SMOKE_SIGN_THUMBPRINT");
        var signStore = Environment.GetEnvironmentVariable("RELEASE_OPS_STUDIO_SMOKE_SIGN_STORE");
        var signTimestampUrl = Environment.GetEnvironmentVariable("RELEASE_OPS_STUDIO_SMOKE_SIGN_TIMESTAMP_URL");

        var environmentScope = new EnvironmentScope()
            .Set("RELEASE_OPS_STUDIO_RUN_LIVE_SMOKE", "true")
            .Set("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", null);

        if (realSigningEnabled)
        {
            EnsureRealSigningConfiguration(signThumbprint);
            environmentScope
                .Set("RELEASE_OPS_STUDIO_SIGN_THUMBPRINT", signThumbprint)
                .Set("RELEASE_OPS_STUDIO_SIGN_STORE", signStore ?? "CurrentUser")
                .Set("RELEASE_OPS_STUDIO_SIGN_TIMESTAMP_URL", string.IsNullOrWhiteSpace(signTimestampUrl) ? "http://timestamp.digicert.com" : signTimestampUrl);
        }
        else
        {
            environmentScope
                .Set("RELEASE_OPS_STUDIO_SIGN_THUMBPRINT", null)
                .Set("RELEASE_OPS_STUDIO_SIGN_STORE", null)
                .Set("RELEASE_OPS_STUDIO_SIGN_TIMESTAMP_URL", null);
        }

        await SmokeEnvironmentLock.WaitAsync();
        try
        {
            using var _ = environmentScope;

            var buildResult = await buildService.ExecuteAsync(repositoryRoot);

            Assert.Contains(buildResult.AdapterResults, result => result.Succeeded);
            Assert.NotEmpty(buildResult.AdapterResults);
            Assert.Contains(buildResult.AdapterResults, result => result.ArtifactFiles.Count > 0 || result.ArtifactDirectories.Count > 0);

            var buildCheckpointJson = JsonSerializer.Serialize(buildResult);
            var signingQueueItem = CreateQueueItem(
                repositoryIdentity,
                repositoryRoot,
                ReleaseQueueStage.Sign,
                ReleaseQueueItemStatus.WaitingApproval,
                "sign.waiting.usb",
                buildCheckpointJson,
                "Smoke build completed.");

            var signingManifest = checkpointReader.BuildSigningManifest([signingQueueItem]);
            Assert.NotEmpty(signingManifest);

            var signingResult = await signingService.ExecuteAsync(signingQueueItem);
            ReleaseSigningExecutionResult publishSigningResult;
            if (realSigningEnabled)
            {
                Assert.True(signingResult.Succeeded, DescribeSigningResult(signingResult));
                Assert.Contains(signingResult.Receipts, receipt => receipt.Status == ReleaseSigningReceiptStatus.Signed);
                publishSigningResult = signingResult;
            }
            else
            {
                Assert.False(signingResult.Succeeded);
                Assert.Contains("RELEASE_OPS_STUDIO_SIGN_THUMBPRINT", signingResult.Summary, StringComparison.OrdinalIgnoreCase);

                var signingFailedSession = CreateSession(signingQueueItem);
                var signingFailureTransition = queueRunner.FailSigning(signingFailedSession, repositoryRoot, signingResult);
                Assert.True(signingFailureTransition.Changed);

                var signingRetryTransition = queueRunner.RetryFailedItem(signingFailureTransition.Session);
                Assert.True(signingRetryTransition.Changed);
                Assert.Equal(ReleaseQueueStage.Sign, signingRetryTransition.Session.Items[0].Stage);
                Assert.Equal(ReleaseQueueItemStatus.WaitingApproval, signingRetryTransition.Session.Items[0].Status);
                Assert.Equal(buildCheckpointJson, signingRetryTransition.Session.Items[0].CheckpointStateJson);

                publishSigningResult = new ReleaseSigningExecutionResult(
                    RootPath: repositoryRoot,
                    Succeeded: true,
                    Summary: $"Smoke signing simulation captured {signingManifest.Count} artifact(s).",
                    SourceCheckpointStateJson: buildCheckpointJson,
                    Receipts: signingManifest.Select(artifact => new ReleaseSigningReceipt(
                        RootPath: repositoryRoot,
                        RepositoryName: repositoryIdentity.RepositoryName,
                        AdapterKind: artifact.AdapterKind,
                        ArtifactPath: artifact.ArtifactPath,
                        ArtifactKind: artifact.ArtifactKind,
                        Status: ReleaseSigningReceiptStatus.Signed,
                        Summary: "Smoke path treats captured build artifacts as signed inputs for publish gating.",
                        SignedAtUtc: DateTimeOffset.UtcNow)).ToList());
            }

            var publishQueueItem = CreateQueueItem(
                repositoryIdentity,
                repositoryRoot,
                ReleaseQueueStage.Publish,
                ReleaseQueueItemStatus.ReadyToRun,
                "publish.ready",
                JsonSerializer.Serialize(publishSigningResult),
                "Smoke signing stage completed.");

            var publishResult = await publishService.ExecuteAsync(publishQueueItem);
            Assert.False(publishResult.Succeeded);
            Assert.NotEmpty(publishResult.Receipts);
            Assert.Contains("RELEASE_OPS_STUDIO_ENABLE_PUBLISH", publishResult.Summary, StringComparison.OrdinalIgnoreCase);

            var publishFailedSession = CreateSession(publishQueueItem);
            var publishFailureTransition = queueRunner.FailPublish(publishFailedSession, repositoryRoot, publishResult);
            Assert.True(publishFailureTransition.Changed);

            var publishRetryTransition = queueRunner.RetryFailedItem(publishFailureTransition.Session);
            Assert.True(publishRetryTransition.Changed);
            Assert.Equal(ReleaseQueueStage.Publish, publishRetryTransition.Session.Items[0].Stage);
            Assert.Equal(ReleaseQueueItemStatus.ReadyToRun, publishRetryTransition.Session.Items[0].Status);
            Assert.Equal(JsonSerializer.Serialize(publishSigningResult), publishRetryTransition.Session.Items[0].CheckpointStateJson);

            var verificationQueueItem = CreateQueueItem(
                repositoryIdentity,
                repositoryRoot,
                ReleaseQueueStage.Verify,
                ReleaseQueueItemStatus.ReadyToRun,
                "verify.ready",
                JsonSerializer.Serialize(publishResult),
                "Smoke publish stage completed.");

            var verificationResult = await verificationService.ExecuteAsync(verificationQueueItem);
            Assert.False(verificationResult.Succeeded);
            Assert.NotEmpty(verificationResult.Receipts);
            Assert.Contains(verificationResult.Receipts, receipt => receipt.Summary.Contains("Publish receipt status", StringComparison.OrdinalIgnoreCase));

            var verificationFailedSession = CreateSession(verificationQueueItem);
            var verificationFailureTransition = queueRunner.FailVerification(verificationFailedSession, repositoryRoot, verificationResult);
            Assert.True(verificationFailureTransition.Changed);

            var verificationRetryTransition = queueRunner.RetryFailedItem(verificationFailureTransition.Session);
            Assert.True(verificationRetryTransition.Changed);
            Assert.Equal(ReleaseQueueStage.Verify, verificationRetryTransition.Session.Items[0].Stage);
            Assert.Equal(ReleaseQueueItemStatus.ReadyToRun, verificationRetryTransition.Session.Items[0].Status);
            Assert.Equal(JsonSerializer.Serialize(publishResult), verificationRetryTransition.Session.Items[0].CheckpointStateJson);
        }
        finally
        {
            SmokeEnvironmentLock.Release();
        }
    }

    private static bool IsRealSigningEnabled()
        => string.Equals(Environment.GetEnvironmentVariable("RELEASE_OPS_STUDIO_SMOKE_ENABLE_SIGNING"), "true", StringComparison.OrdinalIgnoreCase);

    private static void EnsureRealSigningConfiguration(string? thumbprint)
    {
        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            throw new InvalidOperationException("Set RELEASE_OPS_STUDIO_SMOKE_SIGN_THUMBPRINT before enabling real signing smoke mode.");
        }
    }

    private static string ResolveRepositoryRoot()
    {
        var configured = Environment.GetEnvironmentVariable("RELEASE_OPS_STUDIO_SMOKE_ROOT");
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
        {
            return configured;
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PSPublishModule.sln"))
                && (File.Exists(Path.Combine(current.FullName, "Build", "Build-Project.ps1"))
                    || File.Exists(Path.Combine(current.FullName, "Build", "Build-Module.ps1"))))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate a supported repository root for the smoke harness.");
    }

    private static RepositoryIdentity ResolveRepositoryIdentity(string repositoryRoot)
    {
        var repositoryName = Path.GetFileName(repositoryRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var hasProjectBuild = File.Exists(Path.Combine(repositoryRoot, "Build", "Build-Project.ps1"));
        var hasModuleBuild = File.Exists(Path.Combine(repositoryRoot, "Build", "Build-Module.ps1"));
        var repositoryKind = hasProjectBuild && hasModuleBuild
            ? ReleaseRepositoryKind.Mixed
            : hasProjectBuild
                ? ReleaseRepositoryKind.Library
                : hasModuleBuild
                    ? ReleaseRepositoryKind.Module
                    : ReleaseRepositoryKind.Unknown;

        return new RepositoryIdentity(repositoryName, repositoryKind);
    }

    private static ReleaseQueueItem CreateQueueItem(
        RepositoryIdentity repositoryIdentity,
        string repositoryRoot,
        ReleaseQueueStage stage,
        ReleaseQueueItemStatus status,
        string checkpointKey,
        string checkpointStateJson,
        string summary)
    {
        return new ReleaseQueueItem(
            RootPath: repositoryRoot,
            RepositoryName: repositoryIdentity.RepositoryName,
            RepositoryKind: repositoryIdentity.RepositoryKind,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: stage,
            Status: status,
            Summary: summary,
            CheckpointKey: checkpointKey,
            CheckpointStateJson: checkpointStateJson,
            UpdatedAtUtc: DateTimeOffset.UtcNow);
    }

    private static ReleaseQueueSession CreateSession(ReleaseQueueItem item)
    {
        return new ReleaseQueueSession(
            SessionId: Guid.NewGuid().ToString("N"),
            WorkspaceRoot: Path.GetDirectoryName(item.RootPath) ?? item.RootPath,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Summary: new ReleaseQueueSummary(
                TotalItems: 1,
                BuildReadyItems: item.Stage == ReleaseQueueStage.Build && item.Status == ReleaseQueueItemStatus.ReadyToRun ? 1 : 0,
                PreparePendingItems: item.Stage == ReleaseQueueStage.Prepare && item.Status == ReleaseQueueItemStatus.Pending ? 1 : 0,
                WaitingApprovalItems: item.Status == ReleaseQueueItemStatus.WaitingApproval ? 1 : 0,
                BlockedItems: item.Status is ReleaseQueueItemStatus.Blocked or ReleaseQueueItemStatus.Failed ? 1 : 0,
                VerificationReadyItems: item.Stage == ReleaseQueueStage.Verify && item.Status == ReleaseQueueItemStatus.ReadyToRun ? 1 : 0),
            Items: [item]);
    }

    private static string DescribeBuildResult(ReleaseBuildExecutionResult buildResult)
    {
        var adapterDetails = buildResult.AdapterResults.Select(result =>
            $"{result.AdapterKind}: succeeded={result.Succeeded}, exit={result.ExitCode}, summary={result.Summary}, errorTail={result.ErrorTail}, outputTail={result.OutputTail}");
        return $"Build summary: {buildResult.Summary}{Environment.NewLine}{string.Join(Environment.NewLine, adapterDetails)}";
    }

    private static string DescribeSigningResult(ReleaseSigningExecutionResult signingResult)
    {
        var receiptDetails = signingResult.Receipts.Select(receipt =>
            $"{receipt.AdapterKind}:{receipt.ArtifactKind}:{receipt.Status}:{receipt.Summary}:{receipt.ArtifactPath}");
        return $"Signing summary: {signingResult.Summary}{Environment.NewLine}{string.Join(Environment.NewLine, receiptDetails)}";
    }

    private sealed class EnvironmentScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originalValues = new(StringComparer.OrdinalIgnoreCase);

        public EnvironmentScope Set(string name, string? value)
        {
            if (!_originalValues.ContainsKey(name))
            {
                _originalValues[name] = Environment.GetEnvironmentVariable(name);
            }

            Environment.SetEnvironmentVariable(name, value);
            return this;
        }

        public void Dispose()
        {
            foreach (var entry in _originalValues)
            {
                Environment.SetEnvironmentVariable(entry.Key, entry.Value);
            }
        }
    }

    private readonly record struct RepositoryIdentity(string RepositoryName, ReleaseRepositoryKind RepositoryKind);
}

