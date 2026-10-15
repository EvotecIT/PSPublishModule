namespace PowerForge;

/// <summary>Operator-authored review instructions for one existing app version.</summary>
public sealed class AppStoreConnectReviewNotesSpec
{
    /// <summary>Configuration schema version.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>Exact existing version receiving the notes.</summary>
    public AppStoreConnectReviewDetailsVersionRef Target { get; set; } = new();
    /// <summary>Complete replacement notes; an explicit empty string clears them. Keep secrets outside checked-in configurations.</summary>
    public string? Notes { get; set; }
}

/// <summary>Hash-bound review of the desired notes and the exact remote notes being replaced.</summary>
public sealed class AppStoreConnectReviewNotesPlan
{
    /// <summary>Target app identifier.</summary>
    public string AppId { get; set; } = string.Empty;
    /// <summary>Exact marketing version.</summary>
    public string VersionString { get; set; } = string.Empty;
    /// <summary>Target platform.</summary>
    public ApplePlatform Platform { get; set; }
    /// <summary>Resolved version identifier.</summary>
    public string VersionId { get; set; } = string.Empty;
    /// <summary>Existing review details identifier.</summary>
    public string ReviewDetailsId { get; set; } = string.Empty;
    /// <summary>SHA-256 of operator-authored notes.</summary>
    public string DesiredSha256 { get; set; } = string.Empty;
    /// <summary>SHA-256 of current remote notes without exposing their content.</summary>
    public string ObservedSha256 { get; set; } = string.Empty;
    /// <summary>Binding of the exact target and observed and desired notes.</summary>
    public string BindingSha256 { get; set; } = string.Empty;
    /// <summary>Whether the remote notes already match.</summary>
    public bool IsConverged { get; set; }
}
