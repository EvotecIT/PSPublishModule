using PowerForgeStudio.Domain.Catalog;

namespace PowerForgeStudio.Domain.Portfolio;

public sealed record RepositoryPortfolioItem(
    RepositoryCatalogEntry Repository,
    RepositoryGitSnapshot Git,
    RepositoryReadiness Readiness,
    IReadOnlyList<RepositoryPlanResult>? PlanResults = null,
    RepositoryGitHubInbox? GitHubInbox = null,
    RepositoryReleaseDrift? ReleaseDrift = null,
    string? WorkspaceFamilyKey = null,
    string? WorkspaceFamilyName = null)
{
    public string Name => Repository.Name;

    public string RootPath => Repository.RootPath;

    public string FamilyKey => string.IsNullOrWhiteSpace(WorkspaceFamilyKey)
        ? Name
        : WorkspaceFamilyKey!;

    public string FamilyDisplayName => string.IsNullOrWhiteSpace(WorkspaceFamilyName)
        ? Name
        : WorkspaceFamilyName!;

    public ReleaseRepositoryKind RepositoryKind => Repository.RepositoryKind;

    public ReleaseWorkspaceKind WorkspaceKind => Repository.WorkspaceKind;

    public string PrimaryBuildScriptPath => Repository.PrimaryBuildScriptPath ?? string.Empty;

    public string BranchName => Git.BranchDisplay;

    public string UpstreamBranch => string.IsNullOrWhiteSpace(Git.UpstreamBranch) ? "-" : Git.UpstreamBranch!;

    public string AheadBehindDisplay => Git.AheadBehindDisplay;

    public string DirtyStatus => Git.IsDirty
        ? $"{Git.TrackedChangeCount} tracked / {Git.UntrackedChangeCount} untracked"
        : "Clean";

    public string GitGuardStatus => Git.DiagnosticStatus;

    public string GitGuardSummary => Git.DiagnosticSummary;

    public RepositoryReadinessKind ReadinessKind => Readiness.Kind;

    public string ReadinessReason => Readiness.Reason;

    public string PlanStatus
    {
        get
        {
            var results = PlanResults ?? [];
            if (results.Count == 0)
            {
                return "Not run";
            }

            if (results.Any(result => result.Status == RepositoryPlanStatus.Failed))
            {
                return "Failed";
            }

            if (results.All(result => result.Status == RepositoryPlanStatus.Succeeded))
            {
                return "Succeeded";
            }

            return "Partial";
        }
    }

    public string PlanSummary
    {
        get
        {
            var results = PlanResults ?? [];
            if (results.Count == 0)
            {
                return "Plan preview not run yet.";
            }

            return string.Join(" | ", results.Select(result => $"{result.AdapterKind}: {FormatPlanDetail(result)}"));
        }
    }

    public string GitHubStatus => GitHubInbox?.StatusDisplay ?? "Not probed";

    public string GitHubPullRequests => GitHubInbox?.PullRequestDisplay ?? "-";

    public string GitHubSummary => GitHubInbox?.Summary ?? "GitHub inbox not probed.";

    public string ReleaseDriftStatus => ReleaseDrift?.StatusDisplay ?? "Unknown";

    public string ReleaseDriftSummary => ReleaseDrift?.Summary ?? "Release drift not assessed.";

    private static string FormatPlanDetail(RepositoryPlanResult result)
    {
        if (result.Status == RepositoryPlanStatus.Failed)
        {
            var detail = result.ErrorTail ?? result.OutputTail ?? result.Summary;
            var firstLine = detail.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? result.Summary;
            return $"Failed ({result.DurationSeconds:0.##}s) - {firstLine}";
        }

        return $"{result.Status} ({result.DurationSeconds:0.##}s)";
    }
}

