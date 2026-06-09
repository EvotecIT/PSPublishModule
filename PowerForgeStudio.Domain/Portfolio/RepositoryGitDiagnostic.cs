namespace PowerForgeStudio.Domain.Portfolio;

public sealed record RepositoryGitDiagnostic(
    RepositoryGitDiagnosticCode Code,
    RepositoryGitDiagnosticSeverity Severity,
    string Title,
    string Summary,
    string Detail)
{
    public string SeverityDisplay => Severity switch
    {
        RepositoryGitDiagnosticSeverity.Blocked => "Blocked",
        RepositoryGitDiagnosticSeverity.Attention => "Attention",
        _ => "Info"
    };
}
