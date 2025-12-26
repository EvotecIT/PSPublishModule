namespace PowerForge;

/// <summary>
/// Configuration segment that describes module manifest metadata.
/// </summary>
public sealed class ConfigurationManifestSegment : IConfigurationSegment
{
    /// <inheritdoc />
    public string Type => "Manifest";

    /// <summary>
    /// Manifest configuration payload.
    /// </summary>
    public ManifestConfiguration Configuration { get; set; } = new();
}

/// <summary>
/// Manifest configuration payload for <see cref="ConfigurationManifestSegment"/>.
/// </summary>
public sealed class ManifestConfiguration
{
    /// <summary>Specifies the version of the module.</summary>
    public string ModuleVersion { get; set; } = string.Empty;

    /// <summary>Specifies the module's compatible PowerShell editions.</summary>
    public string[] CompatiblePSEditions { get; set; } = Array.Empty<string>();

    /// <summary>Specifies a unique identifier for the module (GUID).</summary>
    public string Guid { get; set; } = string.Empty;

    /// <summary>Identifies the module author.</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>Identifies the company or vendor who created the module.</summary>
    public string? CompanyName { get; set; }

    /// <summary>Specifies a copyright statement for the module.</summary>
    public string? Copyright { get; set; }

    /// <summary>Describes the module at a high level.</summary>
    public string? Description { get; set; }

    /// <summary>Specifies the minimum version of PowerShell this module requires.</summary>
    public string PowerShellVersion { get; set; } = "5.1";

    /// <summary>Specifies tags for the module.</summary>
    public string[]? Tags { get; set; }

    /// <summary>Specifies the URI for the module's icon.</summary>
    public string? IconUri { get; set; }

    /// <summary>Specifies the URI for the module's project page.</summary>
    public string? ProjectUri { get; set; }

    /// <summary>Specifies the minimum version of the Microsoft .NET Framework that the module requires.</summary>
    public string? DotNetFrameworkVersion { get; set; }

    /// <summary>Specifies the URI for the module's license.</summary>
    public string? LicenseUri { get; set; }

    /// <summary>When set, indicates the module requires explicit user license acceptance (PowerShellGet).</summary>
    public bool RequireLicenseAcceptance { get; set; }

    /// <summary>Specifies the prerelease tag for the module.</summary>
    public string? Prerelease { get; set; }

    /// <summary>Overrides functions to export in the module manifest.</summary>
    public string[]? FunctionsToExport { get; set; }

    /// <summary>Overrides cmdlets to export in the module manifest.</summary>
    public string[]? CmdletsToExport { get; set; }

    /// <summary>Overrides aliases to export in the module manifest.</summary>
    public string[]? AliasesToExport { get; set; }

    /// <summary>Specifies formatting files (.ps1xml) that run when the module is imported.</summary>
    public string[]? FormatsToProcess { get; set; }
}

