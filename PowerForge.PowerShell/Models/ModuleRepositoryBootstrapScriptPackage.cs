using System;

namespace PowerForge;

/// <summary>Generated managed private-gallery workstation bootstrap script package.</summary>
public sealed class ModuleRepositoryBootstrapScriptPackage
{
    /// <summary>Output directory that contains the generated bootstrap package.</summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Generated bootstrap script path.</summary>
    public string ScriptPath { get; set; } = string.Empty;

    /// <summary>Generated non-secret profile JSON path.</summary>
    public string ProfilePath { get; set; } = string.Empty;

    /// <summary>Profile names included in the package.</summary>
    public string[] ProfileNames { get; set; } = Array.Empty<string>();

    /// <summary>Module names pre-populated into the generated bootstrap script.</summary>
    public string[] InstallModules { get; set; } = Array.Empty<string>();

    /// <summary>Recommended command for running the generated bootstrap script.</summary>
    public string RecommendedCommand { get; set; } = string.Empty;

    /// <summary>Whether the generated profile file and script intentionally contain no secrets.</summary>
    public bool ContainsSecrets { get; set; }
}
