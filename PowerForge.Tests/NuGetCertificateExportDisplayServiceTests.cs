using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace PowerForge.Tests;

public sealed class NuGetCertificateExportDisplayServiceTests
{
    [Fact]
    public void CreateSuccessSummary_FormatsRegistrationGuidanceAndCertificateDetails()
    {
        using var certificate = CreateCertificate("CN=NuGet Test");
        var result = new NuGetCertificateExportResult
        {
            Success = true,
            CertificatePath = @"C:\Temp\NuGetSigning.cer",
            Certificate = certificate,
            Subject = "CN=NuGet Test",
            Issuer = "CN=NuGet Issuer",
            Sha256 = "ABC123",
            NotBefore = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            NotAfter = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var lines = new NuGetCertificateExportDisplayService().CreateSuccessSummary(result);

        Assert.Contains(lines, line => line.Text == @"Certificate exported successfully to: C:\Temp\NuGetSigning.cer" && line.Color == ConsoleColor.Green);
        Assert.Contains(lines, line => line.Text == "Next steps to register with NuGet.org:" && line.Color == ConsoleColor.Yellow);
        Assert.Contains(lines, line => line.Text == @"4. Upload the file: C:\Temp\NuGetSigning.cer");
        Assert.Contains(lines, line => line.Text == "Certificate details:" && line.Color == ConsoleColor.Cyan);
        Assert.Contains(lines, line => line.Text == "  Subject: CN=NuGet Test");
        Assert.Contains(lines, line => line.Text == "  Issuer: CN=NuGet Issuer");
        Assert.Contains(lines, line => line.Text == "  SHA256: ABC123");
    }

    private static X509Certificate2 CreateCertificate(string subject)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }
}
