using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PowerForge;

/// <summary>Shared certificate policy and lazy secret resolution for module signatures and compilation evidence.</summary>
internal static class ModuleSigningCertificateResolver
{
    internal static ResolvedModuleSigningInput ResolveInput(
        SigningOptionsConfiguration signing,
        Action<string>? report = null,
        Func<string, StoreLocation, X509Certificate2?>? lookup = null)
    {
        if (signing is null) throw new ArgumentNullException(nameof(signing));
        var approved = (signing.AllowedCertificateThumbprints ?? Array.Empty<string>()).Select(NormalizeApproved).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var thumbprint = Normalize(signing.CertificateThumbprint);
        var pfx = ResolveSecret(signing.CertificatePFXBase64, signing.CertificatePFXBase64EnvironmentVariable, "PFX Base64");
        var password = ResolveSecret(signing.CertificatePFXPassword, signing.CertificatePFXPasswordEnvironmentVariable, "PFX password");
        int sources = (thumbprint.Length > 0 ? 1 : 0) + (!string.IsNullOrWhiteSpace(signing.CertificatePFXPath) ? 1 : 0) + (!string.IsNullOrWhiteSpace(pfx) ? 1 : 0);
        if (sources > 1) throw new InvalidOperationException("Configure only one explicit signing certificate source: thumbprint, PFX path or PFX Base64.");
        if (thumbprint.Length > 0) EnsureApproved(thumbprint, approved);
        if (sources == 0 && approved.Length > 0)
        {
            using var certificate = SelectStoreCertificate(approved, lookup ?? ReadStoreCertificate, report);
            thumbprint = Normalize(certificate.Thumbprint);
        }
        return new ResolvedModuleSigningInput(thumbprint, signing.CertificatePFXPath, pfx, password, approved);
    }

