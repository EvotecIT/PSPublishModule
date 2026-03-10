using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.Queue;

namespace PowerForgeStudio.Orchestrator.Portfolio;

public sealed class RepositoryReleaseInboxService
{
    public IReadOnlyList<RepositoryReleaseInboxItem> BuildInbox(
        IReadOnlyList<RepositoryPortfolioItem> portfolioItems,
        ReleaseQueueSession? queueSession,
        int maxItems = 10)
    {
        ArgumentNullException.ThrowIfNull(portfolioItems);

        var candidates = new List<RepositoryReleaseInboxItem>();
        var queueLookup = (queueSession?.Items ?? [])
            .ToDictionary(item => item.RootPath, StringComparer.OrdinalIgnoreCase);

        foreach (var item in queueLookup.Values.OrderBy(queueItem => queueItem.QueueOrder))
        {
            if (item.Status == ReleaseQueueItemStatus.Failed)
            {
                candidates.Add(new RepositoryReleaseInboxItem(
                    RootPath: item.RootPath,
                    RepositoryName: item.RepositoryName,
                    Title: item.RepositoryName,
                    Detail: item.Summary,
                    Badge: "Failed",
                    FocusMode: RepositoryPortfolioFocusMode.Failed,
                    SearchText: string.Empty,
                    PresetKey: null,
                    Priority: 0));
            }
            else if (item.Stage == ReleaseQueueStage.Sign && item.Status == ReleaseQueueItemStatus.WaitingApproval)
            {
                candidates.Add(new RepositoryReleaseInboxItem(
                    RootPath: item.RootPath,
                    RepositoryName: item.RepositoryName,
                    Title: item.RepositoryName,
                    Detail: item.Summary,
                    Badge: "USB Waiting",
                    FocusMode: RepositoryPortfolioFocusMode.WaitingUsb,
                    SearchText: string.Empty,
                    PresetKey: "usb-waiting",
                    Priority: 1));
            }
            else if (item.Stage == ReleaseQueueStage.Publish && item.Status == ReleaseQueueItemStatus.ReadyToRun)
            {
                candidates.Add(new RepositoryReleaseInboxItem(
                    RootPath: item.RootPath,
                    RepositoryName: item.RepositoryName,
                    Title: item.RepositoryName,
                    Detail: item.Summary,
                    Badge: "Publish Ready",
                    FocusMode: RepositoryPortfolioFocusMode.PublishReady,
                    SearchText: string.Empty,
                    PresetKey: null,
                    Priority: 2));
            }
            else if (item.Stage == ReleaseQueueStage.Verify && item.Status == ReleaseQueueItemStatus.ReadyToRun)
            {
                candidates.Add(new RepositoryReleaseInboxItem(
                    RootPath: item.RootPath,
                    RepositoryName: item.RepositoryName,
                    Title: item.RepositoryName,
                    Detail: item.Summary,
                    Badge: "Verify Ready",
                    FocusMode: RepositoryPortfolioFocusMode.VerifyReady,
                    SearchText: string.Empty,
                    PresetKey: null,
                    Priority: 3));
            }
        }

        foreach (var repository in portfolioItems.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (repository.GitHubInbox?.Status == RepositoryGitHubInboxStatus.Attention)
            {
                candidates.Add(new RepositoryReleaseInboxItem(
                    RootPath: repository.RootPath,
                    RepositoryName: repository.Name,
                    Title: repository.Name,
                    Detail: repository.GitHubInbox.Summary,
                    Badge: "GitHub",
                    FocusMode: RepositoryPortfolioFocusMode.Attention,
                    SearchText: string.Empty,
                    PresetKey: "attention",
                    Priority: 4));
            }
            else if (repository.ReleaseDrift?.Status == RepositoryReleaseDriftStatus.Attention)
            {
                candidates.Add(new RepositoryReleaseInboxItem(
                    RootPath: repository.RootPath,
                    RepositoryName: repository.Name,
                    Title: repository.Name,
                    Detail: repository.ReleaseDrift.Summary,
                    Badge: "Release Drift",
                    FocusMode: RepositoryPortfolioFocusMode.Attention,
                    SearchText: string.Empty,
                    PresetKey: "attention",
                    Priority: 5));
            }
            else if (repository.ReadinessKind == RepositoryReadinessKind.Ready
                     && (repository.PlanResults?.Count ?? 0) > 0
                     && repository.PlanResults!.All(result => result.Status == RepositoryPlanStatus.Succeeded))
            {
                candidates.Add(new RepositoryReleaseInboxItem(
                    RootPath: repository.RootPath,
                    RepositoryName: repository.Name,
                    Title: repository.Name,
                    Detail: "Ready today: plan preview succeeded and local readiness is green.",
                    Badge: "Ready Today",
                    FocusMode: RepositoryPortfolioFocusMode.Ready,
                    SearchText: string.Empty,
                    PresetKey: "ready-today",
                    Priority: 6));
            }
        }

        return candidates
            .GroupBy(candidate => candidate.RootPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(candidate => candidate.Priority)
                .ThenBy(candidate => candidate.RepositoryName, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.RepositoryName, StringComparer.OrdinalIgnoreCase)
            .Take(maxItems)
            .ToArray();
    }
}
