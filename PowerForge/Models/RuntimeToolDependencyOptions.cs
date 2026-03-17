using System;

namespace PowerForge;

/// <summary>
/// Options for opt-in runtime installation of tool-style PowerShell module dependencies.
/// </summary>
public sealed class RuntimeToolDependencyOptions
{
    /// <summary>Force reinstall even when the dependency is already present.</summary>
    public bool Force { get; set; }

    /// <summary>Optional repository name to use when installing the dependency.</summary>
    public string? Repository { get; set; }

    /// <summary>Optional repository credential.</summary>
    public RepositoryCredential? Credential { get; set; }

    /// <summary>Allow prerelease versions when resolving/installing the dependency.</summary>
    public bool Prerelease { get; set; }

    /// <summary>Prefer PowerShellGet over PSResourceGet when both are available.</summary>
    public bool PreferPowerShellGet { get; set; }

    /// <summary>Optional timeout to apply per dependency installation attempt.</summary>
    public TimeSpan? TimeoutPerModule { get; set; }
}
