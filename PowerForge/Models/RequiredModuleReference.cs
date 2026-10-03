namespace PowerForge;

/// <summary>
/// Describes a single RequiredModules manifest entry.
/// </summary>
public class RequiredModuleReference
{
    /// <summary>Module name.</summary>
    public string ModuleName { get; }

    /// <summary>Optional explicit module version.</summary>
    public string? ModuleVersion { get; }

    /// <summary>Optional exact required version.</summary>
    public string? RequiredVersion { get; }

    /// <summary>Optional maximum allowed version.</summary>
    public string? MaximumVersion { get; }

    /// <summary>Optional module GUID.</summary>
    public string? Guid { get; }

    /// <summary>
    /// Creates a new required module reference.
    /// </summary>
    public RequiredModuleReference(
        string moduleName,
        string? moduleVersion = null,
        string? requiredVersion = null,
        string? maximumVersion = null,
        string? guid = null)
    {
        ModuleName = moduleName;
        ModuleVersion = moduleVersion;
        RequiredVersion = requiredVersion;
        MaximumVersion = maximumVersion;
        Guid = guid;
    }
}

/// <summary>
/// Keeps the concrete dependency identity selected during planning separate from the
/// manifest-safe <see cref="RequiredModuleReference"/> constraint.
/// </summary>
internal sealed class ResolvedRequiredModuleReference : RequiredModuleReference
{
    internal string? ResolvedVersion { get; }
    internal string? ResolvedMinimumVersion { get; }
    internal bool MatchPrereleaseByBaseVersion { get; }

    internal ResolvedRequiredModuleReference(
        string moduleName,
        string? moduleVersion,
        string? requiredVersion,
        string? maximumVersion,
        string? guid,
        string? resolvedVersion,
        string? resolvedMinimumVersion,
        bool matchPrereleaseByBaseVersion)
        : base(moduleName, moduleVersion, requiredVersion, maximumVersion, guid)
    {
        ResolvedVersion = string.IsNullOrWhiteSpace(resolvedVersion) ? null : resolvedVersion!.Trim();
        ResolvedMinimumVersion = string.IsNullOrWhiteSpace(resolvedMinimumVersion) ? null : resolvedMinimumVersion!.Trim();
        MatchPrereleaseByBaseVersion = matchPrereleaseByBaseVersion;
    }
}
