using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Exports public certificates for NuGet.org package signing registration.
/// </summary>
public sealed class NuGetCertificateExportService
{
    private readonly Func<CertificateStoreLocation, IReadOnlyList<X509Certificate2>> _loadCertificates;

    /// <summary>
    /// Creates a new export service.
    /// </summary>
    public NuGetCertificateExportService(Func<CertificateStoreLocation, IReadOnlyList<X509Certificate2>>? loadCertificates = null)
    {
        _loadCertificates = loadCertificates ?? LoadCertificates;
    }

    /// <summary>
    /// Executes the certificate export request.
    /// </summary>
    public NuGetCertificateExportResult Execute(NuGetCertificateExportRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        try
        {
            var certificates = _loadCertificates(request.StoreLocation);
            var certificate = FindCertificate(certificates, request);
            if (certificate is null)
            {
                return new NuGetCertificateExportResult
                {
                    Success = false,
                    Error = BuildNotFoundMessage(request)
                };
            }

            var outputPath = ResolveOutputPath(request, certificate);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

            File.WriteAllBytes(outputPath, certificate.Export(X509ContentType.Cert));

            return new NuGetCertificateExportResult
            {
                Success = true,
                CertificatePath = outputPath,
                Certificate = certificate,
                HasCodeSigningEku = HasCodeSigningEku(certificate),
                Sha256 = GetSha256Hex(certificate),
                Subject = certificate.Subject,
                Issuer = certificate.Issuer,
                NotBefore = certificate.NotBefore,
                NotAfter = certificate.NotAfter
            };
        }
        catch (Exception ex)
        {
            return new NuGetCertificateExportResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    private static IReadOnlyList<X509Certificate2> LoadCertificates(CertificateStoreLocation storeLocation)
    {
        var location = storeLocation == CertificateStoreLocation.LocalMachine
            ? StoreLocation.LocalMachine
            : StoreLocation.CurrentUser;

        using var store = new X509Store(StoreName.My, location);
        store.Open(OpenFlags.ReadOnly);
        return store.Certificates.Cast<X509Certificate2>().ToArray();
    }

    private static X509Certificate2? FindCertificate(IReadOnlyList<X509Certificate2> certificates, NuGetCertificateExportRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.CertificateThumbprint))
        {
            var thumbprint = request.CertificateThumbprint!.Replace(" ", string.Empty);
            return certificates.FirstOrDefault(c => string.Equals(c.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(request.CertificateSha256))
        {
            var sha = request.CertificateSha256!.Replace(" ", string.Empty);
            return certificates.FirstOrDefault(c => string.Equals(GetSha256Hex(c), sha, StringComparison.OrdinalIgnoreCase));
        }

        throw new InvalidOperationException("Either CertificateThumbprint or CertificateSha256 must be provided.");
    }

    private static string BuildNotFoundMessage(NuGetCertificateExportRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.CertificateThumbprint))
            return $"Certificate with thumbprint '{request.CertificateThumbprint}' not found in {request.StoreLocation}\\My store";

        return $"Certificate with SHA256 '{request.CertificateSha256}' not found in {request.StoreLocation}\\My store";
    }

    private static string ResolveOutputPath(NuGetCertificateExportRequest request, X509Certificate2 certificate)
    {
        if (!string.IsNullOrWhiteSpace(request.OutputPath))
            return Path.GetFullPath(request.OutputPath!.Trim().Trim('"'));

        var first = (certificate.Subject ?? string.Empty).Split(',').FirstOrDefault() ?? string.Empty;
        first = first.Trim();
        if (first.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            first = first.Substring(3);

        var subjectName = Regex.Replace(first, @"[^\w\s-]", string.Empty);
        if (string.IsNullOrWhiteSpace(subjectName))
            subjectName = "Certificate";

        var fileName = $"{subjectName}-CodeSigning.cer";
        var workingDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory)
            ? Environment.CurrentDirectory
            : request.WorkingDirectory;
        return Path.GetFullPath(Path.Combine(workingDirectory, fileName));
    }

    private static bool HasCodeSigningEku(X509Certificate2 certificate)
    {
        const string codeSigningOid = "1.3.6.1.5.5.7.3.3";

        foreach (var extension in certificate.Extensions)
        {
            if (extension is not X509EnhancedKeyUsageExtension eku)
                continue;

            foreach (var oid in eku.EnhancedKeyUsages)
            {
                if (string.Equals(oid.Value, codeSigningOid, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(oid.FriendlyName, "Code Signing", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string GetSha256Hex(X509Certificate2 certificate)
    {
#if NET8_0_OR_GREATER
        return certificate.GetCertHashString(HashAlgorithmName.SHA256);
#else
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(certificate.RawData);
        return BitConverter.ToString(hash).Replace("-", string.Empty);
#endif
    }
}
