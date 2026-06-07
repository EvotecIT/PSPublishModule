namespace PowerForge;

/// <summary>
/// Result returned after an isolated module import.
/// </summary>
public sealed class IsolatedModuleImportResult
{
    /// <summary>Name of the profile used for import.</summary>
    public string ProfileName { get; set; } = string.Empty;

    /// <summary>Name of the module resolved by the profile.</summary>
    public string ModuleName { get; set; } = string.Empty;

    /// <summary>Original module base path.</summary>
    public string SourceModuleBase { get; set; } = string.Empty;

    /// <summary>Generated script module path imported into the current session.</summary>
    public string IsolatedScriptPath { get; set; } = string.Empty;

    /// <summary>Generated manifest path imported into the current session when the profile preserves manifest exports.</summary>
    public string IsolatedManifestPath { get; set; } = string.Empty;

    /// <summary>Path imported into PowerShell, either the generated manifest or generated script.</summary>
    public string IsolatedImportPath { get; set; } = string.Empty;

    /// <summary>Root folder containing the generated copy.</summary>
    public string WorkPath { get; set; } = string.Empty;

    /// <summary>Whether the generated module parent path was requested as the preferred PSModulePath entry.</summary>
    public bool PreferIsolatedModulePath { get; set; }

    /// <summary>PSModulePath entry prepended when <see cref="PreferIsolatedModulePath"/> is enabled.</summary>
    public string IsolatedModuleResolutionPath { get; set; } = string.Empty;

    /// <summary>Load-context name used by the generated wrapper.</summary>
    public string ContextName { get; set; } = string.Empty;

    /// <summary>Number of binary imports configured by the profile.</summary>
    public int BinaryImportCount { get; set; }

    /// <summary>Number of namespace prefixes bridged into PowerShell type resolution.</summary>
    public int TypeAcceleratorNamespaceCount { get; set; }
}
