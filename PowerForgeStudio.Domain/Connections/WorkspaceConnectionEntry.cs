namespace PowerForgeStudio.Domain.Connections;

/// <summary>Secret-free connection evidence owned by an external service or local tool.</summary>
public sealed record WorkspaceConnectionEntry(
    string Id,
    string Name,
    string Provider,
    string Category,
    string State,
    string Endpoint,
    string CredentialReference,
    IReadOnlyList<string> Capabilities,
    DateTimeOffset? VerifiedAtUtc,
    string Evidence,
    string Owner)
{
    public bool IsVerified => State is "Verified" or "Reachable";
    public bool NeedsAttention => State is "Unavailable" or "Authentication required" or "Expired" or "Failed";
    public string CapabilityDisplay => Capabilities.Count == 0 ? "No capabilities reported" : string.Join(" · ", Capabilities);
    public string VerifiedDisplay => VerifiedAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "Not verified";
}

/// <summary>Availability boundary for one connection provider.</summary>
public sealed record WorkspaceConnectionSourceState(
    string Provider,
    string State,
    int EntryCount,
    string Message);

/// <summary>Combined read-only connection inventory for a Studio workspace.</summary>
public sealed record WorkspaceConnectionSnapshot(
    DateTimeOffset InspectedAtUtc,
    IReadOnlyList<WorkspaceConnectionEntry> Entries,
    IReadOnlyList<WorkspaceConnectionSourceState> Sources);
