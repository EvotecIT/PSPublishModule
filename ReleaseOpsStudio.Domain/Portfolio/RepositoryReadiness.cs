namespace ReleaseOpsStudio.Domain.Portfolio;

public sealed record RepositoryReadiness(
    RepositoryReadinessKind Kind,
    string Reason);
