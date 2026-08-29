using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;

namespace PowerForge;

/// <summary>Creates and verifies detached CMS authority for reusable and delivered compilation evidence.</summary>
internal static class PowerShellCompilationEvidenceAuthenticator
{
    internal static string GetSignerThumbprint(SigningOptionsConfiguration signing)
    {
        using var certificate = ResolveSigningCertificate(signing, requirePrivateKey: false);
        return NormalizeThumbprint(certificate.Thumbprint);
    }

    internal static PowerShellCompilationEvidenceSignature Sign(
        byte[] content,
        SigningOptionsConfiguration signing)
    {
        if (content is null) throw new ArgumentNullException(nameof(content));
        using var certificate = ResolveSigningCertificate(signing, requirePrivateKey: true);
        var signature = PowerForgePortablePayloadInventoryCms.Sign(content, certificate);
        return new PowerShellCompilationEvidenceSignature(
            signature,
            NormalizeThumbprint(certificate.Thumbprint));
    }

    internal static PowerForgePayloadInventorySignature Verify(
        byte[] content,
        byte[] signature,
        SigningOptionsConfiguration signing)
    {
        if (content is null) throw new ArgumentNullException(nameof(content));
        if (signature is null) throw new ArgumentNullException(nameof(signature));

        PowerForgePayloadInventorySignature verified;
        try
        {
            verified = PowerForgePortablePayloadInventoryCms.Verify(content, signature);
        }
        catch (Exception exception) when (exception is CryptographicException || exception is InvalidDataException)
        {
            throw new InvalidOperationException("Compilation evidence detached signature is invalid.", exception);
        }
        using var expected = ResolveSigningCertificate(signing, requirePrivateKey: false);
        var expectedThumbprint = NormalizeThumbprint(expected.Thumbprint);
        if (!string.Equals(verified.Thumbprint, expectedThumbprint, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Compilation evidence was not signed by the currently configured signing identity.");
        return verified;
    }

    private static X509Certificate2 ResolveSigningCertificate(
        SigningOptionsConfiguration signing,
        bool requirePrivateKey)
    {
        if (signing is null) throw new ArgumentNullException(nameof(signing));

        X509Certificate2? certificate = null;
        if (!string.IsNullOrWhiteSpace(signing.CertificatePFXBase64))
        {
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(signing.CertificatePFXBase64);
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException("The configured compilation evidence PFX base64 value is invalid.", exception);
            }
            certificate = LoadPfx(bytes, signing.CertificatePFXPassword);
        }
        else if (!string.IsNullOrWhiteSpace(signing.CertificatePFXPath))
        {
            var path = Path.GetFullPath(signing.CertificatePFXPath);
            if (!File.Exists(path))
                throw new FileNotFoundException("The configured compilation evidence PFX was not found.", path);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Compilation evidence signing does not accept a PFX through a symbolic link or junction.");
            certificate = LoadPfx(File.ReadAllBytes(path), signing.CertificatePFXPassword);
        }
        else if (!string.IsNullOrWhiteSpace(signing.CertificateThumbprint))
        {
            certificate = FindStoreCertificate(signing.CertificateThumbprint!, requirePrivateKey);
        }

        if (certificate is null)
            throw new InvalidOperationException(
                "Authenticated compilation evidence requires CertificateThumbprint, CertificatePFXPath, or CertificatePFXBase64.");
        if (requirePrivateKey && !certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw new InvalidOperationException("The compilation evidence signing certificate does not contain a private key.");
        }
        return certificate;
    }

    private static X509Certificate2 LoadPfx(byte[] bytes, string? password)
    {
        try
        {
#if NET10_0_OR_GREATER
            return X509CertificateLoader.LoadPkcs12(
                bytes,
                password ?? string.Empty,
                X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
#else
            return new X509Certificate2(
                bytes,
                password ?? string.Empty,
                X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
#endif
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException || exception is ArgumentException)
        {
            throw new InvalidOperationException("The configured compilation evidence PFX could not be loaded.", exception);
        }
    }

    private static X509Certificate2 FindStoreCertificate(string thumbprint, bool requirePrivateKey)
    {
        var normalized = NormalizeThumbprint(thumbprint);
        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            using var store = new X509Store(StoreName.My, location);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            foreach (var candidate in store.Certificates)
            {
                if (!string.Equals(NormalizeThumbprint(candidate.Thumbprint), normalized, StringComparison.OrdinalIgnoreCase) ||
                    requirePrivateKey && !candidate.HasPrivateKey)
                    continue;
                return new X509Certificate2(candidate);
            }
        }
        throw new InvalidOperationException("The configured compilation evidence signing certificate was not found in the certificate store.");
    }

    internal static string NormalizeThumbprint(string? thumbprint)
        => (thumbprint ?? string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
}

internal sealed class PowerShellCompilationEvidenceSignature
{
    internal PowerShellCompilationEvidenceSignature(byte[] signature, string thumbprint)
    {
        Signature = signature;
        Thumbprint = thumbprint;
    }

    internal byte[] Signature { get; }
    internal string Thumbprint { get; }
}
