using System.Net;
using System.Text.Json;
using PowerForge;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Orchestrator.Queue;
using PowerForgeStudio.Orchestrator.Storage;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioVerificationExecutionServiceTests
{
    [Theory]
    [InlineData("NuGet", null, false)]
    [InlineData("NuGet", "https://feed.test/index.json", true)]
    [InlineData("WinGet", "fixture", false)]
    public async Task UnavailablePublishedTargetCannotCompleteVerification(string kind, string? destination, bool cancel)
    {
        using var package = CreateTemporaryPackage("Fixture", "1.2.3");
        using var cancellation = new CancellationTokenSource();
        var receipt = new ReleasePublishReceipt("root", "Fixture", "ProjectBuild", "Package", kind,
            destination, package.PackagePath, ReleasePublishReceiptStatus.Published, "Published", DateTimeOffset.UtcNow);
        var published = new ReleasePublishExecutionResult("root", true, "Published", "{}", [receipt]);
        var item = CreateVerifyReadyQueueItem("root", "Fixture", ReleaseRepositoryKind.Library, JsonSerializer.Serialize(published));
        using var client = new HttpClient(new StubHttpMessageHandler(_ => {
            cancellation.Cancel();
            throw new OperationCanceledException("fixture-secret", cancellation.Token);
        }));
        using var service = new ReleaseVerificationExecutionService(client,
            new PowerShellRepositoryResolver(new StubPowerShellRunner(_ => throw new InvalidOperationException())));
        var result = await service.ExecuteAsync(item, cancellation.Token);
        Assert.False(result.Succeeded);
        Assert.Equal(cancel, result.WasCancelled);
        Assert.Equal(ReleaseVerificationReceiptStatus.Failed, Assert.Single(result.Receipts).Status);
        Assert.DoesNotContain("fixture-secret", JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData("before")]
    [InlineData("between")]
    [InlineData("during")]
    [InlineData("last-success")]
    public async Task CancellationRetainsCompletedChecksAndDoesNotProbeLaterTargets(string scenario)
    {
        using var package = CreateTemporaryPackage("Fixture", "1.2.3");
        var root = Path.GetDirectoryName(package.PackagePath)!;
        using var cancellation = new CancellationTokenSource();
        var count = scenario == "last-success" ? 1 : 3;
        var receipts = Enumerable.Range(1, count).Select(index => new ReleasePublishReceipt(root, "Fixture", "ProjectBuild",
            $"Release {index}", "GitHub", $"https://github.test/release/{index}", null,
            ReleasePublishReceiptStatus.Published, "Published", DateTimeOffset.UtcNow)).ToArray();
        var published = new ReleasePublishExecutionResult(root, true, "Published", "{}", receipts);
        var item = CreateVerifyReadyQueueItem(root, "Fixture", ReleaseRepositoryKind.Library, JsonSerializer.Serialize(published));
        var requests = new List<string>();
        using var client = new HttpClient(new StubHttpMessageHandler(request => {
            requests.Add(request.RequestUri!.AbsolutePath);
            if (scenario is "between" or "last-success") cancellation.Cancel();
            if (scenario == "during" && request.RequestUri.AbsolutePath == "/release/2")
            {
                cancellation.Cancel();
                throw new OperationCanceledException("fixture-secret-must-not-escape", cancellation.Token);
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        using var service = new ReleaseVerificationExecutionService(client,
            new PowerShellRepositoryResolver(new StubPowerShellRunner(_ => throw new InvalidOperationException())));
        if (scenario == "before") cancellation.Cancel();
        var result = await service.ExecuteAsync(item, cancellation.Token);
        Assert.Equal(scenario == "last-success", result.Succeeded);
        Assert.Equal(scenario != "last-success", result.WasCancelled);
        Assert.Equal(scenario == "before" ? 0 : 1, result.Receipts.Count(value => value.Status == ReleaseVerificationReceiptStatus.Verified));
        Assert.Equal(scenario == "last-success" ? 0 : 1, result.Receipts.Count(value => value.Status == ReleaseVerificationReceiptStatus.Failed));
        Assert.DoesNotContain("/release/3", requests);
        if (scenario == "before") Assert.Empty(requests);
        Assert.DoesNotContain("fixture-secret", JsonSerializer.Serialize(result));
        var database = new ReleaseStateDatabase(Path.Combine(root, "state.db"));
        await database.InitializeAsync();
        var session = ReleaseQueueSessionFactory.Create(root, [item], DateTimeOffset.UtcNow);
        var runner = new ReleaseQueueRunner();
        var completed = result.Succeeded ? runner.CompleteVerification(session, root, result).Session
            : runner.FailVerification(session, root, result).Session;
        await database.PersistReleaseCheckpointAsync(completed, verificationReceipts: result.Receipts);
        var recovered = (await database.LoadReleaseCheckpointAsync(session.SessionId))!;
        Assert.Equal(result.Receipts.Count, recovered.VerificationReceipts.Count);
        Assert.All(result.Receipts, receipt => Assert.Contains(receipt, recovered.VerificationReceipts));
        if (!result.Succeeded)
        {
            var retry = runner.RetryFailedItem(recovered.Session);
            Assert.True(retry.Changed);
            Assert.Equal(item.CheckpointStateJson, Assert.Single(retry.Session.Items).CheckpointStateJson);
        }
    }
}
