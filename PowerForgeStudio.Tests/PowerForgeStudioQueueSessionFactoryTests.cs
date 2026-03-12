using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioQueueSessionFactoryTests
{
    [Fact]
    public void Create_ComputesSummaryAndPreservesScope()
    {
        var items = new[] {
            CreateItem("a", ReleaseQueueStage.Build, ReleaseQueueItemStatus.ReadyToRun),
            CreateItem("b", ReleaseQueueStage.Sign, ReleaseQueueItemStatus.WaitingApproval)
        };

        var session = ReleaseQueueSessionFactory.Create(
            workspaceRoot: @"C:\Support\GitHub",
            items: items,
            createdAtUtc: DateTimeOffset.UtcNow,
            scopeKey: "family-a",
            scopeDisplayName: "Family A");

        Assert.Equal("family-a", session.ScopeKey);
        Assert.Equal("Family A", session.ScopeDisplayName);
        Assert.Equal(1, session.Summary.BuildReadyItems);
        Assert.Equal(1, session.Summary.WaitingApprovalItems);
    }

    [Fact]
    public void WithItems_RecomputesSummary()
    {
        var session = ReleaseQueueSessionFactory.Create(
            workspaceRoot: @"C:\Support\GitHub",
            items: [CreateItem("a", ReleaseQueueStage.Build, ReleaseQueueItemStatus.ReadyToRun)],
            createdAtUtc: DateTimeOffset.UtcNow);
        var updated = ReleaseQueueSessionFactory.WithItems(
            session,
            [CreateItem("b", ReleaseQueueStage.Verify, ReleaseQueueItemStatus.ReadyToRun)]);

        Assert.Equal(session.SessionId, updated.SessionId);
        Assert.Equal(0, updated.Summary.BuildReadyItems);
        Assert.Equal(1, updated.Summary.VerificationReadyItems);
    }

    private static ReleaseQueueItem CreateItem(string name, ReleaseQueueStage stage, ReleaseQueueItemStatus status)
        => new(
            RootPath: $@"C:\Support\GitHub\{name}",
            RepositoryName: name,
            RepositoryKind: ReleaseRepositoryKind.Mixed,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            QueueOrder: 1,
            Stage: stage,
            Status: status,
            Summary: "test",
            CheckpointKey: "test",
            CheckpointStateJson: null,
            UpdatedAtUtc: DateTimeOffset.UtcNow);
}
