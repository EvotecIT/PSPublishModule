using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PowerForge;

/// <summary>
/// Resolves SHA256 certificate fingerprints from the local certificate store.
/// </summary>
public sealed class CertificateFingerprintResolver
{
    private readonly Func<StoreLocation, string, string?> _resolveSha256;

    /// <summary>
    /// Creates a new resolver using the local certificate store.
    /// </summary>
    public CertificateFingerprintResolver()
        : this(ResolveSha256Core)
    {
    }

    internal CertificateFingerprintResolver(Func<StoreLocation, string, string?> resolveSha256)
    {
        _resolveSha256 = resolveSha256 ?? throw new ArgumentNullException(nameof(resolveSha256));
    }

    /// <summary>
    /// Resolves a SHA256 fingerprint for the provided thumbprint and store name.
    /// </summary>
    public string? ResolveSha256(string thumbprint, string storeName)
    {
        if (string.IsNullOrWhiteSpace(thumbprint))
            throw new ArgumentException("Thumbprint is required.", nameof(thumbprint));
        if (string.IsNullOrWhiteSpace(storeName))
            throw new ArgumentException("StoreName is required.", nameof(storeName));

        try
        {
            return _resolveSha256(ParseStoreLocation(storeName), NormalizeThumbprint(thumbprint));
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveSha256Core(StoreLocation storeLocation, string normalizedThumbprint)
    {
        using var store = new X509Store(StoreName.My, storeLocation);
        store.Open(OpenFlags.ReadOnly);
        var certificate = store.Certificates.Cast<X509Certificate2>()
            .FirstOrDefault(candidate => NormalizeThumbprint(candidate.Thumbprint) == normalizedThumbprint);
        return certificate?.GetCertHashString(HashAlgorithmName.SHA256);
    }

    private static StoreLocation ParseStoreLocation(string storeName)
        => string.Equals(storeName, "LocalMachine", StringComparison.OrdinalIgnoreCase)
            ? StoreLocation.LocalMachine
            : StoreLocation.CurrentUser;

    private static string NormalizeThumbprint(string? thumbprint)
        => (thumbprint ?? string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
}
