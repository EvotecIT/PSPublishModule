namespace PowerForge;

/// <summary>Identifies the local boundary that rejected release evidence admission.</summary>
internal enum AppleReleaseLocalEvidenceRejectionPhase
{
    ConfigurationValidation,
    SourceValidation,
    ReceiptValidation,
    JournalLockValidation,
    ArchiveValidation
}

/// <summary>
/// Exact local files validated for working-tree evidence admission. This is neither CI
/// qualification nor proof of Apple delivery and cannot authorize a release operation.
/// </summary>
internal sealed class AppleReleaseLocalEvidence
{
    public bool Success { get; set; }
    public string SourceCommit { get; set; } = string.Empty;
    public string[] EvidencePaths { get; set; } = Array.Empty<string>();
    public AppleArtifactSymbolicLink[] ArchiveLinks { get; set; } = Array.Empty<AppleArtifactSymbolicLink>();
    public AppleReleaseLocalEvidenceRejectionPhase? RejectionPhase { get; set; }
    public string? ErrorMessage { get; set; }
}
