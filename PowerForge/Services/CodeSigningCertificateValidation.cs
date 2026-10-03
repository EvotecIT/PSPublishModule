using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace PowerForge;

/// <summary>Validates prerequisites shared by Authenticode and NuGet signing.</summary>
internal static class CodeSigningCertificateValidation
{
    internal static string? GetSha256FromStore(string thumbprint, CertificateStoreLocation location)
    {
        try
        {
            using var store = new X509Store(StoreName.My,
                location == CertificateStoreLocation.LocalMachine ? StoreLocation.LocalMachine : StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            var normalized = thumbprint.Replace(" ", string.Empty).ToUpperInvariant();
            var certificate = store.Certificates.Cast<X509Certificate2>().FirstOrDefault(cert =>
                cert.Thumbprint.Replace(" ", string.Empty).ToUpperInvariant() == normalized);
            if (certificate is null) return null;
            Validate(certificate);
            return FrameworkCompatibility.GetSha256Hex(certificate);
        }
        catch (InvalidOperationException) { throw; }
        catch { return null; }
    }

    internal static void Validate(X509Certificate2 certificate)
    {
        var now = DateTime.UtcNow;
        if (certificate.NotBefore.ToUniversalTime() > now)
            throw new InvalidOperationException($"Signing certificate '{certificate.Thumbprint}' is not valid until {certificate.NotBefore:u}.");
        if (certificate.NotAfter.ToUniversalTime() <= now)
            throw new InvalidOperationException($"Signing certificate '{certificate.Thumbprint}' expired on {certificate.NotAfter:u}.");
        if (!certificate.HasPrivateKey)
            throw new InvalidOperationException($"Signing certificate '{certificate.Thumbprint}' has no accessible private key.");
        var codeSigning = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>()
            .Any(extension => extension.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>()
                .Any(oid => oid.Value == "1.3.6.1.5.5.7.3.3"));
        if (!codeSigning)
            throw new InvalidOperationException($"Certificate '{certificate.Thumbprint}' does not allow code signing.");
    }
}
