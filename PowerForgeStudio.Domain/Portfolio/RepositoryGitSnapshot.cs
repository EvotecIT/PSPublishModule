namespace PowerForgeStudio.Domain.Portfolio;

public sealed record RepositoryGitSnapshot(
    bool IsGitRepository,
    string? BranchName,
    string? UpstreamBranch,
    int AheadCount,
    int BehindCount,
    int TrackedChangeCount,
    int UntrackedChangeCount,
    IReadOnlyList<RepositoryGitDiagnostic>? Diagnostics = null)
{
    public IReadOnlyList<RepositoryGitDiagnostic> GitDiagnostics => Diagnostics ?? [];

    public bool IsDirty => TrackedChangeCount > 0 || UntrackedChangeCount > 0;

    public string BranchDisplay => string.IsNullOrWhiteSpace(BranchName) ? "-" : BranchName!;

    public string AheadBehindDisplay => $"+{AheadCount} / -{BehindCount}";

    public RepositoryGitDiagnostic? PrimaryDiagnostic => GitDiagnostics
        .OrderByDescending(diagnostic => diagnostic.Severity)
        .FirstOrDefault();

    public RepositoryGitDiagnostic? PrimaryActionableDiagnostic => GitDiagnostics
        .Where(diagnostic => diagnostic.Severity != RepositoryGitDiagnosticSeverity.Info)
        .OrderByDescending(diagnostic => diagnostic.Severity)
        .FirstOrDefault();

    public string DiagnosticStatus => PrimaryDiagnostic?.SeverityDisplay ?? "Clear";

    public string DiagnosticSummary
    {
        get
        {
            if (GitDiagnostics.Count == 0)
            {
                return "Git preflight looks healthy.";
            }

            return string.Join(" | ", GitDiagnostics.Select(diagnostic => diagnostic.Summary));
        }
    }

    public string DiagnosticDetail
    {
        get
        {
            if (GitDiagnostics.Count == 0)
            {
                return "Git preflight did not detect blockers or warnings for this repository.";
            }

            return string.Join(
                $"{Environment.NewLine}{Environment.NewLine}",
                GitDiagnostics.Select(diagnostic => $"{diagnostic.Title}: {diagnostic.Summary}{Environment.NewLine}{diagnostic.Detail}"));
        }
    }
}

