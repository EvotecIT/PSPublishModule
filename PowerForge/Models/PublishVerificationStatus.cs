namespace PowerForge;

/// <summary>
/// Outcome of a shared publish verification probe.
/// </summary>
public enum PublishVerificationStatus
{
    /// <summary>The published target was verified successfully.</summary>
    Verified,

    /// <summary>The published target could not be verified and should be treated as a failure.</summary>
    Failed,

    /// <summary>Verification was intentionally skipped because no reliable probe could be derived.</summary>
    Skipped
}
