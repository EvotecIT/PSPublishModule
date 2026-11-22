namespace PowerForge;

/// <summary>
/// Optional trust policy applied before managed module packages are installed, saved, or updated.
/// </summary>
public sealed class ManagedModuleTrustPolicy
{
    /// <summary>
    /// Require the selected repository descriptor to be trusted before repository access or disk changes.
    /// </summary>
    public bool RequireTrustedRepository { get; set; }

    /// <summary>
    /// Package authors allowed by policy. Matching is case-insensitive against comma, semicolon, or pipe separated nuspec authors.
    /// </summary>
    public IReadOnlyList<string> AllowedAuthors { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Apply package metadata checks to dependency packages as well as the requested root module.
    /// </summary>
    public bool ApplyToDependencies { get; set; } = true;
}
