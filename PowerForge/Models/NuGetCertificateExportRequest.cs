using System;

namespace PowerForge;

/// <summary>
/// Request used to export a public signing certificate for NuGet.org registration.
/// </summary>
public sealed class NuGetCertificateExportRequest
{
    /// <summary>Certificate thumbprint to look up.</summary>
    public string? CertificateThumbprint { get; set; }

    /// <summary>Certificate SHA256 hash to look up.</summary>
    public string? CertificateSha256 { get; set; }

    /// <summary>Output path for the exported certificate file.</summary>
    public string? OutputPath { get; set; }

    /// <summary>Certificate store location to search.</summary>
    public CertificateStoreLocation StoreLocation { get; set; } = CertificateStoreLocation.CurrentUser;

    /// <summary>Working directory used when output path is not specified.</summary>
    public string WorkingDirectory { get; set; } = Environment.CurrentDirectory;
}
