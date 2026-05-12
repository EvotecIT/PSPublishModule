namespace PowerForge;

/// <summary>
/// Configuration segment that describes legacy .NET library build settings.
/// </summary>
public sealed class ConfigurationBuildLibrariesSegment : IConfigurationSegment
{
    /// <inheritdoc />
    public string Type => "BuildLibraries";

    /// <summary>
    /// BuildLibraries configuration payload.
    /// </summary>
    public BuildLibrariesConfiguration BuildLibraries { get; set; } = new();
}

/// <summary>
/// BuildLibraries configuration payload for <see cref="ConfigurationBuildLibrariesSegment"/>.
/// </summary>
public sealed class BuildLibrariesConfiguration
{
    /// <summary>Enables library build.</summary>
    public bool? Enable { get; set; }

    /// <summary>.NET build configuration (e.g., Release or Debug).</summary>
    public string? Configuration { get; set; }

    /// <summary>Target frameworks to build/publish.</summary>
    public string[]? Framework { get; set; }

    /// <summary>.NET project name (legacy field).</summary>
    public string? ProjectName { get; set; }

    /// <summary>Exclude the main library when copying output.</summary>
    public bool? ExcludeMainLibrary { get; set; }

    /// <summary>Optional filter for excluding libraries.</summary>
    public string[]? ExcludeLibraryFilter { get; set; }

    /// <summary>Library names to ignore on load (legacy).</summary>
    public string[]? IgnoreLibraryOnLoad { get; set; }

    /// <summary>Binary module name (legacy).</summary>
    public string[]? BinaryModule { get; set; }

    /// <summary>Handle assemblies with the same name (legacy).</summary>
    public bool? HandleAssemblyWithSameName { get; set; }

    /// <summary>Use line-by-line Add-Type (legacy).</summary>
    public bool? NETLineByLineAddType { get; set; }

    /// <summary>Path to the .NET project (legacy key: NETProjectPath).</summary>
    public string? NETProjectPath { get; set; }

    /// <summary>Disable cmdlet scan for binary modules (legacy).</summary>
    public bool? BinaryModuleCmdletScanDisabled { get; set; }

    /// <summary>Search class used for cmdlet scan (legacy).</summary>
    public string? SearchClass { get; set; }

    /// <summary>Generate documentation for binary modules (legacy).</summary>
    public bool? NETBinaryModuleDocumentation { get; set; }

    /// <summary>Handle runtimes folder when copying libraries.</summary>
    public bool? HandleRuntimes { get; set; }

    /// <summary>Load the binary module and its dependencies through a custom AssemblyLoadContext on PowerShell Core.</summary>
    public bool? UseAssemblyLoadContext { get; set; }

    /// <summary>Legacy alias for <see cref="UseAssemblyLoadContext"/>.</summary>
    public bool? NETAssemblyLoadContext { get; set; }

    /// <summary>Controls optional PowerShell type accelerator exposure for assemblies loaded in the module AssemblyLoadContext.</summary>
    public AssemblyTypeAcceleratorExportMode? AssemblyTypeAcceleratorMode { get; set; }

    /// <summary>Legacy .NET build alias for <see cref="AssemblyTypeAcceleratorMode"/>.</summary>
    public AssemblyTypeAcceleratorExportMode? NETAssemblyTypeAcceleratorMode { get; set; }

    /// <summary>Fully-qualified type names to expose as PowerShell type accelerators from the module AssemblyLoadContext.</summary>
    public string[]? AssemblyTypeAccelerators { get; set; }

    /// <summary>Legacy .NET build alias for <see cref="AssemblyTypeAccelerators"/>.</summary>
    public string[]? NETAssemblyTypeAccelerators { get; set; }

    /// <summary>Assembly simple names whose public types may be exposed as PowerShell type accelerators when assembly mode is enabled.</summary>
    public string[]? AssemblyTypeAcceleratorAssemblies { get; set; }

    /// <summary>Legacy .NET build alias for <see cref="AssemblyTypeAcceleratorAssemblies"/>.</summary>
    public string[]? NETAssemblyTypeAcceleratorAssemblies { get; set; }

    /// <summary>Do not copy libraries recursively (legacy).</summary>
    public bool? NETDoNotCopyLibrariesRecursively { get; set; }
}

/// <summary>
/// Controls how PowerForge exposes types loaded in a module-scoped AssemblyLoadContext to PowerShell callers.
/// </summary>
public enum AssemblyTypeAcceleratorExportMode
{
    /// <summary>Do not register type accelerators for ALC-loaded dependency types.</summary>
    None = 0,

    /// <summary>Register only explicitly configured fully-qualified type names.</summary>
    AllowList = 1,

    /// <summary>
    /// Register all public types from explicitly configured assemblies, plus any explicitly configured type names.
    /// </summary>
    Assembly = 2
}
