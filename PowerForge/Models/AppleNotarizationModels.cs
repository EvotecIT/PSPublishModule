namespace PowerForge;

/// <summary>Request to submit a directly distributed macOS artifact to Apple's notary service.</summary>
public sealed class AppleNotarizationRequest
{
    /// <summary>.app, .dmg, or .pkg artifact to notarize.</summary>
    public string ArtifactPath { get; set; } = string.Empty;

    /// <summary>Optional retained copy path for the exact private zip submitted when ArtifactPath is an .app bundle.</summary>
    public string? SubmissionPath { get; set; }

    /// <summary>xcrun executable.</summary>
    public string XcrunExecutable { get; set; } = "xcrun";

    /// <summary>ditto executable.</summary>
    public string DittoExecutable { get; set; } = "ditto";

    /// <summary>spctl executable.</summary>
    public string SpctlExecutable { get; set; } = "spctl";

    /// <summary>Require fixed system notarization, packaging, and Gatekeeper executables under a sanitized PATH.</summary>
    public bool RequireTrustedSystemTools { get; set; }

    /// <summary>Optional notarytool keychain profile.</summary>
    public string? KeychainProfile { get; set; }

    /// <summary>App Store Connect API private key path when no keychain profile is used.</summary>
    public string? ApiKeyPath { get; set; }

    /// <summary>App Store Connect API key id when no keychain profile is used.</summary>
    public string? ApiKeyId { get; set; }

    /// <summary>App Store Connect API issuer id when no keychain profile is used.</summary>
    public string? ApiIssuerId { get; set; }

    /// <summary>
    /// Previously accepted notary submission id. When supplied, submission is not repeated and
    /// only the requested staple, validation, and Gatekeeper checks run against the original artifact.
    /// </summary>
    public string? AcceptedSubmissionId { get; set; }

    /// <summary>Expected SHA-256 of the retained artifact bytes when resuming an accepted submission.</summary>
    public string? ExpectedArtifactSha256 { get; set; }

    /// <summary>SHA-256 of the exact file previously accepted by Apple's notary service.</summary>
    public string? AcceptedSubmissionSha256 { get; set; }

    /// <summary>Whether stapling already succeeded and must not mutate the artifact again during resume.</summary>
    public bool StaplingCompleted { get; set; }

    /// <summary>Maximum runtime for each external command.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Staple and validate the accepted ticket.</summary>
    public bool Staple { get; set; } = true;

    /// <summary>Run Gatekeeper assessment.</summary>
    public bool Assess { get; set; } = true;

    internal Action<AppleNotarizationAcceptedCheckpoint>? AcceptedCheckpoint { get; set; }

    internal Action<AppleNotarizationStapledCheckpoint>? StapledCheckpoint { get; set; }
}

internal sealed class AppleNotarizationAcceptedCheckpoint
{
    internal string ArtifactPath { get; set; } = string.Empty;

    internal string ArtifactSha256 { get; set; } = string.Empty;

    internal string SubmissionPath { get; set; } = string.Empty;

    internal string SubmissionSha256 { get; set; } = string.Empty;

    internal string SubmissionId { get; set; } = string.Empty;

    internal string Status { get; set; } = "Accepted";
}

internal sealed class AppleNotarizationStapledCheckpoint
{
    internal string ArtifactPath { get; set; } = string.Empty;

    internal string ArtifactSha256 { get; set; } = string.Empty;

    internal string SubmissionSha256 { get; set; } = string.Empty;

    internal string SubmissionId { get; set; } = string.Empty;

    internal string Status { get; set; } = "Accepted";
}

/// <summary>Result of notarizing, stapling, and assessing a direct macOS artifact.</summary>
public sealed class AppleNotarizationResult
{
    /// <summary>Original artifact.</summary>
    public string ArtifactPath { get; set; } = string.Empty;

    /// <summary>SHA-256 binding the receipt to the current artifact bytes after completed local processing.</summary>
    public string ArtifactSha256 { get; set; } = string.Empty;

    /// <summary>File submitted to notarytool.</summary>
    public string SubmissionPath { get; set; } = string.Empty;

    /// <summary>SHA-256 of the exact file read and accepted by notarytool.</summary>
    public string? SubmissionSha256 { get; set; }

    /// <summary>Notary submission id.</summary>
    public string? SubmissionId { get; set; }

    /// <summary>Notary status, normally Accepted or Invalid.</summary>
    public string? Status { get; set; }

    /// <summary>Whether an already accepted submission was resumed instead of uploaded again.</summary>
    public bool ResumedAcceptedSubmission { get; set; }

    /// <summary>notarytool process result.</summary>
    public ProcessRunResult Submission { get; set; } = new(1, string.Empty, string.Empty, "xcrun", TimeSpan.Zero, false);

    /// <summary>stapler staple process result.</summary>
    public ProcessRunResult? Staple { get; set; }

    /// <summary>stapler validate process result.</summary>
    public ProcessRunResult? StapleValidation { get; set; }

    /// <summary>Gatekeeper assessment process result.</summary>
    public ProcessRunResult? Assessment { get; set; }

    /// <summary>True only when submission, requested stapling, and requested assessment succeeded.</summary>
    public bool Succeeded =>
        Submission.Succeeded &&
        string.Equals(Status, "Accepted", StringComparison.OrdinalIgnoreCase) &&
        (Staple is null || Staple.Succeeded) &&
        (StapleValidation is null || StapleValidation.Succeeded) &&
        (Assessment is null || Assessment.Succeeded);
}
