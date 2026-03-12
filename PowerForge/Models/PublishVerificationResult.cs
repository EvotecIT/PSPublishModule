namespace PowerForge;

/// <summary>
/// Result returned by the shared publish verification host service.
/// </summary>
public sealed class PublishVerificationResult
{
    /// <summary>Verification outcome.</summary>
    public PublishVerificationStatus Status { get; set; }

    /// <summary>Human-readable verification summary.</summary>
    public string Summary { get; set; } = string.Empty;
}
