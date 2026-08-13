namespace PowerForge;

internal enum VirusTotalArtifactKind
{
    PowerShellModule,
    NuGetPackage,
    ZipArchive,
    MsiPackage,
    MsixPackage,
    Executable
}

internal sealed class PowerForgeVirusTotalOptions
{
    public bool Enabled { get; set; }

    public string? ProjectName { get; set; }

    public string? ApiKey { get; set; }

    public string? ApiKeyFilePath { get; set; }

    public string? ApiKeyEnvName { get; set; }

    public VirusTotalArtifactKind[] ArtifactKinds { get; set; } = Array.Empty<VirusTotalArtifactKind>();

    public string DestinationPathTemplate { get; set; } = "/{Project}/{Version}/{Kind}/{RelativePath}";

    public string? DetailsTemplate { get; set; } = "PowerForge release artifact {Project} {Version} ({Kind})";

    public bool VerifySha256 { get; set; } = true;

    public int VerificationTimeoutSeconds { get; set; } = 120;

    public int PollingIntervalSeconds { get; set; } = 2;

    public int RequestTimeoutSeconds { get; set; } = 600;

    public bool RequireMatchingArtifacts { get; set; } = true;

    public string? ReceiptPath { get; set; } = "Artifacts/Release/virustotal-monitor-receipt.json";
}

internal sealed class VirusTotalMonitorArtifact
{
    public string SourcePath { get; set; } = string.Empty;

    public VirusTotalArtifactKind Kind { get; set; }

    public string DestinationPath { get; set; } = string.Empty;

    public string? Details { get; set; }

    public string? ExistingItemId { get; set; }
}

internal sealed class VirusTotalMonitorPublishRequest
{
    public string ApiKey { get; set; } = string.Empty;

    public VirusTotalMonitorArtifact[] Artifacts { get; set; } = Array.Empty<VirusTotalMonitorArtifact>();

    public VirusTotalMonitorArtifactReceipt[] ResumeReceipts { get; set; } = Array.Empty<VirusTotalMonitorArtifactReceipt>();

    public bool VerifySha256 { get; set; } = true;

    public TimeSpan VerificationTimeout { get; set; } = TimeSpan.FromMinutes(2);

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(10);

    public Func<VirusTotalMonitorPublishResult, CancellationToken, Task>? CheckpointAsync { get; set; }
}

internal enum VirusTotalMonitorVerificationStatus
{
    NotRequested,
    Pending,
    Verified
}

internal sealed class VirusTotalMonitorUploadResponse
{
    public string MonitorId { get; set; } = string.Empty;

    public string RemotePath { get; set; } = string.Empty;

    public string LocalSha256 { get; set; } = string.Empty;

    public string? RemoteSha256 { get; set; }

    public VirusTotalMonitorVerificationStatus VerificationStatus { get; set; }

    public bool UsedLargeFileUploadUrl { get; set; }

    public int? CurrentDetectionCount { get; set; }
}

internal sealed class VirusTotalMonitorArtifactReceipt
{
    public string SourcePath { get; set; } = string.Empty;

    public VirusTotalArtifactKind Kind { get; set; }

    public string DestinationPath { get; set; } = string.Empty;

    public string MonitorId { get; set; } = string.Empty;

    public string LocalSha256 { get; set; } = string.Empty;

    public string? RemoteSha256 { get; set; }

    public VirusTotalMonitorVerificationStatus VerificationStatus { get; set; }

    public bool UsedLargeFileUploadUrl { get; set; }

    public int? CurrentDetectionCount { get; set; }

    public DateTimeOffset UploadedAtUtc { get; set; }
}

internal sealed class VirusTotalMonitorPublishResult
{
    public bool Success { get; set; }

    public string? ErrorMessage { get; set; }

    public VirusTotalMonitorArtifactReceipt[] Artifacts { get; set; } = Array.Empty<VirusTotalMonitorArtifactReceipt>();
}

internal sealed class VirusTotalMonitorReceiptDocument
{
    public int SchemaVersion { get; set; } = 1;

    public string Provider { get; set; } = "VirusTotal Monitor";

    public string Project { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public bool AsynchronousAnalysis { get; set; } = true;

    public bool HashVerificationRequested { get; set; }

    public bool Success { get; set; }

    public string? ErrorMessage { get; set; }

    public VirusTotalMonitorArtifactReceipt[] Artifacts { get; set; } = Array.Empty<VirusTotalMonitorArtifactReceipt>();
}
