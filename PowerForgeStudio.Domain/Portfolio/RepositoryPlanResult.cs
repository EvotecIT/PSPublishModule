namespace PowerForgeStudio.Domain.Portfolio;

public sealed record RepositoryPlanResult(
    RepositoryPlanAdapterKind AdapterKind,
    RepositoryPlanStatus Status,
    string Summary,
    string? PlanPath,
    int ExitCode,
    double DurationSeconds,
    string? OutputTail = null,
    string? ErrorTail = null)
{
    /// <summary>Reviewed semantic actions. Command arguments, inline scripts, and secret values are excluded.</summary>
    public IReadOnlyList<RepositoryPlanAction> Actions { get; init; } = [];

    public bool IsSucceeded => Status == RepositoryPlanStatus.Succeeded;

    public bool IsFailed => Status == RepositoryPlanStatus.Failed;

    public bool IsNeutral => !IsSucceeded && !IsFailed;
}

