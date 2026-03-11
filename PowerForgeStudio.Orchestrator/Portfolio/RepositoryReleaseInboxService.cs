using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.Queue;

namespace PowerForgeStudio.Orchestrator.Portfolio;

public sealed class RepositoryReleaseInboxService
{
    public IReadOnlyList<RepositoryReleaseInboxItem> BuildInbox(
        IReadOnlyList<RepositoryPortfolioItem> portfolioItems,
        ReleaseQueueSession? queueSession,
        IReadOnlyDictionary<string, RepositoryGitQuickActionReceipt>? gitQuickActionReceipts = null,
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
            if (gitQuickActionReceipts?.TryGetValue(repository.RootPath, out var gitQuickActionReceipt) == true
                && !gitQuickActionReceipt.Succeeded)
            {
                candidates.Add(new RepositoryReleaseInboxItem(
                    RootPath: repository.RootPath,
                    RepositoryName: repository.Name,
                    Title: repository.Name,
                    Detail: $"{gitQuickActionReceipt.Summary} Last action: {gitQuickActionReceipt.ActionTitle}.",
                    Badge: "Git Action Failed",
                    FocusMode: RepositoryPortfolioFocusMode.Attention,
                    SearchText: string.Empty,
                    PresetKey: "attention",
                    Priority: 4));
            }
            else if (repository.Git.PrimaryActionableDiagnostic is { } gitDiagnostic)
            {
                var detail = gitDiagnostic.Code == RepositoryGitDiagnosticCode.ProtectedBaseBranchFlow
                             && repository.GitHubInbox?.BranchProtectionEnabled == true
                    ? $"{gitDiagnostic.Summary} {repository.GitHubInbox.GovernanceSummary}"
                    : gitDiagnostic.Summary;
                candidates.Add(new RepositoryReleaseInboxItem(
                    RootPath: repository.RootPath,
                    RepositoryName: repository.Name,
                    Title: repository.Name,
                    Detail: detail,
                    Badge: GetGitBadge(gitDiagnostic, repository.GitHubInbox),
                    FocusMode: RepositoryPortfolioFocusMode.Attention,
                    SearchText: string.Empty,
                    PresetKey: "attention",
                    Priority: 5));
            }
            else if (repository.GitHubInbox?.Status == RepositoryGitHubInboxStatus.Attention)
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
                    Priority: 6));
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
                    Priority: 7));
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
                    Priority: 8));
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

    private static string GetGitBadge(RepositoryGitDiagnostic diagnostic, RepositoryGitHubInbox? gitHubInbox)
        => diagnostic.Code == RepositoryGitDiagnosticCode.ProtectedBaseBranchFlow && gitHubInbox?.BranchProtectionEnabled == true
            ? "Protected Branch"
            : diagnostic.Code == RepositoryGitDiagnosticCode.ProtectedBaseBranchFlow
                ? "PR Flow"
            : "Git Guard";
}
