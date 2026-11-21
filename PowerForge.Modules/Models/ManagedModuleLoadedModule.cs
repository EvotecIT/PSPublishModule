namespace PowerForge;

/// <summary>
/// Describes module evidence that is already loaded in a PowerShell session.
/// </summary>
public sealed class ManagedModuleLoadedModule
{
    /// <summary>
    /// Loaded module name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Loaded module version, when known.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Loaded module base directory, when known.
    /// </summary>
    public string? ModuleBase { get; set; }

    /// <summary>
    /// Loaded module path, when known.
    /// </summary>
    public string? Path { get; set; }
}
