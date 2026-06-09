using System;
using System.Security.Cryptography.X509Certificates;

namespace PowerForge;

/// <summary>
/// Result returned by the NuGet certificate export service.
/// </summary>
public sealed class NuGetCertificateExportResult
{
    /// <summary>Whether the export completed successfully.</summary>
    public bool Success { get; set; }

    /// <summary>Error message returned when the export fails.</summary>
    public string? Error { get; set; }

    /// <summary>Full path to the exported certificate file.</summary>
    public string? CertificatePath { get; set; }

    /// <summary>The matched certificate.</summary>
    public X509Certificate2? Certificate { get; set; }

    /// <summary>Whether the certificate appears to have Code Signing EKU.</summary>
    public bool HasCodeSigningEku { get; set; }

    /// <summary>SHA256 fingerprint of the matched certificate.</summary>
    public string? Sha256 { get; set; }

    /// <summary>Certificate subject.</summary>
    public string? Subject { get; set; }

    /// <summary>Certificate issuer.</summary>
    public string? Issuer { get; set; }

    /// <summary>Certificate validity start date.</summary>
    public DateTime? NotBefore { get; set; }

    /// <summary>Certificate validity end date.</summary>
    public DateTime? NotAfter { get; set; }
}
