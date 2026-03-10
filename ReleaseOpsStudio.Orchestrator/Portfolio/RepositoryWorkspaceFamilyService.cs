using System.Text;
using ReleaseOpsStudio.Domain.Catalog;
using ReleaseOpsStudio.Domain.Portfolio;
using ReleaseOpsStudio.Domain.Queue;

namespace ReleaseOpsStudio.Orchestrator.Portfolio;

public sealed class RepositoryWorkspaceFamilyService
{
    public IReadOnlyList<RepositoryPortfolioItem> AnnotateFamilies(IReadOnlyList<RepositoryPortfolioItem> portfolioItems)
    {
        ArgumentNullException.ThrowIfNull(portfolioItems);

        if (portfolioItems.Count == 0)
        {
            return [];
        }

        var primaryRepositoryNames = portfolioItems
            .Where(item => item.WorkspaceKind == ReleaseWorkspaceKind.PrimaryRepository)
            .Select(item => item.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(name => name.Length)
            .ToArray();

        return portfolioItems
            .Select(item => {
                var family = ResolveFamily(item, primaryRepositoryNames);
                return item with {
                    WorkspaceFamilyKey = family.FamilyKey,
                    WorkspaceFamilyName = family.DisplayName
                };
            })
            .ToArray();
    }

    public IReadOnlyList<RepositoryWorkspaceFamilySnapshot> BuildFamilies(
        IReadOnlyList<RepositoryPortfolioItem> portfolioItems,
        ReleaseQueueSession? queueSession)
    {
        ArgumentNullException.ThrowIfNull(portfolioItems);

        var queueLookup = (queueSession?.Items ?? [])
            .ToDictionary(item => item.RootPath, StringComparer.OrdinalIgnoreCase);

        return portfolioItems
            .GroupBy(item => item.FamilyKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => {
                var orderedMembers = group
                    .OrderBy(member => member.WorkspaceKind != ReleaseWorkspaceKind.PrimaryRepository)
                    .ThenBy(member => member.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var primaryMember = orderedMembers.FirstOrDefault(member => member.WorkspaceKind == ReleaseWorkspaceKind.PrimaryRepository)
                    ?? orderedMembers[0];
                var worktreeMembers = orderedMembers.Count(member => member.WorkspaceKind == ReleaseWorkspaceKind.Worktree);
                var attentionMembers = orderedMembers.Count(member => IsAttention(member, queueLookup.GetValueOrDefault(member.RootPath)));
                var readyMembers = orderedMembers.Count(member => IsReady(member, queueLookup.GetValueOrDefault(member.RootPath)));
                var queueActiveMembers = orderedMembers.Count(member => IsQueueActive(queueLookup.GetValueOrDefault(member.RootPath)));

                return new RepositoryWorkspaceFamilySnapshot(
                    FamilyKey: primaryMember.FamilyKey,
                    DisplayName: primaryMember.FamilyDisplayName,
                    PrimaryRootPath: orderedMembers
                        .Where(member => member.WorkspaceKind == ReleaseWorkspaceKind.PrimaryRepository)
                        .Select(member => member.RootPath)
                        .FirstOrDefault(),
                    TotalMembers: orderedMembers.Length,
                    WorktreeMembers: worktreeMembers,
                    AttentionMembers: attentionMembers,
                    ReadyMembers: readyMembers,
                    QueueActiveMembers: queueActiveMembers,
                    MemberSummary: BuildMemberSummary(orderedMembers));
            })
            .OrderByDescending(family => family.AttentionMembers)
            .ThenByDescending(family => family.QueueActiveMembers)
            .ThenByDescending(family => family.WorktreeMembers)
            .ThenByDescending(family => family.TotalMembers)
            .ThenBy(family => family.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public RepositoryWorkspaceFamilyLaneSnapshot? BuildFamilyLane(
        IReadOnlyList<RepositoryPortfolioItem> portfolioItems,
        ReleaseQueueSession? queueSession,
        string? familyKey)
    {
        ArgumentNullException.ThrowIfNull(portfolioItems);

        if (string.IsNullOrWhiteSpace(familyKey))
        {
            return null;
        }

        var members = portfolioItems
            .Where(item => string.Equals(item.FamilyKey, familyKey, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.WorkspaceKind != ReleaseWorkspaceKind.PrimaryRepository)
            .ThenBy(item => item.WorkspaceKind)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (members.Length == 0)
        {
            return null;
        }

        var queueLookup = (queueSession?.Items ?? [])
            .ToDictionary(item => item.RootPath, StringComparer.OrdinalIgnoreCase);
        var laneItems = members
            .Select(item => BuildLaneItem(item, queueLookup.GetValueOrDefault(item.RootPath)))
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.WorkspaceKind != ReleaseWorkspaceKind.PrimaryRepository)
            .ThenBy(item => item.WorkspaceKind)
            .ThenBy(item => item.RepositoryName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var displayName = members[0].FamilyDisplayName;
        var readyCount = laneItems.Count(item => item.LaneKey is "ready" or "build-ready");
        var usbWaitingCount = laneItems.Count(item => item.LaneKey == "usb-waiting");
        var publishReadyCount = laneItems.Count(item => item.LaneKey == "publish-ready");
        var verifyReadyCount = laneItems.Count(item => item.LaneKey == "verify-ready");
        var failedCount = laneItems.Count(item => item.LaneKey == "failed");
        var completedCount = laneItems.Count(item => item.LaneKey == "completed");

        return new RepositoryWorkspaceFamilyLaneSnapshot(
            FamilyKey: members[0].FamilyKey,
            DisplayName: displayName,
            Headline: $"{displayName}: {laneItems.Length} member(s) mapped into the release lane board",
            Details: $"{readyCount} ready, {usbWaitingCount} waiting on USB, {publishReadyCount} ready to publish, {verifyReadyCount} ready to verify, {failedCount} blocked or failed, {completedCount} completed.",
            ReadyCount: readyCount,
            UsbWaitingCount: usbWaitingCount,
            PublishReadyCount: publishReadyCount,
            VerifyReadyCount: verifyReadyCount,
            FailedCount: failedCount,
            CompletedCount: completedCount,
            Members: laneItems);
    }

    private static RepositoryWorkspaceFamilyIdentity ResolveFamily(RepositoryPortfolioItem item, IReadOnlyList<string> primaryRepositoryNames)
    {
        if (item.WorkspaceKind == ReleaseWorkspaceKind.PrimaryRepository)
        {
            return new RepositoryWorkspaceFamilyIdentity(NormalizeFamilyKey(item.Name), item.Name);
        }

        var leafName = Path.GetFileName(item.RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var matchedPrimaryName = primaryRepositoryNames.FirstOrDefault(primaryName => MatchesFamilyPrefix(leafName, primaryName));
        if (!string.IsNullOrWhiteSpace(matchedPrimaryName))
        {
            return new RepositoryWorkspaceFamilyIdentity(NormalizeFamilyKey(matchedPrimaryName), matchedPrimaryName);
        }

        var simplifiedName = SimplifyCloneName(item.Name);
        return new RepositoryWorkspaceFamilyIdentity(NormalizeFamilyKey(simplifiedName), simplifiedName);
    }

    private static bool MatchesFamilyPrefix(string candidateName, string primaryName)
    {
        if (string.Equals(candidateName, primaryName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return candidateName.StartsWith($"{primaryName}-", StringComparison.OrdinalIgnoreCase)
               || candidateName.StartsWith($"{primaryName}_", StringComparison.OrdinalIgnoreCase)
               || candidateName.StartsWith($"{primaryName}.", StringComparison.OrdinalIgnoreCase);
    }

    private static string SimplifyCloneName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var markers = new[] {
            "-review",
            "_review",
            ".review",
            "-pr-",
            "_pr_",
            ".pr.",
            "-pr",
            "_pr",
            ".pr",
            "-tmp",
            "_tmp",
            ".tmp",
            "-test",
            "_test",
            ".test",
            "-backup",
            "_backup",
            ".backup"
        };

        foreach (var marker in markers)
        {
            var index = name.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                return name[..index];
            }
        }

        return name;
    }

    private static string NormalizeFamilyKey(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.Length == 0
            ? name.ToLowerInvariant()
            : builder.ToString();
    }

    private static string BuildMemberSummary(IReadOnlyList<RepositoryPortfolioItem> members)
    {
        var primaryCount = members.Count(member => member.WorkspaceKind == ReleaseWorkspaceKind.PrimaryRepository);
        var worktreeCount = members.Count(member => member.WorkspaceKind == ReleaseWorkspaceKind.Worktree);
        var reviewCount = members.Count(member => member.WorkspaceKind == ReleaseWorkspaceKind.ReviewClone);
        var temporaryCount = members.Count(member => member.WorkspaceKind == ReleaseWorkspaceKind.TemporaryClone);

        return $"{primaryCount} primary | {worktreeCount} worktree | {reviewCount} review | {temporaryCount} temp";
    }

    private static bool IsAttention(RepositoryPortfolioItem item, ReleaseQueueItem? queueItem)
    {
        if (item.ReadinessKind is RepositoryReadinessKind.Attention or RepositoryReadinessKind.Blocked)
        {
            return true;
        }

        if (item.GitHubInbox?.Status == RepositoryGitHubInboxStatus.Attention)
        {
            return true;
        }

        if (item.ReleaseDrift?.Status == RepositoryReleaseDriftStatus.Attention)
        {
            return true;
        }

        return queueItem?.Status is ReleaseQueueItemStatus.WaitingApproval
            or ReleaseQueueItemStatus.Failed
            or ReleaseQueueItemStatus.Blocked;
    }

    private static bool IsReady(RepositoryPortfolioItem item, ReleaseQueueItem? queueItem)
    {
        var planResults = item.PlanResults ?? [];
        return item.ReadinessKind == RepositoryReadinessKind.Ready
            && planResults.Count > 0
            && planResults.All(result => result.Status == RepositoryPlanStatus.Succeeded)
            && queueItem?.Status is not ReleaseQueueItemStatus.Blocked
            && queueItem?.Status is not ReleaseQueueItemStatus.Failed;
    }

    private static bool IsQueueActive(ReleaseQueueItem? queueItem)
    {
        if (queueItem is null)
        {
            return false;
        }

        if (queueItem.Stage != ReleaseQueueStage.Prepare)
        {
            return true;
        }

        return queueItem.Status is ReleaseQueueItemStatus.ReadyToRun
            or ReleaseQueueItemStatus.WaitingApproval
            or ReleaseQueueItemStatus.Failed;
    }

    private static RepositoryWorkspaceFamilyLaneItem BuildLaneItem(RepositoryPortfolioItem item, ReleaseQueueItem? queueItem)
    {
        var readinessDisplay = $"{item.ReadinessKind}: {item.ReadinessReason}";

        if (queueItem?.Status == ReleaseQueueItemStatus.Failed)
        {
            return new RepositoryWorkspaceFamilyLaneItem(item.RootPath, item.Name, item.WorkspaceKind, "failed", "Failed", queueItem.Summary, readinessDisplay, 0);
        }

        if (queueItem is { Stage: ReleaseQueueStage.Sign, Status: ReleaseQueueItemStatus.WaitingApproval })
        {
            return new RepositoryWorkspaceFamilyLaneItem(item.RootPath, item.Name, item.WorkspaceKind, "usb-waiting", "USB Waiting", queueItem.Summary, readinessDisplay, 1);
        }

        if (queueItem is { Stage: ReleaseQueueStage.Publish, Status: ReleaseQueueItemStatus.ReadyToRun })
        {
            return new RepositoryWorkspaceFamilyLaneItem(item.RootPath, item.Name, item.WorkspaceKind, "publish-ready", "Publish Ready", queueItem.Summary, readinessDisplay, 2);
        }

        if (queueItem is { Stage: ReleaseQueueStage.Verify, Status: ReleaseQueueItemStatus.ReadyToRun })
        {
            return new RepositoryWorkspaceFamilyLaneItem(item.RootPath, item.Name, item.WorkspaceKind, "verify-ready", "Verify Ready", queueItem.Summary, readinessDisplay, 3);
        }

        if (queueItem is { Stage: ReleaseQueueStage.Verify, Status: ReleaseQueueItemStatus.Succeeded })
        {
            return new RepositoryWorkspaceFamilyLaneItem(item.RootPath, item.Name, item.WorkspaceKind, "completed", "Completed", queueItem.Summary, readinessDisplay, 5);
        }

        if (queueItem is { Stage: ReleaseQueueStage.Build, Status: ReleaseQueueItemStatus.ReadyToRun })
        {
            return new RepositoryWorkspaceFamilyLaneItem(item.RootPath, item.Name, item.WorkspaceKind, "build-ready", "Build Ready", queueItem.Summary, readinessDisplay, 2);
        }

        if (queueItem is { Stage: ReleaseQueueStage.Prepare, Status: ReleaseQueueItemStatus.Pending })
        {
            return new RepositoryWorkspaceFamilyLaneItem(item.RootPath, item.Name, item.WorkspaceKind, "prepare", "Prepare", queueItem.Summary, readinessDisplay, 4);
        }

        if (queueItem is { Stage: ReleaseQueueStage.Prepare, Status: ReleaseQueueItemStatus.Blocked } || item.ReadinessKind == RepositoryReadinessKind.Blocked)
        {
            return new RepositoryWorkspaceFamilyLaneItem(item.RootPath, item.Name, item.WorkspaceKind, "failed", "Blocked", queueItem?.Summary ?? item.ReadinessReason, readinessDisplay, 0);
        }

        if (item.ReadinessKind == RepositoryReadinessKind.Attention)
        {
            return new RepositoryWorkspaceFamilyLaneItem(item.RootPath, item.Name, item.WorkspaceKind, "attention", "Attention", item.PlanSummary, readinessDisplay, 4);
        }

        var planResults = item.PlanResults ?? [];
        if (item.ReadinessKind == RepositoryReadinessKind.Ready
            && planResults.Count > 0
            && planResults.All(result => result.Status == RepositoryPlanStatus.Succeeded))
        {
            return new RepositoryWorkspaceFamilyLaneItem(item.RootPath, item.Name, item.WorkspaceKind, "ready", "Ready", item.PlanSummary, readinessDisplay, 2);
        }

        return new RepositoryWorkspaceFamilyLaneItem(item.RootPath, item.Name, item.WorkspaceKind, "prepare", "Plan Needed", item.PlanSummary, readinessDisplay, 4);
    }

    private readonly record struct RepositoryWorkspaceFamilyIdentity(string FamilyKey, string DisplayName);
}
