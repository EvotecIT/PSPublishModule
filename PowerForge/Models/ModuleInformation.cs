using System;

namespace PowerForge;

/// <summary>
/// Represents key information extracted from a PowerShell module manifest (.psd1).
/// </summary>
public sealed class ModuleInformation
{
    /// <summary>Name of the module (derived from manifest file name).</summary>
    public string ModuleName { get; }
    /// <summary>Full path to the manifest (.psd1) file.</summary>
    public string ManifestPath { get; }
    /// <summary>Full path to the project root that was analyzed.</summary>
    public string ProjectPath { get; }
    /// <summary>Module version as declared in the manifest (string form).</summary>
    public string? ModuleVersion { get; }
    /// <summary>Root module entry from the manifest (RootModule).</summary>
    public string? RootModule { get; }
    /// <summary>PowerShell version requirement from the manifest (PowerShellVersion).</summary>
    public string? PowerShellVersion { get; }
    /// <summary>GUID value from the manifest (GUID), when present and parseable.</summary>
    public Guid? Guid { get; }
    /// <summary>Typed RequiredModules entries extracted from the manifest.</summary>
    public ManifestEditor.RequiredModule[] RequiredModules { get; }
    /// <summary>Raw manifest text, when available.</summary>
    public string? ManifestText { get; }

    /// <summary>
    /// Creates a new <see cref="ModuleInformation"/> instance.
    /// </summary>
    public ModuleInformation(
        string moduleName,
        string manifestPath,
        string projectPath,
        string? moduleVersion,
        string? rootModule,
        string? powerShellVersion,
        Guid? guid,
        ManifestEditor.RequiredModule[] requiredModules,
        string? manifestText)
    {
        ModuleName = moduleName;
        ManifestPath = manifestPath;
        ProjectPath = projectPath;
        ModuleVersion = moduleVersion;
        RootModule = rootModule;
        PowerShellVersion = powerShellVersion;
        Guid = guid;
        RequiredModules = requiredModules ?? Array.Empty<ManifestEditor.RequiredModule>();
        ManifestText = manifestText;
    }
}

