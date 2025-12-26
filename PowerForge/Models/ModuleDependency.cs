namespace PowerForge;

/// <summary>
/// Declares a PowerShell module dependency with optional version constraints.
/// </summary>
public sealed class ModuleDependency
{
    /// <summary>Module name.</summary>
    public string Name { get; }

    /// <summary>Exact required version (mutually exclusive with Minimum/Maximum when used).</summary>
    public string? RequiredVersion { get; }

    /// <summary>Minimum required version.</summary>
    public string? MinimumVersion { get; }

    /// <summary>Maximum allowed version.</summary>
    public string? MaximumVersion { get; }

    /// <summary>
    /// Creates a new dependency specification.
    /// </summary>
    public ModuleDependency(string name, string? requiredVersion = null, string? minimumVersion = null, string? maximumVersion = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Dependency name is required.", nameof(name));
        Name = name.Trim();
        RequiredVersion = string.IsNullOrWhiteSpace(requiredVersion) ? null : requiredVersion!.Trim();
        MinimumVersion = string.IsNullOrWhiteSpace(minimumVersion) ? null : minimumVersion!.Trim();
        MaximumVersion = string.IsNullOrWhiteSpace(maximumVersion) ? null : maximumVersion!.Trim();
    }
}
