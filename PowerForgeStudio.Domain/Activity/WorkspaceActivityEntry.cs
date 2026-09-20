namespace PowerForgeStudio.Domain.Activity;

/// <summary>One read-only signal in the cross-project attention inbox.</summary>
public sealed record WorkspaceActivityEntry(
    string Id,
    string Project,
    string Kind,
    string Severity,
    string State,
    string Title,
    string Detail,
    string Provider,
    DateTimeOffset ObservedAtUtc,
    string Source,
    string? OpenTarget,
    bool IsActionable)
{
    public bool NeedsAttention => Severity is "Critical" or "Warning";

    public bool IsCritical => Severity == "Critical";

    public bool IsWarning => Severity == "Warning";

    public bool IsReview => Severity == "Review";

    public string ObservedDisplay => ObservedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string SourceDisplay => string.IsNullOrWhiteSpace(Source) ? Provider : Source;
}

/// <summary>Freshness and availability for an owner contributing to Activity.</summary>
public sealed record WorkspaceActivitySourceState(
    string Provider,
    string State,
    int EntryCount,
    DateTimeOffset? ObservedAtUtc,
    string Message)
{
    public string ObservedDisplay => ObservedAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "Not observed";

    public bool IsUnavailable => State is "Unavailable" or "Authentication required" or "Access denied" or "Rate limited";

    public bool IsAvailable => State is "Available" or "Definitions";

    public bool IsPartial => State is "Partial" or "Deferred";
}

/// <summary>Combined read-only attention snapshot. Provider failures remain visible in Sources.</summary>
public sealed record WorkspaceActivitySnapshot(
    DateTimeOffset InspectedAtUtc,
    IReadOnlyList<WorkspaceActivityEntry> Entries,
    IReadOnlyList<WorkspaceActivitySourceState> Sources,
    int RepositoryCount,
    int TotalEntryCount)
{
    public bool IsTruncated => TotalEntryCount > Entries.Count;

    public int OmittedEntryCount => Math.Max(0, TotalEntryCount - Entries.Count);
}

/// <summary>Bounds external probes so opening Activity cannot scan an unbounded workspace remotely.</summary>
public sealed record WorkspaceActivityOptions(
    int MaxGitHubRepositories = 8,
    int MaxIssuesPerRepository = 3,
    int MaxEntries = 120,
    int GitHubTimeoutSeconds = 60);
