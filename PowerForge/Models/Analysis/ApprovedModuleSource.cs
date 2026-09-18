namespace PowerForge;

/// <summary>
/// Identifies the concrete installed or downloaded module selected as an approved function donor.
/// </summary>
public sealed class ApprovedModuleSource
{
    /// <summary>Module name declared by the build configuration.</summary>
    public string Name { get; }

    /// <summary>Concrete selected module version, when known.</summary>
    public string? Version { get; }

    /// <summary>Absolute module base path containing the module manifest.</summary>
    public string ModuleBasePath { get; }

    /// <summary>Concrete selected module GUID, when constrained or discovered.</summary>
    public string? Guid { get; }

    /// <summary>Creates a concrete approved-module source.</summary>
    public ApprovedModuleSource(string name, string? version, string moduleBasePath, string? guid = null)
    {
        Name = name?.Trim() ?? string.Empty;
        Version = string.IsNullOrWhiteSpace(version) ? null : version!.Trim();
        ModuleBasePath = moduleBasePath?.Trim() ?? string.Empty;
        Guid = string.IsNullOrWhiteSpace(guid) ? null : guid!.Trim();
    }
}
