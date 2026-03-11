using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace PowerForge.Tests;

public sealed class NuGetCertificateExportServiceTests
{
    [Fact]
    public void Execute_ExportsCertificateUsingThumbprint()
    {
        using var certificate = CreateCertificate("CN=NuGet Test", includeCodeSigningEku: true);
        var tempPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);

        try
        {
            var service = new NuGetCertificateExportService(_ => new[] { certificate });

            var result = service.Execute(new NuGetCertificateExportRequest
            {
                CertificateThumbprint = certificate.Thumbprint,
                WorkingDirectory = tempPath
            });

            Assert.True(result.Success);
            Assert.NotNull(result.CertificatePath);
            Assert.True(File.Exists(result.CertificatePath));
            Assert.True(result.HasCodeSigningEku);
            Assert.Equal(certificate.Subject, result.Subject);
            Assert.Equal(certificate.Issuer, result.Issuer);
            Assert.Equal(certificate.Thumbprint, result.Certificate?.Thumbprint);
            Assert.False(string.IsNullOrWhiteSpace(result.Sha256));
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    [Fact]
    public void Execute_ReturnsFailureWhenCertificateIsMissing()
    {
        var service = new NuGetCertificateExportService(_ => Array.Empty<X509Certificate2>());

        var result = service.Execute(new NuGetCertificateExportRequest
        {
            CertificateThumbprint = "ABC123",
            StoreLocation = CertificateStoreLocation.CurrentUser
        });

        Assert.False(result.Success);
        Assert.Contains("thumbprint 'ABC123'", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static X509Certificate2 CreateCertificate(string subject, bool includeCodeSigningEku)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        if (includeCodeSigningEku)
        {
            var usages = new OidCollection { new("1.3.6.1.5.5.7.3.3") };
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, critical: false));
        }

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
