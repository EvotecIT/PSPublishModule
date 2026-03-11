using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Workspace;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioWorkspaceSnapshotServiceTests
{
    [Fact]
    public async Task RefreshAsync_BuildsReusableSnapshotForTempWorkspace()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "PowerForgeStudio", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(workspaceRoot, "state", "releaseops.db");

        try
        {
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "LibraryRepo", "Build"));
            await File.WriteAllTextAsync(Path.Combine(workspaceRoot, "LibraryRepo", "Build", "Build-Project.ps1"), "# test");

            Directory.CreateDirectory(Path.Combine(workspaceRoot, "ModuleRepo", "Build"));
            await File.WriteAllTextAsync(Path.Combine(workspaceRoot, "ModuleRepo", "Build", "Build-Module.ps1"), "# test");

            var service = new WorkspaceSnapshotService();
            var snapshot = await service.RefreshAsync(
                workspaceRoot,
                databasePath,
                new WorkspaceRefreshOptions(
                    MaxPlanRepositories: 0,
                    MaxGitHubRepositories: 0,
                    PersistState: true));

            Assert.Equal(workspaceRoot, snapshot.WorkspaceRoot);
            Assert.Equal(databasePath, snapshot.DatabasePath);
            Assert.Equal(2, snapshot.PortfolioItems.Count);
            Assert.Equal(2, snapshot.Summary.TotalRepositories);
            Assert.All(snapshot.PortfolioItems, item => Assert.Equal(RepositoryReadinessKind.Blocked, item.ReadinessKind));
            Assert.NotEmpty(snapshot.ReleaseInboxItems);
            Assert.Equal(5, snapshot.DashboardCards.Count);
            Assert.Equal(2, snapshot.RepositoryFamilies.Count);
            Assert.Equal(2, snapshot.RepositoryFamilyLanes.Count);
            Assert.Contains(snapshot.RepositoryFamilies, family => family.DisplayName == "LibraryRepo");
            Assert.Contains(snapshot.RepositoryFamilies, family => family.DisplayName == "ModuleRepo");
            Assert.Contains(snapshot.RepositoryFamilyLanes, lane => lane.DisplayName == "LibraryRepo");
            Assert.Contains(snapshot.RepositoryFamilyLanes, lane => lane.DisplayName == "ModuleRepo");
            Assert.Equal(2, snapshot.QueueSession.Items.Count);
            Assert.Equal(2, snapshot.QueueSession.Summary.BlockedItems);
            Assert.Equal("No signing batch waiting.", snapshot.SigningStation.Headline);
            Assert.Equal("No publish batch ready.", snapshot.PublishStation.Headline);
            Assert.Equal("No verification batch ready.", snapshot.VerificationStation.Headline);
            Assert.True(File.Exists(databasePath));
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }
}
