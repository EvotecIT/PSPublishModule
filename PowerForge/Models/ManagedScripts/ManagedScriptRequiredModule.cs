namespace PowerForge;

/// <summary>
/// Represents one <c>#Requires -Module</c> entry in a managed PowerShell script file.
/// </summary>
public sealed class ManagedScriptRequiredModule
{
    /// <summary>Required module name.</summary>
    public string ModuleName { get; set; } = string.Empty;

    /// <summary>Optional module GUID constraint.</summary>
    public string? Guid { get; set; }

    /// <summary>Minimum module version constraint.</summary>
    public string? ModuleVersion { get; set; }

    /// <summary>Exact required module version constraint.</summary>
    public string? RequiredVersion { get; set; }

    /// <summary>Maximum module version constraint.</summary>
    public string? MaximumVersion { get; set; }
}
