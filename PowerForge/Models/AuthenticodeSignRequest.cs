using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

namespace PowerForge;

/// <summary>
/// Request describing an Authenticode signing operation.
/// </summary>
public sealed class AuthenticodeSignRequest
{
    /// <summary>Certificate used to sign files.</summary>
    public X509Certificate2 Certificate { get; set; } = null!;

    /// <summary>Files to evaluate and sign.</summary>
    public IReadOnlyList<string> FilePaths { get; set; } = System.Array.Empty<string>();

    /// <summary>RFC3161 timestamp server URL.</summary>
    public string TimeStampServer { get; set; } = string.Empty;

    /// <summary>Hash algorithm name passed to the signing command.</summary>
    public string HashAlgorithm { get; set; } = "SHA256";

    /// <summary>Windows include-chain argument for Set-AuthenticodeSignature.</summary>
    public string WindowsIncludeChain { get; set; } = "All";

    /// <summary>Non-Windows include-chain argument for Set-OpenAuthenticodeSignature.</summary>
    public string NonWindowsIncludeChain { get; set; } = "WholeChain";
}
