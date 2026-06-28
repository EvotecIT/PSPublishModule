namespace PowerForge;

/// <summary>
/// Evidence returned when managed module Authenticode validation is requested.
/// </summary>
public sealed class ManagedModuleAuthenticodeVerificationResult
{
    /// <summary>
    /// Number of files inspected for Authenticode signatures.
    /// </summary>
    public int CheckedFiles { get; set; }

    /// <summary>
    /// Relative paths of files whose Authenticode signatures were validated.
    /// </summary>
    public IReadOnlyList<string> Files { get; set; } = Array.Empty<string>();
}
