using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Result produced by isolated module profile validation.
/// </summary>
public sealed class IsolatedModuleProfileValidationResult
{
    /// <summary>Resolved profile name.</summary>
    public string ProfileName { get; set; } = string.Empty;

    /// <summary>PowerShell module name resolved for the profile.</summary>
    public string ModuleName { get; set; } = string.Empty;

    /// <summary>Stable load-context name used by the profile.</summary>
    public string ContextName { get; set; } = string.Empty;

    /// <summary>Minimum module version required by the profile, when one is declared.</summary>
    public Version? MinimumVersion { get; set; }

    /// <summary>Resolved module version from the source manifest, when available.</summary>
    public Version? ResolvedVersion { get; set; }

    /// <summary>Resolved source module base directory.</summary>
    public string SourceModuleBase { get; set; } = string.Empty;

    /// <summary>Resolved source manifest path.</summary>
    public string ManifestPath { get; set; } = string.Empty;

    /// <summary>Checked profile paths.</summary>
    public List<IsolatedModuleProfileValidationPath> Paths { get; } = new();

    /// <summary>Validation issues.</summary>
    public List<IsolatedModuleProfileValidationIssue> Issues { get; } = new();

    /// <summary>Whether validation completed without error-level issues.</summary>
    public bool IsValid => Issues.All(static issue => !string.Equals(issue.Severity, "Error", StringComparison.OrdinalIgnoreCase));
}
