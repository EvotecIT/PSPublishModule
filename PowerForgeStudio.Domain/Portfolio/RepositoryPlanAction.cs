namespace PowerForgeStudio.Domain.Portfolio;

/// <summary>A bounded, secret-safe action shown while reviewing a repository build plan.</summary>
public sealed record RepositoryPlanAction(
    int Sequence,
    string Lane,
    string Action,
    string Target,
    string Detail);
