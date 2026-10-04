namespace PowerForge;

/// <summary>Explicit metadata for packaging one qualified Strict library project target as NuGet.</summary>
public sealed class PowerShellCompilationProjectNuGetPackage
{
    /// <summary>NuGet package identity.</summary>
    public string PackageId { get; set; } = string.Empty;

    /// <summary>Stable three-part package version.</summary>
    public string PackageVersion { get; set; } = string.Empty;

    /// <summary>Package authors supplied by the project owner.</summary>
    public string Authors { get; set; } = string.Empty;

    /// <summary>Description of the library provided by this package.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>SPDX license expression chosen by the project owner.</summary>
    public string LicenseExpression { get; set; } = string.Empty;

    /// <summary>Optional source repository URL, paired with a repository commit.</summary>
    public string? RepositoryUrl { get; set; }

    /// <summary>Optional source repository revision, paired with a repository URL.</summary>
    public string? RepositoryCommit { get; set; }

    /// <summary>Optional project-relative ABI manifest whose public contract must remain compatible.</summary>
    public string? CompatibilityBaseline { get; set; }
}
