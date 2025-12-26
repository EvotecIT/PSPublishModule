namespace PowerForge;

/// <summary>
/// Outcome of ensuring a dependency is installed.
/// </summary>
public enum ModuleDependencyInstallStatus
{
    /// <summary>Dependency was skipped.</summary>
    Skipped,
    /// <summary>Dependency is already installed and satisfies requirements.</summary>
    Satisfied,
    /// <summary>Dependency was installed.</summary>
    Installed,
    /// <summary>Dependency was updated/reinstalled.</summary>
    Updated,
    /// <summary>Dependency install/update failed.</summary>
    Failed
}

/// <summary>
/// Result for a single dependency.
/// </summary>
public sealed class ModuleDependencyInstallResult
{
    /// <summary>Module name.</summary>
    public string Name { get; }

    /// <summary>Latest installed version before the operation (when available).</summary>
    public string? InstalledVersion { get; }

    /// <summary>Latest installed version after the operation (when available).</summary>
    public string? ResolvedVersion { get; }

    /// <summary>Version constraint requested for the install (when any).</summary>
    public string? RequestedVersion { get; }

    /// <summary>Status of the operation.</summary>
    public ModuleDependencyInstallStatus Status { get; }

    /// <summary>Installer used (PSResourceGet/PowerShellGet), when a change occurred.</summary>
    public string? Installer { get; }

    /// <summary>Reason or error message (when available).</summary>
    public string? Message { get; }

    /// <summary>
    /// Creates a new result instance.
    /// </summary>
    public ModuleDependencyInstallResult(
        string name,
        string? installedVersion,
        string? resolvedVersion,
        string? requestedVersion,
        ModuleDependencyInstallStatus status,
        string? installer,
        string? message)
    {
        Name = name;
        InstalledVersion = installedVersion;
        ResolvedVersion = resolvedVersion;
        RequestedVersion = requestedVersion;
        Status = status;
        Installer = installer;
        Message = message;
    }
}

