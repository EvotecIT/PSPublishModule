namespace PowerForgeStudio.Orchestrator.Portfolio;

public sealed record GitHubInboxOptions
{
    public int MaxRepositories { get; init; } = -1;
}
