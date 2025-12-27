namespace PowerForge;

/// <summary>
/// Result returned by <see cref="DotNetReleaseBuildService"/>.
/// </summary>
public sealed class DotNetReleaseBuildResult
{
    /// <summary>Resolved project name (from the csproj file name).</summary>
    public string? ProjectName { get; set; }

    /// <summary>Resolved csproj file path.</summary>
    public string? CsprojPath { get; set; }

    /// <summary>Whether the command completed successfully.</summary>
    public bool Success { get; set; }

    /// <summary>Resolved project version (from VersionPrefix).</summary>
    public string? Version { get; set; }

    /// <summary>Path to the bin/Release folder.</summary>
    public string? ReleasePath { get; set; }

    /// <summary>Path to the created zip archive.</summary>
    public string? ZipPath { get; set; }

    /// <summary>NuGet packages produced (or simulated in WhatIf).</summary>
    public string[] Packages { get; set; } = Array.Empty<string>();

    /// <summary>Dependency project paths discovered when <see cref="DotNetReleaseBuildSpec.PackDependencies"/> is used.</summary>
    public string[] DependencyProjects { get; set; } = Array.Empty<string>();

    /// <summary>Optional error message when <see cref="Success"/> is false.</summary>
    public string? ErrorMessage { get; set; }
}