    internal static X509Certificate2 ResolveCertificate(SigningOptionsConfiguration signing, bool requirePrivateKey)
    {
        var input = ResolveInput(signing);
        X509Certificate2? certificate = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(input.PfxBase64))
            {
                byte[] bytes;
                try { bytes = Convert.FromBase64String(input.PfxBase64); }
                catch (FormatException) { throw new InvalidOperationException("The configured signing PFX Base64 value is invalid."); }
                try { certificate = LoadPfx(bytes, input.PfxPassword); }
                finally { Array.Clear(bytes, 0, bytes.Length); }
            }
            else if (!string.IsNullOrWhiteSpace(input.PfxPath))
            {
                var path = Path.GetFullPath(input.PfxPath);
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Signing does not accept a PFX through a symbolic link or junction.");
                byte[] bytes = File.ReadAllBytes(path);
                try { certificate = LoadPfx(bytes, input.PfxPassword); }
                finally { Array.Clear(bytes, 0, bytes.Length); }
            }
            else if (input.Thumbprint.Length > 0)
            {
                certificate = SelectStoreCertificate(new[] { input.Thumbprint }, ReadStoreCertificate, null, requirePrivateKey);
            }
            if (certificate is null)
                throw new InvalidOperationException("Signing requires AllowedCertificateThumbprints, CertificateThumbprint, CertificatePFXPath or CertificatePFXBase64 (or its environment reference).");
            EnsureApproved(certificate.Thumbprint, input.ApprovedThumbprints);
            var reason = GetRejection(certificate, requirePrivateKey);
            if (reason is not null) throw new InvalidOperationException($"Signing certificate '{certificate.Thumbprint}' cannot be used: {reason}.");
            var result = certificate;
            certificate = null;
            return result;
        }
        finally { certificate?.Dispose(); }
    }

    internal static X509Certificate2 SelectStoreCertificate(
        string[] thumbprints,
        Func<string, StoreLocation, X509Certificate2?> lookup,
        Action<string>? report = null,
        bool requirePrivateKey = true)
    {
        foreach (var thumbprint in thumbprints)
        {
            foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
            {
                X509Certificate2? certificate = null;
                try
                {
                    certificate = lookup(thumbprint, location);
                    var reason = GetRejection(certificate, requirePrivateKey);
                    if (reason is null && string.Equals(Normalize(certificate!.Thumbprint), Normalize(thumbprint), StringComparison.Ordinal))
                    {
                        var result = certificate;
                        certificate = null;
                        report?.Invoke($"Selected signing certificate {thumbprint} in {location}/My.");
                        return result;
                    }
                    report?.Invoke($"Signing certificate {thumbprint} in {location}/My: {reason ?? "identity mismatch"}.");
                }
                catch (Exception error) when (error is CryptographicException || error is PlatformNotSupportedException || error is UnauthorizedAccessException)
                {
                    report?.Invoke($"Signing certificate {thumbprint} in {location}/My: certificate store unavailable.");
                }
                finally { certificate?.Dispose(); }
            }
        }
        throw new InvalidOperationException("No usable approved code-signing certificate was found. Install an approved certificate with a private key, valid dates and code-signing EKU; no other identity is selected.");
    }

    internal static string? GetRejection(X509Certificate2? certificate, bool requirePrivateKey = true)
    {
        if (certificate is null) return "not found";
        if (requirePrivateKey && !certificate.HasPrivateKey) return "no private key";
        var now = DateTime.UtcNow;
        if (now < certificate.NotBefore.ToUniversalTime()) return "not yet valid";
        if (now >= certificate.NotAfter.ToUniversalTime()) return "expired";
        var restrictions = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().Where(extension => extension.EnhancedKeyUsages.Count > 0).ToArray();
        if (restrictions.Length > 0 && !restrictions.Any(extension => extension.EnhancedKeyUsages.Cast<Oid>().Any(oid => oid.Value == "1.3.6.1.5.5.7.3.3")))
            return "code signing is not permitted by EKU";
        return null;
    }

    private static X509Certificate2? ReadStoreCertificate(string thumbprint, StoreLocation location)
    {
        using var store = new X509Store(StoreName.My, location);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        X509Certificate2? result = null;
        foreach (var certificate in store.Certificates)
        {
            using (certificate)
            {
                if (result is null && string.Equals(Normalize(certificate.Thumbprint), Normalize(thumbprint), StringComparison.Ordinal))
                    result = new X509Certificate2(certificate);
            }
        }
        return result;
    }

    private static X509Certificate2 LoadPfx(byte[] bytes, string? password)
    {
        try
        {
#if NET10_0_OR_GREATER
            return X509CertificateLoader.LoadPkcs12(bytes, password ?? string.Empty, X509KeyStorageFlags.EphemeralKeySet);
#else
            return new X509Certificate2(bytes, password ?? string.Empty, X509KeyStorageFlags.EphemeralKeySet);
#endif
        }
        catch (CryptographicException) { throw new InvalidOperationException("The configured signing PFX could not be loaded. Check its payload and password."); }
    }

    private static string? ResolveSecret(string? literal, string? name, string description)
    {
        if (string.IsNullOrWhiteSpace(name)) return literal;
        if (!string.IsNullOrEmpty(literal)) throw new InvalidOperationException($"Configure either a literal {description} or its environment-variable reference, not both.");
        var value = Environment.GetEnvironmentVariable(name!);
        if (string.IsNullOrEmpty(value)) throw new InvalidOperationException($"Signing {description} environment variable '{name}' is missing or empty.");
        return value;
    }

    private static string NormalizeApproved(string value)
    {
        var normalized = Normalize(value);
        if (normalized.Length != 40 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException("AllowedCertificateThumbprints entries must be complete 40-character hexadecimal certificate thumbprints.");
        return normalized;
    }

    internal static string Normalize(string? value) => new string((value ?? string.Empty).Where(character => !char.IsWhiteSpace(character)).ToArray()).ToUpperInvariant();
    private static void EnsureApproved(string thumbprint, string[] approved)
    {
        if (approved.Length > 0 && !approved.Contains(Normalize(thumbprint), StringComparer.Ordinal))
            throw new InvalidOperationException($"Signing certificate '{Normalize(thumbprint)}' is not in AllowedCertificateThumbprints.");
    }
}

internal sealed class ResolvedModuleSigningInput
{
    internal ResolvedModuleSigningInput(string thumbprint, string? pfxPath, string? pfxBase64, string? pfxPassword, string[] approved)
    { Thumbprint = thumbprint; PfxPath = pfxPath; PfxBase64 = pfxBase64; PfxPassword = pfxPassword; ApprovedThumbprints = approved; }
    internal string Thumbprint { get; }
    internal string? PfxPath { get; }
    internal string? PfxBase64 { get; }
    internal string? PfxPassword { get; }
    internal string[] ApprovedThumbprints { get; }
}
