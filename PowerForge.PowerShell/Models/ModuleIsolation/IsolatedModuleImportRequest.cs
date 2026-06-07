namespace PowerForge;

/// <summary>
/// Request for importing a PowerShell module through a known module-isolation profile.
/// </summary>
public sealed class IsolatedModuleImportRequest
{
    /// <summary>Name of the built-in profile to use.</summary>
    public string ProfileName { get; set; } = string.Empty;

    /// <summary>Optional module name override. When omitted, the profile's module name is used.</summary>
    public string? ModuleName { get; set; }

    /// <summary>Optional module manifest or module base path. When omitted, the module is resolved from the current session.</summary>
    public string? Path { get; set; }

    /// <summary>Optional root folder for generated isolated module copies.</summary>
    public string? WorkRoot { get; set; }

    /// <summary>Prepend the generated module parent path to PSModulePath after a successful import.</summary>
    public bool PreferIsolatedModulePath { get; set; }
}
