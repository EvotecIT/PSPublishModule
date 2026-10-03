namespace PowerForge;

/// <summary>
/// Identifies a concrete installed or downloaded module source selected for dependency analysis.
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

    /// <summary>Optional module search root that contains dependencies downloaded beside this donor.</summary>
    public string? ModuleSearchRoot { get; }

    /// <summary>Creates a concrete module-analysis source.</summary>
    public ApprovedModuleSource(
        string name,
        string? version,
        string moduleBasePath,
        string? guid = null,
        string? moduleSearchRoot = null)
    {
        Name = name?.Trim() ?? string.Empty;
        Version = string.IsNullOrWhiteSpace(version) ? null : version!.Trim();
        ModuleBasePath = moduleBasePath?.Trim() ?? string.Empty;
        Guid = string.IsNullOrWhiteSpace(guid) ? null : guid!.Trim();
        ModuleSearchRoot = string.IsNullOrWhiteSpace(moduleSearchRoot) ? null : moduleSearchRoot!.Trim();
    }
}
