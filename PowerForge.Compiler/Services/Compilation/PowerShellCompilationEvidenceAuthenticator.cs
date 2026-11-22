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

    private static X509Certificate2 ResolveSigningCertificate(SigningOptionsConfiguration signing, bool requirePrivateKey)
        => ModuleSigningCertificateResolver.ResolveCertificate(signing, requirePrivateKey);

    internal static string NormalizeThumbprint(string? thumbprint)
        => ModuleSigningCertificateResolver.Normalize(thumbprint);
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
