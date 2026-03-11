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
