namespace PowerForge;

/// <summary>Trusted build evidence describing the complete signable surface of a packed PowerShell module.</summary>
public sealed class PowerForgeModuleSigningEvidence
{
    /// <summary>Evidence schema version.</summary>
    public int SchemaVersion { get; set; }

    /// <summary>Module name.</summary>
    public string ModuleName { get; set; } = string.Empty;

    /// <summary>Module version.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Clean source revision used by the build.</summary>
    public string SourceRevision { get; set; } = string.Empty;

    /// <summary>Whether tracked or untracked source changes were present when evidence was produced.</summary>
    public bool? SourceDirty { get; set; }

    /// <summary>Archive-relative module manifest entry.</summary>
    public string ManifestPath { get; set; } = string.Empty;

    /// <summary>Archive-relative files whose Authenticode signatures were verified by the build.</summary>
    public string[] SignableFiles { get; set; } = Array.Empty<string>();

    /// <summary>SHA-256 binding of the complete signable-file inventory and signer ownership recorded in signed module provenance.</summary>
    public string SigningInventorySha256 { get; set; } = string.Empty;

    /// <summary>Verified third-party signatures intentionally preserved by the signing pipeline.</summary>
    public PowerForgeModulePreservedSignature[] PreservedThirdPartySignatures { get; set; } = Array.Empty<PowerForgeModulePreservedSignature>();
}

/// <summary>Immutable archive-relative identity for one preserved third-party module signature.</summary>
public sealed class PowerForgeModulePreservedSignature
{
    /// <summary>Archive-relative signed file path.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Signer certificate subject observed by the signing pipeline.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Normalized signer certificate thumbprint observed by the signing pipeline.</summary>
    public string Thumbprint { get; set; } = string.Empty;
}
