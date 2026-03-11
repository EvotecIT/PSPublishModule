using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

namespace PowerForge;

/// <summary>
/// Result of locating a code-signing certificate in a certificate store.
/// </summary>
public sealed class CodeSigningCertificateLookupResult
{
    /// <summary>The selected certificate, when a unique match was found.</summary>
    public X509Certificate2? Certificate { get; set; }

    /// <summary>All matching code-signing certificates discovered in the queried store.</summary>
    public IReadOnlyList<X509Certificate2> AvailableCertificates { get; set; } = System.Array.Empty<X509Certificate2>();
}
