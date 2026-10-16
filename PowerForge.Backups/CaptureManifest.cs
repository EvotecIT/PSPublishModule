namespace PowerForge.Backups;

/// <summary>Non-secret inventory bound to a native recovery set.</summary>
public sealed class CaptureManifest
{
    public int SchemaVersion { get; set; } = 2;
    public string Format { get; set; } = "native";
    public string HostId { get; set; } = "";
    public string Stamp { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public List<CapturedDatabase> Databases { get; set; } = [];
    public List<CapturedFile> Files { get; set; } = [];
}

public sealed class CapturedDatabase
{
    public string Id { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Entry { get; set; } = "";
    public DateTimeOffset CapturedAtUtc { get; set; }
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public bool Verified { get; set; }
    public long TableCount { get; set; }
    public List<long> ValidationCounts { get; set; } = [];
    public SqlBackupIdentity? SqlIdentity { get; set; }
}

public sealed record SqlBackupIdentity(string Database, Guid BackupSetGuid, string MediaName, Guid MediaSetId);
public sealed record CapturedFile(string Entry, long SizeBytes, string Sha256);

public sealed class BackupRunState
{
    public int SchemaVersion { get; set; } = 1;
    public string HostId { get; set; } = "";
    public string Stamp { get; set; } = "";
    public string Status { get; set; } = "";
    public string Stage { get; set; } = "";
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? ErrorType { get; set; }
    public string? CaptureSha256 { get; set; }
    public long TransferBytes { get; set; }
    public CaptureManifest? Manifest { get; set; }
    public DateTimeOffset? NasVerifiedAtUtc { get; set; }
    public string? CleanupErrorType { get; set; }
    public long CleanupLogicalBytes { get; set; }
    public string[] HeldStagingPaths { get; set; } = [];
}
