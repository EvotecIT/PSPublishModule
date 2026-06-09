namespace PowerForge;

/// <summary>
/// Host-facing request describing a published target that should be verified.
/// </summary>
public sealed class PublishVerificationRequest
{
    /// <summary>Repository root used for local file and repository resolution.</summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>Repository display name.</summary>
    public string RepositoryName { get; set; } = string.Empty;

    /// <summary>Adapter kind that produced the published target.</summary>
    public string AdapterKind { get; set; } = string.Empty;

    /// <summary>Target display name.</summary>
    public string TargetName { get; set; } = string.Empty;

    /// <summary>Target kind such as <c>GitHub</c>, <c>NuGet</c>, or <c>PowerShellRepository</c>.</summary>
    public string TargetKind { get; set; } = string.Empty;

    /// <summary>Recorded destination string for the publish target.</summary>
    public string? Destination { get; set; }

    /// <summary>Recorded local source path used during publish.</summary>
    public string? SourcePath { get; set; }
}
