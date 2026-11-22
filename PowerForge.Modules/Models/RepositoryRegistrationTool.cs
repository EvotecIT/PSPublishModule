namespace PowerForge;

/// <summary>
/// Tool selection used when registering repositories for end users.
/// </summary>
public enum RepositoryRegistrationTool
{
    /// <summary>
    /// Prefer PSResourceGet first, then fall back to PowerShellGet when one of the tools is unavailable.
    /// </summary>
    Auto,
    /// <summary>Register the PSResourceGet repository only.</summary>
    PSResourceGet,
    /// <summary>Register the PowerShellGet repository only.</summary>
    PowerShellGet,
    /// <summary>Register both PowerShellGet and PSResourceGet repositories.</summary>
    Both
}
