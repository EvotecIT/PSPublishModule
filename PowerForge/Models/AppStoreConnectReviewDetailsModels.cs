namespace PowerForge;

/// <summary>Version-scoped App Store Review contact and demo-account details.</summary>
public sealed class AppStoreConnectReviewDetailsInfo
{
    /// <summary>Opaque App Store Connect resource identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Review contact first name.</summary>
    public string ContactFirstName { get; set; } = string.Empty;

    /// <summary>Review contact last name.</summary>
    public string ContactLastName { get; set; } = string.Empty;

    /// <summary>Review contact phone number.</summary>
    public string ContactPhone { get; set; } = string.Empty;

    /// <summary>Review contact email address.</summary>
    public string ContactEmail { get; set; } = string.Empty;

    /// <summary>Whether App Review needs a demo account.</summary>
    public bool? DemoAccountRequired { get; set; }

    /// <summary>Demo-account name when one is required.</summary>
    public string? DemoAccountName { get; set; }

    /// <summary>Demo-account password when one is required.</summary>
    public string? DemoAccountPassword { get; set; }
}

/// <summary>Non-sensitive declaration for copying App Review contact settings between exact app versions.</summary>
public sealed class AppStoreConnectReviewDetailsCopySpec
{
    /// <summary>Configuration schema version.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Existing app version whose contact settings are authoritative.</summary>
    public AppStoreConnectReviewDetailsVersionRef Source { get; set; } = new();

    /// <summary>Draft app version that should receive the contact settings.</summary>
    public AppStoreConnectReviewDetailsVersionRef Target { get; set; } = new();

    /// <summary>Create the exact target draft version when it does not exist.</summary>
    public bool CreateTargetVersion { get; set; }
}

/// <summary>Exact App Store version reference without contact data.</summary>
public sealed class AppStoreConnectReviewDetailsVersionRef
{
    /// <summary>App Store Connect app identifier.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>Exact marketing version.</summary>
    public string VersionString { get; set; } = string.Empty;

    /// <summary>Apple platform.</summary>
    public ApplePlatform Platform { get; set; } = ApplePlatform.iOS;
}

/// <summary>Privacy-preserving reviewed plan for App Review contact synchronization.</summary>
public sealed class AppStoreConnectReviewDetailsCopyPlan
{
    /// <summary>Target app identifier.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>Target marketing version.</summary>
    public string VersionString { get; set; } = string.Empty;

    /// <summary>Target platform.</summary>
    public ApplePlatform Platform { get; set; }

    /// <summary>Source App Store version identifier.</summary>
    public string SourceVersionId { get; set; } = string.Empty;

    /// <summary>Target App Store version identifier.</summary>
    public string? TargetVersionId { get; set; }

    /// <summary>Whether the target App Store version already exists.</summary>
    public bool TargetVersionExists { get; set; }

    /// <summary>Whether a target App Review Details resource already exists.</summary>
    public bool TargetExists { get; set; }

    /// <summary>Whether the copied source declares that a demo account is required.</summary>
    public bool DemoAccountRequired { get; set; }

    /// <summary>Whether source and target contact settings already match.</summary>
    public bool IsConverged { get; set; }

    /// <summary>One-way fingerprint of the source contact settings. No contact value is serialized.</summary>
    public string DesiredFingerprint { get; set; } = string.Empty;

    /// <summary>One-way fingerprint of current target settings, when present.</summary>
    public string? ObservedFingerprint { get; set; }

    /// <summary>Binding over the exact spec, version ids, and observed fingerprints.</summary>
    public string BindingSha256 { get; set; } = string.Empty;

    /// <summary>Time at which the plan was observed.</summary>
    public DateTimeOffset CheckedAtUtc { get; set; }
}

/// <summary>Result of applying an approved App Review details copy plan.</summary>
public sealed class AppStoreConnectReviewDetailsCopyResult
{
    /// <summary>Whether the target converged.</summary>
    public bool Success { get; set; }

    /// <summary>Whether a new details resource was created.</summary>
    public bool Created { get; set; }

    /// <summary>Whether the exact target draft version was created.</summary>
    public bool CreatedVersion { get; set; }

    /// <summary>Whether an existing details resource was updated.</summary>
    public bool Updated { get; set; }

    /// <summary>Stable failure code when convergence did not complete.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Privacy-safe remediation message without contact values.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Plan that was approved and revalidated immediately before mutation.</summary>
    public AppStoreConnectReviewDetailsCopyPlan InitialPlan { get; set; } = new();

    /// <summary>Fresh plan observed after mutation.</summary>
    public AppStoreConnectReviewDetailsCopyPlan FinalPlan { get; set; } = new();
}
