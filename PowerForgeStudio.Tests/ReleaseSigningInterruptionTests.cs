using System.Text.Json;
using PowerForge;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed class ReleaseSigningInterruptionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InterruptedArtifactRetainsPriorReceiptAndDoesNotReportSuccess(bool cancellation)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-sign-interruption-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var files = Enumerable.Range(1, 3).Select(i => Path.Combine(root, i + ".ps1")).ToArray();
            foreach (var file in files) await File.WriteAllTextAsync(file, "unsigned");
            var calls = 0;
            var service = CreateService((request, token) =>
            {
                calls++;
                if (calls == 2)
                {
                    File.WriteAllText(Path.Combine(request.SigningPath, request.IncludePatterns[0]), "partially signed");
                    return Task.FromException<AuthenticodeSigningHostResult>(cancellation ? new OperationCanceledException() : new IOException("Signing device unavailable"));
                }
                File.WriteAllText(Path.Combine(request.SigningPath, request.IncludePatterns[0]), "signed");
                return Task.FromResult(new AuthenticodeSigningHostResult { ExitCode = 0 });
            });
            var result = await service.ExecuteAsync(Item(root, files));
            Assert.False(result.Succeeded); Assert.True(result.RequiresRebuild); Assert.Equal(3, result.Receipts.Count);
            Assert.Equal(ReleaseSigningReceiptStatus.Signed, result.Receipts[0].Status);
            Assert.False(string.IsNullOrEmpty(result.Receipts[0].ContentSha256));
            Assert.Equal(ReleaseSigningReceiptStatus.Failed, result.Receipts[1].Status);
            Assert.Equal("partially signed", await File.ReadAllTextAsync(files[1]));
            var runner = new ReleaseQueueRunner();
            var session = ReleaseQueueSessionFactory.Create(root, [Item(root, files)], DateTimeOffset.UtcNow);
            var failedSession = runner.FailSigning(session, root, result).Session;
            var retry = runner.RetryFailedItem(failedSession);
            Assert.Equal(ReleaseQueueStage.Build, Assert.Single(retry.Session.Items).Stage);
            Assert.Null(retry.Session.Items[0].CheckpointStateJson);
            var batchRetry = runner.RetryFailedItems(failedSession, _ => true);
            Assert.Equal(ReleaseQueueStage.Build, Assert.Single(batchRetry.Session.Items).Stage);
            Assert.Equal(cancellation ? 2 : 3, calls);
            if (cancellation)
            {
                Assert.Contains("Not attempted", result.Receipts[2].Summary);
                Assert.Equal("unsigned", await File.ReadAllTextAsync(files[2]));
                Assert.Contains("cancelled", result.Summary);
            }
            else Assert.Equal(ReleaseSigningReceiptStatus.Signed, result.Receipts[2].Status);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CancellationAfterLastArtifactCannotProduceSuccessfulSigningResult()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-sign-last-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var file = Path.Combine(root, "one.ps1"); await File.WriteAllTextAsync(file, "unsigned");
            using var cancellation = new CancellationTokenSource();
            var service = CreateService((_, _) => { cancellation.Cancel(); return Task.FromResult(new AuthenticodeSigningHostResult { ExitCode = 0 }); });
            var result = await service.ExecuteAsync(Item(root, [file]), cancellation.Token);
            Assert.False(result.Succeeded); Assert.True(result.RequiresRebuild); Assert.Equal(ReleaseSigningReceiptStatus.Signed, Assert.Single(result.Receipts).Status);
            Assert.Contains("cancelled", result.Summary);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task MissingOrFailedBuildCheckpointCannotAdvanceSigning()
    {
        var service = CreateService((_, _) => throw new InvalidOperationException("Must not sign"));
        var item = Item(Path.GetTempPath(), []);
        Assert.False((await service.ExecuteAsync(item with { CheckpointStateJson = null })).Succeeded);
        Assert.False((await service.ExecuteAsync(item with { Stage = ReleaseQueueStage.Publish })).Succeeded);
        Assert.False((await service.ExecuteAsync(item)).Succeeded);
    }

    private static ReleaseQueueItem Item(string root, string[] files)
    {
        var build = new ReleaseBuildExecutionResult(root, true, "Built", 1, [new(ReleaseBuildAdapterKind.ProjectBuild, true, "Built", 0, 1, [], files)]);
        return new(root, "Fixture", ReleaseRepositoryKind.Library, ReleaseWorkspaceKind.PrimaryRepository, 1, ReleaseQueueStage.Sign,
            ReleaseQueueItemStatus.WaitingApproval, "Ready", "sign.waiting.usb", JsonSerializer.Serialize(build), DateTimeOffset.UtcNow);
    }
    private static ReleaseSigningExecutionService CreateService(Func<AuthenticodeSigningHostRequest, CancellationToken, Task<AuthenticodeSigningHostResult>> sign)
        => new(new ReleaseBuildCheckpointReader(), new ReleaseSigningHostSettingsResolver(name => name == "RELEASE_OPS_STUDIO_SIGN_THUMBPRINT" ? "test-thumbprint" : null, () => "fixture-module"),
            new CertificateFingerprintResolver((_, _) => "test-fingerprint"), sign,
            (_, _) => throw new InvalidOperationException("No NuGet signing in this fixture"));
}
