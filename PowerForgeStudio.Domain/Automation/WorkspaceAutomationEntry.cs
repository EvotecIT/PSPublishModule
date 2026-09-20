namespace PowerForgeStudio.Domain.Automation;

/// <summary>One schedule definition or runtime observation owned by an external provider.</summary>
public sealed record WorkspaceAutomationEntry(
    string Id,
    string Name,
    string Provider,
    string Scope,
    string Project,
    string Schedule,
    string State,
    DateTimeOffset? NextRunAt,
    DateTimeOffset? LastRunAt,
    string LastResult,
    bool HasRuntimeEvidence,
    bool IsEnabled,
    bool IsRelevant,
    string SourcePath,
    string Detail)
{
    public bool NeedsAttention => State is "Failed" or "Inaccessible" or "Expired";
    public string NextRunDisplay => NextRunAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "No verified next run";
    public string LastRunDisplay => LastRunAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "No verified run";
    public string ProjectDisplay => string.IsNullOrWhiteSpace(Project) ? Scope : Project;
}

/// <summary>Availability and evidence boundary for one automation provider.</summary>
public sealed record WorkspaceAutomationSourceState(
    string Provider,
    string State,
    int EntryCount,
    string Message);

/// <summary>Combined read-only automation inventory for a workspace.</summary>
public sealed record WorkspaceAutomationSnapshot(
    DateTimeOffset InspectedAtUtc,
    IReadOnlyList<WorkspaceAutomationEntry> Entries,
    IReadOnlyList<WorkspaceAutomationSourceState> Sources);
