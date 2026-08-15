namespace PowerForge.Web;

/// <summary>Options for scanning machine-facing website artifacts for unsafe package and agent instructions.</summary>
public sealed class WebAgentContentSecurityOptions
{
    /// <summary>Root directory containing the generated artifacts.</summary>
    public string SiteRoot { get; set; } = ".";
    /// <summary>Artifact paths, relative to <see cref="SiteRoot"/>, to scan.</summary>
    public string[] Files { get; set; } = new[] { "llms.txt", "llms-full.txt", "llms.json" };
    /// <summary>Optional owner-scoped ecosystem catalog used to verify Evotec-owned NuGet and PowerShell Gallery packages.</summary>
    public string? PublicationCatalogPath { get; set; }
    /// <summary>Maximum accepted publication catalog age in hours. Zero disables the age check.</summary>
    public int PublicationCatalogMaxAgeHours { get; set; }
    /// <summary>Expected NuGet owner when owner verification is required.</summary>
    public string? NuGetOwner { get; set; }
    /// <summary>Expected PowerShell Gallery owner when owner verification is required.</summary>
    public string? PowerShellGalleryOwner { get; set; }
    /// <summary>
    /// Package selectors that require owner-catalog verification. Selectors use <c>ecosystem:pattern</c>,
    /// for example <c>nuget:*</c> or <c>powershellgallery:Evotec*</c>.
    /// </summary>
    public string[] RequireOwnerVerification { get; set; } = Array.Empty<string>();
    /// <summary>
    /// Explicit third-party package exceptions that may use registry-existence verification instead of owner verification.
    /// Entries use <c>ecosystem:package-id</c>.
    /// </summary>
    public string[] RegistryVerifiedPackages { get; set; } = Array.Empty<string>();
    /// <summary>When true, verify extracted package names and versions against their public registries.</summary>
    public bool VerifyPackages { get; set; } = true;
    /// <summary>When true, scan HTTP(S) destinations and verify that untrusted hosts resolve.</summary>
    public bool VerifyExternalHosts { get; set; }
    /// <summary>Domains trusted as owned or intentionally depended on. A leading dot matches subdomains.</summary>
    public string[] TrustedDomains { get; set; } = Array.Empty<string>();
    /// <summary>HTTP request timeout in seconds.</summary>
    public int RequestTimeoutSeconds { get; set; } = 15;
    /// <summary>Maximum artifact size in bytes. Larger configured artifacts fail closed.</summary>
    public long MaxArtifactBytes { get; set; } = 5 * 1024 * 1024;
    /// <summary>Maximum unique package references accepted before registry work is refused.</summary>
    public int MaxPackageReferences { get; set; } = 100;
    /// <summary>Maximum unique external hosts or origins accepted before host verification is refused.</summary>
    public int MaxExternalHosts { get; set; } = 100;
    /// <summary>Maximum decompressed registry response size in bytes.</summary>
    public long MaxRegistryResponseBytes { get; set; } = 2 * 1024 * 1024;
    /// <summary>Maximum total time for all network verification in one scan.</summary>
    public int MaxNetworkDurationSeconds { get; set; } = 120;
    /// <summary>When true, emit warnings for high-confidence agent-directed prompt injection phrases.</summary>
    public bool CheckPromptInjection { get; set; } = true;
}

/// <summary>A package reference extracted from a machine-facing artifact.</summary>
public sealed class WebAgentPackageReference
{
    /// <summary>Normalized package ecosystem.</summary>
    public string Ecosystem { get; set; } = string.Empty;
    /// <summary>Package identifier.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Exact requested version when present.</summary>
    public string? Version { get; set; }
    /// <summary>Artifact-relative source path.</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>One-based source line.</summary>
    public int Line { get; set; }
    /// <summary>The command family that introduced the package reference.</summary>
    public string Command { get; set; } = string.Empty;
}

/// <summary>A finding produced by the agent-content security scanner.</summary>
public sealed class WebAgentContentSecurityFinding
{
    /// <summary>Severity: error, warning, or info.</summary>
    public string Severity { get; set; } = "warning";
    /// <summary>Stable finding code.</summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>Artifact-relative source path.</summary>
    public string? Path { get; set; }
    /// <summary>One-based source line when available.</summary>
    public int? Line { get; set; }
    /// <summary>Human-readable finding detail.</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>Result of scanning machine-facing website artifacts.</summary>
public sealed class WebAgentContentSecurityResult
{
    /// <summary>True when no error findings were emitted.</summary>
    public bool Success { get; set; }
    /// <summary>Number of configured artifacts scanned.</summary>
    public int ArtifactCount { get; set; }
    /// <summary>Number of package references extracted.</summary>
    public int PackageReferenceCount { get; set; }
    /// <summary>Number of package references verified successfully.</summary>
    public int VerifiedPackageCount { get; set; }
    /// <summary>Number of unique external hosts checked.</summary>
    public int ExternalHostCount { get; set; }
    /// <summary>Structured scanner findings.</summary>
    public WebAgentContentSecurityFinding[] Findings { get; set; } = Array.Empty<WebAgentContentSecurityFinding>();
}
