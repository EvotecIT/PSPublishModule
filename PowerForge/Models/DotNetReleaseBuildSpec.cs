namespace PowerForge;

/// <summary>
/// Specification for building a .NET project in Release configuration and preparing release artefacts.
/// </summary>
public sealed class DotNetReleaseBuildSpec
{
    /// <summary>Path to the folder containing the project (*.csproj) file (or the csproj file itself).</summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>Build configuration (defaults to Release).</summary>
    public string Configuration { get; set; } = "Release";

    /// <summary>Optional certificate thumbprint used to sign assemblies and packages. When omitted, no signing is performed.</summary>
    public string? CertificateThumbprint { get; set; }

    /// <summary>Certificate store location used when searching for the signing certificate.</summary>
    public CertificateStoreLocation LocalStore { get; set; } = CertificateStoreLocation.CurrentUser;

    /// <summary>Timestamp server URL used while signing.</summary>
    public string TimeStampServer { get; set; } = "http://timestamp.digicert.com";

    /// <summary>When enabled, also packs all project dependencies that have their own .csproj files.</summary>
    public bool PackDependencies { get; set; }

    /// <summary>When true, does not execute build/pack/sign, but still resolves paths/versions and returns a simulated result.</summary>
    public bool WhatIf { get; set; }

    /// <summary>Patterns used to select which assemblies should be signed.</summary>
    public string[] AssemblyIncludePatterns { get; set; } = { "*.dll" };
}

