namespace ReleaseOpsStudio.Domain.Portfolio;

public sealed record RepositoryPlanResult(
    RepositoryPlanAdapterKind AdapterKind,
    RepositoryPlanStatus Status,
    string Summary,
    string? PlanPath,
    int ExitCode,
    double DurationSeconds,
    string? OutputTail = null,
    string? ErrorTail = null);
